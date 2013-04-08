using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

using MCEBuddy.Globals;
using MCEBuddy.Engine;
using MCEBuddy.Util;
using MCEBuddy.Configuration;

namespace MCEBuddy.Engine
{
    public class QueueManager
    {
        private List<ConversionJob> _jobQueue = new List<ConversionJob>();
        private List<string> _processedFiles = new List<string>();
        private List<string> _monitorTaskFilterMismatchFiles = new List<string>();
        private List<string> _conversionTaskFilterMismatchFiles = new List<string>();
        private List<string> _archivedFiles = new List<string>();
        private List<string> _monitorFiles = new List<string>();

        private int _minimumAge = 0;

        public QueueManager()
        {
            // Connect net drives and setup search records for source locations
            foreach (MonitorJobOptions mjo in MCEBuddyConf.GlobalMCEConfig.AllMonitorTasks)
            {
                string searchPath = mjo.searchPath;
                string domainName = mjo.domainName;
                string userName = mjo.userName;
                string password = mjo.password;

                // Connect network drives if needed
                if (Util.Net.IsUNCPath(searchPath))
                    ConnectNet(searchPath, domainName, userName, password);
            }

            // Connect net drives for task destinations
            foreach (ConversionJobOptions cjo in MCEBuddyConf.GlobalMCEConfig.AllConversionTasks)
            {
                string destinationPath = cjo.destinationPath;
                string domainName = cjo.domainName;
                string userName = cjo.userName;
                string password = cjo.password;

                // Connect network drives if needed
                if (Util.Net.IsUNCPath(destinationPath))
                    ConnectNet(destinationPath, domainName, userName, password);
            }

            _minimumAge = MCEBuddyConf.GlobalMCEConfig.GeneralOptions.minimumAge;
        }

        private void ConnectNet(string path, string domain, string userName, string password)
        {
            int ret = 0;
            string noConnectError = Localise.GetPhrase("Unable to connect to network location") + " " + path + "\r\n" + "Domain name:" + domain + "\r\nUsername:" + userName + "\r\nPassword:" + password + "\r\n";

            Log.AppLog.WriteEntry(this, Localise.GetPhrase("Attempting to connect to network share")+ " " + path, Log.LogEntryType.Debug);
            try
            {
                ret = Util.Net.ConnectShare(path, domain, userName, password);
            }
            catch (Exception ex)
            {
                Log.AppLog.WriteEntry(noConnectError + ex.Message, Log.LogEntryType.Error);
            }
            if (ret == 86)
            {
                System.ComponentModel.Win32Exception wex = new System.ComponentModel.Win32Exception(ret);
                Log.AppLog.WriteEntry(noConnectError + wex.Message + "\r\n" + Localise.GetPhrase("This is most likely caused by the currently logged on user being having a drive connected to the network location.  Please disconnect all network drives to the specified network locations for MCEBuddy."), Log.LogEntryType.Error);

            }
            else if (ret != 0)
            {
                System.ComponentModel.Win32Exception wex = new System.ComponentModel.Win32Exception(ret);
                Log.AppLog.WriteEntry(noConnectError + Localise.GetPhrase("Return code is") + " " + ret.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n" + wex.Message, Log.LogEntryType.Error);
            }
        }

        public bool QueueContains(string filePath)
        {
            foreach (ConversionJob job in _jobQueue)
            {
                if (job.OriginalFileName == filePath) return true;
            }
            return false;
        }

        public int JobCount(string filePath)
        {
            int jobCount = 0;
            foreach (ConversionJob job in _jobQueue)
            {
                if (job.OriginalFileName == filePath) jobCount++;
            }
            return jobCount;
        }


        private void AddJobs(string filePath, string monitorTaskName, int idx)
        {
            foreach (ConversionJobOptions conversionTask in MCEBuddyConf.GlobalMCEConfig.AllConversionTasks)
            {
                // Check if the task is disabled, which case skip
                if (!conversionTask.enabled)
                {
                    if (!_conversionTaskFilterMismatchFiles.Contains(filePath)) // don't overburden the log file
                    {
                        _conversionTaskFilterMismatchFiles.Add(filePath); // add to the list once printed
                        Log.AppLog.WriteEntry(this, Localise.GetPhrase("Conversion Task") + " " + conversionTask.taskName + " " + Localise.GetPhrase("disabled, skipping file") + " " + filePath, Log.LogEntryType.Debug, true);
                    }

                    continue;
                }

                conversionTask.sourceVideo = filePath;

                // Monitor Task name matching if not empty
                if (!String.IsNullOrWhiteSpace(monitorTaskName) && (conversionTask.monitorTaskNames != null))
                {
                    bool foundMatch = false;
                    foreach (string matchMonitorTaskName in conversionTask.monitorTaskNames)
                    {
                        if (monitorTaskName.ToLower().Trim() != matchMonitorTaskName.ToLower().Trim()) // match the list of a name
                            continue; // move onto next monitor task name
                        else
                        {
                            foundMatch = true;
                            break;
                        }
                    }

                    if (!foundMatch)
                    {
                        if (!_conversionTaskFilterMismatchFiles.Contains(filePath)) // don't overburden the log file
                        {
                            _conversionTaskFilterMismatchFiles.Add(filePath); // add to the list once printed
                            Log.AppLog.WriteEntry(this, "Skipping Conversion task " + conversionTask.taskName + " for file " + filePath + " since Monitor task " + monitorTaskName + " does not match the list of monitor tasks in the conversion task.", Log.LogEntryType.Debug, true);
                        }

                        continue; // move into next conversion task
                    }
                }

                // Filename pattern match from conversion task
                if ((Util.FilePaths.WildcardVideoMatch(Path.GetFileName(filePath), conversionTask.fileSelection)) || (String.IsNullOrWhiteSpace(conversionTask.fileSelection))) // only add files that match the conversion task file pattern or have a blanket file pattern
                {
                    Log.AppLog.WriteEntry(this, Localise.GetPhrase("Added new job to queue for") + " " + filePath, Log.LogEntryType.Debug, true);
                    ConversionJob job = new ConversionJob(conversionTask);
                    _jobQueue.Insert(idx, job);
                }
                else
                {
                    if (!_conversionTaskFilterMismatchFiles.Contains(filePath)) // don't overburden the log file
                    {
                        _conversionTaskFilterMismatchFiles.Add(filePath); // add to the list once printed
                        Log.AppLog.WriteEntry(this, Localise.GetPhrase("File filter selection mismatch") + " FileSelectionPattern=" + conversionTask.fileSelection + " fileName=" + Path.GetFileName(filePath), Log.LogEntryType.Debug);
                    }

                    // Manual files may be added multiple times and the filter may already have it from the last time, so be safe and delete it
                    Ini iniManualQueue = new Ini(GlobalDefs.ManualQueueFile); // Delete the entry from the manual queue
                    iniManualQueue.DeleteKey("ManualQueue", filePath);
                }
            }
        }

        private void CheckAndAddFile(string filePath, string monitorTaskName, bool manualFile)
        {
            Ini iniHistory = new Ini(GlobalDefs.HistoryFile);
            if (!QueueContains(filePath))
            {
                if (!Util.FileIO.FileLocked(filePath))
                {
                    string historyRec = iniHistory.ReadString(filePath, "Status", "");
                    if (historyRec == "" || manualFile) // Either the file has not been processed (Status is missing) or readded manually (Status is blank)
                    {
                        if (File.Exists(filePath))
                        {
                            if (manualFile)
                            {
                                int i = 0;
                                for (i = 0; i < _jobQueue.Count; i++)
                                {
                                    if (!_jobQueue[i].Active)
                                        break;
                                }
                                AddJobs(filePath, monitorTaskName, i); //add manual jobs for conversion at the head of queue (after the last currently active job)
                            }
                            else
                            {
                                // Check to see if the age of the file is old enough
                                DateTime timeStamp = Util.FileIO.GetFileModifiedTime(filePath);
                                if (timeStamp.AddHours(_minimumAge) < DateTime.Now)
                                {
                                    AddJobs(filePath, monitorTaskName, _jobQueue.Count); // Always add new files to the end of the queue
                                }
                            }
                        }
                        else if (manualFile) //delete only if a manual entry is made
                        {
                            Log.AppLog.WriteEntry(this, Localise.GetPhrase("Unable to queue file for conversion - file not found") + " " + filePath + "\r\n", Log.LogEntryType.Warning);
                            Ini iniManualQueue = new Ini(GlobalDefs.ManualQueueFile);
                            iniManualQueue.DeleteKey("ManualQueue", filePath);
                        }
                    }
                    else
                    {
                        if (!_processedFiles.Contains(filePath)) // Keep a track of processed file so we don't overload the mcebuddy.log file
                        {
                            _processedFiles.Add(filePath); // to the list of processed file so we don't end up doing it again
                            Log.AppLog.WriteEntry(this, Localise.GetPhrase("File already converted with status") + " " + historyRec + "\r\n", Log.LogEntryType.Debug);
                            if (manualFile) // Delete the manual entry
                            {
                                Ini iniManualQueue = new Ini(GlobalDefs.ManualQueueFile);
                                iniManualQueue.DeleteKey("ManualQueue", filePath);
                            }
                        }
                    }
                }
                else
                {
                    if (!_processedFiles.Contains(filePath)) // Keep a track of processed file so we don't overload the mcebuddy.log file
                    {
                        _processedFiles.Add(filePath); // to the list of processed file so we don't end up doing it again
                        Log.AppLog.WriteEntry(this, Localise.GetPhrase("Unable to queue file for conversion - file locked by another process") + " " + filePath + "\r\n", Log.LogEntryType.Debug);
                        if (manualFile) // Delete the manual entry
                        {
                            Ini iniManualQueue = new Ini(GlobalDefs.ManualQueueFile);
                            iniManualQueue.DeleteKey("ManualQueue", filePath);
                        }
                    }
                }
            }
        }

        public void ScanForFiles()
        {
            // Search the specific directories
            foreach (MonitorJobOptions monitorTask in MCEBuddyConf.GlobalMCEConfig.AllMonitorTasks)
            {
                IEnumerable<string> foundFiles = null;
                try
                {
                    foundFiles = Directory.EnumerateFiles(monitorTask.searchPath, "*.*", (monitorTask.monitorSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)).OrderBy(File.GetLastWriteTime); // We sort the files by last modified time when scanning and adding (oldest to newest)
                }
                catch (Exception ex)
                {
                    Log.AppLog.WriteEntry(Localise.GetPhrase("Unable to search for files in location") + " " + monitorTask.searchPath + "\r\n" + ex.Message, Log.LogEntryType.Warning);
                    foundFiles = null;
                }
                if (foundFiles != null)
                {
                    foreach (string foundFile in foundFiles)
                    {
                        //first check if this file is in the MCEBuddyArchive directory (which contains converted files, if so skip them)
                        if (Path.GetDirectoryName(foundFile).ToLower().Contains((string.IsNullOrEmpty(MCEBuddyConf.GlobalMCEConfig.GeneralOptions.archivePath) ? GlobalDefs.MCEBUDDY_ARCHIVE.ToLower() : MCEBuddyConf.GlobalMCEConfig.GeneralOptions.archivePath.ToLower())))
                        {
                            if (!_archivedFiles.Contains(foundFile)) // don't want to overburden the log file
                            {
                                _archivedFiles.Add(foundFile); // add to the list
                                Log.AppLog.WriteEntry(this, Localise.GetPhrase("File") + " " + foundFile + " " + Localise.GetPhrase("has been converted and archived, skipping"), Log.LogEntryType.Debug);
                            }
                            
                            continue;
                        }

                        // Found a file, filename pattern match from monitor task
                        if (Util.FilePaths.WildcardVideoMatch(Path.GetFileName(foundFile), monitorTask.searchPattern)) // Check pattern match for Monitor Locations filter
                        {
                            CheckAndAddFile(foundFile, monitorTask.taskName, false);
                        }
                        else
                        {
                            if (!_monitorTaskFilterMismatchFiles.Contains(foundFile)) // don't overburden the log file
                            {
                                _monitorTaskFilterMismatchFiles.Add(foundFile); // add to the list once printed
                                Log.AppLog.WriteEntry(this, Localise.GetPhrase("File") + " " + Path.GetFileName(foundFile) + " "+ Localise.GetPhrase("did not match wildcard") + " " + monitorTask.searchPattern, Log.LogEntryType.Debug);
                            }
                        }
                    }
                }
            }

            // Read the manual queue - manual selections are always first
            Ini iniQueue = new Ini(GlobalDefs.ManualQueueFile);
            SortedList<string, string> manualQueue = iniQueue.GetSectionKeyValuePairs("ManualQueue");
            foreach (KeyValuePair<string, string> node in manualQueue)
            {
                Log.AppLog.WriteEntry(Localise.GetPhrase("Added manually selected file to queue") + " " + node.Key, Log.LogEntryType.Debug);
                CheckAndAddFile(node.Key, "", true);
            }
        }

        /// <summary>
        /// Deletes the Parent directory recursively if the directories are empty
        /// </summary>
        /// <param name="filePath">File name with complete path to scan recursively up the parent chain of directories</param>
        private void DeleteParentDirectoryChainIfEmpty(string filePath)
        {
            if (String.IsNullOrWhiteSpace(filePath))
                return;

            if (!Directory.Exists(Path.GetDirectoryName(filePath)))
                return;

            try
            {
                if (Directory.EnumerateFiles(Path.GetDirectoryName(filePath), "*.*", SearchOption.TopDirectoryOnly).ToList<string>().Count == 0) // there are no files in the directory
                {
                    Log.AppLog.WriteEntry(this, "Deleting directory " + Path.GetDirectoryName(filePath), Log.LogEntryType.Debug);
                    Directory.Delete(Path.GetDirectoryName(filePath)); // Delete the directory
                    DeleteParentDirectoryChainIfEmpty(Path.GetDirectoryName(filePath)); // Kick it up one more level to check for empty directories
                }
            }
            catch (Exception ex)
            {
                Log.AppLog.WriteEntry("Unable to read parent directory contents " + Path.GetDirectoryName(filePath) + "\r\n" + ex.Message, Log.LogEntryType.Warning);
            }
        }

        /// <summary>
        /// Checks if the converted files need to be kept in sync with source files. It scans the monitor directories and keeps tracks of new, processed and deleted files.
        /// It deletes the converted file if the source file is deleted
        /// </summary>
        /// <param name="useRecycleBin">True to use Recycle Bin while deleting</param>
        public void SyncConvertedFiles(bool useRecycleBin)
        {
            // Search the specific directories
            foreach (MonitorJobOptions monitorTask in MCEBuddyConf.GlobalMCEConfig.AllMonitorTasks)
            {
                IEnumerable<string> foundFiles = null;
                try
                {
                    foundFiles = Directory.EnumerateFiles(monitorTask.searchPath, "*.*", (monitorTask.monitorSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)); // We sort the files by last modified time when scanning and adding (get all files for now)
                }
                catch (Exception ex)
                {
                    Log.AppLog.WriteEntry(Localise.GetPhrase("SyncConvertedFiles: Unable to search for files in location") + " " + monitorTask.searchPath + "\r\n" + ex.Message, Log.LogEntryType.Warning);
                    foundFiles = null;
                }

                if (foundFiles != null)
                {
                    foreach (string foundFile in foundFiles)
                    {
                        //first check if this file is in the MCEBuddyArchive directory (which contains converted files, if so skip them
                        if (Path.GetDirectoryName(foundFile).ToLower().Contains((string.IsNullOrEmpty(MCEBuddyConf.GlobalMCEConfig.GeneralOptions.archivePath) ? GlobalDefs.MCEBUDDY_ARCHIVE.ToLower() : MCEBuddyConf.GlobalMCEConfig.GeneralOptions.archivePath.ToLower())))
                            continue;

                        // Check if the file is being monitored for conversion (other converted files may exist in the same folder)
                        if (Util.FilePaths.WildcardVideoMatch(Path.GetFileName(foundFile), monitorTask.searchPattern)) // Check pattern match for Monitor Locations filter
                        {
                            if (!_monitorFiles.Contains(foundFile)) // We just keep building the list here and check if they are deleted later
                            {
                                _monitorFiles.Add(foundFile); // add to the list
                                Log.AppLog.WriteEntry(this, "File " + foundFile + " is being monitored for syncing with output file", Log.LogEntryType.Debug);
                            }
                        }
                    }
                }
            }

            // Now check if the sources files are deleted and delete the converted files
            foreach (string sourceFile in new List<string>(_monitorFiles)) // Create a new list to iterate through else it throws an exception when we modify it
            {
                if (File.Exists(sourceFile))
                    continue; // Source file still exists, nothing to do

                // Source file no longer, exists, it has been deleted
                // Check if the file has been converted and if so then get the location of the converted file from the history file
                Ini historyIni = new Ini(GlobalDefs.HistoryFile);
                int convCount = 0;
                string convFile = "";
                while ((convFile = historyIni.ReadString(sourceFile, "ConvertedTo" + convCount.ToString(System.Globalization.CultureInfo.InvariantCulture), "")) != "")
                {
                    // Delete the EDL, SRT, XML, NFO etc files also along with the original file if present
                    foreach (string supportFileExt in GlobalDefs.supportFilesExt)
                    {
                        string extFile = Path.Combine(Path.GetDirectoryName(convFile), Path.GetFileNameWithoutExtension(convFile) + supportFileExt); // support file

                        Util.FileIO.TryFileDelete(extFile, useRecycleBin); // Delete support file
                    }
                    
                    Util.FileIO.TryFileDelete(convFile, useRecycleBin); // Try to delete the converted file since the source file is deleted
                    DeleteParentDirectoryChainIfEmpty(convFile); // Delete the parent directory chain if empty for the converted file
                    Log.AppLog.WriteEntry(this, "Source File " + sourceFile + " deleted, deleting converted file " + convFile, Log.LogEntryType.Debug);
                    convCount++;
                }

                _monitorFiles.Remove(sourceFile); // Stop tracking this source file
            }
        }

        public List<ConversionJob> Queue
        {
            get
            {
                return _jobQueue;
            }
        }
    }
}
