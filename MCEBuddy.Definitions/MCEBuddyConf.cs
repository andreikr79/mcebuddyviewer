using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Globalization;

using MCEBuddy.Globals;
using MCEBuddy.Util;

namespace MCEBuddy.Configuration
{
    /// <summary>
    /// MCEBuddyConf contains all the Engine settings (GeneralOptions), Conversion Jobs (ConversionJobOptions) and Monitor Jobs (MonitorJobOptions) in mcebuddy.conf
    /// It can also read and write settings from a conf file into a new conf file (configIni)
    /// It does not contain any profile information
    /// </summary>
    public class MCEBuddyConf
    {
        public static MCEBuddyConf GlobalMCEConfig; // One static instance to be shared globally across all modules of the program

        private ConfSettings mceBuddyConfSettings; // Serializabele group of all settings
        private Ini configIni = null; // Config Ini file

        /// <summary>
        /// Returns the Conf settings
        /// </summary>
        public ConfSettings ConfigSettings
        {
            get { return new ConfSettings(AllConversionTasks, AllMonitorTasks, GeneralOptions); }
        }

        /// <summary>
        /// Returns a copy of the list of all conversion tasks
        /// </summary>
        public List<ConversionJobOptions> AllConversionTasks
        {
            get
            {
                List<ConversionJobOptions> cjoList = new List<ConversionJobOptions>();
                foreach (ConversionJobOptions cjo in mceBuddyConfSettings.conversionTasks)
                    cjoList.Add(cjo.Clone());
                return cjoList;
            }
        }

        /// <summary>
        /// Returns a copy of the list of all monitor tasks
        /// </summary>
        public List<MonitorJobOptions> AllMonitorTasks
        {
            get
            {
                List<MonitorJobOptions> mjoList = new List<MonitorJobOptions>();
                foreach (MonitorJobOptions mjo in mceBuddyConfSettings.monitorTasks)
                    mjoList.Add(mjo.Clone());
                return mjoList;
            }
        }

        /// <summary>
        /// Gets a copy of the General Options object
        /// </summary>
        public GeneralOptions GeneralOptions
        {
            get { return mceBuddyConfSettings.generalOptions.Clone(); }
        }

        /// <summary>
        /// Get a copy of Conversion Task Object by Name
        /// </summary>
        /// <param name="taskName">Name of conversion task</param>
        /// <returns>Copy of the conversion task object if found, else NULL</returns>
        public ConversionJobOptions GetConversionTaskByName(string taskName)
        {
            int index = mceBuddyConfSettings.conversionTasks.FindIndex(item => item.taskName == taskName);

            if (index < 0) // new task, cannot find it
                return null;
            else
                return mceBuddyConfSettings.conversionTasks[index].Clone();
        }

        /// <summary>
        /// Get a copy of the Monitor Task Object by Name
        /// </summary>
        /// <param name="taskName">Name of monitor task</param>
        /// <returns>Copy of the monitor task object if found, else NULL</returns>
        public MonitorJobOptions GetMonitorTaskByName(string taskName)
        {
            int index = mceBuddyConfSettings.monitorTasks.FindIndex(item => item.taskName == taskName);

            if (index < 0) // new task, cannot find it
                return null;
            else
                return mceBuddyConfSettings.monitorTasks[index].Clone();
        }

        /// <summary>
        /// Update the General Options, optionally write to file
        /// </summary>
        /// <param name="go">General options settings</param>
        /// <param name="write">Write to configuration file immediately</param>
        public void UpdateGeneralOptions(GeneralOptions go, bool write)
        {
            // Clone it, to avoid conflict
            mceBuddyConfSettings.generalOptions = go.Clone();

            if (write)
                WriteGeneralSettings(configIni);
        }

        /// <summary>
        /// Update a Monitor Task or Add to the list if not found (by Name), optionally write to file
        /// </summary>
        /// <param name="mjo">Monitor job options</param>
        /// <param name="write">Write to configuration file immediately</param>
        public void AddOrUpdateMonitorTask(MonitorJobOptions mjo, bool write)
        {
            int index = mceBuddyConfSettings.monitorTasks.FindIndex(item => item.taskName == mjo.taskName);

            // Clone it, to avoid conflict
            if (index < 0) // new task, cannot find it
                mceBuddyConfSettings.monitorTasks.Add(mjo.Clone());
            else
                mceBuddyConfSettings.monitorTasks[index] = mjo.Clone();

            if (write)
                WriteMonitorSettings(configIni);
        }

        /// <summary>
        /// Update a Conversion Task or Add to the list if not found (by Name), optionally write to file
        /// </summary>
        /// <param name="cjo">Conversion job options</param>
        /// <param name="write">Write to configuration file immediately</param>
        public void AddOrUpdateConversionTask(ConversionJobOptions cjo, bool write)
        {
            int index = mceBuddyConfSettings.conversionTasks.FindIndex(item => item.taskName == cjo.taskName);

            // Clone it, to avoid conflict
            if (index < 0) // new task, cannot find it
                mceBuddyConfSettings.conversionTasks.Add(cjo.Clone());
            else
                mceBuddyConfSettings.conversionTasks[index] = cjo.Clone();

            if (write)
                WriteConversionSettings(configIni);
        }

        /// <summary>
        /// Delete a conversion task from the list and optionally write to the configuration file
        /// </summary>
        /// <param name="taskName">Name of conversion task</param>
        /// <param name="write">Write to configuration file immediately</param>
        public void DeleteConversionTask(string taskName, bool write)
        {
            ConversionJobOptions cjo = mceBuddyConfSettings.conversionTasks.Find(item => item.taskName == taskName);
            if (cjo != null)
            {
                mceBuddyConfSettings.conversionTasks.Remove(cjo); // Remove from list
                if (write)
                {
                    configIni.DeleteSection(taskName); // Remove from file
                    WriteConversionTasksList(configIni);
                }
            }
        }

        /// <summary>
        /// Delete a monitor task from the list and optionally write to the configuration file
        /// </summary>
        /// <param name="taskName">Name of monitor task</param>
        /// <param name="write">Write to configuration file immediately</param>
        public void DeleteMonitorTask(string taskName, bool write)
        {
            MonitorJobOptions mjo = mceBuddyConfSettings.monitorTasks.Find(item => item.taskName == taskName);
            if (mjo != null)
            {
                mceBuddyConfSettings.monitorTasks.Remove(mjo); // Remove from list
                if (write)
                {
                    configIni.DeleteSection(taskName); // Remove from file
                    WriteMonitorTasksList(configIni);
                }
            }
        }

        /// <summary>
        /// The MCEBuddyConf object is initialized with values read from the config file specified.
        /// If there are missing values in a config file, then default values are initialized.
        /// If no config file is specified, default values are initialized and no config file is written
        /// </summary>
        /// <param name="configFile">Path to config file</param>
        public MCEBuddyConf(string configFile = "")
        {
            if (configFile == null)
                configFile = ""; // don't use null as it leads to excessive reads / writes of win.ini

            mceBuddyConfSettings.conversionTasks = new List<ConversionJobOptions>();
            mceBuddyConfSettings.monitorTasks = new List<MonitorJobOptions>();
            mceBuddyConfSettings.generalOptions = new GeneralOptions();

            if (!String.IsNullOrEmpty(configFile))
            {
                configIni = new Ini(configFile);
                ReadMonitorSettings(configIni);
                ReadConversionSettings(configIni);
                ReadGeneralSettings(configIni);
            }
        }

        /// <summary>
        /// A MCEBuddyConf object is initialized and a new config file is created/written from the MCEBuddyConf parameters passed.
        /// If no config file is specified, default values are initialized and no config file is written
        /// </summary>
        /// <param name="configOptions">Set of MCEBuddyConf parameters to copy</param>
        /// <param name="configFile">Path to config file</param>
        public MCEBuddyConf(ConfSettings configOptions, string configFile = "")
        {
            if (configFile == null)
                configFile = ""; // don't use null as it leads to excessive reads / writes of win.ini

            mceBuddyConfSettings.conversionTasks = new List<ConversionJobOptions>();
            mceBuddyConfSettings.monitorTasks = new List<MonitorJobOptions>();
            mceBuddyConfSettings.generalOptions = new GeneralOptions();

            mceBuddyConfSettings.generalOptions = configOptions.generalOptions; // Copy of the General Options

            foreach (MonitorJobOptions mjo in configOptions.monitorTasks)
                AddOrUpdateMonitorTask(mjo, true); // Add the MonitorTasks
            
            foreach (ConversionJobOptions cjo in configOptions.conversionTasks)
                AddOrUpdateConversionTask(cjo, true); // Add the Conversion Tasks

            if (!String.IsNullOrEmpty(configFile))
            {
                configIni = new Ini(configFile);
                WriteSettings(); // Write the settings to the config file
            }
        }

        /// <summary>
        /// Creates a new Config File and a new MCEBuddyConf object with parameters copied from the individual options objects passed.
        /// If no config file is specified, default values are initialized and no config file is written
        /// </summary>
        /// <param name="configFile">Config file to write to</param>
        /// <param name="go">GeneralOptions parameters</param>
        /// <param name="mjoList">List of MonitorJobOption parameters</param>
        /// <param name="cjoList">List of ConversionJobOption parameters</param>
        public MCEBuddyConf(GeneralOptions go, List<MonitorJobOptions> mjoList, List<ConversionJobOptions> cjoList, string configFile = "")
        {
            if (configFile == null)
                configFile = ""; // don't use null as it leads to excessive reads / writes of win.ini

            mceBuddyConfSettings.conversionTasks = new List<ConversionJobOptions>();
            mceBuddyConfSettings.monitorTasks = new List<MonitorJobOptions>();
            mceBuddyConfSettings.generalOptions = new GeneralOptions();

            mceBuddyConfSettings.generalOptions = go.Clone();
            
            foreach (MonitorJobOptions mjo in mjoList)
                AddOrUpdateMonitorTask(mjo, false);
            
            foreach (ConversionJobOptions cjo in cjoList)
                AddOrUpdateConversionTask(cjo, false);

            if (!String.IsNullOrEmpty(configFile))
            {
                configIni = new Ini(configFile);
                WriteSettings();
            }
        }

        /// <summary>
        /// Used to write the MCEBuddyConf configuration settings to the config file specified while creating this object
        /// </summary>
        public void WriteSettings()
        {
            if (configIni != null)
            {
                WriteMonitorSettings(configIni);
                WriteConversionSettings(configIni);
                WriteGeneralSettings(configIni);
            }
        }

        /// <summary>
        /// Used to write the MCEBuddyConf configuration settings to a new config file
        /// </summary>
        /// <param name="configfile">Path to config file</param>
        public void WriteSettings(string configFile)
        {
            if (configFile == null)
                configFile = ""; // don't use null as it leads to excessive reads / writes of win.ini

            if (!String.IsNullOrEmpty(configFile))
            {
                Ini newConfigIni = new Ini(configFile);

                WriteMonitorSettings(newConfigIni);
                WriteConversionSettings(newConfigIni);
                WriteGeneralSettings(newConfigIni);
            }
        }


        private void ReadHourMinute(Ini configIni, ref int hour, ref int minute, string prefix)
        {
            hour = configIni.ReadInteger("Engine", prefix + "Hour", -1);
            minute = configIni.ReadInteger("Engine", prefix + "Minute", -1);

            if ((hour < 0) || (hour > 24)) hour = -1;
            if ((minute < 0) || (minute > 59)) minute = -1;
        }

        private void ReadEMailSettings(Ini configIni)
        {
            mceBuddyConfSettings.generalOptions.eMailSettings.smtpServer = configIni.ReadString("Engine", "eMailServer", "");
            mceBuddyConfSettings.generalOptions.eMailSettings.port = configIni.ReadInteger("Engine", "eMailPort", 25); // default port is 25
            mceBuddyConfSettings.generalOptions.eMailSettings.ssl = configIni.ReadBoolean("Engine", "eMailSSL", false);
            mceBuddyConfSettings.generalOptions.eMailSettings.fromAddress = configIni.ReadString("Engine", "eMailFrom", "");
            mceBuddyConfSettings.generalOptions.eMailSettings.toAddresses = configIni.ReadString("Engine", "eMailTo", "");
            mceBuddyConfSettings.generalOptions.eMailSettings.successEvent = configIni.ReadBoolean("Engine", "eMailSuccess", true);
            mceBuddyConfSettings.generalOptions.eMailSettings.failedEvent = configIni.ReadBoolean("Engine", "eMailFailed", true);
            mceBuddyConfSettings.generalOptions.eMailSettings.cancelledEvent = configIni.ReadBoolean("Engine", "eMailCancelled", true);
            mceBuddyConfSettings.generalOptions.eMailSettings.startEvent = configIni.ReadBoolean("Engine", "eMailStart", true);
            mceBuddyConfSettings.generalOptions.eMailSettings.downloadFailedEvent = configIni.ReadBoolean("Engine", "eMailDownloadFailed", true); 
            mceBuddyConfSettings.generalOptions.eMailSettings.userName = configIni.ReadString("Engine", "eMailUsername", "");
            mceBuddyConfSettings.generalOptions.eMailSettings.password = configIni.ReadString("Engine", "eMailPassword", "");
            if (!String.IsNullOrEmpty(mceBuddyConfSettings.generalOptions.eMailSettings.password))
                mceBuddyConfSettings.generalOptions.eMailSettings.password = Util.Crypto.Decrypt(mceBuddyConfSettings.generalOptions.eMailSettings.password); // password is stored encrypted
        }

        private void ReadGeneralSettings(Ini configIni)
        {
            // Read the General Settings
            ReadHourMinute(configIni, ref mceBuddyConfSettings.generalOptions.wakeHour, ref mceBuddyConfSettings.generalOptions.wakeMinute, "Wake");
            ReadHourMinute(configIni, ref mceBuddyConfSettings.generalOptions.startHour, ref mceBuddyConfSettings.generalOptions.startMinute, "Start");
            ReadHourMinute(configIni, ref mceBuddyConfSettings.generalOptions.stopHour, ref mceBuddyConfSettings.generalOptions.stopMinute, "Stop");

            mceBuddyConfSettings.generalOptions.daysOfWeek = configIni.ReadString("Engine", "DaysOfWeek", "Sunday,Monday,Tuesday,Wednesday,Thursday,Friday,Saturday");
            mceBuddyConfSettings.generalOptions.maxConcurrentJobs = configIni.ReadInteger("Engine", "MaxConcurrentJobs", 1);
            mceBuddyConfSettings.generalOptions.logJobs = configIni.ReadBoolean("Engine", "LogJobs", false);
            mceBuddyConfSettings.generalOptions.logLevel = configIni.ReadInteger("Engine", "LogLevel", 3);
            mceBuddyConfSettings.generalOptions.logKeepDays = configIni.ReadInteger("Engine", "LogKeepDays", 15);
            mceBuddyConfSettings.generalOptions.deleteOriginal = configIni.ReadBoolean("Engine", "DeleteOriginal", false);
            mceBuddyConfSettings.generalOptions.useRecycleBin = configIni.ReadBoolean("Engine", "UseRecycleBin", false);
            mceBuddyConfSettings.generalOptions.archiveOriginal = configIni.ReadBoolean("Engine", "ArchiveOriginal", false);
            mceBuddyConfSettings.generalOptions.deleteConverted = configIni.ReadBoolean("Engine", "DeleteConverted", false); 
            mceBuddyConfSettings.generalOptions.allowSleep = configIni.ReadBoolean("Engine", "AllowSleep", true);
            mceBuddyConfSettings.generalOptions.minimumAge = configIni.ReadInteger("Engine", "MinimumAge", 0);
            mceBuddyConfSettings.generalOptions.sendEmail = configIni.ReadBoolean("Engine", "SendEmail", false);
            mceBuddyConfSettings.generalOptions.locale = configIni.ReadString("Engine", "Locale", CultureInfo.CurrentCulture.Name);
            mceBuddyConfSettings.generalOptions.tempWorkingPath = configIni.ReadString("Engine", "TempWorkingPath", "");
            mceBuddyConfSettings.generalOptions.archivePath = configIni.ReadString("Engine", "ArchivePath", "");
            mceBuddyConfSettings.generalOptions.spaceCheck = configIni.ReadBoolean("Engine", "SpaceCheck", true);
            mceBuddyConfSettings.generalOptions.hangTimeout = configIni.ReadInteger("Engine", "HangPeriod", 300);
            mceBuddyConfSettings.generalOptions.pollPeriod = configIni.ReadInteger("Engine", "PollPeriod", 300);
            mceBuddyConfSettings.generalOptions.processPriority = configIni.ReadString("Engine", "ProcessPriority", "Normal");
            mceBuddyConfSettings.generalOptions.CPUAffinity = (IntPtr) configIni.ReadLong("Engine", "CPUAffinity", 0);
            mceBuddyConfSettings.generalOptions.engineRunning = configIni.ReadBoolean("Engine", "EngineRunning", false);
            mceBuddyConfSettings.generalOptions.ignoreCopyProtection = configIni.ReadBoolean("Engine", "IgnoreCopyProtection", false);
            mceBuddyConfSettings.generalOptions.comskipPath = configIni.ReadString("Engine", "CustomComskipPath", "");
            mceBuddyConfSettings.generalOptions.localServerPort = configIni.ReadInteger("Engine", "LocalServerPort", int.Parse(GlobalDefs.MCEBUDDY_SERVER_PORT));
            mceBuddyConfSettings.generalOptions.uPnPEnable = configIni.ReadBoolean("Engine", "UPnPEnable", false);
            string srtSegmentOffset = configIni.ReadString("Engine", "SubtitleSegmentOffset", GlobalDefs.SEGMENT_CUT_OFFSET_GOP_COMPENSATE);
            double.TryParse(srtSegmentOffset, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out mceBuddyConfSettings.generalOptions.subtitleSegmentOffset);
            
            ReadEMailSettings(configIni);
        }

        private void WriteEMailSettings(Ini configIni)
        {
            if (configIni == null)
                return;

            configIni.Write("Engine", "eMailServer", mceBuddyConfSettings.generalOptions.eMailSettings.smtpServer);
            configIni.Write("Engine", "eMailPort", mceBuddyConfSettings.generalOptions.eMailSettings.port);
            configIni.Write("Engine", "eMailSSL", mceBuddyConfSettings.generalOptions.eMailSettings.ssl);
            configIni.Write("Engine", "eMailFrom", mceBuddyConfSettings.generalOptions.eMailSettings.fromAddress);
            configIni.Write("Engine", "eMailTo", mceBuddyConfSettings.generalOptions.eMailSettings.toAddresses);
            configIni.Write("Engine", "eMailSuccess", mceBuddyConfSettings.generalOptions.eMailSettings.successEvent);
            configIni.Write("Engine", "eMailFailed", mceBuddyConfSettings.generalOptions.eMailSettings.failedEvent);
            configIni.Write("Engine", "eMailCancelled", mceBuddyConfSettings.generalOptions.eMailSettings.cancelledEvent);
            configIni.Write("Engine", "eMailStart", mceBuddyConfSettings.generalOptions.eMailSettings.startEvent);
            configIni.Write("Engine", "eMailDownloadFailed", mceBuddyConfSettings.generalOptions.eMailSettings.downloadFailedEvent);
            configIni.Write("Engine", "eMailUsername", mceBuddyConfSettings.generalOptions.eMailSettings.userName);
            if (!String.IsNullOrEmpty(mceBuddyConfSettings.generalOptions.eMailSettings.password))
                configIni.Write("Engine", "eMailPassword", Util.Crypto.Encrypt(mceBuddyConfSettings.generalOptions.eMailSettings.password)); // password is stored encrypted
        }

        private void WriteGeneralSettings(Ini configIni)
        {
            if (configIni == null)
                return;

            // Write the General Settings
            configIni.Write("Engine", "WakeHour", mceBuddyConfSettings.generalOptions.wakeHour);
            configIni.Write("Engine", "WakeMinute", mceBuddyConfSettings.generalOptions.wakeMinute);
            configIni.Write("Engine", "StartHour", mceBuddyConfSettings.generalOptions.startHour);
            configIni.Write("Engine", "StartMinute", mceBuddyConfSettings.generalOptions.startMinute);
            configIni.Write("Engine", "StopHour", mceBuddyConfSettings.generalOptions.stopHour);
            configIni.Write("Engine", "StopMinute", mceBuddyConfSettings.generalOptions.stopMinute);
            configIni.Write("Engine", "DaysOfWeek", mceBuddyConfSettings.generalOptions.daysOfWeek);
            configIni.Write("Engine", "MaxConcurrentJobs", mceBuddyConfSettings.generalOptions.maxConcurrentJobs);
            configIni.Write("Engine", "LogJobs", mceBuddyConfSettings.generalOptions.logJobs);
            configIni.Write("Engine", "LogLevel", mceBuddyConfSettings.generalOptions.logLevel);
            configIni.Write("Engine", "LogKeepDays", mceBuddyConfSettings.generalOptions.logKeepDays);
            configIni.Write("Engine", "DeleteOriginal", mceBuddyConfSettings.generalOptions.deleteOriginal);
            configIni.Write("Engine", "UseRecycleBin", mceBuddyConfSettings.generalOptions.useRecycleBin);
            configIni.Write("Engine", "ArchiveOriginal", mceBuddyConfSettings.generalOptions.archiveOriginal);
            configIni.Write("Engine", "DeleteConverted", mceBuddyConfSettings.generalOptions.deleteConverted); 
            configIni.Write("Engine", "AllowSleep", mceBuddyConfSettings.generalOptions.allowSleep);
            configIni.Write("Engine", "MinimumAge", mceBuddyConfSettings.generalOptions.minimumAge);
            configIni.Write("Engine", "SendEmail", mceBuddyConfSettings.generalOptions.sendEmail);
            configIni.Write("Engine", "Locale", mceBuddyConfSettings.generalOptions.locale);
            configIni.Write("Engine", "TempWorkingPath", mceBuddyConfSettings.generalOptions.tempWorkingPath);
            configIni.Write("Engine", "ArchivePath", mceBuddyConfSettings.generalOptions.archivePath);
            configIni.Write("Engine", "SpaceCheck", mceBuddyConfSettings.generalOptions.spaceCheck);
            configIni.Write("Engine", "HangPeriod", mceBuddyConfSettings.generalOptions.hangTimeout);
            configIni.Write("Engine", "PollPeriod", mceBuddyConfSettings.generalOptions.pollPeriod);
            configIni.Write("Engine", "ProcessPriority", mceBuddyConfSettings.generalOptions.processPriority);
            configIni.Write("Engine", "CPUAffinity", mceBuddyConfSettings.generalOptions.CPUAffinity.ToInt32());
            configIni.Write("Engine", "EngineRunning", mceBuddyConfSettings.generalOptions.engineRunning);
            configIni.Write("Engine", "CustomComskipPath", mceBuddyConfSettings.generalOptions.comskipPath);
            configIni.Write("Engine", "IgnoreCopyProtection", mceBuddyConfSettings.generalOptions.ignoreCopyProtection);
            configIni.Write("Engine", "LocalServerPort", mceBuddyConfSettings.generalOptions.localServerPort);
            configIni.Write("Engine", "UPnPEnable", mceBuddyConfSettings.generalOptions.uPnPEnable);
            configIni.Write("Engine", "SubtitleSegmentOffset", mceBuddyConfSettings.generalOptions.subtitleSegmentOffset.ToString(CultureInfo.InvariantCulture));

            WriteEMailSettings(configIni);
        }



        private void ReadConversionSettings(Ini configIni)
        {
            // Read the Conversion Tasks
            string[] conversionRecords = configIni.ReadString("Engine", "Tasks", "").Split(',');
            foreach (string conversionRecord in conversionRecords)
            {
                if (String.IsNullOrEmpty(conversionRecord))
                    continue;

                ConversionJobOptions cjo = new ConversionJobOptions();

                cjo.taskName = conversionRecord;
                cjo.profile = configIni.ReadString(conversionRecord, "Profile", "");
                cjo.destinationPath = configIni.ReadString(conversionRecord, "DestinationPath", "");
                cjo.fallbackToSourcePath = configIni.ReadBoolean(conversionRecord, "FallbackDestination", false);
                cjo.maxWidth = configIni.ReadInteger(conversionRecord, "MaxWidth", 720);
                cjo.renameBySeries = configIni.ReadBoolean(conversionRecord, "RenameBySeries", true);
                cjo.altRenameBySeries = configIni.ReadBoolean(conversionRecord, "AltRenameBySeries", false);
                cjo.customRenameBySeries = configIni.ReadString(conversionRecord, "CustomRenameBySeries", "");
                cjo.renameOnly = configIni.ReadBoolean(conversionRecord, "RenameOnly", false);
                cjo.fileSelection = configIni.ReadString(conversionRecord, "FileSelection", "");
                cjo.metaShowSelection = configIni.ReadString(conversionRecord, "MetaSelection", "");
                cjo.metaNetworkSelection = configIni.ReadString(conversionRecord, "MetaChannelSelection", "");
                string monitorNameList = configIni.ReadString(conversionRecord, "MonitorTaskNames", "");
                if (MCEBuddy.Globals.GlobalDefs.IsNullOrWhiteSpace(monitorNameList))
                    cjo.monitorTaskNames = null; // list should be empty if nothing is there
                else
                    cjo.monitorTaskNames = monitorNameList.Split(',');
                cjo.audioLanguage = configIni.ReadString(conversionRecord, "AudioLanguage", "");
                string audioOffsetStr = configIni.ReadString(conversionRecord, "AudioOffset", "0");
                double.TryParse(audioOffsetStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out cjo.audioOffset);
                cjo.drc = configIni.ReadBoolean(conversionRecord, "DRC", true);
                cjo.stereoAudio = configIni.ReadBoolean(conversionRecord, "StereoAudio", true);
                cjo.startTrim = configIni.ReadInteger(conversionRecord, "StartTrim", 0);
                cjo.endTrim = configIni.ReadInteger(conversionRecord, "EndTrim", 0);
                cjo.extractXML = configIni.ReadBoolean(conversionRecord, "ExtractXML", false);
                cjo.disableCropping = configIni.ReadBoolean(conversionRecord, "DisableCropping", false);
                cjo.commercialSkipCut = configIni.ReadBoolean(conversionRecord, "TaskCommercialSkipCut", false);
                cjo.tivoMAKKey = configIni.ReadString(conversionRecord, "TiVOMAKKey", "");
                cjo.downloadSeriesDetails = configIni.ReadBoolean(conversionRecord, "DownloadSeriesDetails", true);
                cjo.downloadBanner = configIni.ReadBoolean(conversionRecord, "DownloadBanner", true);
                cjo.tvdbSeriesId = configIni.ReadString(conversionRecord, "TVDBSeriesId", "");
                cjo.imdbSeriesId = configIni.ReadString(conversionRecord, "IMDBSeriesId", "");
                cjo.enabled = configIni.ReadBoolean(conversionRecord, "Enabled", true);
                cjo.extractCC = configIni.ReadString(conversionRecord, "ExtractCC", "");

                string ccOffsetStr = configIni.ReadString(conversionRecord, "CCOffset", GlobalDefs.DefaultCCOffset);
                double.TryParse(ccOffsetStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out cjo.ccOffset);

                string qualityMultiplierStr = configIni.ReadString(conversionRecord, "QualityMultiplier", "1");
                double.TryParse(qualityMultiplierStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out cjo.qualityMultiplier);
                if (cjo.qualityMultiplier <= 0.01) cjo.qualityMultiplier = 0.01F;
                if (cjo.qualityMultiplier > 4) cjo.qualityMultiplier = 4F;

                string volumeMultiplierStr = configIni.ReadString(conversionRecord, "VolumeMultiplier", "0");
                double.TryParse(volumeMultiplierStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out cjo.volumeMultiplier);
                if (cjo.volumeMultiplier <= -20) cjo.volumeMultiplier = -20F; //-10db minimum
                if (cjo.volumeMultiplier > 60) cjo.volumeMultiplier = 60F; //30db max

                string commercialRemovalStr = configIni.ReadString(conversionRecord, "CommercialRemoval", "comskip");
                if (commercialRemovalStr.ToLower().Contains("comskip"))
                    cjo.commercialRemoval = CommercialRemovalOptions.Comskip;
                else if (commercialRemovalStr.ToLower().Contains("showanalyzer"))
                    cjo.commercialRemoval = CommercialRemovalOptions.ShowAnalyzer;
                else
                    cjo.commercialRemoval = CommercialRemovalOptions.None;
                
                cjo.comskipIni = configIni.ReadString(conversionRecord, "ComskipINI", "");

                cjo.domainName = configIni.ReadString(conversionRecord, "DomainName", "");
                cjo.userName = configIni.ReadString(conversionRecord, "UserName", "");
                cjo.password = configIni.ReadString(conversionRecord, "Password", "");
                if (!String.IsNullOrEmpty(cjo.password))
                    cjo.password = Crypto.Decrypt(cjo.password); // Password is kept as encrypted

                mceBuddyConfSettings.conversionTasks.Add(cjo); // Add the Monitor Task object
            }
        }

        private void WriteConversionTasksList(Ini configIni)
        {
            if (configIni == null)
                return;

            string conversionTaskNames = ""; // A list of all the conversion task names

            // Write the Converstion Task Settings
            foreach (ConversionJobOptions conversionTask in mceBuddyConfSettings.conversionTasks)
            {
                string section = conversionTask.taskName;

                if (conversionTaskNames == "")
                    conversionTaskNames = section;
                else
                    conversionTaskNames += "," + section;
            }

            configIni.Write("Engine", "Tasks", conversionTaskNames); // this list goes in the Engine section
        }

        private void WriteConversionSettings(Ini configIni)
        {
            if (configIni == null)
                return;

            string conversionTaskNames = ""; // A list of all the conversion task names

            // Write the Converstion Task Settings
            foreach (ConversionJobOptions conversionTask in mceBuddyConfSettings.conversionTasks)
            {
                string section = conversionTask.taskName;

                configIni.Write(section, "Profile", conversionTask.profile);
                configIni.Write(section, "DestinationPath", conversionTask.destinationPath);
                configIni.Write(section, "FallbackDestination", conversionTask.fallbackToSourcePath);
                configIni.Write(section, "MaxWidth", conversionTask.maxWidth);
                configIni.Write(section, "VolumeMultiplier", conversionTask.volumeMultiplier.ToString(CultureInfo.InvariantCulture));
                configIni.Write(section, "QualityMultiplier", conversionTask.qualityMultiplier.ToString(CultureInfo.InvariantCulture));
                configIni.Write(section, "RenameBySeries", conversionTask.renameBySeries);
                configIni.Write(section, "AltRenameBySeries", conversionTask.altRenameBySeries);
                configIni.Write(section, "CustomRenameBySeries", conversionTask.customRenameBySeries);
                configIni.Write(section, "RenameOnly", conversionTask.renameOnly);
                configIni.Write(section, "DownloadSeriesDetails", conversionTask.downloadSeriesDetails);
                configIni.Write(section, "DownloadBanner", conversionTask.downloadBanner); 
                configIni.Write(section, "TVDBSeriesId", conversionTask.tvdbSeriesId);
                configIni.Write(section, "IMDBSeriesId", conversionTask.imdbSeriesId);
                configIni.Write(section, "FileSelection", conversionTask.fileSelection);
                configIni.Write(section, "MetaSelection", conversionTask.metaShowSelection);
                configIni.Write(section, "MetaChannelSelection", conversionTask.metaNetworkSelection);
                configIni.Write(section, "MonitorTaskNames", (conversionTask.monitorTaskNames == null ? "" : String.Join(",", conversionTask.monitorTaskNames)));
                configIni.Write(section, "DRC", conversionTask.drc);
                configIni.Write(section, "AudioLanguage", conversionTask.audioLanguage);
                configIni.Write(section, "AudioOffset", conversionTask.audioOffset.ToString(CultureInfo.InvariantCulture));
                configIni.Write(section, "ExtractXML", conversionTask.extractXML);
                configIni.Write(section, "StereoAudio", conversionTask.stereoAudio);
                configIni.Write(section, "DisableCropping", conversionTask.disableCropping);
                configIni.Write(section, "StartTrim", conversionTask.startTrim);
                configIni.Write(section, "EndTrim", conversionTask.endTrim);
                configIni.Write(section, "ExtractCC", conversionTask.extractCC);
                configIni.Write(section, "CCOffset", conversionTask.ccOffset.ToString(CultureInfo.InvariantCulture));
                configIni.Write(section, "TaskCommercialSkipCut", conversionTask.commercialSkipCut);
                configIni.Write(section, "TiVOMAKKey", conversionTask.tivoMAKKey);
                configIni.Write(section, "Enabled", conversionTask.enabled);

                switch (conversionTask.commercialRemoval)
                {
                    case CommercialRemovalOptions.None:
                        configIni.Write(section, "CommercialRemoval", "none");
                        break;

                    case CommercialRemovalOptions.Comskip:
                        configIni.Write(section, "CommercialRemoval", "comskip");
                        break;

                    case CommercialRemovalOptions.ShowAnalyzer:
                        configIni.Write(section, "CommercialRemoval", "showanalyzer");
                        break;
                }
                
                configIni.Write(section, "ComskipINI", conversionTask.comskipIni);

                configIni.Write(section, "DomainName", conversionTask.domainName);
                configIni.Write(section, "UserName", conversionTask.userName);
                string password = conversionTask.password;
                if (!String.IsNullOrEmpty(password))
                    password = Crypto.Encrypt(password); // Password is written as encrypted
                configIni.Write(section, "Password", password);

                if (conversionTaskNames == "")
                    conversionTaskNames = section;
                else
                    conversionTaskNames += "," + section;
            }

            configIni.Write("Engine", "Tasks", conversionTaskNames); // this list goes in the Engine section
        }



        private void ReadMonitorSettings(Ini configIni)
        {
            // Read the Monitor Tasks
            string[] searchRecords = configIni.ReadString("Engine", "SearchRecords", "").Split(',');
            foreach (string searchRecord in searchRecords)
            {
                if (String.IsNullOrEmpty(searchRecord))
                    continue;

                MonitorJobOptions mjo = new MonitorJobOptions();

                mjo.taskName = searchRecord;
                mjo.searchPath = configIni.ReadString(searchRecord, "SearchPath", "");
                mjo.searchPattern = configIni.ReadString(searchRecord, "SearchPattern", "[video]");
                mjo.monitorSubdirectories = configIni.ReadBoolean(searchRecord, "MonitorSubdirectories", true);

                mjo.domainName = configIni.ReadString(searchRecord, "DomainName", "");
                mjo.userName = configIni.ReadString(searchRecord, "UserName", "");
                mjo.password = configIni.ReadString(searchRecord, "Password", "");
                if (!String.IsNullOrEmpty(mjo.password))
                    mjo.password = Crypto.Decrypt(mjo.password); // Password is kept as encrypted

                mceBuddyConfSettings.monitorTasks.Add(mjo); // Add the Monitor Task object
            }
        }

        private void WriteMonitorTasksList(Ini configIni)
        {
            if (configIni == null)
                return;

            string monitorTaskNames = ""; // A list of of all the monitor task names

            // Write the Monitor Tasks Settings
            foreach (MonitorJobOptions monitorTask in mceBuddyConfSettings.monitorTasks)
            {
                string section = monitorTask.taskName;

                if (monitorTaskNames == "")
                    monitorTaskNames = section;
                else
                    monitorTaskNames += "," + section;
            }

            configIni.Write("Engine", "SearchRecords", monitorTaskNames); // this list goes in the Engine section
        }

        private void WriteMonitorSettings(Ini configIni)
        {
            if (configIni == null)
                return;

            string monitorTaskNames = ""; // A list of of all the monitor task names

            // Write the Monitor Tasks Settings
            foreach (MonitorJobOptions monitorTask in mceBuddyConfSettings.monitorTasks)
            {
                string section = monitorTask.taskName;

                configIni.Write(section, "SearchPath", monitorTask.searchPath);
                configIni.Write(section, "SearchPattern", monitorTask.searchPattern);
                configIni.Write(section, "MonitorSubdirectories", monitorTask.monitorSubdirectories);

                configIni.Write(section, "DomainName", monitorTask.domainName);
                configIni.Write(section, "UserName", monitorTask.userName);
                string password = monitorTask.password;
                if (!String.IsNullOrEmpty(password))
                    password = Crypto.Encrypt(password); // Password is written as encrypted
                configIni.Write(section, "Password", password);

                if (monitorTaskNames == "")
                    monitorTaskNames = section;
                else
                    monitorTaskNames += "," + section;
            }

            configIni.Write("Engine", "SearchRecords", monitorTaskNames); // this list goes in the Engine section
        }

        public override string ToString()
        {
            string allOpts = "";

            foreach (ConversionJobOptions cjo in mceBuddyConfSettings.conversionTasks)
                allOpts += "CONVERSION TASK OPTIONS ==>\n" + cjo.ToString() + "\n";

            foreach (MonitorJobOptions mjo in mceBuddyConfSettings.monitorTasks)
                allOpts += "MONITOR TASK OPTIONS ==>\n" + mjo.ToString() + "\n";

            allOpts += "GENERAL OPTIONS ==>\n" + mceBuddyConfSettings.generalOptions.ToString() + "\n";

            return allOpts;
        }
    }
}
