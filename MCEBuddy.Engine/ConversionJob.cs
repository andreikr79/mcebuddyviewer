using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

using MCEBuddy.CommercialScan;
using MCEBuddy.Globals;
using MCEBuddy.MetaData;
using MCEBuddy.RemuxMediaCenter;
using MCEBuddy.VideoProperties;
using MCEBuddy.Util;
using MCEBuddy.Transcode;
using MCEBuddy.Configuration;
using MCEBuddy.AppWrapper;

namespace MCEBuddy.Engine
{
    public class ConversionJob
    {
        private const int MAX_SHUTDOWN_WAIT = 5000;
        private const double SPACE_REQUIRED_MULTIPLIER = 2.5; // Ideally this should be 2, 1 for the TS file and 1 incase of NoRecode which doesn't compress, but most folks who use multiple conversions are converting to mp4 so 1.5 is more than enough.
        private const double MAX_CONCURRENT_SPACE_REQUIRED_MULTIPLIER = 0.5; // Then multiple tasks are running what are the chances of requiring free space simultaneously

        private ConversionJobOptions _conversionOptions;
        private string _originalFileName = "";
        private string _remuxedVideo = "";
        private MetaData.VideoMetaData _metaData;
        protected CommercialScan.Scanner _commercialScan = null;
        protected CommercialScan.Remover _commercialRemover = null;
        protected VideoInfo _videoFile;
        private ClosedCaptions cc;
        protected string _convertedFile = "";
        volatile private bool _completed = false; // these are accessed by multiple threads and can potentially cause a race condition
        volatile private bool _active = false; // these are accessed by multiple threads and can potentially cause a race condition
        private bool _saveEDLFile = false;
        private bool _saveSRTFile = false;
        private bool spaceCheck = true;

        private bool commercialSkipCut = false; // do commercial scan keep EDL file but skip cutting the commercials
        private int maxConcurrentJobs = 1;
        private bool zeroTSChannelAudio = false;
        private bool preConversionCommercialRemover = false;
        private bool ignoreCopyProtection = false;
        private string _srtFile = ""; // location of SRT file
        private double subtitleSegmentOffset = 0; // compensation for each segment cut

        protected JobStatus _jobStatus;

        protected Thread _conversionThread;

        public ConversionJob(ConversionJobOptions conversionJobOptions)
        {
            _conversionOptions = conversionJobOptions;

            _originalFileName = _conversionOptions.sourceVideo; // This is what we use to report to the world what we're working on, _sourceVideo may change under certain conditions below
            if (String.IsNullOrEmpty(_conversionOptions.destinationPath))
                _conversionOptions.destinationPath = Path.GetDirectoryName(_conversionOptions.sourceVideo); // No dest path = convert in place

            _jobStatus = new JobStatus();
            _jobStatus.SourceFile = OriginalFileName;

            // Read various engine parameters
            maxConcurrentJobs = MCEBuddyConf.GlobalMCEConfig.GeneralOptions.maxConcurrentJobs;
            spaceCheck = MCEBuddyConf.GlobalMCEConfig.GeneralOptions.spaceCheck;
            ignoreCopyProtection = MCEBuddyConf.GlobalMCEConfig.GeneralOptions.ignoreCopyProtection;
            subtitleSegmentOffset = MCEBuddyConf.GlobalMCEConfig.GeneralOptions.subtitleSegmentOffset;

            // Read various profile parameters
            Ini configProfileIni = new Ini(GlobalDefs.ProfileFile);
            commercialSkipCut = (conversionJobOptions.commercialSkipCut || (configProfileIni.ReadBoolean(_conversionOptions.profile, "CommercialSkipCut", false))); // 1 of 2 places that commercial skipcut can be defined (in the profile or in the conversion task settings)
            preConversionCommercialRemover = configProfileIni.ReadBoolean(_conversionOptions.profile, "PreConversionCommercialRemover", false); // Check if the user wants to remove commercials before the actual conversion in which case we always return false - i.e. remove commercial during remux stage
        }

        public string TaskName
        {
            get { return _conversionOptions.taskName; }
        }

        public string ConvertedFile
        {
            get { return _convertedFile; }
        }

        public bool Completed
        {
            get { return _completed; }
        }

        public JobStatus Status
        {
            get { return _jobStatus; }
        }

        public string OriginalFileName
        {
            get
            {
                return _originalFileName;
            }
        }

        public string WorkingPath
        {
            get { return _conversionOptions.workingPath; }
            set { _conversionOptions.workingPath = value; }
        }

        public bool Active
        {
            get { return _active; }
            set { _active = value; }
        }

        public VideoMetaData MetaData
        {
            get { return _metaData; }
        }

        public string Profile
        {
            get { return _conversionOptions.profile; }
        }

        protected string SourceVideo
        {
            get
            {
                if (!String.IsNullOrEmpty(_remuxedVideo)) return _remuxedVideo;
                return _conversionOptions.sourceVideo;
            }
        }

        /// <summary>
        /// Check for sufficient disk space for conversion, source, target and temp
        /// </summary>
        /// <param name="jobLog">joblog</param>
        /// <returns>True is there is sufficient space in all places</returns>
        private bool SufficientSpace( Log jobLog)
        {
            if (!_jobStatus.Cancelled)
            {
                double videoFileSpace = 0;
                long fiSize = -1;
                long workingSpace = -1;
                long destinationSpace = -1;

                double spaceMultiplier = SPACE_REQUIRED_MULTIPLIER * (1 + ((maxConcurrentJobs - 1) * MAX_CONCURRENT_SPACE_REQUIRED_MULTIPLIER)); // for each simultaneous conversion we need enough free space (each incremental conversion we allocate space - statistically significnat space reuqired for probabalistic simultaneous reuqirements)

                try
                {
                    FileInfo fi = new FileInfo(_conversionOptions.sourceVideo);
                    fiSize = fi.Length;
                    videoFileSpace = fiSize * spaceMultiplier;
                    workingSpace = Util.FileIO.GetFreeDiskSpace(_conversionOptions.workingPath);
                    destinationSpace = Util.FileIO.GetFreeDiskSpace(_conversionOptions.destinationPath);
                }
                catch (Exception e)
                {
                    string errMsg = Localise.GetPhrase("Error: Unable to obtain available disk space");
                    jobLog.WriteEntry(this, errMsg + "\r\nError : " + e.ToString(), Log.LogEntryType.Warning);
                    return true;
                }

                if (destinationSpace < 0)
                {
                    string errMsg = Localise.GetPhrase("Unable to obtain available disk space in ") + " " + _conversionOptions.destinationPath;
                    jobLog.WriteEntry(this, errMsg, Log.LogEntryType.Warning);
                    return true;
                }

                if (workingSpace < 0)
                {

                    string errorMsg = Localise.GetPhrase("Unable to obtain available disk space in ") + " " + _conversionOptions.workingPath;
                    jobLog.WriteEntry(this, errorMsg, Log.LogEntryType.Warning);
                    return true;
                }

                jobLog.WriteEntry(this, "File size -> " + (((float)fiSize) / 1024 / 1024 / 1024).ToString(System.Globalization.CultureInfo.InvariantCulture) + " GB", Log.LogEntryType.Debug);
                jobLog.WriteEntry(this, "Destination space -> " + (((float)destinationSpace)/1024/1024/1024).ToString(System.Globalization.CultureInfo.InvariantCulture) + " GB", Log.LogEntryType.Debug);
                jobLog.WriteEntry(this, "Working space -> " + (((float)workingSpace)/1024/1024/1024).ToString(System.Globalization.CultureInfo.InvariantCulture) + " GB", Log.LogEntryType.Debug);

                if (!spaceCheck) // skip space check
                {
                    jobLog.WriteEntry(this, "SKIPPING FREE SPACE CHECK", Log.LogEntryType.Warning);
                    return true;
                }

                jobLog.WriteEntry(this, "Required free space -> " + (((float)videoFileSpace)/1024/1024/1024).ToString(System.Globalization.CultureInfo.InvariantCulture) + " GB", Log.LogEntryType.Debug);
                jobLog.WriteEntry(this, "Max concurrent conversion jobs -> " + maxConcurrentJobs.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);

                if (destinationSpace < videoFileSpace/spaceMultiplier) // For destination we just need to check enough space for each conversion
                {
                    string errorMsg = Localise.GetPhrase("Insufficient destination disk space avalable in") + " " + _conversionOptions.destinationPath + ". " +
                                            destinationSpace.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + Localise.GetPhrase("available, required") + " " +
                                            (videoFileSpace/spaceMultiplier).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (destinationSpace < 0)
                        errorMsg += "\n" +
                                    Localise.GetPhrase("Most likely cause is insufficient premissions for the MCEBuddy engine at") + " " +
                                    _conversionOptions.destinationPath + " " + Localise.GetPhrase("Please check connection credentials.");
                    jobLog.WriteEntry(this, errorMsg, Log.LogEntryType.Error);
                    _jobStatus.ErrorMsg = errorMsg;
                    return false;
                }

                if (workingSpace < videoFileSpace) // for working space we need a multipler for each file and for each conversion queue
                {
                    string errorMsg = Localise.GetPhrase("Insufficient temp working disk space avalable in") + " " +
                                        _conversionOptions.workingPath + ". " +
                                        workingSpace.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + Localise.GetPhrase("available, required") + " " + videoFileSpace.ToString(System.Globalization.CultureInfo.InvariantCulture);
                                        videoFileSpace.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    
                    if (maxConcurrentJobs > 1)
                        errorMsg += "\n" + "Try reducing the number of simultaneous conversions to reduce free disk space requirements.";

                    if (destinationSpace < 0)
                        errorMsg += "\n" +
                                    Localise.GetPhrase("Most likely cause is insufficient premissions for the MCEBuddy engine at") + " " +
                                    _conversionOptions.destinationPath + " " + Localise.GetPhrase("Please check connection credentials.");
                    jobLog.WriteEntry(this, errorMsg, Log.LogEntryType.Error);
                    _jobStatus.ErrorMsg = errorMsg;
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Start the conversion task
        /// </summary>
        public void StartConversionThread()
        {
            Log.AppLog.WriteEntry(this, "Starting Conversion Thread", Log.LogEntryType.Debug, true);
            _conversionThread = new Thread(this.Convert);
            _conversionThread.CurrentCulture = _conversionThread.CurrentUICulture = Localise.MCEBuddyCulture;
            _active = true; // Mark it here before the thread starts otherwise it leads to race conditions since the thread takes time to start and the same job is assigned to another queue
            _conversionThread.Start();
        }

        /// <summary>
        /// Stop the conversion task
        /// </summary>
        public void StopConversionThread()
        {
            if (_conversionThread != null)
            {
                Log.AppLog.WriteEntry(this, "Marking conversion thread cancelled and waiting for " + (MAX_SHUTDOWN_WAIT/1000).ToString(System.Globalization.CultureInfo.InvariantCulture) + " seconds to terminate running processes", Log.LogEntryType.Debug, true);

                _jobStatus.Cancelled = true; // Give a chance to kill any spawned processes

                int totalSleep = 0;
                while (_active && (totalSleep < MAX_SHUTDOWN_WAIT))
                {
                    Thread.Sleep(200); // Wait for running processes to be terminated
                    totalSleep += 200;
                }

                if (_active)
                {
                    Log.AppLog.WriteEntry(this, "Aborting conversion thread", Log.LogEntryType.Debug, true);
                    _conversionThread.Abort();
                }
            }
        }

        /// <summary>
        /// Create a log file for the conversion job
        /// </summary>
        /// <param name="videoFileName">Name of file being converted</param>
        /// <returns>Log object</returns>
        private Log CreateLog(string videoFileName)
        {
            if (MCEBuddyConf.GlobalMCEConfig.GeneralOptions.logJobs)
            {
                //Delay for a random time to avoid log creation problem for 2 threads started simultaneously for same profile causing logfile not be created
                Thread.Sleep(GlobalDefs.random.Next(100)); //generate a Random number to sleep for upto 100ms

                string logFile = "";
                try
                {
                    logFile = Path.Combine(GlobalDefs.LogPath,
                                                  Path.GetFileName(videoFileName) + "-" + Util.FilePaths.RemoveIllegalFilePathChars(_conversionOptions.taskName) + "-" +
                                                  DateTime.Now.ToString("o", System.Globalization.CultureInfo.InvariantCulture).Replace(':', '-') +
                                                  ".log");

                    Log.AppLog.WriteEntry(this, Localise.GetPhrase("Creating log file") + " : " + logFile, Log.LogEntryType.Information);

                    Log newLog = new Log(Log.LogDestination.LogFile, logFile);
                    return newLog;
                }
                catch (Exception e)
                {
                    Log.AppLog.WriteEntry(this, Localise.GetPhrase("Error creating log file") + " : " + logFile, Log.LogEntryType.Error);
                    Log.AppLog.WriteEntry(this, "Error : " + e.ToString(), Log.LogEntryType.Debug);
                }
            }
            return new Log(Log.LogDestination.Null);
        }

        /// <summary>
        /// Clean up the temp folder and ready to close job on a failed or completed conversions
        /// </summary>
        /// <param name="jobLog">KpbLog</param>
        private void Cleanup( ref Log jobLog)
        {
            // These files may outside the temp working directory if the source wasn't copied - only we if created them (i.e. commercial scanning done)
            if (_commercialScan != null)
            {
                Util.FileIO.TryFileDelete(Util.FilePaths.GetFullPathWithoutExtension(OriginalFileName) + ".log"); // Delete the .log file created by Comskip
                Util.FileIO.TryFileDelete(Util.FilePaths.GetFullPathWithoutExtension(OriginalFileName) + ".logo.txt"); // Delete the .logo.txt file created by Comskip
                Util.FileIO.TryFileDelete(Util.FilePaths.GetFullPathWithoutExtension(OriginalFileName) + ".txt"); // Delete the txt file created by Comskip
                Util.FileIO.TryFileDelete(Util.FilePaths.GetFullPathWithoutExtension(OriginalFileName) + ".edlp"); // Delete the edlp file created by Comskip
                
                if (!_saveEDLFile) // If the was with the original recording it would be saved, so if we didn't save it then it needs to be deleted unless the destination file and source file are in the same directory
                    if ((Util.FilePaths.GetFullPathWithoutExtension(OriginalFileName) + ".edl") != (Util.FilePaths.GetFullPathWithoutExtension(_convertedFile) + ".edl")) // Don't delete if they are the same file, e.g. TS to TS in same directory
                        Util.FileIO.TryFileDelete(Util.FilePaths.GetFullPathWithoutExtension(OriginalFileName) + ".edl"); // Delete the edl file - check if it existed
            }

            if (!_saveSRTFile) // If the was with the original recording it would be saved, so if we didn't save it then it needs to be deleted unless the destination file and source file are in the same directory
                if (_srtFile != (Util.FilePaths.GetFullPathWithoutExtension(_convertedFile) + ".srt")) // Don't delete if they are the same file, e.g. TS to TS in same directory
                    Util.FileIO.TryFileDelete(_srtFile); // Delete the srt file - check if it existed

            Util.FileIO.ClearFolder(_conversionOptions.workingPath); // Clean up the temp working directory
            
            LogStatus("", ref jobLog); // clear the output on the screen
            _jobStatus.PercentageComplete = 0; //Reset the counter
            _jobStatus.ETA = "";

            jobLog.Close();
            _completed = true; // mark it completed only after an exit to indicate thread is finished
            _active = false; // mark active false only after completed true to avoid race condition with monitor thread
        }

        /// <summary>
        /// Write current activity to log and update current action
        /// </summary>
        /// <param name="activity">Status/Activity</param>
        /// <param name="jobLog">JobLog</param>
        private void LogStatus( string activity, ref Log jobLog)
        {
            _jobStatus.CurrentAction = activity;
            jobLog.WriteEntry(this, activity, Log.LogEntryType.Information, true);
        }

        /// <summary>
        /// Rename the converted file using the metadata extracted and downloaded, including custom renames
        /// </summary>
        /// <param name="jobLog">JobLog</param>
        /// <returns>True if successful</returns>
        private bool RenameByMetaData(ref Log jobLog)
        {
            string subDestinationPath = "";

            if (_metaData != null)
            {
                if ((_conversionOptions.renameBySeries) & (!String.IsNullOrEmpty(_metaData.MetaData.Title)))
                {
                    LogStatus(Localise.GetPhrase("Renaming file from show information"), ref jobLog);
                    string title = _metaData.MetaData.Title;
                    string subTitle = _metaData.MetaData.SubTitle;

                    //Get the date field
                    string date;
                    if (_metaData.MetaData.RecordedDateTime > GlobalDefs.NO_BROADCAST_TIME)
                    {
                        date = _metaData.MetaData.RecordedDateTime.ToLocalTime().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        DateTime dt = Util.FileIO.GetFileCreationTime(this.OriginalFileName);

                        if (dt > GlobalDefs.NO_BROADCAST_TIME)
                        {
                            date = dt.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                        }
                        else
                        {
                            jobLog.WriteEntry(this, Localise.GetPhrase("Cannot get recorded date and time, using current date and time"), Log.LogEntryType.Warning);
                            date = DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                        }
                    }

                    // Build the new file name, check which naming convention we are using
                    string newFileName = "";

                    if (!String.IsNullOrEmpty(_conversionOptions.customRenameBySeries))
                    {
                        jobLog.WriteEntry(this, Localise.GetPhrase("Custom Renaming Command") + " -> " + _conversionOptions.customRenameBySeries, Log.LogEntryType.Debug);
                        try
                        {
                            CustomRename.CustomRenameFilename(_conversionOptions.customRenameBySeries, ref newFileName, ref subDestinationPath, OriginalFileName, _metaData.MetaData, jobLog);

                            newFileName = newFileName.Replace(@"\\", @"\");
                            newFileName += Path.GetExtension(_convertedFile);
                        }
                        catch (Exception e)
                        {
                            jobLog.WriteEntry(this, Localise.GetPhrase("Error in file naming format detected, fallback to default naming convention") + "\r\nError : " + e.ToString(), Log.LogEntryType.Warning); // We had an invalid format
                            newFileName = ""; // Reset since we had an error so fall back can work
                            subDestinationPath = ""; // Reset path for failure
                        }
                    }
                    else if (_conversionOptions.altRenameBySeries) // Alternate renaming pattern
                    {
                        // ALTERNATE MC COMPATIBLE --> SHOWNAME/SEASON XX/SXXEYY-EPISODENAME.ext

                        if ((_metaData.MetaData.Season > 0) && (_metaData.MetaData.Episode > 0))
                        {
                            newFileName += "S" + _metaData.MetaData.Season.ToString("00", System.Globalization.CultureInfo.InvariantCulture) + "E" + _metaData.MetaData.Episode.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
                            if (subTitle != "")
                                newFileName += "-" + subTitle;
                        }
                        else
                        {
                            jobLog.WriteEntry(this, Localise.GetPhrase("No Season and Episode information available, using show name"), Log.LogEntryType.Warning); // if there is not season episode name available
                            newFileName = title;
                            if (subTitle != "")
                                newFileName += "-" + subTitle;
                            else
                                newFileName += "-" + date + " " + DateTime.Now.ToString("HH:MM", System.Globalization.CultureInfo.InvariantCulture);
                        }
                        newFileName = newFileName.Replace(@"\\", @"\");
                        newFileName += Path.GetExtension(_convertedFile);

                        // Create the directory structure
                        subDestinationPath += Util.FilePaths.RemoveIllegalFilePathChars(_metaData.MetaData.Title);
                        if ((_metaData.MetaData.Season > 0))
                            subDestinationPath += "\\Season " + Util.FilePaths.RemoveIllegalFilePathChars(_metaData.MetaData.Season.ToString("00", System.Globalization.CultureInfo.InvariantCulture));
                    }
                    
                    if (newFileName == "") // this is our default/fallback option
                    {
                        // STANDARD --> SHOWNAME/SHOWNAME-SXXEYY-EPISODENAME<-RECORDDATE>.ext // Record date is used where there is no season and episode info

                        newFileName = title;

                        if ((_metaData.MetaData.Season > 0) && (_metaData.MetaData.Episode > 0))
                        {
                            newFileName += "-" + "S" + _metaData.MetaData.Season.ToString("00", System.Globalization.CultureInfo.InvariantCulture) + "E" + _metaData.MetaData.Episode.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
                            if (subTitle != "") newFileName += "-" + subTitle;
                        }
                        else
                        {
                            jobLog.WriteEntry(this, Localise.GetPhrase("No Season and Episode information available, using episode name/record date"), Log.LogEntryType.Warning); // if there is not season episode name available
                            if (subTitle != "")
                                newFileName += "-" + subTitle;
                            else
                                newFileName += "-" + date + " " + DateTime.Now.ToString("HH:MM", System.Globalization.CultureInfo.InvariantCulture); // Backup to create a unique name if season/episode is not available
                        }

                        newFileName = newFileName.Replace(@"\\", @"\");
                        newFileName += Path.GetExtension(_convertedFile);

                        // Create the directory structure
                        subDestinationPath += Util.FilePaths.RemoveIllegalFilePathChars(_metaData.MetaData.Title);
                    }

                    //Try to rename the file with the new convention
                    try
                    {
                        newFileName = Util.FilePaths.RemoveIllegalFilePathChars(newFileName); // clean it up
                        newFileName = Path.Combine(Path.GetDirectoryName(_convertedFile), newFileName);
                        if (_convertedFile != newFileName) // If the filename are the same else File.Move throws an error
                        {
                            jobLog.WriteEntry(Localise.GetPhrase("Rename file to") + " " + newFileName, Log.LogEntryType.Information);
                            FileIO.TryFileDelete(newFileName);
                            File.Move(_convertedFile, newFileName);
                            _convertedFile = newFileName; //update file name
                        }
                    }
                    catch (Exception e)
                    {
                        jobLog.WriteEntry(Localise.GetPhrase("Unable to rename file") + " " + _convertedFile + " " + Localise.GetPhrase("to") + " " + newFileName, Log.LogEntryType.Warning); //not an error since we can continue with origianl filename also
                        jobLog.WriteEntry("Error : " + e.ToString(), Log.LogEntryType.Debug);
                    }
                }
                else
                    jobLog.WriteEntry(this, "Skipping Renaming by Series details", Log.LogEntryType.Information);
            }
            else
                jobLog.WriteEntry(this, "Renaming by Series, no Metadata", Log.LogEntryType.Warning);

            RenameFileExtension(ref jobLog); // Rename the file extension if required

            try
            {
                LogStatus(Localise.GetPhrase("Moving converted file to destination"), ref jobLog);

                string destpath = Path.Combine(_conversionOptions.destinationPath, subDestinationPath);                                  
                Util.FilePaths.CreateDir(Path.Combine(_conversionOptions.destinationPath, subDestinationPath)); // Create the destination directory path
                //string _destinationFile = Path.Combine(_conversionOptions.destinationPath, subDestinationPath, Path.GetFileName(_convertedFile));
                string _destinationFile = Path.Combine(destpath, Path.GetFileName(_convertedFile));
                if (_destinationFile != _convertedFile) // If they are the same file, don't delete accidentally (TS to TS conversions in same directory are example)
                    Util.FileIO.TryFileDelete(_destinationFile); // Delete file in destination if it already exists
                jobLog.WriteEntry(this, Localise.GetPhrase("Moving converted file") + " " + _convertedFile + " " + Localise.GetPhrase("to") + " " + _destinationFile, Log.LogEntryType.Information);
                File.Move(_convertedFile, _destinationFile); // move the file to the destination

                _convertedFile = _destinationFile; // update on success

                return true; // home free...
            }
            catch (Exception e)
            {
                jobLog.WriteEntry(Localise.GetPhrase("Unable to move file") + " " + _convertedFile + " " + Localise.GetPhrase("to") + " " + _conversionOptions.destinationPath + ". Error :" + e.ToString(), Log.LogEntryType.Error);

                if (_conversionOptions.fallbackToSourcePath)
                {
                    try
                    {
                        LogStatus("Fallback to Source Path is enabled, trying to move directory to source path -> " + Path.GetDirectoryName(_originalFileName), ref jobLog);
                        
                        _conversionOptions.destinationPath = Path.GetDirectoryName(_originalFileName); // update the Destination Path to Source Path

                        string destpath = Path.Combine(_conversionOptions.destinationPath, subDestinationPath);
                        Util.FilePaths.CreateDir(Path.Combine(_conversionOptions.destinationPath, subDestinationPath)); // Create the destination directory path
                        //string _destinationFile = Path.Combine(_conversionOptions.destinationPath, subDestinationPath, Path.GetFileName(_convertedFile));
                        string _destinationFile = Path.Combine(destpath, Path.GetFileName(_convertedFile));
                        if (_destinationFile != _convertedFile) // If they are the same file, don't delete accidentally (TS to TS conversions in same directory are example)
                            Util.FileIO.TryFileDelete(_destinationFile); // Delete file in destination if it already exists
                        jobLog.WriteEntry(this, Localise.GetPhrase("Moving converted file") + " " + _convertedFile + " " + Localise.GetPhrase("to") + " " + _destinationFile, Log.LogEntryType.Information);
                        File.Move(_convertedFile, _destinationFile); // move the file to the destination

                        _convertedFile = _destinationFile; // update on success
                        
                        return true; // home free...
                    }
                    catch
                    {
                        jobLog.WriteEntry(Localise.GetPhrase("Unable to move file") + " " + _convertedFile + " " + Localise.GetPhrase("to") + " " + _conversionOptions.destinationPath + ". Error :" + e.ToString(), Log.LogEntryType.Error);
                        return false;
                    }
                }
                else
                    return false;
            }
        }

        /// <summary>
        /// Rename the file extension
        /// </summary>
        /// <param name="jobLog">JobLog</param>
        private void RenameFileExtension(ref Log jobLog)
        {
            Ini profileIni = new Ini(GlobalDefs.ProfileFile);
            string renameExt = profileIni.ReadString(_conversionOptions.profile, "RenameExt", "");
            
            if (String.IsNullOrEmpty(renameExt)) return;
            if (renameExt[0] != '.') renameExt = "." + renameExt;  // Just in case someone does something dumb like forget the leading "."

            jobLog.WriteEntry(Localise.GetPhrase("Rename file to") + " " + FilePaths.GetFullPathWithoutExtension(_convertedFile) + renameExt.ToLower(), Log.LogEntryType.Information);

            try
            {
                FileIO.TryFileDelete(FilePaths.GetFullPathWithoutExtension(_convertedFile) + renameExt); // First delete incase it exists
                File.Move(_convertedFile, FilePaths.GetFullPathWithoutExtension(_convertedFile) + renameExt);
                _convertedFile = FilePaths.GetFullPathWithoutExtension(_convertedFile) + renameExt;
            }
            catch (Exception e)
            {
                jobLog.WriteEntry(Localise.GetPhrase("Unable to rename file") + " " + _convertedFile + " " + Localise.GetPhrase("to") + " " + FilePaths.GetFullPathWithoutExtension(_convertedFile) + renameExt + ". Error :" + e.ToString(), Log.LogEntryType.Error);
            }
        }

        /// <summary>
        /// Main conversion routine
        /// </summary>
        public void Convert()
        {
            _jobStatus.ErrorMsg = ""; //start clean
            _jobStatus.SuccessfulConversion = false; //no successful conversion yet
            _completed = false;

            Log jobLog = CreateLog(_conversionOptions.sourceVideo);
            if (Log.LogDestination.Null == jobLog.Destination)
                Log.AppLog.WriteEntry(this, "ERROR: Failed to create JobLog for File " + Path.GetFileName(_conversionOptions.sourceVideo), Log.LogEntryType.Error, true);

            //Debug, dump all the conversion parameter before starting to help with debugging
            jobLog.WriteEntry("Starting conversion - DEBUG MESSAGES", Log.LogEntryType.Debug);
            jobLog.WriteEntry("Windows OS Version -> " + Environment.OSVersion.ToString(), Log.LogEntryType.Debug);
            jobLog.WriteEntry("Windows Platform -> " + (MCEBuddy.Globals.GlobalDefs.Is64BitOperatingSystem ? "64 Bit" : "32 Bit"), Log.LogEntryType.Debug);
            jobLog.WriteEntry("MCEBuddy Platform -> " + ((IntPtr.Size == 4) ? "32 Bit" : "64 Bit"), Log.LogEntryType.Debug);
            string currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            jobLog.WriteEntry("MCEBuddy Current Version : " + currentVersion, Log.LogEntryType.Debug);
            jobLog.WriteEntry("MCEBuddy Build Date : " + File.GetLastWriteTime(System.Reflection.Assembly.GetExecutingAssembly().Location).ToString(CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
            jobLog.WriteEntry(MCEBuddyConf.GlobalMCEConfig.GeneralOptions.ToString(), Log.LogEntryType.Debug);
            jobLog.WriteEntry(_conversionOptions.ToString(), Log.LogEntryType.Debug);
            jobLog.WriteEntry("Max Concurrent Jobs -> " + maxConcurrentJobs.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
            jobLog.WriteEntry("Commercial Skip Cut (profile + task) -> " + commercialSkipCut.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
            jobLog.WriteEntry("Pre-Conversion Commercial Remover -> " + preConversionCommercialRemover.ToString(CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
            jobLog.WriteEntry("Ignore Copy Protection -> " + ignoreCopyProtection.ToString(CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
            jobLog.WriteEntry("Free Space Check -> " + spaceCheck.ToString(CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
            jobLog.WriteEntry("Subtitle Cut Segment Incremental Offset -> " + subtitleSegmentOffset.ToString(CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
            jobLog.WriteEntry("Locale Language -> " + Localise.ThreeLetterISO().ToUpper(), Log.LogEntryType.Debug);
            
            try
            {
                RegistryKey installed_versions = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP");
                string[] version_names = installed_versions.GetSubKeyNames();
                //version names start with 'v', eg, 'v3.5' which needs to be trimmed off before conversion
                string Framework = version_names[version_names.Length - 1].Remove(0, 1);
                string SP = (string)installed_versions.OpenSubKey(version_names[version_names.Length - 1]).GetValue("SP", 0).ToString();
                jobLog.WriteEntry(".NET Framework Version -> " + Framework + ", Service Pack -> " + SP, Log.LogEntryType.Debug);
            }
            catch
            {
                jobLog.WriteEntry("Cannot get .NET Framework version from Registry", Log.LogEntryType.Debug);
            }

            try // the entire conversion process, incase of an abort signal
            {
                jobLog.WriteEntry(this, "Current System language is " + CultureInfo.CurrentCulture.DisplayName + " (" + CultureInfo.CurrentCulture.ThreeLetterISOLanguageName.ToString() + ")", Log.LogEntryType.Information);

                if (!_jobStatus.Cancelled)
                {
                    // Download/extract the video metadata first
                    LogStatus(Localise.GetPhrase("Getting show information and banner from Internet sources"), ref jobLog);
                    _metaData = new VideoMetaData(_conversionOptions, ref _jobStatus, jobLog);
                    _metaData.Extract();

                    // Check for meta data match for match filters
                    jobLog.WriteEntry(this, "File " + Path.GetFileName(_conversionOptions.sourceVideo) + " checking for showname filter >" + _conversionOptions.metaShowSelection + "<", Log.LogEntryType.Information);
                    if ((!_jobStatus.Cancelled) && (!String.IsNullOrEmpty(_conversionOptions.metaShowSelection)))
                    {
                        LogStatus(Localise.GetPhrase("Show information matching"), ref jobLog);
                        bool avoidList = false;
                        string wildcardRegex = _conversionOptions.metaShowSelection.ToLower();

                        // Check if this is a void list, i.e. select all files except... (starts with a ~)
                        if (wildcardRegex.Contains("~"))
                        {
                            jobLog.WriteEntry(this, Localise.GetPhrase("Showname match contains an avoid list"), Log.LogEntryType.Information);
                            avoidList = true;
                            wildcardRegex = wildcardRegex.Replace("~", ""); // remove the ~ character from the expression
                        }

                        if (wildcardRegex.Contains("regex:"))
                        {
                            wildcardRegex = wildcardRegex.Replace("regex:", "");
                        }
                        else
                        {
                            wildcardRegex = Util.FilePaths.WildcardToRegex(wildcardRegex);
                        }
                        Regex rxFileMatch = new Regex(wildcardRegex, RegexOptions.IgnoreCase);

                        if (avoidList) // if it's avoiding and we find a match, return false
                        {
                            if ((rxFileMatch.IsMatch(_metaData.MetaData.Title)))
                            {
                                // Don't see any ERROR code since this is not an error but a design to skip processing file in case of META filter mismatch
                                jobLog.WriteEntry(this, Localise.GetPhrase("File") + " " + Path.GetFileName(_conversionOptions.sourceVideo) + " " + Localise.GetPhrase("matched avoid meta data wildcard") + " " + _conversionOptions.metaShowSelection, Log.LogEntryType.Error);
                                Cleanup(ref jobLog);
                                return;
                            }
                        }
                        else
                        {
                            if (!(rxFileMatch.IsMatch(_metaData.MetaData.Title)))
                            {
                                // Don't see any ERROR code since this is not an error but a design to skip processing file in case of META filter mismatch
                                jobLog.WriteEntry(this, Localise.GetPhrase("File") + " " + Path.GetFileName(_conversionOptions.sourceVideo) + " " + Localise.GetPhrase("did not match meta data wildcard") + " " + _conversionOptions.metaShowSelection, Log.LogEntryType.Error);
                                Cleanup(ref jobLog);
                                return;
                            }
                        }
                    }

                    jobLog.WriteEntry(this, "File " + Path.GetFileName(_conversionOptions.sourceVideo) + " checking for network/channel filter >" + _conversionOptions.metaNetworkSelection + "<", Log.LogEntryType.Information);
                    if ((!_jobStatus.Cancelled) && (!String.IsNullOrEmpty(_conversionOptions.metaNetworkSelection)))
                    {
                        LogStatus(Localise.GetPhrase("Channel information matching"), ref jobLog);
                        bool avoidList = false;
                        string wildcardRegex = _conversionOptions.metaNetworkSelection.ToLower();

                        // Check if this is a void list, i.e. select all files except... (starts with a ~)
                        if (wildcardRegex.Contains("~"))
                        {
                            jobLog.WriteEntry(this, Localise.GetPhrase("Channel name match contains an avoid list"), Log.LogEntryType.Information);
                            avoidList = true;
                            wildcardRegex = wildcardRegex.Replace("~", ""); // remove the ~ character from the expression
                        }

                        if (wildcardRegex.Contains("regex:"))
                        {
                            wildcardRegex = wildcardRegex.Replace("regex:", "");
                        }
                        else
                        {
                            wildcardRegex = Util.FilePaths.WildcardToRegex(wildcardRegex);
                        }
                        Regex rxFileMatch = new Regex(wildcardRegex, RegexOptions.IgnoreCase);

                        if (avoidList) // if it's avoiding and we find a match, return false
                        {
                            if ((rxFileMatch.IsMatch(_metaData.MetaData.Network)))
                            {
                                // Don't see any ERROR code since this is not an error but a design to skip processing file in case of META filter mismatch
                                jobLog.WriteEntry(this, Localise.GetPhrase("File") + " " + Path.GetFileName(_conversionOptions.sourceVideo) + " " + Localise.GetPhrase("matched avoid meta data wildcard") + " " + _conversionOptions.metaNetworkSelection, Log.LogEntryType.Error);
                                Cleanup(ref jobLog);
                                return;
                            }
                        }
                        else
                        {
                            if (!(rxFileMatch.IsMatch(_metaData.MetaData.Network)))
                            {
                                // Don't see any ERROR code since this is not an error but a design to skip processing file in case of META filter mismatch
                                jobLog.WriteEntry(this, Localise.GetPhrase("File") + " " + Path.GetFileName(_conversionOptions.sourceVideo) + " " + Localise.GetPhrase("did not match meta data wildcard") + " " + _conversionOptions.metaNetworkSelection, Log.LogEntryType.Error);
                                Cleanup(ref jobLog);
                                return;
                            }
                        }
                    }

                    // Check metadata for Copy Protection
                    if (_metaData.MetaData.CopyProtected && !_conversionOptions.renameOnly) // If it's copy protected fail the conversion here with the right message and status - unless we doing rename only in which there is no conversion
                    {
                        jobLog.WriteEntry(this, Localise.GetPhrase("ERROR: VIDEO IS COPYPROTECTED. CONVERSION MIGHT FAIL"), Log.LogEntryType.Error);
                        if (!ignoreCopyProtection) // If we are asked to ignore copy protection, we will just continue
                        {
                            _jobStatus.ErrorMsg = Localise.GetPhrase("ERROR: VIDEO IS COPYPROTECTED. CONVERSION MIGHT FAIL");
                            Cleanup(ref jobLog);
                            return;
                        }
                    }
                }

                string sourceExt = Path.GetExtension(_conversionOptions.sourceVideo.Trim().ToLower());

                // Create and/or clear the contents of the working folder
                Util.FilePaths.CreateDir(_conversionOptions.workingPath);
                Util.FileIO.ClearFolder(_conversionOptions.workingPath);

                //Check for available disk space
                LogStatus(Localise.GetPhrase("Checking for disk space"), ref jobLog);
                if (!SufficientSpace(jobLog))
                {
                    if (!_jobStatus.Error)
                    {
                        jobLog.WriteEntry(this, Localise.GetPhrase("Insufficient disk space"), Log.LogEntryType.Error);
                        _jobStatus.ErrorMsg = "Insufficient disk space";
                    }
                    Cleanup(ref jobLog); // close the joblog and clean up
                    return;
                }

                // Check for small test files);)
                if (Util.FileIO.FileSize(_conversionOptions.sourceVideo) < 100000000)
                {
                    jobLog.WriteEntry(this, Localise.GetPhrase("This is a small video file less than 100MB. Some actions such as advertisement removal will not occur"), Log.LogEntryType.Warning);
                    _conversionOptions.commercialRemoval = CommercialRemovalOptions.None;
                }

                if (!_jobStatus.Cancelled)
                {
                    // If the SRT File exists, copy it
                    jobLog.WriteEntry(this, Localise.GetPhrase("Checking for SRT File"), Log.LogEntryType.Information);
                    string srtFile = Util.FilePaths.GetFullPathWithoutExtension(OriginalFileName) + ".srt"; // SRT file along with original source video

                    if (File.Exists(srtFile)&& (Util.FileIO.FileSize(srtFile) > 0)) // Exist and non empty
                    {
                        string srtDest = Path.Combine(_conversionOptions.workingPath, Path.GetFileName(srtFile));
                        try
                        {
                            File.Copy(srtFile, srtDest);
                            _saveSRTFile = true;
                            _srtFile = srtFile;
                            jobLog.WriteEntry(this, Localise.GetPhrase("Found existing SRT file and saved it"), Log.LogEntryType.Information);
                        }
                        catch (Exception e)
                        {
                            jobLog.WriteEntry(this, Localise.GetPhrase("Found existing SRT file but unable to save it") + "\r\nError : " + e.ToString(), Log.LogEntryType.Warning);
                        }
                    }

                    //If the EDL file exists, copy it
                    jobLog.WriteEntry(this, Localise.GetPhrase("Checking for EDL File"), Log.LogEntryType.Information);
                    string edlFile = Util.FilePaths.GetFullPathWithoutExtension(OriginalFileName) + ".edl";
                    if (File.Exists(edlFile))
                    {
                        string edlDest = Path.Combine(_conversionOptions.workingPath, Path.GetFileName(edlFile));
                        try
                        {
                            File.Copy(edlFile, edlDest);
                            _saveEDLFile = true;
                            jobLog.WriteEntry(this, Localise.GetPhrase("Found existing EDL file and saved it"), Log.LogEntryType.Information);
                        }
                        catch (Exception e)
                        {
                            jobLog.WriteEntry(this, Localise.GetPhrase("Found existing EDL file but unable to save it") + "\r\nError : " + e.ToString(), Log.LogEntryType.Warning);
                        }
                    }
                }

                if (!_jobStatus.Cancelled)
                {
                    // DO this last before starting the analysis/conversion process after copying SRT, EDL, NFO etc files
                    // If Commerical removal is set for TS files copy them to the temp directory (During Commercial removal, files (except TS) are remuxed later into their own temp working directories)
                    // We need exclusive access to each copy of the file in their respective temp working directories otherwise commercial skipping gets messed up when we have multiple simultaneous tasks for the same file (they all share/overwrite the same EDL file) which casuses a failure
                    // Similarly when trimming is enabled, we need to have a separate copy to avoid overwriting the original
                    // Check if the TS file has any Zero Channel Audio tracks, in which case it needs to be remuxed to remove/compensate for them, remuxing will overwrite the original file if it's a TS file, so make a local copy
                    if (sourceExt == ".ts")
                    {
                        FFmpegMediaInfo ffmpegStreamInfo = new FFmpegMediaInfo(_conversionOptions.sourceVideo, ref _jobStatus, jobLog);
                        ffmpegStreamInfo.Run();
                        if (ffmpegStreamInfo.Success && !ffmpegStreamInfo.ParseError)
                        {
                            if (ffmpegStreamInfo.ZeroChannelAudioTrackCount > 0)
                                zeroTSChannelAudio = true;
                        }
                        else
                        {
                            jobLog.WriteEntry(this, Localise.GetPhrase("Unable to read FFMPEG MediaInfo to verify remuxsupp audio streams"), Log.LogEntryType.Warning);
                        }
                    }

                    // Copy only if we are renaming, has zero channel audio or is a TS file with some conditions met (like commercial removal, trimming etc where the original file may be modified or accessed simultaneously)
                    if (_conversionOptions.renameOnly || zeroTSChannelAudio || ((sourceExt == ".ts") && ((_conversionOptions.commercialRemoval != CommercialRemovalOptions.None && maxConcurrentJobs > 1) || _conversionOptions.startTrim != 0 || _conversionOptions.endTrim != 0)))
                    {
                        string newSource = Path.Combine(_conversionOptions.workingPath, Path.GetFileName(_conversionOptions.sourceVideo));
                        LogStatus(Localise.GetPhrase("Copying source file to working directory"), ref jobLog);
                        try
                        {
                            jobLog.WriteEntry(this, Localise.GetPhrase("Commercial skipping enabled for TS file, copying source video to working directory") + " Source:" + _conversionOptions.sourceVideo + ", Target:" + newSource, Log.LogEntryType.Information);
                            File.Copy(_conversionOptions.sourceVideo, newSource, true); // Copy the file, overwrite if required
                            _conversionOptions.sourceVideo = newSource; // replace the source file that we will work on going forward on success

                        }
                        catch (Exception e)
                        {
                            jobLog.WriteEntry(this, Localise.GetPhrase("Unable to copy source video to working directory") + " Source:" + _conversionOptions.sourceVideo + ", Target:" + newSource + " Error : " + e.ToString(), Log.LogEntryType.Error);
                            _jobStatus.ErrorMsg = Localise.GetPhrase("Unable to copy source video to working directory");
                            Cleanup(ref jobLog);
                            return;
                        }
                    }
                }

                // If we are ONLY renaming, we skip the video processing, removal etc
                if (!_conversionOptions.renameOnly)
                {
                    if (!_jobStatus.Cancelled)
                    {
                        // Remux media center recordings or any video if commercials are being removed to TS (except TS)
                        // Unless TS tracks have zero channel audio in them, then remux them to remove the zero audio channel tracks, otherwise future functions fails (like trim) - NOTE while 0 TS channel audio is remuxed, it ends up replacing the original file
                        if ((sourceExt != ".ts") || zeroTSChannelAudio)
                        {
                            LogStatus(Localise.GetPhrase("Remuxing recording"), ref jobLog);
                            RemuxMediaCenter.RemuxMCERecording remux = new RemuxMCERecording(_conversionOptions, ref _jobStatus, jobLog);
                            bool res = remux.Remux();
                            jobLog.WriteEntry(this, Localise.GetPhrase("Remuxing: Percentage Complete") + " " + _jobStatus.PercentageComplete.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
                            if (!res)
                            {
                                // Remux failed
                                _jobStatus.ErrorMsg = Localise.GetPhrase("Remux failed");
                                jobLog.WriteEntry(this, _jobStatus.ErrorMsg, Log.LogEntryType.Error);
                                Cleanup(ref jobLog);
                                return;
                            }
                            _remuxedVideo = remux.RemuxedFile;
                            jobLog.WriteEntry(this, "Remuxed video file : " + _remuxedVideo.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
                            jobLog.WriteEntry(this, Localise.GetPhrase("Finished Remuxing, file size [KB]") + " " + (Util.FileIO.FileSize(_remuxedVideo) / 1024).ToString("N", System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
                        }
                    }

                    // If we are trimming the video, lets do it here (for TS video; DVR-MS and WTV -> remuxed to .TS)
                    // Trimming for non TS/WTV/DVRMS files is done during the conversion itself
                    if (!_jobStatus.Cancelled)
                    {
                        if (Path.GetExtension(SourceVideo.ToLower()) == ".ts")
                        {
                            LogStatus(Localise.GetPhrase("Trimming video recording"), ref jobLog);
                            TrimVideo trimVideo = new TrimVideo(_conversionOptions.profile, ref _jobStatus, jobLog);
                            if (!trimVideo.Trim(SourceVideo, _conversionOptions.startTrim, _conversionOptions.endTrim))
                            {
                                // Trimming failed
                                _jobStatus.ErrorMsg = Localise.GetPhrase("Trimming failed");
                                jobLog.WriteEntry(this, _jobStatus.ErrorMsg, Log.LogEntryType.Error);
                                Cleanup(ref jobLog);
                                return;
                            }
                        }
                    }

                    // Get the Video duration
                    FFmpegMediaInfo ffmpegStreamInfo = new FFmpegMediaInfo(SourceVideo, ref _jobStatus, jobLog);
                    ffmpegStreamInfo.Run();
                    if (!ffmpegStreamInfo.Success || ffmpegStreamInfo.ParseError)
                    {
                        // Getting Video Duration failed
                        _jobStatus.ErrorMsg = Localise.GetPhrase("Unable to get Video Duration");
                        jobLog.WriteEntry(this, _jobStatus.ErrorMsg, Log.LogEntryType.Error);
                        Cleanup(ref jobLog);
                        return;
                    }

                    float duration = ffmpegStreamInfo.MediaInfo.VideoInfo.Duration;

                    // If we are using ShowAnalyzer, do it here
                    if (!_jobStatus.Cancelled)
                    {
                        jobLog.WriteEntry(this, "Checking for ShowAnalyzer", Log.LogEntryType.Information);
                        if (_conversionOptions.commercialRemoval == CommercialRemovalOptions.ShowAnalyzer)
                        {
                            LogStatus(Localise.GetPhrase("ShowAnalyzer Advertisement scan"), ref jobLog);
                            _commercialScan = new Scanner(_conversionOptions, SourceVideo, true, duration, ref _jobStatus, jobLog);
                            if (!_commercialScan.Scan())
                            {
                                _jobStatus.ErrorMsg = Localise.GetPhrase("ShowAnalyzer failed");
                                jobLog.WriteEntry(this, _jobStatus.ErrorMsg, Log.LogEntryType.Error);
                                Cleanup(ref jobLog);
                                return;
                            }
                        }
                    }

                    // If we are using comskip, do it here
                    if (!_jobStatus.Cancelled)
                    {
                        jobLog.WriteEntry(this, "Checking for Comskip", Log.LogEntryType.Information);
                        if (_conversionOptions.commercialRemoval == CommercialRemovalOptions.Comskip)
                        {
                            LogStatus(Localise.GetPhrase("Comskip Advertisement scan"), ref jobLog);
                            _commercialScan = new Scanner(_conversionOptions, SourceVideo, false, duration, ref _jobStatus, jobLog);
                            if (!_commercialScan.Scan())
                            {
                                _jobStatus.ErrorMsg = "Comskip failed";
                                jobLog.WriteEntry(this, (_jobStatus.ErrorMsg), Log.LogEntryType.Error);
                                Cleanup(ref jobLog);
                                return;
                            }
                        }
                    }

                    // Get the video properties
                    if (!_jobStatus.Cancelled)
                    {
                        // Create the Video File object and get the container + stream information
                        LogStatus(Localise.GetPhrase("Analyzing video information"), ref jobLog);

                        _videoFile = new VideoInfo(_conversionOptions.profile, _conversionOptions.sourceVideo, _remuxedVideo, (_commercialScan != null ? _commercialScan.EDLFile : ""), _conversionOptions.audioLanguage, ref _jobStatus, jobLog); // If we have scanned for commercial pass along the EDL file to speed up the crop detect

                        if (_videoFile.Error)
                        {
                            _jobStatus.ErrorMsg = "Analyzing video information failed";
                            jobLog.WriteEntry(this, (_jobStatus.ErrorMsg), Log.LogEntryType.Error);
                            Cleanup(ref jobLog);
                            return;
                        }
                    }

                    // We extract the closed captions after the commerical scanning, trimming and video info is complete
                    // We check if the SRT file alread exists and needs to be adjusted for EDL commercials
                    // If not we check if commerical removal is enabled, if so we need to create a temp file which we will cut to compensate for EDL and THEN extract the CC (this helps keep the audio in sync with the CC due to cutting on non KeyFrames issues)
                    // If no commercial removal we just extract the CC from the remuxed file (TS)
                    if (!_jobStatus.Cancelled)
                    {
                        if ((!String.IsNullOrEmpty(_conversionOptions.extractCC)) && (Path.GetExtension(SourceVideo.ToLower()) == ".ts"))
                        {
                            LogStatus(Localise.GetPhrase("Extracting Closed Captions"), ref jobLog);

                            // Setup closed captions for extraction
                            cc = new ClosedCaptions(_conversionOptions.profile, ref _jobStatus, jobLog);

                            if (_saveSRTFile) // We already have a SRT file to work with
                            {
                                jobLog.WriteEntry(this, "Found saved SRT file -> " + _srtFile, Log.LogEntryType.Debug);

                                if ((_commercialScan != null) && (!commercialSkipCut)) // Incase we asked not to cut the video, just create the EDL file, let us not cut the SRT files also
                                {
                                    if (_commercialScan.CommercialsFound) // We just adjust the SRT file with the EDL file
                                    {
                                        LogStatus(Localise.GetPhrase("Trimming Closed Captions"), ref jobLog);
                                        if (!cc.EDLTrim(_commercialScan.EDLFile, _srtFile, _conversionOptions.ccOffset, subtitleSegmentOffset))
                                        {
                                            // Trimming CC failed
                                            _jobStatus.ErrorMsg = Localise.GetPhrase("Trimming closed captions failed");
                                            jobLog.WriteEntry(this, _jobStatus.ErrorMsg, Log.LogEntryType.Error);
                                            Cleanup(ref jobLog);
                                            return;
                                        }
                                    }
                                }
                            }
                            else // We need to create a SRT file
                            {
                                string tempCCFile = SourceVideo; // If there are no commercials to remove, we can work directly on the remuxed file

                                // DONT NEED TO ACTUALLY CUT THE VIDEO, RATHER JUST EXTRACT AND ADJUST, GIVES GREATER DEGREE OF CONTROL ON SYNC ISSUES WHEN THE ACTUAL VIDEO IS CUT
                                /*if ((_commercialScan != null) && (!commercialSkipCut)) // If we are not asked to skip cutting commercials (we don't need to adjust CC), for commerical cutting we need to create a special EDL adjusted file before extracting of CC
                                {
                                    jobLog.WriteEntry(this, "Checking if commercials were found", Log.LogEntryType.Information);
                                    if (_commercialScan.CommercialsFound) // If there are commericals we need to create a special EDL cut file to work with before extracting CC
                                    {
                                        // Copy the remuxed file to a temp file which we will then cut using the EDL file and then extract the closed captions
                                        // This helps keep the closed captions in sync with the cut video
                                        tempCCFile = Util.FilePaths.GetFullPathWithoutExtension(SourceVideo) + "-CCTemp" + Path.GetExtension(SourceVideo);
                                        try
                                        {
                                            // Create a temp file for CC EDL cutting and extracting
                                            jobLog.WriteEntry(this, "Creating temporary remuxed file for extracting CC and adjusting for commercials", Log.LogEntryType.Debug);
                                            File.Copy(SourceVideo, tempCCFile);
                                        }
                                        catch (Exception e)
                                        {
                                            // Creating temp CC file failed
                                            _jobStatus.ErrorMsg = Localise.GetPhrase("Creating temporary file for CC extracting file failed");
                                            jobLog.WriteEntry(this, _jobStatus.ErrorMsg + "\r\nError : " + e.ToString(), Log.LogEntryType.Error);
                                            Cleanup(ref jobLog);
                                            return;
                                        }

                                        // Now adjust the file for commercial removal
                                        jobLog.WriteEntry(this, "Removing commercials from temp file before extracting CC", Log.LogEntryType.Debug);
                                        Remover edlCCAdjust = new Remover(_conversionOptions.profile, tempCCFile, _commercialScan.EDLFile, ref _videoFile, ref _jobStatus, jobLog); // Pass the original or remuxed video here
                                        edlCCAdjust.StripCommercials();
                                        _videoFile.AdsRemoved = false; // Reset it, since we didn't really remove the ad's, just on temp file for extracting CC's
                                        if (_jobStatus.PercentageComplete == 0) // for Commercial Stripping failure, this numbers is set to 0
                                        {
                                            // Adjusting EDL CC failed
                                            _jobStatus.ErrorMsg = Localise.GetPhrase("Removing commercials from temp file for CC extracting failed");
                                            jobLog.WriteEntry(this, _jobStatus.ErrorMsg, Log.LogEntryType.Error);
                                            Cleanup(ref jobLog);
                                            return;
                                        }
                                    }
                                }*/

                                // Trimming is already complete so we just need to extract 
                                LogStatus(Localise.GetPhrase("Extracting Closed Captions"), ref jobLog);
                                bool ret = cc.Extract(tempCCFile, _conversionOptions.workingPath, _conversionOptions.extractCC, 0, 0, _conversionOptions.ccOffset);
                                if (!ret)
                                {
                                    // Extracting CC failed
                                    _jobStatus.ErrorMsg = Localise.GetPhrase("Extracting closed captions failed");
                                    jobLog.WriteEntry(this, _jobStatus.ErrorMsg, Log.LogEntryType.Error);
                                    Cleanup(ref jobLog);
                                    return;
                                }

                                if (!MCEBuddy.Globals.GlobalDefs.IsNullOrWhiteSpace(cc.SRTFile)) // If we have a valid SRT file
                                {
                                    if ((_commercialScan != null) && (!commercialSkipCut)) // Incase we asked not to cut the video, just create the EDL file, let us not cut the SRT files also
                                    {
                                        if (_commercialScan.CommercialsFound) // We just adjust the SRT file with the EDL file
                                        {
                                            LogStatus(Localise.GetPhrase("Trimming Closed Captions"), ref jobLog);
                                            if (!cc.EDLTrim(_commercialScan.EDLFile, cc.SRTFile, 0, subtitleSegmentOffset)) // Offset is already compensated for while extraction
                                            {
                                                // Trimming CC failed
                                                _jobStatus.ErrorMsg = Localise.GetPhrase("Trimming closed captions failed");
                                                jobLog.WriteEntry(this, _jobStatus.ErrorMsg, Log.LogEntryType.Error);
                                                Cleanup(ref jobLog);
                                                return;
                                            }
                                        }
                                    }

                                    if (tempCCFile != SourceVideo) // If we created a temp file, lets get rid of it now and also need to remove the SRT file
                                    {
                                        try
                                        {
                                            jobLog.WriteEntry(this, "Renaming SRT file to " + Util.FilePaths.GetFullPathWithoutExtension(SourceVideo) + ".srt", Log.LogEntryType.Debug);
                                            File.Move(cc.SRTFile, Util.FilePaths.GetFullPathWithoutExtension(SourceVideo) + ".srt");
                                        }
                                        catch (Exception e)
                                        {
                                            // Renaming SRT file
                                            _jobStatus.ErrorMsg = Localise.GetPhrase("Error renaming SRT file after extracting");
                                            jobLog.WriteEntry(this, _jobStatus.ErrorMsg + "\r\nError : " + e.ToString(), Log.LogEntryType.Error);
                                            Cleanup(ref jobLog);
                                            return;
                                        }

                                        Util.FileIO.TryFileDelete(tempCCFile);
                                    }

                                    _srtFile = Util.FilePaths.GetFullPathWithoutExtension(SourceVideo) + ".srt";
                                    jobLog.WriteEntry(this, "SRT file -> " + _srtFile, Log.LogEntryType.Debug);
                                }
                            }
                        }
                    }

                    // Remove the commercials incase they aren't supported format for after conversion for commercial removal
                    if (!_jobStatus.Cancelled)
                    {
                        if (_commercialScan != null)
                        {
                            // Check if the final conversion extension has a format that's supported by the commercial remover, else remove the commercials here itself during the TS file stage
                            // Check if the profile dicates to remove commercials before the actual conversion
                            if (preConversionCommercialRemover || (!Remover.IsSupportedExtension(Transcode.Convert.GetConversionExtension(_conversionOptions), _conversionOptions.profile)))
                            {
                                jobLog.WriteEntry(this, "Final format is not a supported format for removing commercials, PRE-Removing commercials for Ext -> " + Transcode.Convert.GetConversionExtension(_conversionOptions), Log.LogEntryType.Information);
                                jobLog.WriteEntry(this, "Checking if commercials were found", Log.LogEntryType.Information);
                                if ((_commercialScan.CommercialsFound) && (!_videoFile.AdsRemoved)) //commercials might be stripped
                                {
                                    if (!commercialSkipCut)
                                    {
                                        LogStatus(Localise.GetPhrase("Removing commercials"), ref jobLog);
                                        _commercialRemover = new Remover(_conversionOptions.profile, SourceVideo, _commercialScan.EDLFile, ref _videoFile, ref _jobStatus, jobLog);
                                        _commercialRemover.StripCommercials(true); // we need to select the language while stripping the TS file else we lose the language information

                                        //We dont' check for % completion here since some files are very short and % isn't reliable
                                        if (_videoFile.AdsRemoved) // for Commercial Stripping success, this is true
                                        {
                                            jobLog.WriteEntry(this, Localise.GetPhrase("Finished removing commercials, file size [KB]") + " " + (Util.FileIO.FileSize(SourceVideo) / 1024).ToString("N", System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);

                                            // After removing commercials it's possible that the Audio/Video streams properties have changed - this is due to MEncoder and cutting TS, it only keeps 1 audio stream, so rescan
                                            // Create the Video File object and get the container + stream information
                                            jobLog.WriteEntry(this, "ReAnalyzing video information post commercial removal before video conversion", Log.LogEntryType.Information);
                                            LogStatus(Localise.GetPhrase("Analyzing video information"), ref jobLog);

                                            // While updating we don't need to pass EDL file anymore since the ad's have been removed
                                            _videoFile.UpdateVideoInfo(_conversionOptions.profile, _conversionOptions.sourceVideo, _remuxedVideo, "", _conversionOptions.audioLanguage, ref _jobStatus, jobLog);

                                            if (_videoFile.Error)
                                            {
                                                _jobStatus.ErrorMsg = "Analyzing video information failed";
                                                jobLog.WriteEntry(this, (_jobStatus.ErrorMsg), Log.LogEntryType.Error);
                                                Cleanup(ref jobLog);
                                                return;
                                            }
                                        }
                                        else
                                            jobLog.WriteEntry(this, "Not able to remove commercials, will try again after conversion using unsupported format cutter", Log.LogEntryType.Warning);
                                    }
                                    else
                                        jobLog.WriteEntry(this, Localise.GetPhrase("Skipping commercial cutting, preserving EDL file"), Log.LogEntryType.Information);
                                }
                                else
                                    jobLog.WriteEntry(this, Localise.GetPhrase("Commercials not found or cutting already completed"), Log.LogEntryType.Information);
                            }
                        }
                    }

                    // Convert the video
                    if (!_jobStatus.Cancelled)
                    {
                        // Convert the file
                        Transcode.Convert conv = new Transcode.Convert(ref _jobStatus, jobLog);
                        LogStatus(Localise.GetPhrase("Converting"), ref jobLog);
                        bool res = conv.Run(_conversionOptions, ref _videoFile, ref _commercialScan); // if we're using MEncoder, then we will complete the commercial stripping here itself
                        if (!res)
                        {
                            _jobStatus.ErrorMsg = Localise.GetPhrase("Conversion failed");
                            jobLog.WriteEntry(this, (_jobStatus.ErrorMsg), Log.LogEntryType.Error);
                            // Conversion failed
                            Cleanup(ref jobLog);
                            return;
                        }
                        _convertedFile = conv.ConvertedFile;
                        jobLog.WriteEntry(this, "Converted File : " + _convertedFile, Log.LogEntryType.Information);
                        jobLog.WriteEntry(this, Localise.GetPhrase("Finished conversion, file size [KB]") + " " + (Util.FileIO.FileSize(_convertedFile) / 1024).ToString("N", System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
                    }

                    // Remove the commercials incase they weren't removed earlier
                    if (!_jobStatus.Cancelled)
                    {
                        if (_commercialScan != null)
                        {
                            jobLog.WriteEntry(this, "Checking if commercials were found", Log.LogEntryType.Information);
                            if ((_commercialScan.CommercialsFound) && (!_videoFile.AdsRemoved)) //commercials might be stripped during conversion or before conversion
                            {
                                if (!commercialSkipCut)
                                {
                                    LogStatus(Localise.GetPhrase("Removing commercials"), ref jobLog);
                                    _commercialRemover = new Remover(_conversionOptions.profile, _convertedFile, _commercialScan.EDLFile, ref _videoFile, ref _jobStatus, jobLog);
                                    _commercialRemover.StripCommercials();

                                    //We dont' check for % completion here since some files are very short and % isn't reliable
                                    if (_jobStatus.PercentageComplete == 0) // for Commercial Stripping failure, this numbers is set to 0
                                    {
                                        _jobStatus.ErrorMsg = Localise.GetPhrase("Removing commercials failed");
                                        jobLog.WriteEntry(this, (_jobStatus.ErrorMsg), Log.LogEntryType.Error);
                                        Cleanup(ref jobLog);
                                        return;
                                    }
                                    jobLog.WriteEntry(this, Localise.GetPhrase("Finished removing commercials, file size [KB]") + " " + (Util.FileIO.FileSize(_convertedFile) / 1024).ToString("N", System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
                                }
                                else
                                    jobLog.WriteEntry(this, Localise.GetPhrase("Skipping commercial cutting, preserving EDL file"), Log.LogEntryType.Information);
                            }
                            else
                                jobLog.WriteEntry(this, Localise.GetPhrase("Commercials not found or cutting already completed"), Log.LogEntryType.Information);
                        }
                    }

                    // Add the subtitles to the container if possible
                    if (!_jobStatus.Cancelled)
                    {
                        string chapFile = ""; // Nero Chapter file
                        string xmlChapFile = ""; // iTunes Chapter file

                        // Convert the EDL file to Chapters and store only if skip commercial cutting is set
                        if ((_commercialScan != null) && commercialSkipCut)
                        {
                            EDL edl = new EDL(_conversionOptions.profile, _convertedFile, _videoFile.Duration, _commercialScan.EDLFile, ref _jobStatus, jobLog);
                            if (edl.ConvertEDLToChapters())
                            {
                                chapFile = edl.CHAPFile; // Get the Nero chapter file
                                xmlChapFile = edl.XMLCHAPFile; // Get the iTunes chapter file
                            }
                        }

                        LogStatus(Localise.GetPhrase("Adding subtitles and chapters to file"), ref jobLog);
                        if (!_metaData.AddSubtitlesAndChaptersToFile(_srtFile, chapFile, xmlChapFile, _convertedFile))
                        {
                            _jobStatus.ErrorMsg = Localise.GetPhrase("Adding subtitles and chapters failed");
                            jobLog.WriteEntry(this, (_jobStatus.ErrorMsg), Log.LogEntryType.Error);
                            Cleanup(ref jobLog);
                            return;
                        }
                    }

                    // This point onward there is not ETA or % so set ETA to working
                    _jobStatus.ETA = "Working...";

                    // Write the meta data
                    if (!_jobStatus.Cancelled)
                    {
                        LogStatus(Localise.GetPhrase("Writing show information"), ref jobLog);
                        _metaData.WriteTags(_convertedFile); // we can ignore failure of writing meta data, not critical
                        jobLog.WriteEntry(this, Localise.GetPhrase("Finished writing tags, file size [KB]") + " " + (Util.FileIO.FileSize(_convertedFile) / 1024).ToString("N", System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
                    }
                }
                else // If we are ONLY RENAMING, then we are working direcly on the orignal file
                {
                    // Get the video properties
                    if (!_jobStatus.Cancelled)
                    {
                        // Create the Video File object and get the container + stream information
                        LogStatus(Localise.GetPhrase("Analyzing video information"), ref jobLog);

                        _videoFile = new VideoInfo("", _conversionOptions.sourceVideo, "", "", "", ref _jobStatus, jobLog); // No work done here just get basic properties

                        // Dont' need to worrk about errors/failues here since this is only used if we are extracting MC information
                    }

                    _jobStatus.ETA = "Working...";
                    _convertedFile = _conversionOptions.sourceVideo; // We are working directly on the source file here (copied)
                }

                // Create the XML file with Source video information for WTV and DVRMS file (XBMC compliant NFO file, http://wiki.xbmc.org/index.php?title=Import_-_Export_Library)
                if (_conversionOptions.extractXML && ((Path.GetExtension(OriginalFileName).ToLower() == ".wtv") || (Path.GetExtension(OriginalFileName).ToLower() == ".dvr-ms")))
                {
                    _metaData.WriteXBMCXMLTags(OriginalFileName, _conversionOptions.workingPath, _videoFile);
                }

                // Run a custom command from the user if configured
                if (!_jobStatus.Cancelled)
                {
                    LogStatus(Localise.GetPhrase("Running Custom Commands"), ref jobLog);

                    CustomCommand customCommand = new CustomCommand(_conversionOptions.profile, _convertedFile, _originalFileName, _remuxedVideo, (_commercialScan == null ? "" : _commercialScan.EDLFile), _metaData.MetaData, ref _jobStatus, jobLog);
                    if (!customCommand.Run())
                    {
                        jobLog.WriteEntry(this, Localise.GetPhrase("Custom command failed to run, critical failure"), Log.LogEntryType.Error);
                        _jobStatus.ErrorMsg = Localise.GetPhrase("Custom command failed to run, critical failure");
                        Cleanup(ref jobLog);
                        return; // serious problem
                    }

                    // Check if the converted file has been tampered with
                    if (!File.Exists(_convertedFile))
                    {
                        jobLog.WriteEntry(this, Localise.GetPhrase("Converted file has been renamed or deleted by custom command") + " -> " + _convertedFile, Log.LogEntryType.Error);
                        _jobStatus.ErrorMsg = Localise.GetPhrase("Converted file has been renamed or deleted by custom command");
                        Cleanup(ref jobLog);
                        return; // serious problem
                    }
                    else
                        jobLog.WriteEntry(this, Localise.GetPhrase("Finished custom command, file size [KB]") + " " + (Util.FileIO.FileSize(_convertedFile) / 1024).ToString("N", System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
                }

                // Rename and move the file based upon meta data
                if (!_jobStatus.Cancelled)
                {
                    // Before renaming get the MediaInformation once to dump into the Log File for debugging purposes.
                    FFmpegMediaInfo ffmpegStreamInfo = new FFmpegMediaInfo(_convertedFile, ref _jobStatus, jobLog);
                    ffmpegStreamInfo.Run();

                    LogStatus(Localise.GetPhrase("Renaming file using show information"), ref jobLog);
                    if (!RenameByMetaData(ref jobLog))
                    {
                        _jobStatus.ErrorMsg = Localise.GetPhrase("Renaming and moving file by Metadata failed"); // don't set this unless you want to indicate failure up the chain and kill the conversion process
                        jobLog.WriteEntry(this, (_jobStatus.ErrorMsg), Log.LogEntryType.Error);
                        Cleanup(ref jobLog);
                        return;
                    }
                }

                // Move the remaining files file
                if (!_jobStatus.Cancelled)
                {
                    // XML FILE (generated by Comskip or any other program)
                    string xmlFile = Path.Combine(_conversionOptions.workingPath, (Path.GetFileNameWithoutExtension(OriginalFileName) + ".xml")); // XML file created by 3rd Party in temp working directory
                    try
                    {
                        if (File.Exists(xmlFile))
                        {
                            if (xmlFile != (Util.FilePaths.GetFullPathWithoutExtension(_convertedFile) + ".xml")) // Don't delete if they are the same file, e.g. TS to TS in same directory
                            {
                                jobLog.WriteEntry(this, Localise.GetPhrase("Found XML file, moving to destination") + " XML:" + xmlFile + " Destination:" + Path.GetDirectoryName(_convertedFile), Log.LogEntryType.Information);
                                FileIO.TryFileDelete((Util.FilePaths.GetFullPathWithoutExtension(_convertedFile) + ".xml"));
                                File.Move(xmlFile, (Util.FilePaths.GetFullPathWithoutExtension(_convertedFile) + ".xml")); //rename to match destination file
                            }
                        }
                    }
                    catch (Exception e) // Not critial to be unable to move SRT File
                    {
                        jobLog.WriteEntry(this, Localise.GetPhrase("Unable to move XML file to destination") + " XML:" + xmlFile + " Destination:" + Path.GetDirectoryName(_convertedFile) + " Error -> " + e.ToString(), Log.LogEntryType.Warning);
                    }

                    // SRT FILE
                    try
                    {
                        if (File.Exists(_srtFile))
                        {
                            if (_srtFile != (Util.FilePaths.GetFullPathWithoutExtension(_convertedFile) + ".srt")) // Don't delete if they are the same file, e.g. TS to TS in same directory
                            {
                                jobLog.WriteEntry(this, Localise.GetPhrase("Found SRT file, moving to destination") + " SRT:" + _srtFile + " Destination:" + Path.GetDirectoryName(_convertedFile), Log.LogEntryType.Information);
                                FileIO.TryFileDelete((Util.FilePaths.GetFullPathWithoutExtension(_convertedFile) + ".srt"));
                                File.Move(_srtFile, (Util.FilePaths.GetFullPathWithoutExtension(_convertedFile) + ".srt")); //rename to match destination file
                            }
                        }
                    }
                    catch (Exception e) // Not critial to be unable to move SRT File
                    {
                        jobLog.WriteEntry(this, Localise.GetPhrase("Unable to move SRT file to destination") + " SRT:" + _srtFile + " Destination:" + Path.GetDirectoryName(_convertedFile) + " Error -> " + e.ToString(), Log.LogEntryType.Warning);
                    }

                    // EDL FILE
                    if (_commercialScan != null)
                    {
                        try
                        {
                            if (File.Exists(_commercialScan.EDLFile) && commercialSkipCut) // if we are asked to keep EDL file, we copy it out to output
                            {
                                if (_commercialScan.EDLFile != (Util.FilePaths.GetFullPathWithoutExtension(_convertedFile) + ".edl")) // don't delete the same file, e.g. TS to TS in same directory
                                {
                                    jobLog.WriteEntry(this, Localise.GetPhrase("Found EDL file, request to move to destination") + " EDL:" + _commercialScan.EDLFile + " Destination:" + Path.GetDirectoryName(_convertedFile), Log.LogEntryType.Information);
                                    FileIO.TryFileDelete((Util.FilePaths.GetFullPathWithoutExtension(_convertedFile) + ".edl"));
                                    File.Move(_commercialScan.EDLFile, (Util.FilePaths.GetFullPathWithoutExtension(_convertedFile) + ".edl"));
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            jobLog.WriteEntry(this, Localise.GetPhrase("Unable to move EDL file to destination") + " EDL:" + _commercialScan.EDLFile + " Destination:" + Path.GetDirectoryName(_convertedFile) + " Error -> " + e.ToString(), Log.LogEntryType.Warning);
                        }
                    }
                    else if (_saveEDLFile) // no commercial scan but we still found an EDL file with the source, we copy it to the output
                    {
                        string edlFile = Path.Combine(_conversionOptions.workingPath, (Path.GetFileNameWithoutExtension(OriginalFileName) + ".edl")); // Saved EDL file
                        try
                        {
                            if (File.Exists(edlFile))
                            {
                                if (edlFile != (Util.FilePaths.GetFullPathWithoutExtension(_convertedFile) + ".edl")) // Don't delete if they are the same file, e.g. TS to TS in same directory
                                {
                                    jobLog.WriteEntry(this, Localise.GetPhrase("Found EDL file, moving to destination") + " EDL:" + edlFile + " Destination:" + Path.GetDirectoryName(_convertedFile), Log.LogEntryType.Information);
                                    FileIO.TryFileDelete((Util.FilePaths.GetFullPathWithoutExtension(_convertedFile) + ".edl"));
                                    File.Move(edlFile, (Util.FilePaths.GetFullPathWithoutExtension(_convertedFile) + ".edl")); //rename to match destination file
                                }
                            }
                        }
                        catch (Exception e) // Not critial to be unable to move EDL File
                        {
                            jobLog.WriteEntry(this, Localise.GetPhrase("Unable to move EDL file to destination") + " EDL:" + edlFile + " Destination:" + Path.GetDirectoryName(_convertedFile) + " Error -> " + e.ToString(), Log.LogEntryType.Warning);
                        }
                    }

                    // NFO FILE
                    string nfoFile = Path.Combine(_conversionOptions.workingPath, Path.GetFileNameWithoutExtension(OriginalFileName) + ".nfo"); // Path\FileName.nfo
                    try
                    {
                        if (File.Exists(nfoFile))
                        {
                            if (nfoFile != (Util.FilePaths.GetFullPathWithoutExtension(_convertedFile) + ".nfo")) // Don't delete if they are the same file, e.g. TS to TS in same directory
                            {
                                jobLog.WriteEntry(this, Localise.GetPhrase("Found NFO file, moving to destination") + " NFO:" + nfoFile + " Destination:" + Path.GetDirectoryName(_convertedFile), Log.LogEntryType.Information);
                                FileIO.TryFileDelete((Util.FilePaths.GetFullPathWithoutExtension(_convertedFile) + ".nfo"));
                                File.Move(nfoFile, (Util.FilePaths.GetFullPathWithoutExtension(_convertedFile) + ".nfo")); //rename to match destination file
                            }
                        }
                    }
                    catch (Exception e) // Not critial to be unable to move NFO File
                    {
                        jobLog.WriteEntry(this, Localise.GetPhrase("Unable to move NFO file to destination") + " NFO:" + _srtFile + " Destination:" + Path.GetDirectoryName(_convertedFile) + " Error -> " + e.ToString(), Log.LogEntryType.Warning);
                    }

                    _jobStatus.ErrorMsg = ""; // all done, we are in the clear, success
                    _jobStatus.SuccessfulConversion = true; //now the original file can be deleted if required
                    LogStatus(Localise.GetPhrase("Success - All done!"), ref jobLog);
                }

                if (_conversionOptions.renameOnly) // If we are only renaming, we don't need original saved files as they will be copied if present
                    _saveEDLFile = _saveSRTFile = false; // Clean it up

                Cleanup(ref jobLog); // all done, clean it up, close jobLog and mark it inactive
            }
            catch(ThreadAbortException)
            {
                //incase the conversion thread is stopped due to the GUI stopping/cancelling, then release the logfile locks
                _jobStatus.ErrorMsg = "Error during conversion, conversion cancelled";
                jobLog.WriteEntry(this, (_jobStatus.ErrorMsg), Log.LogEntryType.Error);
                Cleanup(ref jobLog);
            }
        }
    }
}
