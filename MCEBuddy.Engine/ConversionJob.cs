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

namespace MCEBuddy.Engine
{
    public class ConversionJob
    {
        private const int MAX_SHUTDOWN_WAIT = 5000;
        private const double SPACE_REQUIRED_MULTIPLIER = 1.5; // Ideally this should be 2, 1 for the TS file and 1 incase of NoRecode which doesn't compress, but most folks who use multiple conversions are converting to mp4 so 1.5 is more than enough.

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
        private bool _fixCorruptedRemux = false; // Had remux fixed a corrupted video file
        private bool _saveEDLFile = false;
        private bool _saveSRTFile = false;
        private bool spaceCheck = true;

        private bool commercialSkipCut = false; // do commercial scan keep EDL file but skip cutting the commercials
        private int maxConcurrentJobs = 1;

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

            // Read various profile parameters
            Ini configProfileIni = new Ini(GlobalDefs.ProfileFile);
            commercialSkipCut = (conversionJobOptions.commercialSkipCut || (configProfileIni.ReadBoolean(_conversionOptions.profile, "CommercialSkipCut", false))); // 1 of 2 places that commercial skipcut can be defined (in the profile or in the conversion task settings)
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

        private bool SufficientSpace( Log jobLog)
        {
            if (!_jobStatus.Cancelled)
            {
                double videoFileSpace = 0;
                long fiSize = -1;
                long workingSpace = -1;
                long destinationSpace = -1;

                try
                {
                    FileInfo fi = new FileInfo(_conversionOptions.sourceVideo);
                    fiSize = fi.Length;
                    videoFileSpace = fiSize * SPACE_REQUIRED_MULTIPLIER * maxConcurrentJobs; // for each simultaneous conversion we need enough free space
                    workingSpace = Util.FileIO.GetFreeDiskSpace(_conversionOptions.workingPath);
                    destinationSpace = Util.FileIO.GetFreeDiskSpace(_conversionOptions.destinationPath);
                }
                catch (Exception)
                {
                    string errMsg = Localise.GetPhrase("Error: Unable to obtain available disk space");
                    jobLog.WriteEntry(this, errMsg, Log.LogEntryType.Warning);
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

                if (destinationSpace < videoFileSpace/SPACE_REQUIRED_MULTIPLIER) // For destination we just need to check enough space for each conversion
                {
                    string errorMsg = Localise.GetPhrase("Insufficient destination disk space avalable in") + " " + _conversionOptions.destinationPath + ". " +
                                            destinationSpace.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + Localise.GetPhrase("available, required") + " " +
                                            (videoFileSpace/SPACE_REQUIRED_MULTIPLIER).ToString(System.Globalization.CultureInfo.InvariantCulture);
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

        private void Cleanup( ref Log jobLog)
        {
            // These files may outside the temp working directory if the source wasn't copied - only we if created them (i.e. commercial scanning done)
            if (_commercialScan != null)
            {
                Util.FileIO.TryFileDelete(Util.FilePaths.GetFullPathWithoutExtension(OriginalFileName) + ".log"); // Delete the .log file created by Comskip
                Util.FileIO.TryFileDelete(Util.FilePaths.GetFullPathWithoutExtension(OriginalFileName) + ".logo.txt"); // Delete the .logo.txt file created by Comskip
                if (!_saveEDLFile)
                    Util.FileIO.TryFileDelete(Util.FilePaths.GetFullPathWithoutExtension(OriginalFileName) + ".edl"); // Delete the edl file - check if it existed
                Util.FileIO.TryFileDelete(Util.FilePaths.GetFullPathWithoutExtension(OriginalFileName) + ".txt"); // Delete the txt file created by Comskip
                Util.FileIO.TryFileDelete(Util.FilePaths.GetFullPathWithoutExtension(OriginalFileName) + ".edlp"); // Delete the edlp file created by Comskip
            }

            Util.FileIO.ClearFolder(_conversionOptions.workingPath); // Clean up the temp working directory
            
            LogStatus("", ref jobLog); // clear the output on the screen
            _jobStatus.PercentageComplete = 0; //Reset the counter
            _jobStatus.ETA = "";

            jobLog.Close();
            _completed = true; // mark it completed only after an exit to indicate thread is finished
            _active = false; // mark active false only after completed true to avoid race condition with monitor thread
        }

        private void LogStatus( string activity, ref Log jobLog)
        {
            _jobStatus.CurrentAction = activity;
            jobLog.WriteEntry(this, activity, Log.LogEntryType.Information, true);
        }

        /// <summary>
        /// This function is used to create a custom filename and path.
        /// This function throws an exception if an invalid rename pattern is provided
        /// </summary>
        /// <param name="customRenamePattern">The renaming pattern</param>
        /// <param name="newFileName">Reference to a string that will contain the new custom Filename</param>
        /// <param name="destinationPath">Reference to a string that will contains the new custom Path</param>
        /// <param name="sourceVideo">Path to Source Video file</param>
        /// <param name="metaData">Metadata for the Source Video file</param>
        /// <param name="jobLog">Log object</param>
        public static void CustomRenameFilename(string customRenamePattern, ref string newFileName, ref string destinationPath, string sourceVideo, VideoTags metaData, Log jobLog)
        {
            char[] renameBytes = customRenamePattern.ToCharArray();
            for (int i = 0; i < renameBytes.Length; i++)
            {
                switch (renameBytes[i])
                {
                    case '%':
                        string command = "";
                        while (renameBytes[++i] != '%')
                            command += renameBytes[i].ToString(System.Globalization.CultureInfo.InvariantCulture).ToLower();

                        string format = "";
                        switch (command)
                        {
                            case "originalfilename": // Name of the source file without the extension or path
                                newFileName += Path.GetFileNameWithoutExtension(sourceVideo);
                                break;

                            case "showname": // %sn% - Showname / title
                                newFileName += metaData.Title;
                                break;

                            case "airyear": // %ad% - original Air Year
                                if (metaData.OriginalBroadcastDateTime > GlobalDefs.NO_BROADCAST_TIME)
                                    newFileName += metaData.OriginalBroadcastDateTime.ToLocalTime().Year.ToString("0000", System.Globalization.CultureInfo.InvariantCulture);
                                else
                                    jobLog.WriteEntry(Localise.GetPhrase("Cannot find AirYear"), Log.LogEntryType.Warning);
                                break;

                            case "airmonth": // %ad% - original Air Month
                                if (metaData.OriginalBroadcastDateTime > GlobalDefs.NO_BROADCAST_TIME)
                                    newFileName += metaData.OriginalBroadcastDateTime.ToLocalTime().Month.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
                                else
                                    jobLog.WriteEntry(Localise.GetPhrase("Cannot find AirMonth"), Log.LogEntryType.Warning);
                                break;

                            case "airmonthshort": // %ad% - original Air Month abbreviation
                                if (metaData.OriginalBroadcastDateTime > GlobalDefs.NO_BROADCAST_TIME)
                                    newFileName += metaData.OriginalBroadcastDateTime.ToLocalTime().ToString("MMM"); // need to keep it culture sensitive here
                                else
                                    jobLog.WriteEntry(Localise.GetPhrase("Cannot find AirMonthShort"), Log.LogEntryType.Warning);
                                break;

                            case "airmonthlong": // %ad% - original Air Month full name
                                if (metaData.OriginalBroadcastDateTime > GlobalDefs.NO_BROADCAST_TIME)
                                    newFileName += metaData.OriginalBroadcastDateTime.ToLocalTime().ToString("MMMM"); // need to keep it culture sensitive here
                                else
                                    jobLog.WriteEntry(Localise.GetPhrase("Cannot find AirMonthLong"), Log.LogEntryType.Warning);
                                break;

                            case "airday": // %ad% - original Air Date
                                if (metaData.OriginalBroadcastDateTime > GlobalDefs.NO_BROADCAST_TIME)
                                    newFileName += metaData.OriginalBroadcastDateTime.ToLocalTime().Day.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
                                else
                                    jobLog.WriteEntry(Localise.GetPhrase("Cannot find AirDay"), Log.LogEntryType.Warning);
                                break;

                            case "airdayshort": // %ad% - original Air Day of week abbreviation
                                if (metaData.OriginalBroadcastDateTime > GlobalDefs.NO_BROADCAST_TIME)
                                    newFileName += metaData.OriginalBroadcastDateTime.ToLocalTime().ToString("ddd");  // need to keep it culture sensitive here
                                else
                                    jobLog.WriteEntry(Localise.GetPhrase("Cannot find AirDayShort"), Log.LogEntryType.Warning);
                                break;

                            case "airdaylong": // %ad% - original Air Day of week full name
                                if (metaData.OriginalBroadcastDateTime > GlobalDefs.NO_BROADCAST_TIME)
                                    newFileName += metaData.OriginalBroadcastDateTime.ToLocalTime().ToString("dddd");  // need to keep it culture sensitive here
                                else
                                    jobLog.WriteEntry(Localise.GetPhrase("Cannot find AirDayLong"), Log.LogEntryType.Warning);
                                break;

                            case "airhour": // %ad% - original Air Hour
                                if (metaData.OriginalBroadcastDateTime > GlobalDefs.NO_BROADCAST_TIME)
                                    newFileName += metaData.OriginalBroadcastDateTime.ToLocalTime().Hour.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
                                else
                                    jobLog.WriteEntry("Cannot find AirHour", Log.LogEntryType.Warning);
                                break;

                            case "airminute": // %ad% - original Air Minute
                                if (metaData.OriginalBroadcastDateTime > GlobalDefs.NO_BROADCAST_TIME)
                                    newFileName += metaData.OriginalBroadcastDateTime.ToLocalTime().Minute.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
                                else
                                    jobLog.WriteEntry("Cannot find AirMinute", Log.LogEntryType.Warning);
                                break;

                            case "recordyear": // %rd% - Record Year
                                if (metaData.RecordedDateTime > GlobalDefs.NO_BROADCAST_TIME)
                                    newFileName += metaData.RecordedDateTime.ToLocalTime().Year.ToString("0000", System.Globalization.CultureInfo.InvariantCulture);
                                else
                                {
                                    jobLog.WriteEntry(Localise.GetPhrase("Cannot find RecordYear using File Creation Date"), Log.LogEntryType.Warning);
                                    DateTime dt = Util.FileIO.GetFileCreationTime(sourceVideo);
                                    if (dt > GlobalDefs.NO_BROADCAST_TIME)
                                        newFileName += dt.Year.ToString("0000", System.Globalization.CultureInfo.InvariantCulture);
                                }
                                break;

                            case "recordmonth": // %rd% - Record Month
                                if (metaData.RecordedDateTime > GlobalDefs.NO_BROADCAST_TIME)
                                    newFileName += metaData.RecordedDateTime.ToLocalTime().Month.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
                                else
                                {
                                    jobLog.WriteEntry(Localise.GetPhrase("Cannot find RecordMonth using File Creation Date"), Log.LogEntryType.Warning);
                                    DateTime dt = Util.FileIO.GetFileCreationTime(sourceVideo);
                                    if (dt > GlobalDefs.NO_BROADCAST_TIME)
                                        newFileName += dt.Month.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
                                }
                                break;

                            case "recordmonthshort": // %rd% - Record Month abbreviation
                                if (metaData.RecordedDateTime > GlobalDefs.NO_BROADCAST_TIME)
                                    newFileName += metaData.RecordedDateTime.ToLocalTime().ToString("MMM"); // Need to keep it culture sensitive
                                else
                                {
                                    jobLog.WriteEntry(Localise.GetPhrase("Cannot find RecordMonthShort using File Creation Date"), Log.LogEntryType.Warning);
                                    DateTime dt = Util.FileIO.GetFileCreationTime(sourceVideo);
                                    if (dt > GlobalDefs.NO_BROADCAST_TIME)
                                        newFileName += dt.ToString("MMM");
                                }
                                break;

                            case "recordmonthlong": // %rd% - Record Month full name
                                if (metaData.RecordedDateTime > GlobalDefs.NO_BROADCAST_TIME)
                                    newFileName += metaData.RecordedDateTime.ToLocalTime().ToString("MMMM"); // Need to keep it culture sensitive
                                else
                                {
                                    jobLog.WriteEntry(Localise.GetPhrase("Cannot find RecordMonthLong using File Creation Date"), Log.LogEntryType.Warning);
                                    DateTime dt = Util.FileIO.GetFileCreationTime(sourceVideo);
                                    if (dt > GlobalDefs.NO_BROADCAST_TIME)
                                        newFileName += dt.ToString("MMMM");
                                }
                                break;

                            case "recordday": // %rd% - Record Day
                                if (metaData.RecordedDateTime > GlobalDefs.NO_BROADCAST_TIME)
                                    newFileName += metaData.RecordedDateTime.ToLocalTime().Day.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
                                else
                                {
                                    jobLog.WriteEntry(Localise.GetPhrase("Cannot find RecordDay using File Creation Date"), Log.LogEntryType.Warning);
                                    DateTime dt = Util.FileIO.GetFileCreationTime(sourceVideo);
                                    if (dt > GlobalDefs.NO_BROADCAST_TIME)
                                        newFileName += dt.Day.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
                                }
                                break;

                            case "recorddayshort": // %rd% - Record Day of week abbreviation
                                if (metaData.RecordedDateTime > GlobalDefs.NO_BROADCAST_TIME)
                                    newFileName += metaData.RecordedDateTime.ToLocalTime().ToString("ddd"); // Keep it culture sensitive
                                else
                                {
                                    jobLog.WriteEntry(Localise.GetPhrase("Cannot find RecordDayShort using File Creation Date"), Log.LogEntryType.Warning);
                                    DateTime dt = Util.FileIO.GetFileCreationTime(sourceVideo);
                                    if (dt > GlobalDefs.NO_BROADCAST_TIME)
                                        newFileName += dt.ToString("ddd");
                                }
                                break;

                            case "recorddaylong": // %rd% - Record Day of week full name
                                if (metaData.RecordedDateTime > GlobalDefs.NO_BROADCAST_TIME)
                                    newFileName += metaData.RecordedDateTime.ToLocalTime().ToString("dddd"); // Keep it culture sensitive
                                else
                                {
                                    jobLog.WriteEntry(Localise.GetPhrase("Cannot find RecordDayLong using File Creation Date"), Log.LogEntryType.Warning);
                                    DateTime dt = Util.FileIO.GetFileCreationTime(sourceVideo);
                                    if (dt > GlobalDefs.NO_BROADCAST_TIME)
                                        newFileName += dt.ToString("dddd");
                                }
                                break;

                            case "recordhour": // Record Hour
                                if (metaData.RecordedDateTime > GlobalDefs.NO_BROADCAST_TIME)
                                    newFileName += metaData.RecordedDateTime.ToLocalTime().Hour.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
                                else
                                {
                                    jobLog.WriteEntry(Localise.GetPhrase("Cannot find RecordHour using File Creation Date"), Log.LogEntryType.Warning);
                                    DateTime dt = Util.FileIO.GetFileCreationTime(sourceVideo);
                                    if (dt > GlobalDefs.NO_BROADCAST_TIME)
                                        newFileName += dt.Hour.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
                                }
                                break;

                            case "recordminute": // Record minute
                                if (metaData.RecordedDateTime > GlobalDefs.NO_BROADCAST_TIME)
                                    newFileName += metaData.RecordedDateTime.ToLocalTime().Minute.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
                                else
                                {
                                    jobLog.WriteEntry(Localise.GetPhrase("Cannot find RecordMinute using File Creation Date"), Log.LogEntryType.Warning);
                                    DateTime dt = Util.FileIO.GetFileCreationTime(sourceVideo);
                                    if (dt > GlobalDefs.NO_BROADCAST_TIME)
                                        newFileName += dt.Minute.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
                                }
                                break;

                            case "episodename": // %en% - episode name / subtitle
                                if (!String.IsNullOrEmpty(metaData.SubTitle))
                                    newFileName += metaData.SubTitle;
                                else
                                    jobLog.WriteEntry(Localise.GetPhrase("Cannot find Episode Name"), Log.LogEntryType.Warning);
                                break;

                            case "network": // %en% - recorded channel name
                                if (!String.IsNullOrEmpty(metaData.Network))
                                    newFileName += metaData.Network;
                                else
                                    jobLog.WriteEntry(Localise.GetPhrase("Cannot find Network Channel Name"), Log.LogEntryType.Warning);
                                break;

                            case "season": // %ss%### - season no
                                format = "";
                                try
                                {
                                    if (renameBytes[i + 1] == '#')
                                    {
                                        while (renameBytes[++i] == '#')
                                            format += "0";

                                        --i; // adjust for last increment
                                    }
                                }
                                catch { } // this is normal incase it doesn't exist

                                if (metaData.Season != 0)
                                    newFileName += metaData.Season.ToString(format);
                                else
                                    jobLog.WriteEntry(Localise.GetPhrase("Cannot find Season No"), Log.LogEntryType.Warning);
                                break;

                            case "episode": // %ee%### - episode no
                                format = "";
                                try
                                {
                                    if (renameBytes[i + 1] == '#')
                                    {
                                        while (renameBytes[++i] == '#')
                                            format += "0";

                                        --i; // adjust for last increment
                                    }
                                }
                                catch { } // this is normal incase it doesn't exist

                                if (metaData.Episode != 0)
                                    newFileName += metaData.Episode.ToString(format);
                                else
                                    jobLog.WriteEntry(Localise.GetPhrase("Cannot find Episode No"), Log.LogEntryType.Warning);
                                break;

                            default:
                                jobLog.WriteEntry(Localise.GetPhrase("Invalid file naming format detected, skipping") + " : " + command, Log.LogEntryType.Warning); // We had an invalid format
                                break;
                        }
                        break;

                    case '\\':
                        if (i == 0)
                            break; // skip if the first character is a directory separator

                        if (!GlobalDefs.IsNullOrWhiteSpace(destinationPath)) // First directory should not start with a '\'
                            destinationPath += "\\";
                        destinationPath += Util.FilePaths.RemoveIllegalFilePathChars(newFileName);
                        newFileName = ""; // reset the new filename
                        break;

                    default:
                        newFileName += renameBytes[i];
                        break;
                }
            }
        }

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
                            CustomRenameFilename(_conversionOptions.customRenameBySeries, ref newFileName, ref subDestinationPath, OriginalFileName, _metaData.MetaData, jobLog);

                            newFileName = newFileName.Replace(@"\\", @"\");
                            newFileName += Path.GetExtension(_convertedFile);
                        }
                        catch (Exception)
                        {
                            jobLog.WriteEntry(this, Localise.GetPhrase("Error in file naming format detected, fallback to default naming convention"), Log.LogEntryType.Warning); // We had an invalid format
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
                        // STANDARD --> SHOWNAME/SHOWNAME-SXXEYY-EPISODENAME-RECORDDATE.ext

                        newFileName = title;

                        if ((_metaData.MetaData.Season > 0) && (_metaData.MetaData.Episode > 0))
                        {
                            newFileName += "-" + "S" + _metaData.MetaData.Season.ToString("00", System.Globalization.CultureInfo.InvariantCulture) + "E" + _metaData.MetaData.Episode.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
                            if (subTitle != "") newFileName += "-" + subTitle;
                            newFileName += "-" + date;
                        }
                        else
                        {
                            if (subTitle != "")
                            {
                                newFileName += "-" + subTitle;
                                newFileName += "-" + date;
                            }
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

        public void Convert()
        {
            _jobStatus.ErrorMsg = ""; //start clean
            _jobStatus.SuccessfulConversion = false; //no successful conversion yet
            _completed = false;

            Log jobLog = CreateLog(_conversionOptions.sourceVideo);
            if (Log.LogDestination.Null == jobLog.Destination)
                Log.AppLog.WriteEntry(this, "ERROR: Failed to create JobLog for File " + Path.GetFileName(_conversionOptions.sourceVideo), Log.LogEntryType.Error, true);

            //Debug, dump all the conversion parameter before starting to help with debugging
            //jobLog.WriteEntry("Starting conversion - DEBUG MESSAGES", Log.LogEntryType.Debug);
            //jobLog.WriteEntry("Windows OS Version -> " + Environment.OSVersion.ToString(), Log.LogEntryType.Debug);
            //jobLog.WriteEntry("Windows 64Bit -> " + Environment.Is64BitOperatingSystem.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
            //jobLog.WriteEntry("MCEBuddy Platform -> " + ((IntPtr.Size == 4) ? "32 Bit" : "64 Bit"), Log.LogEntryType.Debug);
            string currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            //jobLog.WriteEntry("MCEBuddy Current Version : " + currentVersion, Log.LogEntryType.Debug);
            //jobLog.WriteEntry(_conversionOptions.ToString(), Log.LogEntryType.Debug);
            //jobLog.WriteEntry("Max Concurrent Jobs -> " + maxConcurrentJobs.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
            //jobLog.WriteEntry("Commercial Skip Cut (profile + task) -> " + commercialSkipCut.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
            //jobLog.WriteEntry("Locale Language -> " + Localise.ThreeLetterISO().ToUpper(), Log.LogEntryType.Debug);
            
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

            if (String.IsNullOrEmpty(_conversionOptions.audioLanguage))
            {
                // Doing this will make ReMux copy all Audio tracks so that VideoFile can then isolate the necessary tracks
                // Otherwise if audioLanguage is left blank, ReMux will strip out the audio tracks up front.
                // Problem with the blank is that if we are using non WTV/DVRMS formats, then VideoFile may not strip out non required tracks by default (unless the language is set)
                // Setting the language to locale ensures that the default locale language is ALWAYS selected when possible unless it is overridden by the Profile Audio Language
                _conversionOptions.audioLanguage = Localise.ThreeLetterISO();
                jobLog.WriteEntry("No Profile Audio Language found, defaulting to selected Locale" + " " + _conversionOptions.audioLanguage.ToUpper(), Log.LogEntryType.Debug);
            }

            try
            {
                // Check for meta data match
                jobLog.WriteEntry(this, "File " + Path.GetFileName(_conversionOptions.sourceVideo) + " checking for showname filter >" + _conversionOptions.metaSelection + "<", Log.LogEntryType.Information);
                if ((!_jobStatus.Cancelled) && (!String.IsNullOrEmpty(_conversionOptions.metaSelection)))
                {
                    LogStatus(Localise.GetPhrase("Show information matching"), ref jobLog);
                    _metaData = new VideoMetaData(_conversionOptions.sourceVideo, false, _conversionOptions.imdbSeriesId, _conversionOptions.tvdbSeriesId, ref _jobStatus, jobLog);
                    _metaData.Extract();

                    bool avoidList = false;
                    string wildcardRegex = _conversionOptions.metaSelection.ToLower();

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
                            jobLog.WriteEntry(this, Localise.GetPhrase("File") + " " + Path.GetFileName(_conversionOptions.sourceVideo) + " " + Localise.GetPhrase("matched avoid meta data wildcard") + " " + _conversionOptions.metaSelection, Log.LogEntryType.Error);
                            _jobStatus.ErrorMsg = Localise.GetPhrase("File matched avoid meta data wildcard");
                            Cleanup(ref jobLog);
                            return;
                        }
                    }
                    else
                    {
                        if (!(rxFileMatch.IsMatch(_metaData.MetaData.Title)))
                        {
                            jobLog.WriteEntry(this, Localise.GetPhrase("File") + " " + Path.GetFileName(_conversionOptions.sourceVideo) + " " + Localise.GetPhrase("did not match meta data wildcard") + " " + _conversionOptions.metaSelection, Log.LogEntryType.Error);
                            _jobStatus.ErrorMsg = Localise.GetPhrase("File did not match meta data wildcard");
                            Cleanup(ref jobLog);
                            return;
                        }
                    }

                    _metaData = null;
                }

                jobLog.WriteEntry(this, Localise.GetPhrase("System language for stream purposes is") + " " + System.Globalization.CultureInfo.CurrentCulture.DisplayName + " (" + System.Globalization.CultureInfo.CurrentCulture.ThreeLetterISOLanguageName.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")", Log.LogEntryType.Information);
                jobLog.WriteEntry(this, "System language for stream purposes is" + " " + System.Globalization.CultureInfo.CurrentCulture.DisplayName + " (" + System.Globalization.CultureInfo.CurrentCulture.ThreeLetterISOLanguageName.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")", Log.LogEntryType.Information);

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

                // Get the meta data
                if (!_jobStatus.Cancelled)
                {
                    LogStatus(Localise.GetPhrase("Getting show information and banner from Internet sources"), ref jobLog);
                    _metaData = new VideoMetaData(_conversionOptions.sourceVideo, _conversionOptions.downloadSeriesDetails, _conversionOptions.imdbSeriesId, _conversionOptions.tvdbSeriesId, ref _jobStatus, jobLog);
                    _metaData.Extract();

                    if (_metaData.MetaData.CopyProtected) // If it's copy protected fail the conversion here with the right message and status
                    {
                        jobLog.WriteEntry(this, Localise.GetPhrase("ERROR: VIDEO IS COPYPROTECTED. CONVERSION WILL FAIL"), Log.LogEntryType.Error);
                        _jobStatus.ErrorMsg = Localise.GetPhrase("ERROR: VIDEO IS COPYPROTECTED. CONVERSION WILL FAIL");
                        Cleanup(ref jobLog);
                        return;
                    }
                }

                if (!_jobStatus.Cancelled)
                {
                    // If the SRT File exists, copy it
                    jobLog.WriteEntry(this, Localise.GetPhrase("Checking for SRT File"), Log.LogEntryType.Information);
                    string srtFile = Util.FilePaths.GetFullPathWithoutExtension(OriginalFileName) + ".srt"; // SRT file along with original source video

                    if (File.Exists(srtFile))
                    {
                        string srtDest = Path.Combine(_conversionOptions.workingPath, Path.GetFileName(srtFile));
                        try
                        {
                            File.Copy(srtFile, srtDest);
                            _saveSRTFile = true;
                            jobLog.WriteEntry(this, Localise.GetPhrase("Found existing SRT file and saved it"), Log.LogEntryType.Information);
                        }
                        catch (Exception)
                        {
                            jobLog.WriteEntry(this, Localise.GetPhrase("Found existing SRT file but unable to save it"), Log.LogEntryType.Warning);
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
                        catch (Exception)
                        {
                            jobLog.WriteEntry(this, Localise.GetPhrase("Found existing EDL file but unable to save it"), Log.LogEntryType.Warning);
                        }
                    }

                    // If Commerical removal is set for TS files copy them to the temp directory (During Commercial removal, files (except TS) are remuxed later into their own temp working directories)
                    // We need exclusive access to each copy of the file in their respective temp working directories otherwise commercial skipping gets messed up
                    // when we have multiple simultaneous tasks for the same file (they all share/overwrite the same EDL file) which casuses a failure
                    // Similarly when trimming is enabled, we need to have a separate copy to avoid overwriting the original
                    if ((sourceExt == ".ts") && (_conversionOptions.commercialRemoval != CommercialRemovalOptions.None || _conversionOptions.startTrim != 0 || _conversionOptions.endTrim != 0))
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
                            jobLog.WriteEntry(this, Localise.GetPhrase("Commercial skipping enabled for TS file, Unable to copy source video to working directory") + " Source:" + _conversionOptions.sourceVideo + ", Target:" + newSource + " Error : " + e.ToString(), Log.LogEntryType.Error);
                            _jobStatus.ErrorMsg = Localise.GetPhrase("Commercial skipping enabled for TS file, Unable to copy source video to working directory");
                            Cleanup(ref jobLog);
                            return;
                        }
                    }
                }

                if (!_jobStatus.Cancelled)
                {
                    // Remux media center recordings or any video if commercials are being removed to TS (except TS)
                    if ((sourceExt == ".dvr-ms") || (sourceExt == ".wtv") || ((_conversionOptions.commercialRemoval != CommercialRemovalOptions.None) && (sourceExt != ".ts")))
                    {
                        LogStatus(Localise.GetPhrase("Remuxing recording"), ref jobLog);
                        RemuxMediaCenter.RemuxMCERecording remux = new RemuxMCERecording(_conversionOptions.profile, _conversionOptions.sourceVideo, _conversionOptions.workingPath, _conversionOptions.audioLanguage, ref _jobStatus, jobLog);
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
                        _fixCorruptedRemux = remux.FixCorruptedRemux; // Did we fix a corrupted video file through remux
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

                // If we are using ShowAnalyzer, do it here
                if (!_jobStatus.Cancelled)
                {
                    jobLog.WriteEntry(this, "Checking for ShowAnalyzer", Log.LogEntryType.Information);
                    if (_conversionOptions.commercialRemoval == CommercialRemovalOptions.ShowAnalyzer)
                    {
                        LogStatus(Localise.GetPhrase("ShowAnalyzer Advertisement scan"), ref jobLog);
                        _commercialScan = new Scanner(_conversionOptions, SourceVideo, true, ref _jobStatus, jobLog);
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
                        _commercialScan = new Scanner(_conversionOptions, SourceVideo, false, ref _jobStatus, jobLog);
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
                            if ((_commercialScan != null) && (!commercialSkipCut)) // Incase we asked not to cut the video, just create the EDL file, let us not cut the SRT files also
                            {
                                if (_commercialScan.CommercialsFound) // We just adjust the SRT file with the EDL file
                                {
                                    LogStatus(Localise.GetPhrase("Trimming Closed Captions"), ref jobLog);
                                    string srtFile = Util.FilePaths.GetFullPathWithoutExtension(OriginalFileName) + ".srt";
                                    string srtDest = Path.Combine(_conversionOptions.workingPath, Path.GetFileName(srtFile)); // working copy SRT filename
                                    if (!cc.EDLTrim(_commercialScan.EDLFile, srtDest, _conversionOptions.ccOffset))
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

                            if ((_commercialScan != null) && (!commercialSkipCut)) // If we are not asked to skip cutting commercials (we don't need to adjust CC), for commerical cutting we need to create a special EDL adjusted file before extracting of CC
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
                                    catch (Exception)
                                    {
                                        // Creating temp CC file failed
                                        _jobStatus.ErrorMsg = Localise.GetPhrase("Creating temporary file for CC extracting file failed");
                                        jobLog.WriteEntry(this, _jobStatus.ErrorMsg, Log.LogEntryType.Error);
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
                            }

                            // Trimming and EDL cutting is already complete so we just need to extract directly 
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

                            if (tempCCFile != SourceVideo) // If we created a temp file, lets get rid of it now and also need to remove the SRT file
                            {
                                try
                                {
                                    jobLog.WriteEntry(this, "Renaming SRT file to " + Util.FilePaths.GetFullPathWithoutExtension(SourceVideo) + ".srt", Log.LogEntryType.Debug);
                                    File.Move(Util.FilePaths.GetFullPathWithoutExtension(tempCCFile) + ".srt", Util.FilePaths.GetFullPathWithoutExtension(SourceVideo) + ".srt");
                                }
                                catch (Exception)
                                {
                                    // Renaming SRT file
                                    _jobStatus.ErrorMsg = Localise.GetPhrase("Error renaming SRT file after extracting");
                                    jobLog.WriteEntry(this, _jobStatus.ErrorMsg, Log.LogEntryType.Error);
                                    Cleanup(ref jobLog);
                                    return;
                                }

                                Util.FileIO.TryFileDelete(tempCCFile);
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
                        if (!Remover.IsSupportedExtension(Transcode.Convert.GetConversionExtension(_conversionOptions)))
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
                    bool res = conv.Run(_conversionOptions, ref _videoFile, ref _commercialScan, _fixCorruptedRemux); // if we're using MEncoder, then we will complete the commercial stripping here itself
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

                // This point onward there is not ETA or % so set ETA to working
                _jobStatus.ETA = "Working...";

                // Write the meta data
                if (!_jobStatus.Cancelled)
                {
                    LogStatus(Localise.GetPhrase("Writing show information"), ref jobLog);
                    _metaData.WriteTags(_convertedFile); // we can ignore failure of writing meta data, not critical
                    jobLog.WriteEntry(this, Localise.GetPhrase("Finished writing tags, file size [KB]") + " " + (Util.FileIO.FileSize(_convertedFile) / 1024).ToString("N", System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
                }

                // Run a custom command from the user if configured
                if (!_jobStatus.Cancelled)
                {
                    LogStatus(Localise.GetPhrase("Running Custom Commands"), ref jobLog);

                    CustomCommand customCommand = new CustomCommand(_conversionOptions.profile, _convertedFile, _originalFileName, _remuxedVideo, _metaData.MetaData, ref _jobStatus, jobLog);
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
                    string srtFile = Path.Combine(_conversionOptions.workingPath, (Path.GetFileNameWithoutExtension(OriginalFileName) + ".srt")); // SRT file created by 3rd Party in temp working directory

                    try
                    {
                        if (File.Exists(srtFile))
                        {
                            jobLog.WriteEntry(this, Localise.GetPhrase("Found SRT file, moving to destination") + " SRT:" + srtFile + " Destination:" + _conversionOptions.destinationPath, Log.LogEntryType.Information);
                            if (srtFile != (Util.FilePaths.GetFullPathWithoutExtension(_convertedFile) + ".srt")) // Don't delete if they are the same file, e.g. TS to TS in same directory
                                FileIO.TryFileDelete((Util.FilePaths.GetFullPathWithoutExtension(_convertedFile) + ".srt"));
                            File.Move(srtFile, (Util.FilePaths.GetFullPathWithoutExtension(_convertedFile) + ".srt")); //rename to match destination file
                        }
                    }
                    catch (Exception) // Not critial to be unable to move SRT File
                    {
                        jobLog.WriteEntry(this, Localise.GetPhrase("Unable to move SRT file to destination") + " SRT:" + srtFile + " Destination:" + _conversionOptions.destinationPath, Log.LogEntryType.Warning);
                    }

                    if (_commercialScan != null)
                    {
                        try
                        {
                            if (File.Exists(_commercialScan.EDLFile) && commercialSkipCut) // if we are asked to keep EDL file, we copy it out to output
                            {
                                jobLog.WriteEntry(this, Localise.GetPhrase("Found EDL file, request to move to destination") + " EDL:" + _commercialScan.EDLFile + " Destination:" + _conversionOptions.destinationPath, Log.LogEntryType.Information);
                                if(_commercialScan.EDLFile != (Util.FilePaths.GetFullPathWithoutExtension(_convertedFile) + ".edl")) // don't delete the same file, e.g. TS to TS in same directory
                                    FileIO.TryFileDelete((Util.FilePaths.GetFullPathWithoutExtension(_convertedFile) + ".edl"));
                                File.Move(_commercialScan.EDLFile, (Util.FilePaths.GetFullPathWithoutExtension(_convertedFile) + ".edl"));
                            }
                        }
                        catch (Exception)
                        {
                            jobLog.WriteEntry(this, Localise.GetPhrase("Unable to move EDL file to destination") + " EDL:" + _commercialScan.EDLFile + " Destination:" + _conversionOptions.destinationPath, Log.LogEntryType.Warning);
                        }
                    }

                    // Create the XML file with Source video information for WTV and DVRMS file (XBMC compliant NFO file, http://wiki.xbmc.org/index.php?title=Import_-_Export_Library)
                    if (_conversionOptions.extractXML && ((Path.GetExtension(OriginalFileName).ToLower() == ".wtv") || (Path.GetExtension(OriginalFileName).ToLower() == ".dvr-ms")))
                    {
                        _metaData.MCECreateXMLTags(OriginalFileName, _convertedFile, _videoFile);
                    }

                    _jobStatus.ErrorMsg = ""; // all done, we are in the clear, success
                    _jobStatus.SuccessfulConversion = true; //now the original file can be deleted if required
                    LogStatus(Localise.GetPhrase("Success - All done!"), ref jobLog);
                }

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
