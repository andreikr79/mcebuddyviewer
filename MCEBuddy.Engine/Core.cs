using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.ServiceModel;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Windows.Forms;
using System.Globalization;

using MCEBuddy.Engine;
using MCEBuddy.Util;
using MCEBuddy.Globals;
using MCEBuddy.Configuration;
using MCEBuddy.AppWrapper;
using MCEBuddy.VideoProperties;
using MCEBuddy.CommercialScan;

namespace MCEBuddy.Engine
{
    [ServiceBehavior(ConcurrencyMode = ConcurrencyMode.Multiple,InstanceContextMode = InstanceContextMode.Single)]
    public class Core: ICore 
    {
        private const int MAX_SHUTDOWN_WAIT = 2000;

        volatile private QueueManager _queueManager;
        private int _maxConcurrentJobs;
        private int _pollPeriod;
        private bool _rescan = false;
        private int _wakeHour = -1;
        private int _wakeMinute = -1; 
        private int _startHour = -1;
        private int _startMinute = -1;
        private int _stopHour = -1;
        private int _stopMinute = -1;
        private int logKeepDays = 0; // Number of days to keep log files
        volatile private ConversionJob[] _conversionJobs;
        private Util.PowerManagement.WakeUp _wakeUp;
        private bool _deleteOriginal = false;
        private bool _useRecycleBin = false;
        private bool _archiveOriginal = false;
        private bool _allowSleep = true;
        private DateTime _lastUpdateCheck = DateTime.Now.AddDays(-1);
        private Thread _updateCheckThread = null;
        private Thread _uPnPCheckThread = null;
        private UpdateCheck _updateCheck = null;
        private string[] daysOfWeek;
        volatile private Thread _monitorThread;
        volatile private bool _engineCrashed = false; // check if engine crashed
        private bool _uPnPEnabled;
        static private Object monitorLock = new Object(); // Object to lock for monitorThread sync
        private bool _serviceShutdownBySystem = false;

        public Core()
        {
            try
            {
                CreateExposedDirectory(GlobalDefs.ConfigPath); // Get admin access to the config directory so we can write to it
                CreateExposedDirectory(GlobalDefs.LogPath); // Get admin access to the log directory so we can write to it
                CreateExposedDirectory(GlobalDefs.CachePath); // Artwork Cache
                Log.AppLog = new Log(Log.LogDestination.LogFile, GlobalDefs.AppLogFile);
                _updateCheck = new UpdateCheck(); // Create only instance of this and resuse, otherwise it fails due to embedded objects
                MCEBuddyConf.GlobalMCEConfig = new MCEBuddyConf(GlobalDefs.ConfigFile);

                ReadConfig();

                //UPnP
                if (_uPnPEnabled)
                {
                    // Create a thread that keeps checking on the UPnP port mapping (some routers drop the mapping)
                    _uPnPCheckThread = new Thread(UPnPMonitorThread);
                    _uPnPCheckThread.CurrentCulture = _uPnPCheckThread.CurrentUICulture = Localise.MCEBuddyCulture;
                    _uPnPCheckThread.Start();
                }
            }
            catch (Exception e)
            {
                Log.AppLog.WriteEntry(this, "Unable to complete initialization of MCEBuddy Engine Core. Error: " + e.ToString(), Log.LogEntryType.Error, true); // This may or may not work depending upon whether the llog has been initialized otherwise it goes into NULL log
                Log.WriteSystemEventLog("Unable to complete initialization of MCEBuddy Engine Core. Error: " + e.ToString(), EventLogEntryType.Error);
                throw e; // Continue throwing the exception so that the engine stops
            }
        }

        /// <summary>
        /// Checks if the MCEBuddy CORE engine is running
        /// </summary>
        /// <returns>True is engine is running</returns>
        public bool EngineRunning()
        {
            Monitor.Enter(monitorLock); // Need to ensure when this function is called, the Engine is not in Starting or stopping mode
            bool ret = MCEBuddyConf.GlobalMCEConfig.GeneralOptions.engineRunning;
            Monitor.Exit(monitorLock);

            return ret;
        }

        /// <summary>
        /// Check if the cause of the engine stopping is if it has crashed. Should be called if the EngineRunning status is false.
        /// </summary>
        /// <returns>True is the engine stopped due to a crash</returns>
        public bool EngineCrashed()
        {
            Monitor.Enter(monitorLock); // Need to ensure when this function is called, the Engine is not in Starting or stopping mode
            bool ret = _engineCrashed;
            Monitor.Exit(monitorLock); // Need to ensure when this function is called, the Engine is not in Starting or stopping mode

            return ret;
        }

        /// <summary>
        /// Check if the engine is currently actively converting any jobs
        /// </summary>
        /// <returns>True if there are any active running conversions</returns>
        public bool Active()
        {
            Monitor.Enter(monitorLock); // Need to ensure when this function is called, the Engine is not in Starting or stopping mode
            bool ret = GlobalDefs.Active;
            Monitor.Exit(monitorLock); // Need to ensure when this function is called, the Engine is not in Starting or stopping mode

            return ret;
        }

        /// <summary>
        /// Forces MCEBuddy to ReScan the monitor tasks locations for new files to convert
        /// </summary>
        public void Rescan()
        {
            _rescan = true;
        }

        /// <summary>
        /// Checks if ShowAnalyzer is installed on the machine with the engine
        /// </summary>
        /// <returns>True if ShowAnalyzer is installed</returns>
        public bool ShowAnalyzerInstalled()
        {
            return Scanner.ShowAnalyzerInstalled();
        }

        /// <summary>
        /// Used to indicate whether the service has been shutdown by the system
        /// </summary>
        /// <returns>True is system initiated shutdown</returns>
        public bool ServiceShutdownBySystem()
        {
            return _serviceShutdownBySystem;
        }

        /// <summary>
        /// Returns the maximum number of simultaneous job queues
        /// </summary>
        /// <returns>Number of job queues</returns>
        public int NumConversionJobs()
        {
            Monitor.Enter(monitorLock); // Need to ensure when this function is called, the Engine is not in Starting or stopping mode
            int ret = _maxConcurrentJobs;
            Monitor.Exit(monitorLock); // Need to ensure when this function is called, the Engine is not in Starting or stopping mode

            return ret;
        }

        /// <summary>
        /// Returns a List to that contains the Windows Event logs entries created by MCEBuddy
        /// </summary>
        public List<EventLogEntry> GetWindowsEventLogs()
        {
            List<EventLogEntry> retList = new List<EventLogEntry>();

            try
            {
                EventLog eventLog = new EventLog("Application", ".", GlobalDefs.MCEBUDDY_EVENT_LOG_SOURCE);
                foreach (EventLogEntry eventLogEntry in eventLog.Entries)
                {
                    if (eventLogEntry.Source == GlobalDefs.MCEBUDDY_EVENT_LOG_SOURCE)
                        retList.Add(eventLogEntry);
                }
            }
            catch (Exception e)
            {
                Log.AppLog.WriteEntry(this, "Unable to get Event Log Entries", Log.LogEntryType.Error, true);
                Log.AppLog.WriteEntry(this, "Error -> " + e.ToString(), Log.LogEntryType.Error, true);
            }

            return retList;
        }

        /// <summary>
        /// Returns an 2 array string List which contains the Name and Description of all the Profiles in profiles.conf
        /// </summary>
        /// <returns>2 array string List</returns>
        public List<string[]> GetProfilesSummary()
        {
            List<string[]> profileSummary = new List<string[]>();

            // Open and read all profiles
            Ini profileIni = new Ini(GlobalDefs.ProfileFile);
            foreach (string profile in profileIni.GetSectionNames())
                profileSummary.Add(new String[] { profile, profileIni.ReadString(profile, "Description", "") }); // 2 array string -> Profile Name, Description

            return profileSummary;
        }

        /// <summary>
        /// Updates the MCEBuddyConf global configuration object and writes the settings to MCEBuddy.conf
        /// Calling this assumes that the Engine is in a stopped state
        /// </summary>
        /// <param name="configOptions">MCEBuddyConf parameters object</param>
        /// <returns>False if engine was not stopped when calling this function, true on a successful update</returns>
        public bool UpdateConfigParameters(ConfSettings configOptions)
        {
            Monitor.Enter(monitorLock); // Only one action update the object at a time

            if (EngineRunning()) // Check if engine has been stopped
            {
                Monitor.Exit(monitorLock);
                return false;
            }

            // Set the engine running flag to Stop, just in case wrong settings were passed
            configOptions.generalOptions.engineRunning = false;

            MCEBuddyConf.GlobalMCEConfig = new MCEBuddyConf(configOptions, GlobalDefs.ConfigFile); // Create a new object and write the settings
            
            Monitor.Exit(monitorLock);

            return true;
        }

        /// <summary>
        /// Returns a copy of the MCEBuddyConf global object configuration.
        /// The return object cannot read or write to a config file and is only a static set of configuration parameters.
        /// </summary>
        /// <returns>MCEBuddyConf global object parameters</returns>
        public ConfSettings GetConfigParameters()
        {
            return MCEBuddyConf.GlobalMCEConfig.ConfigSettings;
        }

        /// <summary>
        /// Gets the JobStatus for a given job queue
        /// </summary>
        /// <param name="jobNumber">JobQueue Number</param>
        /// <returns>JobStatus</returns>
        public JobStatus GetJobStatus(int jobNumber)
        {
            JobStatus retStatus = null;

            Monitor.Enter(monitorLock); // Need to ensure when this function is called, the Engine is not in Starting or stopping mode
            Monitor.Enter(_queueManager.Queue); //the lock is always on the jobQueue
            try
            {
                if (jobNumber < _maxConcurrentJobs)
                {
                    if (_conversionJobs[jobNumber] != null)
                    {
                        if (_conversionJobs[jobNumber].Status != null)
                        {
                            retStatus = _conversionJobs[jobNumber].Status;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.AppLog.WriteEntry(this, "Unable to get Job Status. MaxJobs = " + _maxConcurrentJobs.ToString() + " Job Stautus Req = " + jobNumber.ToString() + " Conversion Jobs Initialized = " + _conversionJobs.Length.ToString(), Log.LogEntryType.Error, true);
                Log.AppLog.WriteEntry(this, "Error -> " + e.ToString(), Log.LogEntryType.Error, true);
            }
            Monitor.Exit(_queueManager.Queue); //the lock is always on the jobQueue
            Monitor.Exit(monitorLock); // Need to ensure when this function is called, the Engine is not in Starting or stopping mode

            return retStatus;
        }

        /// <summary>
        /// Moves a job in the queue to a new location in the queue
        /// </summary>
        /// <param name="currentJobNo">Job no for the job to move</param>
        /// <param name="newJobNo">New location to move to (0 based index)</param>
        public bool UpdateFileQueue(int currentJobNo, int newJobNo)
        {
            bool ret = false;

            Monitor.Enter(monitorLock); // Need to ensure when this function is called, the Engine is not in Starting or stopping mode
            Monitor.Enter(_queueManager.Queue); // Important otherwise we get an exception that queue has changed
            try
            {
                if ((currentJobNo >= _maxConcurrentJobs) && (newJobNo >= _maxConcurrentJobs) && (currentJobNo < _queueManager.Queue.Count) && (newJobNo <= _queueManager.Queue.Count))
                {
                    ConversionJob item = _queueManager.Queue[currentJobNo];

                    _queueManager.Queue.RemoveAt(currentJobNo);

                    if (newJobNo > currentJobNo)
                        newJobNo--; // the actual index could have shifted due to the removal 

                    _queueManager.Queue.Insert(newJobNo, item);

                    ret = true; // Success
                }
                else
                    Log.AppLog.WriteEntry(this, "Unable to move Job, current of new job number less then number of max concurrent jobs or greater than current queue size", Log.LogEntryType.Error, true);
            }
            catch (Exception e)
            {
                Log.AppLog.WriteEntry(this, "Unable to move Job. MaxJobs = " + _maxConcurrentJobs.ToString() + " Current Job No = " + currentJobNo.ToString() + " New Job No = " + newJobNo.ToString() + " Conversion Jobs Initialized = " + _conversionJobs.Length.ToString(), Log.LogEntryType.Error, true);
                Log.AppLog.WriteEntry(this, "Error -> " + e.ToString(), Log.LogEntryType.Error, true);
            }
            Monitor.Exit(_queueManager.Queue);
            Monitor.Exit(monitorLock); // Need to ensure when this function is called, the Engine is not in Starting or stopping mode

            return ret;
        }

        /// <summary>
        /// Returns a list of all the files in the conversion queue
        /// </summary>
        /// <returns>List of Jobs with Source video fileName and Conversion task name</returns>
        public List<string[]> FileQueue()
        {
            List<string[]> fileList = new List<string[]>();

            Monitor.Enter(monitorLock); // Need to ensure when this function is called, the Engine is not in Starting or stopping mode
            Monitor.Enter(_queueManager.Queue); // Important otherwise we get an exception that queue has changed
            foreach (ConversionJob job in _queueManager.Queue)
            {
                string[] item = new string[2];
                item[0] = job.OriginalFileName;
                item[1] = job.TaskName;
                fileList.Add(item);
            }
            Monitor.Exit(_queueManager.Queue);
            Monitor.Exit(monitorLock); // Need to ensure when this function is called, the Engine is not in Starting or stopping mode

            return fileList;
        }

        /// <summary>
        /// Adds a manual job to the queue for conversion
        /// </summary>
        /// <param name="videoFilePath">Fully qualified path to video file</param>
        public void AddManualJob(string videoFilePath)
        {
            // Add to the manual queue file to pick up
            Ini manualQueueIni = new Ini(GlobalDefs.ManualQueueFile);
            manualQueueIni.Write("ManualQueue", videoFilePath, "ManualSelect");

            // Clear the status on the manually added file incase it was converted earlier or was an output of a conversion earlier, otherwise ReScan won't pick up
            Ini historyIni = new Ini(GlobalDefs.HistoryFile);
            historyIni.Write(videoFilePath, "Status", "");
        }

        /// <summary>
        /// Called when the system shuts down MCEBuddy from Stop Service or Restart computer etc
        /// </summary>
        public void StopBySystem() //called by MCEBuddy ServiceShutdown
        {
            _serviceShutdownBySystem = true; // Indicate to app to close to avoid uninstall issues
            Thread.Sleep(300); // Give the GUI time to register the shutdown

            Log.AppLog.WriteEntry(this, "MCEBuddy Stop by system initiated", Log.LogEntryType.Information, true);

            Stop(false); // Stop the service but dont' store it since it's a reboot, we want it to start on reboot

            if (_uPnPCheckThread != null)
            {
                _uPnPEnabled = false; // Signal for thread to exit (incase the Join fails)

                // Stop the UPnP Thread
                _uPnPCheckThread.Abort(); // Abort the thread (it will disable UPnP also)
            }

            Log.AppLog.Close(); // Service shutdown, now close the system log so we can cleanup
        }

        /// <summary>
        /// Stop the MCEBuddy Monitor CORE engine thread
        /// </summary>
        /// <param name="preserveState"></param>
        public void Stop(bool preserveState)
        {
            Monitor.Enter(monitorLock);

            GlobalDefs.Suspend = false; // Reset suspension state
            GlobalDefs.Shutdown = true;

            if (_wakeUp != null)
            {
                _wakeUp.Abort();
                _wakeUp = null; //let the timer not fire again
            }

            while (_monitorThread != null)
            {
                Thread.Sleep(300);
            }

            Log.AppLog.WriteEntry(this, Localise.GetPhrase("MCEBuddy engine stopped"), Log.LogEntryType.Information, true);

            GeneralOptions go = MCEBuddyConf.GlobalMCEConfig.GeneralOptions;
            go.engineRunning = false;
            MCEBuddyConf.GlobalMCEConfig.UpdateGeneralOptions(go, preserveState); // Write the engine settings

            if (preserveState) // Check if we are asked to preserve the stop state
                Log.AppLog.WriteEntry(this, Localise.GetPhrase("Setting engine last running state to stop"), Log.LogEntryType.Information, true);

            Monitor.Exit(monitorLock);
        }

        /// <summary>
        /// Start the MCEBuddy Monitor CORE engine thread
        /// </summary>
        public void Start()
        {
            Monitor.Enter(monitorLock);

            if (_monitorThread != null)
            {
                Monitor.Exit(monitorLock);
                return;
            }

            // Re-read the Settings from the file here, this is called when the engine is Started, i.e. the settings may have changed
            MCEBuddyConf.GlobalMCEConfig = new MCEBuddyConf(GlobalDefs.ConfigFile);

            ReadConfig();

            _monitorThread = new Thread(MonitorThread);
            _monitorThread.CurrentCulture = _monitorThread.CurrentUICulture = Localise.MCEBuddyCulture;
            _monitorThread.Start();

            if ((_wakeHour > -1) && (_wakeMinute > -1))
            {
                _wakeUp = new PowerManagement.WakeUp();
                _wakeUp.Woken += WakeUp_Woken;
                SetNextWake();
            }
            Log.AppLog.WriteEntry(this, Localise.GetPhrase("MCEBuddy engine started.  Setting engine last running state to start."), Log.LogEntryType.Debug, true);

            GeneralOptions go = MCEBuddyConf.GlobalMCEConfig.GeneralOptions;
            go.engineRunning = true;
            MCEBuddyConf.GlobalMCEConfig.UpdateGeneralOptions(go, true); // Write the engine settings

            Monitor.Exit(monitorLock);
        }

        /// <summary>
        /// Cancel a job currently in the conversion queue (not job queue)
        /// </summary>
        /// <param name="jobList">Job number from the conversion queue</param>
        public void CancelJob(int[] jobList)
        {
            Monitor.Enter(monitorLock); // Need to ensure when this function is called, the Engine is not in Starting or stopping mode
            Monitor.Enter(_queueManager.Queue);

            List<ConversionJob> convJob = new List<ConversionJob>();
            for (int i = 0; i < jobList.Length; i++)
            {
                //Build the list of conversion jobs to cancel, don't use job numbers since they will change once removed from the queue
                if ((jobList[i] >= 0) && (jobList[i] < _queueManager.Queue.Count))
                    convJob.Add(_queueManager.Queue[jobList[i]]);
            }

            foreach (ConversionJob cj in convJob)
            {
                if (cj != null)
                {
                    if (cj.Active) // if job is active, remove from manual queue and mark it cancelled for monitor thread to process
                    {
                        Log.AppLog.WriteEntry(this, Localise.GetPhrase("Active job signalled cancellation for") + " " + cj.OriginalFileName, Log.LogEntryType.Information, true);
                        cj.Status.Cancelled = true;
                    }
                    else
                    {
                        Log.AppLog.WriteEntry(this, Localise.GetPhrase("Inactive job cancelled") + " " + cj.OriginalFileName, Log.LogEntryType.Information, true);

                        cj.Status.Cancelled = true; // mark the job as cancelled (for write history and tracking)

                        // First delete the job from the manual file entry while the ref is active, if this is the last task for the job
                        if (_queueManager.JobCount(cj.OriginalFileName) <= 1)
                        {
                            Ini iniManualQueue = new Ini(GlobalDefs.ManualQueueFile);
                            iniManualQueue.DeleteKey("ManualQueue", cj.Status.SourceFile);
                        }

                        WriteHistory(cj); //  Write to history file (it will check for last task for job)

                        _queueManager.Queue.Remove(cj); // now remove the job from the queue
                    }
                }
            }

            Monitor.Exit(_queueManager.Queue);
            Monitor.Exit(monitorLock); // Need to ensure when this function is called, the Engine is not in Starting or stopping mode
        }


        /// <summary>
        /// Suspend or Resume the conversion process
        /// </summary>
        /// <param name="suspend">True to suspend, false to resume</param>
        public void SuspendConversion(bool suspend)
        {
            GlobalDefs.Suspend = suspend;
        }

        /// <summary>
        /// Check if the conversions are suspended
        /// </summary>
        /// <returns>True if tasks are suspended</returns>
        public bool IsSuspended()
        {
            return GlobalDefs.Suspend;
        }

        /// <summary>
        /// Change the priority of MCEBuddy and child tasks
        /// </summary>
        /// <param name="processPriority">High, Normal or Low</param>
        public void ChangePriority(string processPriority)
        {
            //Change the priority of the of Global Objects used to start conversion theads
            //Process process = Process.GetCurrentProcess(); //Get an instance of current process // Do not change current process as it makes it unresponsive and faults the pipe timeout

            switch (processPriority)
            {
                case "High":
                    //process.PriorityClass = ProcessPriorityClass.AboveNormal;
                    GlobalDefs.Priority = ProcessPriorityClass.AboveNormal;
                    GlobalDefs.IOPriority = MCEBuddy.Globals.PriorityTypes.ABOVE_NORMAL_PRIORITY_CLASS;
                    break;

                case "Normal":
                    //process.PriorityClass = ProcessPriorityClass.Normal;
                    GlobalDefs.Priority = ProcessPriorityClass.Normal;
                    GlobalDefs.IOPriority = MCEBuddy.Globals.PriorityTypes.NORMAL_PRIORITY_CLASS;
                    break;

                case "Low":
                    //process.PriorityClass = ProcessPriorityClass.Idle;
                    GlobalDefs.Priority = ProcessPriorityClass.Idle;
                    GlobalDefs.IOPriority = MCEBuddy.Globals.PriorityTypes.IDLE_PRIORITY_CLASS;
                    break;

                default:
                    //process.PriorityClass = ProcessPriorityClass.Normal;
                    GlobalDefs.Priority = ProcessPriorityClass.Normal;
                    GlobalDefs.IOPriority = MCEBuddy.Globals.PriorityTypes.NORMAL_PRIORITY_CLASS;
                    break;
            }

            // Store the new value
            GeneralOptions go = MCEBuddyConf.GlobalMCEConfig.GeneralOptions;
            go.processPriority = processPriority;
            MCEBuddyConf.GlobalMCEConfig.UpdateGeneralOptions(go, true);
        }

        /// <summary>
        /// Used to change the state of UPnP Mappings
        /// </summary>
        /// <param name="enable">True to enable UPnP, false to disable it</param>
        public void SetUPnPState(bool enable)
        {
            if (enable)
            {
                // Check if UPnP Monitor thread is running, if not then start it
                if (_uPnPCheckThread == null)
                {
                    _uPnPEnabled = true; // Flag to keep thread going

                    // Create a thread that enabled UPnP keeps checking on the UPnP port mapping (some routers drop the mapping)
                    _uPnPCheckThread = new Thread(UPnPMonitorThread);
                    _uPnPCheckThread.CurrentCulture = _uPnPCheckThread.CurrentUICulture = Localise.MCEBuddyCulture;
                    _uPnPCheckThread.Start();
                }
            }
            else
            {
                // Check if the UPnP thread is running, if so then stop it
                if (_uPnPCheckThread != null)
                {
                    _uPnPEnabled = false; // Just in case abort/join fails
                    _uPnPCheckThread.Abort(); // Abort the thread (it will disable UPnP also)
                }
            }
        }

        /// <summary>
        /// Gets the Audio, Video and other media info for a file
        /// </summary>
        /// <param name="videoFilePath">Path to the video file</param>
        /// <returns>Media information</returns>
        public MediaInfo GetFileInfo(string videoFilePath)
        {
            // Get the properties of this source video
            JobStatus jobStatus = new JobStatus();

            // Get the FPS from MediaInfo, more reliable then FFMPEG but it doesn't always work
            float FPS = 0;

            try
            {
                MediaInfoDll mi = new MediaInfoDll();
                mi.Open(videoFilePath);
                mi.Option("Inform", "Video; %FrameRate%");
                float.TryParse(mi.Inform(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out FPS);
            }
            catch
            {
                FPS = 0; // reset it
            }

            FFmpegMediaInfo videoInfo = new FFmpegMediaInfo(videoFilePath, ref jobStatus, Log.AppLog, true); // cannot suspend during a UI request else it hangs
            videoInfo.Run();
            if (videoInfo.Success && !videoInfo.ParseError)
            {
                if (FPS != 0) // MediaInfo worked
                    videoInfo.MediaInfo.VideoInfo.FPS = FPS; // update it
            }
            else
            {
                Log.AppLog.WriteEntry(Localise.GetPhrase("Error trying to get Audio Video information"), Localise.GetPhrase("Unable to Read Media File"), Log.LogEntryType.Error, true);
            }

            return videoInfo.MediaInfo;
        }

        /// <summary>
        /// Generates a XML file for the source video containing XBMC style metadata for the file (taken from the internet) and named as per the original file along side the original file
        /// </summary>
        /// <param name="jobList">List of job numbers for which to generate the XML file</param>
        public void GenerateXML(int[] jobList)
        {
            Monitor.Enter(_queueManager.Queue); // Queue cannot be manipulated while we build the list of jobs

            List<string> fileList = new List<string>();
            for (int i = 0; i < jobList.Length; i++)
            {
                //Build the list of files, don't use job numbers since they will change once removed from the queue
                if ((jobList[i] >= 0) && (jobList[i] < _queueManager.Queue.Count))
                    fileList.Add(_queueManager.Queue[jobList[i]].OriginalFileName);
            }

            Monitor.Exit(_queueManager.Queue);

            foreach (string file in fileList)
            {
                JobStatus jobStatus = new JobStatus();
                MetaData.VideoMetaData metaData = new MetaData.VideoMetaData(file, true, ref jobStatus, Log.AppLog);
                metaData.Extract(); // Extract and download show information
                VideoInfo videoFile = new VideoInfo(file, ref jobStatus, Log.AppLog, true); // Get media properties, do not suspend since GUI will hang
                metaData.WriteXBMCXMLTags(file, Path.GetDirectoryName(file), videoFile); // source and target file name/directory are the same here
            }

            return;
        }


        /// <summary>
        /// Create administrative access to directory for reading, writing and modifying
        /// </summary>
        /// <param name="dirPath">Path to directory</param>
        private void CreateExposedDirectory(string dirPath) // this enables us to take administrative right on a directory and files that was installed by a 3rd party (e.g. installer) so we can write to it
        {
            if (!Directory.Exists(dirPath))
            {
                try
                {
                    Directory.CreateDirectory(dirPath);
                }
                catch (Exception e)
                {
                    Log.WriteSystemEventLog("Unable to create directory during installation. Error:" + e.ToString(), EventLogEntryType.Error);
                }
            }

            DirectoryInfo dirInfo = new DirectoryInfo(dirPath);

            SecurityIdentifier sid = new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.WorldSid, null);
            NTAccount acct = sid.Translate(typeof(System.Security.Principal.NTAccount)) as System.Security.Principal.NTAccount;

            FileSystemAccessRule rule = new FileSystemAccessRule(acct.ToString(), FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow);
            if (!dirInfo.Exists)
            {
                DirectorySecurity security = new DirectorySecurity();
                security.SetAccessRule(rule);
                dirInfo.Create(security);
            }
            else
            {
                DirectorySecurity security = dirInfo.GetAccessControl();
                security.AddAccessRule(rule);
                dirInfo.SetAccessControl(security);
            }
        }

        /// <summary>
        /// Setup the engine configuration from the GlobalMCEConfig object
        /// </summary>
        private void ReadConfig()
        {
            GeneralOptions go = MCEBuddyConf.GlobalMCEConfig.GeneralOptions;

            if ((!String.IsNullOrEmpty(go.tempWorkingPath)) && (!Directory.Exists(go.tempWorkingPath)))
            {
                Log.AppLog.WriteEntry(this, "Temporary working directory " + go.tempWorkingPath + " does not exist! Trying to create it.", Log.LogEntryType.Information, true);
                try
                {
                    Directory.CreateDirectory(go.tempWorkingPath);
                }
                catch (Exception e)
                {
                    Log.AppLog.WriteEntry(this, "Unable to create temporary working directory " + go.tempWorkingPath + ". Using default working directory. Error: " + e.ToString(), Log.LogEntryType.Error, true);
                    go.tempWorkingPath = "";
                }
            }
            int logLevel = go.logLevel;
            if ((logLevel < 0) || (logLevel > 3)) logLevel = 1;
            Log.LogLevel = (Log.LogEntryType)logLevel;

            _maxConcurrentJobs = go.maxConcurrentJobs;
            _pollPeriod = go.pollPeriod;
            logKeepDays = go.logKeepDays;

            _wakeHour = go.wakeHour;
            _wakeMinute = go.wakeMinute; 
            _startHour = go.startHour;
            _startMinute = go.startMinute;
            _stopHour = go.stopHour;
            _stopMinute = go.stopMinute;
            if ((_startHour < 0) || (_startHour > 23)) _startHour = -1;
            if ((_startMinute < 0) || (_startMinute > 59)) _startMinute = -1;
            if ((_stopHour < 0) || (_stopHour > 23)) _stopHour = -1;
            if ((_stopMinute < 0) || (_stopMinute > 59)) _stopMinute = -1;
            if ((_wakeHour < 0) || (_wakeHour > 23)) _wakeHour = -1;
            if ((_wakeMinute < 0) || (_wakeMinute > 59)) _wakeMinute = -1;

            daysOfWeek = go.daysOfWeek.Split(',');

            _deleteOriginal = go.deleteOriginal;
            _useRecycleBin = go.useRecycleBin;
            _archiveOriginal = go.archiveOriginal;
            _allowSleep = go.allowSleep;

            ChangePriority(go.processPriority); // Set the default process priority on load

            _conversionJobs = new ConversionJob[_maxConcurrentJobs]; // Update the number of jobs
            _queueManager = new QueueManager(); // update the search paths and UNC credentials

            _uPnPEnabled = go.uPnPEnable;

            string locale = go.locale;
            Localise.Init(locale);

            Log.AppLog.WriteEntry(this, Localise.GetPhrase("Loaded MCEBuddy engine settings"), Log.LogEntryType.Debug, true);
            
            //Debug, dump all the settings to help with debugging
            Log.AppLog.WriteEntry("Windows OS Version -> " + Environment.OSVersion.ToString(), Log.LogEntryType.Debug);
            Log.AppLog.WriteEntry("Windows Platform -> " + (MCEBuddy.Globals.GlobalDefs.Is64BitOperatingSystem ? "64 Bit" : "32 Bit"), Log.LogEntryType.Debug);
            Log.AppLog.WriteEntry("MCEBuddy Platform -> " + ((IntPtr.Size == 4) ? "32 Bit" : "64 Bit"), Log.LogEntryType.Debug);
            string currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            Log.AppLog.WriteEntry("MCEBuddy Current Version : " + currentVersion, Log.LogEntryType.Debug);
            Log.AppLog.WriteEntry("MCEBuddy Build Date : " + File.GetLastWriteTime(System.Reflection.Assembly.GetExecutingAssembly().Location).ToString(CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
            Log.AppLog.WriteEntry("Locale Language -> " + Localise.ThreeLetterISO().ToUpper(), Log.LogEntryType.Debug);
            Log.AppLog.WriteEntry(this, "MCEBuddy engine settings -> " + MCEBuddyConf.GlobalMCEConfig.ToString(), Log.LogEntryType.Debug);
        }

        /// <summary>
        /// Returns the number of days to add to the given day of
        /// the week in order to calculate the next occurrence of the
        /// desired day of the week.
        /// </summary>
        /// <param name="current">
        ///		The current day of the week.
        /// </param>
        /// <param name="desired">
        ///		The desired day of the week.
        /// </param>
        /// <returns>
        ///		The number of days to add to <var>current</var> day of week
        ///		in order to achieve the next <var>desired</var> day of week.
        /// </returns>
        private int DaysToAdd(DayOfWeek current, DayOfWeek desired)
        {
            // f( c, d ) = [7 - (c - d)] mod 7
            //   where 0 <= c < 7 and 0 <= d < 7

            int c = (int)current;
            int d = (int)desired;
            return (7 - c + d) % 7;
        }

        /// <summary>
        /// Sets the next wake timer after the system wakesup from sleep
        /// </summary>
        private void SetNextWake()
        {
            if ((_wakeHour < 0) || (_wakeHour > 23) || (_wakeMinute < 0) ||( _wakeMinute > 59))
                return;

            //Calc the next wake time
            DateTime now = DateTime.Now;
            DateTime wakeTime = new DateTime(now.Year, now.Month, now.Day, _wakeHour, _wakeMinute, 0);
            if (wakeTime < now) wakeTime = wakeTime.AddDays(1);

            // Now re-calculate the next day of the week to set the wakeup based on what days are chosen
            // First get the current scheduled wakeup day of the week
            DayOfWeek cd = wakeTime.DayOfWeek;

            // Find the next day of the week enabled for a wakeup, loop through all the days of a the week (0-> Sunday, 6->Saturday)
            while (true)
            {
                if (daysOfWeek.Contains(cd.ToString()))
                {
                    wakeTime = wakeTime.AddDays(DaysToAdd(wakeTime.DayOfWeek, cd));
                    break;
                }

                cd = (DayOfWeek)((int)++cd % 7); // Check the next day, loop around EOW
            }

            //Set the wake timer
            if (_wakeUp != null) // if it null then MCEBuddy has been stopped or shutdown, no timer - will be reset when the engine starts
            {
                _wakeUp.Abort();
                _wakeUp.SetWakeUpTime(wakeTime);
            }
        }

        /// <summary>
        /// System entry after the systems awakes from sleep (configured wake timer from MCEBuddy)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void WakeUp_Woken(object sender, EventArgs e)
        {
            SetNextWake();
        }

        private void WriteHistory( ConversionJob job)
        {
            Ini historyIni = new Ini(GlobalDefs.HistoryFile);
            GeneralOptions go = MCEBuddyConf.GlobalMCEConfig.GeneralOptions;

            bool sendEMail = go.sendEmail; // do we need to send an eMail after each job
            bool sendSuccess = go.eMailSettings.successEvent;
            bool sendFailed = go.eMailSettings.failedEvent;
            bool sendCancelled = go.eMailSettings.cancelledEvent;

            string result = "Converted";
            if (String.IsNullOrEmpty(job.ConvertedFile)) result = "NoMetaMatch";
            if (job.Status.Error) result = "Error";
            if (job.Status.Cancelled) result = "Cancelled"; // Cancelled should be the last status to set because an error can be set if is cancelled

            int convCount = 0;
            while (historyIni.ReadString(job.OriginalFileName, result + "At" + convCount.ToString(System.Globalization.CultureInfo.InvariantCulture), "") != "")
                convCount++;
            historyIni.Write(job.OriginalFileName, result + "At" + convCount.ToString(System.Globalization.CultureInfo.InvariantCulture), DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture));

            if (result == "Converted")
            {
                historyIni.Write(job.OriginalFileName, "ConvertedTo" + convCount.ToString(System.Globalization.CultureInfo.InvariantCulture), job.ConvertedFile);
                
                //Ensure converted files are not then re-converted during a scan
                historyIni.Write(job.ConvertedFile, "Status", "OutputFromConversion"); // Status indicates destination file is output of an conversion, if the same file is added back for reconversion then it would log as converted
                historyIni.Write(job.ConvertedFile, "ConvertedToOutputAt" + convCount.ToString(System.Globalization.CultureInfo.InvariantCulture), DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture));

                // Send an eMail if required
                if (sendEMail && sendSuccess)
                {
                    string subject = Localise.GetPhrase("MCEBuddy successfully converted a video");
                    string message = Localise.GetPhrase("Source Video") + " -> " + job.OriginalFileName + "\r\n";
                    message += Localise.GetPhrase("Converted Video") + " -> " + job.ConvertedFile + "\r\n";
                    message += Localise.GetPhrase("Profile") + " -> " + job.Profile + "\r\n";
                    message += Localise.GetPhrase("Conversion Task") + " -> " + job.TaskName + "\r\n";
                    message += Localise.GetPhrase("Converted At") + " -> " + DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture);

                    new Thread(() => eMail.SendEMail(go.eMailSettings, subject, message, ref Log.AppLog, this)).Start(); // Send the eMail through a thead
                }
            }

            if (result == "Error")
            {
                historyIni.Write(job.OriginalFileName, "ErrorMessage" + convCount.ToString(System.Globalization.CultureInfo.InvariantCulture), job.Status.ErrorMsg);
                historyIni.Write(job.OriginalFileName, "ErrorUsingProfile" + convCount.ToString(System.Globalization.CultureInfo.InvariantCulture), job.Profile);
                historyIni.Write(job.OriginalFileName, "ErrorUsingTask" + convCount.ToString(System.Globalization.CultureInfo.InvariantCulture), job.TaskName);

                // Send an eMail if required
                if (sendEMail && sendFailed)
                {
                    string subject = Localise.GetPhrase("MCEBuddy failed to converted a video");
                    string message = Localise.GetPhrase("Source Video") + " -> " + job.OriginalFileName + "\r\n";
                    message += Localise.GetPhrase("Profile") + " -> " + job.Profile + "\r\n";
                    message += Localise.GetPhrase("Conversion Task") + " -> " + job.TaskName + "\r\n";
                    message += Localise.GetPhrase("Error") + " -> " + job.Status.ErrorMsg + "\r\n";
                    message += Localise.GetPhrase("Failed At") + " -> " + DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture);

                    new Thread(() => eMail.SendEMail(go.eMailSettings, subject, message, ref Log.AppLog, this)).Start(); // Send the eMail through a thead
                }
            }

            if (result == "Cancelled")
            {
                // Send an eMail if required
                if (sendEMail && sendCancelled)
                {
                    string subject = Localise.GetPhrase("MCEBuddy cancelled a video conversion");
                    string message = Localise.GetPhrase("Source Video") + " -> " + job.OriginalFileName + "\r\n";
                    message += Localise.GetPhrase("Profile") + " -> " + job.Profile + "\r\n";
                    message += Localise.GetPhrase("Conversion Task") + " -> " + job.TaskName + "\r\n";
                    message += Localise.GetPhrase("Cancelled At") + " -> " + DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture);

                    new Thread(() => eMail.SendEMail(go.eMailSettings, subject, message, ref Log.AppLog, this)).Start(); // Send the eMail through a thead
                }
            }

            if ( _queueManager.JobCount(job.OriginalFileName) <= 1)
            {
                // This is the last job processing this file, so write the status to the history file STATUS
                historyIni.Write(job.OriginalFileName, "Status", result); // Source file status
            }
        }

        /// <summary>
        /// Checks the log folder to clean up old log files if required
        /// </summary>
        /// <returns></returns>
        private void CleanLogFiles()
        {
            if (logKeepDays == 0)
                return; // Keep log files forever

            IEnumerable<string> foundFiles;
            try
            {
                //foundFiles = Directory.EnumerateFiles(GlobalDefs.LogPath, "*.log", SearchOption.TopDirectoryOnly).OrderBy(File.GetLastWriteTime); // We sort the files by last modified time
                foundFiles = Directory.GetFiles(GlobalDefs.LogPath, "*.log", SearchOption.TopDirectoryOnly).OrderBy(File.GetLastWriteTime); // We sort the files by last modified time
            }
            catch (Exception ex)
            {
                Log.AppLog.WriteEntry(Localise.GetPhrase("Unable to search for files in location") + " " + GlobalDefs.LogPath + "\r\n" + ex.Message, Log.LogEntryType.Warning);
                foundFiles = new List<string>();
            }
            if (foundFiles != null)
            {
                foreach (string foundFile in foundFiles)
                {
                    // Found a file
                    if (File.GetLastWriteTime(foundFile).AddDays(logKeepDays) < DateTime.Now) // check if the log file is older than requested
                    {
                        Log.AppLog.WriteEntry(Localise.GetPhrase("Deleting log file") + " " + foundFile + "\r\n", Log.LogEntryType.Debug);
                        Util.FileIO.TryFileDelete(foundFile); // Delete the log file
                    }
                }
            }

            return;
        }

        /// <summary>
        /// Thread to keep monitoring status of UPnP and keep the mappings active
        /// </summary>
        private void UPnPMonitorThread()
        {
            Log.WriteSystemEventLog("MCEBuddy starting UPnP Monitor Thread", EventLogEntryType.Information);

            // Check/Enable UPnP - verbose
            //UPnP.EnableUPnP(MCEBuddyConf.GlobalMCEConfig.GeneralOptions.localServerPort, true);

            try
            {
                while (_uPnPEnabled) // Check this since sometimes the Join/Abort signal may not come (EnableUPnP uses try/catch which can interrupt a join/abort)
                {
                    Thread.Sleep(GlobalDefs.UPNP_POLL_PERIOD); // Wait for a while to repoll

                    // Check/Enable UPnP - non verbose
                    //UPnP.EnableUPnP(MCEBuddyConf.GlobalMCEConfig.GeneralOptions.localServerPort, false);
                }
            }
            catch // Catch an Abort or Join
            {
                // Disable UPnP Port Forwarding - verbose
                //UPnP.DisableUPnP(MCEBuddyConf.GlobalMCEConfig.GeneralOptions.localServerPort, true);

                _uPnPCheckThread = null; // This thread is dead
                Log.WriteSystemEventLog("MCEBuddy exiting UPnP Monitor Thread - abort successful", EventLogEntryType.Information);
                return;
            }

            // Just incase the Abort or Join exception was not caught
            // Disable UPnP Port Forwarding - verbose
            //UPnP.DisableUPnP(MCEBuddyConf.GlobalMCEConfig.GeneralOptions.localServerPort, true);

            _uPnPCheckThread = null; // This thread is dead
            Log.WriteSystemEventLog("MCEBuddy exiting UPnP Monitor Thread - abort Failed", EventLogEntryType.Information);
            return;
        }

        /// <summary>
        /// Checks if the current time is within the configured Conversion Start times
        /// </summary>
        /// <returns>True is it is within the configured conversion start time</returns>
        private bool WithinConversionTimes()
        {
            if ((_startHour < 0) || (_startMinute < 0) || (_stopHour < 0) || (_stopMinute < 0)) return true;

            DateTime rn = DateTime.Now;
            DateTime startTime = new DateTime(rn.Year, rn.Month, rn.Day, _startHour, _startMinute, 0);
            DateTime endTime = new DateTime(rn.Year, rn.Month, rn.Day, _stopHour, _stopMinute, 0);

            // Fix for start time > end time (e.g. 11PM to 11AM)
            if ((startTime > endTime) && (rn.Hour >= 0) && (rn.Hour < 12))
                startTime = startTime.AddDays(-1); // If we are past midnight and before noon, we reduce the start to the day before
            else if (startTime > endTime)
                endTime = endTime.AddDays(1); // else we jump the endTime forward

            return ((rn >= startTime) && (rn <= endTime) && daysOfWeek.Contains(rn.DayOfWeek.ToString())); // If we are between Start and Stop time and also on the right day of the week
        }

        /// <summary>
        /// Main CORE engine thread of MCEBuddy which runs in the background to check for new jobs, scans for new files, checks for updates to MCEBuddy etc
        /// </summary>
        private void MonitorThread() // Background thread that check for job starting, additions and completion
        {
            try
            {
                DateTime LastCheck = DateTime.Now.AddSeconds(_pollPeriod * -1);
                GlobalDefs.Shutdown = false;
                while (!GlobalDefs.Shutdown)
                {
                    // Check for updated version and messages
                    if (DateTime.Now > _lastUpdateCheck.AddHours(GlobalDefs.random.Next(12, 25)))
                    {
                        Log.AppLog.WriteEntry(this, Localise.GetPhrase("Checking for new version of MCEBuddy"), Log.LogEntryType.Information);
                        _lastUpdateCheck = DateTime.Now;
                        _updateCheckThread = new Thread(new ThreadStart(_updateCheck.Check));
                        _updateCheckThread.SetApartmentState(ApartmentState.STA);
                        _updateCheckThread.CurrentCulture = _updateCheckThread.CurrentUICulture = Localise.MCEBuddyCulture;
                        _updateCheckThread.Start();
                    }

                    //Check for new files and clean up log files
                    if ((DateTime.Now > LastCheck.AddSeconds(_pollPeriod)) || (_rescan))
                    {
                        // check for new files
                        _rescan = false;
                        LastCheck = DateTime.Now;

                        Monitor.Enter(_queueManager.Queue); //the lock is always on the jobQueue
                        _queueManager.ScanForFiles(); // Check for new files in the monitor folders
                        if (MCEBuddyConf.GlobalMCEConfig.GeneralOptions.deleteConverted) // check if converted files need to kept in sync with source files (deleted)
                            _queueManager.SyncConvertedFiles(_useRecycleBin);
                        Monitor.Exit(_queueManager.Queue);

                        // check for log files clean up
                        CleanLogFiles();                        
                    }

                    //Check the status of the jobs and start new ones 
                    bool SomeRunning = false;
                    for (int i = 0; i < _conversionJobs.Length; i++)
                    {
                        if (_conversionJobs[i] != null)
                        {
                            // Check for running and clean up completed jobs
                            if (_conversionJobs[i].Completed)
                            {
                                Monitor.Enter(_queueManager.Queue); //the lock is always on the jobQueue

                                Log.AppLog.WriteEntry(this, Localise.GetPhrase("Job for") + " " + _conversionJobs[i].OriginalFileName + " " + Localise.GetPhrase("completed"), Log.LogEntryType.Information, true);

                                // Now delete the job from the manual file entry, if it has succeeded great, if it has failed we don't want an endless loop of conversion
                                if ((_queueManager.JobCount(_conversionJobs[i].OriginalFileName) <= 1)) // Whether it is cancelled of finished delete from the manual queue either way
                                {
                                    Ini iniManualQueue = new Ini(GlobalDefs.ManualQueueFile);
                                    iniManualQueue.DeleteKey("ManualQueue", _conversionJobs[i].OriginalFileName);
                                }

                                // Delete/archive only if the conversion was successful and original marked for deletion and it is the last task for the job and the original file and converted file don't have the same name+path (since it's been replaced already)
                                if ((_deleteOriginal) && (_conversionJobs[i].Status.SuccessfulConversion) && (_queueManager.JobCount(_conversionJobs[i].OriginalFileName) <= 1) && (_conversionJobs[i].OriginalFileName != _conversionJobs[i].ConvertedFile))
                                {
                                    // Delete the EDL, SRT, XML, NFO etc files also along with the original file if present
                                    foreach (string supportFileExt in GlobalDefs.supportFilesExt)
                                    {
                                        string extFile = Path.Combine(Path.GetDirectoryName(_conversionJobs[i].OriginalFileName), Path.GetFileNameWithoutExtension(_conversionJobs[i].OriginalFileName) + supportFileExt); // support file

                                        Util.FileIO.TryFileDelete(extFile, _useRecycleBin); // Delete support file
                                    }

                                    Util.FileIO.TryFileDelete(_conversionJobs[i].OriginalFileName, _useRecycleBin); // delete original file
                                    Log.AppLog.WriteEntry(this, "Deleting original file " + _conversionJobs[i].OriginalFileName, Log.LogEntryType.Debug, true);
                                }
                                else if ((_archiveOriginal) && (_conversionJobs[i].Status.SuccessfulConversion) && (_queueManager.JobCount(_conversionJobs[i].OriginalFileName) <= 1) && (_conversionJobs[i].OriginalFileName != _conversionJobs[i].ConvertedFile))
                                {
                                    string pathName = Path.GetDirectoryName(_conversionJobs[i].OriginalFileName); //get the directory name
                                    if (!pathName.ToLower().Contains((string.IsNullOrEmpty(MCEBuddyConf.GlobalMCEConfig.GeneralOptions.archivePath) ? GlobalDefs.MCEBUDDY_ARCHIVE.ToLower() : MCEBuddyConf.GlobalMCEConfig.GeneralOptions.archivePath.ToLower()))) //check if we are currently operating from the archive folder (manual queue), in which case don't archive
                                    {
                                        string archivePath = MCEBuddyConf.GlobalMCEConfig.GeneralOptions.archivePath; // use the specified archive path

                                        if (archivePath == "") // Default archive location to be used
                                            archivePath = Path.Combine(pathName, GlobalDefs.MCEBUDDY_ARCHIVE); //update the path name for a new sub-directory called Archive

                                        Util.FilePaths.CreateDir(archivePath); //create the sub-directory if required
                                        string newFilePath = Path.Combine(archivePath, Path.GetFileName(_conversionJobs[i].OriginalFileName));

                                        Log.AppLog.WriteEntry(this, "Archiving original file " + _conversionJobs[i].OriginalFileName + " to Archive folder " + archivePath, Log.LogEntryType.Debug);

                                        try
                                        {
                                            // Archive the EDL, SRT, XML, NFO etc files also along with the original file if present
                                            foreach (string supportFileExt in GlobalDefs.supportFilesExt)
                                            {
                                                string extFile = Path.Combine(Path.GetDirectoryName(_conversionJobs[i].OriginalFileName), Path.GetFileNameWithoutExtension(_conversionJobs[i].OriginalFileName) + supportFileExt); // Saved support file

                                                if (File.Exists(extFile))
                                                    File.Move(extFile, Path.Combine(archivePath, Path.GetFileName(extFile)));
                                            }

                                            // Last file to move
                                            File.Move(_conversionJobs[i].OriginalFileName, newFilePath); //move the file into the archive folder
                                        }
                                        catch (Exception e)
                                        {
                                            Log.AppLog.WriteEntry(this, "Unable to move converted file " + _conversionJobs[i].OriginalFileName + " to Archive folder " + archivePath, Log.LogEntryType.Error);
                                            Log.AppLog.WriteEntry(this, "Error : " + e.ToString(), Log.LogEntryType.Error);
                                        }
                                    }
                                }

                                //First write the history and finish the job, then remove from the queue
                                WriteHistory(_conversionJobs[i]);

                                _queueManager.Queue.Remove(_conversionJobs[i]);
                                _conversionJobs[i] = null;

                                Monitor.Exit(_queueManager.Queue);
                            }
                            else
                            {
                                SomeRunning = true;
                            }
                        }
                        else
                        {
                            // Start new jobs
                            if (WithinConversionTimes())
                            {
                                Monitor.Enter(_queueManager.Queue);
                                for (int j = 0; j < _queueManager.Queue.Count; j++)
                                {
                                    if ((!_queueManager.Queue[j].Completed) && (!_queueManager.Queue[j].Active))
                                    {
                                        _conversionJobs[i] = _queueManager.Queue[j];
                                        // Checking for a custom temp working path for conversion (other drives/locations) 
                                        _conversionJobs[i].WorkingPath = Path.Combine((String.IsNullOrEmpty(MCEBuddyConf.GlobalMCEConfig.GeneralOptions.tempWorkingPath) ? GlobalDefs.AppPath : MCEBuddyConf.GlobalMCEConfig.GeneralOptions.tempWorkingPath), "working" + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                                        // Start the conversion
                                        _conversionJobs[i].StartConversionThread();

                                        SomeRunning = true; // Update status

                                        // Send an eMail if required
                                        GeneralOptions go = MCEBuddyConf.GlobalMCEConfig.GeneralOptions;
                                        bool sendEMail = go.sendEmail; // do we need to send an eMail after each job
                                        bool sendStart = go.eMailSettings.startEvent;
                                        if (sendEMail && sendStart)
                                        {
                                            string subject = Localise.GetPhrase("MCEBuddy started a video conversion");
                                            string message = Localise.GetPhrase("Source Video") + " -> " + _conversionJobs[i].OriginalFileName + "\r\n";
                                            message += Localise.GetPhrase("Profile") + " -> " + _conversionJobs[i].Profile + "\r\n";
                                            message += Localise.GetPhrase("Conversion Task") + " -> " + _conversionJobs[i].TaskName + "\r\n";
                                            message += Localise.GetPhrase("Conversion Started At") + " -> " + DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture);

                                            new Thread(() => eMail.SendEMail(go.eMailSettings, subject, message, ref Log.AppLog, this)).Start(); // Send the eMail through a thead
                                        }

                                        Log.AppLog.WriteEntry(this, "Job for " + _conversionJobs[i].OriginalFileName + " started", Log.LogEntryType.Information, true);
                                        Log.AppLog.WriteEntry(this, "Temp working path is " + _conversionJobs[i].WorkingPath, Log.LogEntryType.Debug, true);
                                        break;
                                    }
                                }
                                Monitor.Exit(_queueManager.Queue);
                            }
                        }
                    }

                    if ((GlobalDefs.Active) && !SomeRunning)
                    {
                        Log.AppLog.WriteEntry(this, Localise.GetPhrase("No conversions running , allowing system sleep"), Log.LogEntryType.Debug, true);
                        Util.PowerManagement.AllowSleep();
                    }
                    else if ((!GlobalDefs.Active) && SomeRunning)
                    {
                        if (_allowSleep)
                        {
                            Log.AppLog.WriteEntry(this, Localise.GetPhrase("Starting new conversions, allowing system sleep"), Log.LogEntryType.Debug, true);
                            Util.PowerManagement.AllowSleep();
                        }
                        else
                        {
                            Log.AppLog.WriteEntry(this, Localise.GetPhrase("Starting new conversions, preventing system sleep"), Log.LogEntryType.Debug, true);
                            Util.PowerManagement.PreventSleep();
                        }
                    }
                    GlobalDefs.Active = SomeRunning;
                    _engineCrashed = false; // we've reached here so, reset it

                    // Sleep then shutdown check
                    Thread.Sleep(100);
                }

                // Shut down the threads
                Monitor.Enter(_queueManager.Queue);
                for (int i = 0; i < _conversionJobs.Length; i++)
                {
                    if (_conversionJobs[i] != null)
                    {
                        _conversionJobs[i].StopConversionThread();
                        _conversionJobs[i] = null;
                    }
                }
                _queueManager.Queue.Clear(); // Clear the entire queue
                Monitor.Exit(_queueManager.Queue);

                Util.PowerManagement.AllowSleep(); //reset to default
                GlobalDefs.Active = false;
                _monitorThread = null; // this thread is done
            }
            catch (Exception e)
            {
                Util.PowerManagement.AllowSleep(); //reset to default
                GlobalDefs.Active = false;
                _engineCrashed = true;

                GeneralOptions go = MCEBuddyConf.GlobalMCEConfig.GeneralOptions;
                go.engineRunning = false;
                MCEBuddyConf.GlobalMCEConfig.UpdateGeneralOptions(go, true); // Write the stop engine settings, since it crashed

                Log.AppLog.WriteEntry(this, "MCEBuddy Monitor Thread Crashed. Error: " + e.ToString(), Log.LogEntryType.Error, true); // This may or may not work depending upon whether the llog has been initialized otherwise it goes into NULL log
                Log.WriteSystemEventLog("MCEBuddy Monitor Thread Crashed. Error: " + e.ToString(), EventLogEntryType.Error);

                _monitorThread = null; // this thread is done
            }
        }
    }

    [ServiceContract]
    public interface ICore
    {
        [OperationContract]
        void CancelJob(int[] jobList);

        [OperationContract]
        void Start();

        [OperationContract]
        void Stop(bool preserveState);

        [OperationContract]
        void StopBySystem();

        [OperationContract]
        void Rescan();

        [OperationContract]
        int NumConversionJobs();

        [OperationContract]
        JobStatus GetJobStatus(int jobNumber);

        [OperationContract]
        bool Active();

        [OperationContract]
        bool ServiceShutdownBySystem();
        
        [OperationContract]
        List<string[]> FileQueue();

        [OperationContract]
        bool UpdateFileQueue(int currentJobNo, int newJobNo);

        [OperationContract]
        bool EngineRunning();

        [OperationContract]
        bool EngineCrashed();

        [OperationContract]
        void SuspendConversion(bool suspend);

        [OperationContract]
        bool IsSuspended();

        [OperationContract]
        void ChangePriority(string processPriority);

        [OperationContract]
        void SetUPnPState(bool enable);

        [OperationContract]
        bool UpdateConfigParameters(ConfSettings configOptions);

        [OperationContract]
        ConfSettings GetConfigParameters();

        [OperationContract]
        List<string[]> GetProfilesSummary();

        [OperationContract]
        void AddManualJob(string videoFilePath);

        [OperationContract]
        MediaInfo GetFileInfo(string videoFilePath);

        [OperationContract]
        void GenerateXML(int[] jobList);

        [OperationContract]
        bool ShowAnalyzerInstalled();

        [OperationContract]
        List<EventLogEntry> GetWindowsEventLogs();
    }
}
