using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Globalization;

namespace MCEBuddy.Globals
{
    public enum CommercialRemovalOptions
    {
        None = 0,
        Comskip = 1,
        ShowAnalyzer = 2
    }

    [Serializable]
    public struct ConfSettings
    {
        public List<ConversionJobOptions> conversionTasks; // List of Conversion Tasks and conversion options for each task
        public List<MonitorJobOptions> monitorTasks; // List of Monitor Tasks and options for each task
        public GeneralOptions generalOptions; // General settings for MCEBuddy

        public ConfSettings(List<ConversionJobOptions> cjo, List<MonitorJobOptions> mjo, GeneralOptions go)
        {
            conversionTasks = cjo;
            monitorTasks = mjo;
            generalOptions = go;
        }
    }

    [Serializable]
    public class ConversionJobOptions
    {
        public string taskName; // Name of conversion task
        public string profile; // Profile name used for this task

        public string sourceVideo; // Filepath of source video
        public string destinationPath; // Output path
        public string workingPath; // Temp working path
        public bool fallbackToSourcePath; // Fallback to source directory if destination path is unavailable (for network drives)

        public int maxWidth; // Maximum width
        public double qualityMultiplier; // Quality

        public double volumeMultiplier; // Volume
        public bool drc; // Dynamic Range Control
        public bool stereoAudio; // Limit output to 2 channels
        public string audioLanguage; // Audio Language selection
        public double audioOffset; // Audio offset

        public int startTrim; // Trim initial video
        public int endTrim; // Trim end of video

        public string extractCC; // Extract closed captions
        public double ccOffset; // Closed Captions offset

        public CommercialRemovalOptions commercialRemoval; // Commercial removal program
        public string comskipIni; // Path to custom Comskip INI file

        public bool downloadSeriesDetails; // Download information from internet
        public string tvdbSeriesId; // TVDB Series Id
        public string imdbSeriesId; // IMDB Series Id

        public bool renameBySeries; // rename by information
        public bool altRenameBySeries; // alternate renaming
        public string customRenameBySeries; // custom rename file

        public string fileSelection; // File selection filter
        public string metaSelection; // Metadata selection filter

        public bool extractXML; // Create XML file from video properties

        public bool disableCropping; // Disable auto cropping
        public bool commercialSkipCut; // do commercial scan keep EDL file but skip cutting the commercials

        public string domainName; // domain name for network credentials
        public string userName; // user name for network credentials
        public string password; // password for network credentials

        public bool enabled; // If the conversion task enabled

        public override string ToString()
        {
            string allOpts = "";

            allOpts += "Task -> " + taskName + "\n";
            allOpts += "Profile -> " + profile + "\n";
            allOpts += "Source File -> " + sourceVideo + "\n";
            allOpts += "Destination Path -> " + destinationPath + "\n";
            allOpts += "Working Path -> " + workingPath + "\n";
            allOpts += "Fallback Destination -> " + fallbackToSourcePath.ToString(CultureInfo.InvariantCulture) + "\n";
            allOpts += "Max Width -> " + maxWidth.ToString(CultureInfo.InvariantCulture) + "\n";
            allOpts += "Quality Multipltier -> " + qualityMultiplier.ToString(CultureInfo.InvariantCulture) + "\n";
            allOpts += "Volume Multipltier -> " + volumeMultiplier.ToString(CultureInfo.InvariantCulture) + "\n";
            allOpts += "DRC -> " + drc.ToString(CultureInfo.InvariantCulture) + "\n";
            allOpts += "Force Stereo -> " + stereoAudio.ToString(CultureInfo.InvariantCulture) + "\n";
            allOpts += "Profile Audio Language -> " + audioLanguage.ToUpper() + "\n";
            allOpts += "Audio Offset -> " + audioOffset.ToString(CultureInfo.InvariantCulture) + "\n";
            allOpts += "Start Trim -> " + startTrim.ToString(CultureInfo.InvariantCulture) + "\n";
            allOpts += "End Trim -> " + endTrim.ToString(CultureInfo.InvariantCulture) + "\n";
            allOpts += "Closed Captions -> " + extractCC + "\n";
            allOpts += "Closed Captions Offset -> " + ccOffset.ToString(CultureInfo.InvariantCulture) + "\n";
            allOpts += "Commercial Removal -> " + commercialRemoval.ToString() + "\n";
            allOpts += "Custom Comskip INI Path -> " + comskipIni + "\n";
            allOpts += "Download Series Details -> " + downloadSeriesDetails.ToString(CultureInfo.InvariantCulture) + "\n";
            allOpts += "TVDB Series Id -> " + tvdbSeriesId + "\n";
            allOpts += "IMDB Series Id -> " + imdbSeriesId + "\n";
            allOpts += "Rename by Series -> " + renameBySeries.ToString(CultureInfo.InvariantCulture) + "\n";
            allOpts += "Alt Rename by Series -> " + altRenameBySeries.ToString(CultureInfo.InvariantCulture) + "\n";
            allOpts += "Custom Rename by Series -> " + customRenameBySeries + "\n";
            allOpts += "File Selection Pattern -> " + fileSelection + "\n";
            allOpts += "Show Selection Pattern -> " + metaSelection + "\n";
            allOpts += "Extract XML -> " + extractXML.ToString(CultureInfo.InvariantCulture) + "\n";
            allOpts += "Disable Cropping -> " + disableCropping.ToString(CultureInfo.InvariantCulture) + "\n";
            allOpts += "Task Commercial Skip Cut -> " + commercialSkipCut.ToString(CultureInfo.InvariantCulture) + "\n";
            allOpts += "Domain Name -> " + domainName + "\n";
            allOpts += "User Name -> " + userName + "\n";
            allOpts += "Password -> " + new String('*', password.Length) + "\n"; // mask the password, preserve the length
            allOpts += "Task Enabled -> " + enabled.ToString(CultureInfo.InvariantCulture) + "\n";

            return allOpts;
        }

        /// <summary>
        /// Clones the current object and returns a new instance of the object
        /// </summary>
        /// <returns>New clone object</returns>
        public ConversionJobOptions Clone()
        {
            return (ConversionJobOptions) this.MemberwiseClone();
        }
    }

    [Serializable]
    public class MonitorJobOptions
    {
        public string taskName; // Name of monitor task
        public string searchPath; // directory to search for files
        public string searchPattern; // file search pattern
        public bool monitorSubdirectories; // monitor sub directories

        public string domainName; // domain name for network credentials
        public string userName; // user name for network credentials
        public string password; // password for network credentials

        public override string ToString()
        {
            string allOpts = "";

            allOpts += "Task -> " + taskName + "\n";
            allOpts += "Search Path -> " + searchPath + "\n";
            allOpts += "Search Pattern -> " + searchPattern + "\n";
            allOpts += "Domain Name -> " + domainName + "\n";
            allOpts += "User Name -> " + userName + "\n";
            allOpts += "Password -> " + new String('*', password.Length) + "\n"; // mask the password, preserve the length

            return allOpts;
        }

        /// <summary>
        /// Clones the current object and returns a new instance of the object
        /// </summary>
        /// <returns>New clone object</returns>
        public MonitorJobOptions Clone()
        {
            return (MonitorJobOptions)this.MemberwiseClone();
        }
    }

    [Serializable]
    public class EMailOptions
    {
        public string smtpServer; // Name of SMTP server
        public int port; // SMTP port number
        public bool ssl; // Use SSL

        public string userName; // username for SMTP server
        public string password; // password fro SMTP server

        public string fromAddress; // From eMail address
        public string toAddresses; // to eMail addresses (multiple separated by ;)

        public bool successEvent; // send eMail on successful conversion
        public bool failedEvent; // send eMail on failed conversion
        public bool cancelledEvent; // send eMail on cancelled conversion
        public bool startEvent; // send eMail on start of conversion


        public override string ToString()
        {
            string allOpts = "";

            allOpts += "SMTP Server -> " + smtpServer + "\n";
            allOpts += "Port -> " + port.ToString(CultureInfo.InvariantCulture) + "\n";
            allOpts += "SSL -> " + ssl.ToString(CultureInfo.InvariantCulture) + "\n";
            allOpts += "User Name -> " + userName + "\n";
            allOpts += "Password -> " + new String('*', password.Length) + "\n"; // mask the password, preserve the length
            allOpts += "From -> " + fromAddress + "\n";
            allOpts += "To -> " + toAddresses + "\n";
            allOpts += "eMail On Success -> " + successEvent.ToString(CultureInfo.InvariantCulture) + "\n";
            allOpts += "eMail On Failure -> " + failedEvent.ToString(CultureInfo.InvariantCulture) + "\n";
            allOpts += "eMail On Cancellation -> " + cancelledEvent.ToString(CultureInfo.InvariantCulture) + "\n";
            allOpts += "eMail On Start -> " + startEvent.ToString(CultureInfo.InvariantCulture) + "\n";

            return allOpts;
        }

        /// <summary>
        /// Clones the current object and returns a new instance of the object
        /// </summary>
        /// <returns>New clone object</returns>
        public EMailOptions Clone()
        {
            return (EMailOptions)this.MemberwiseClone();
        }
    }

    [Serializable]
    public class GeneralOptions
    {
        public int wakeHour; // Hour to wake system
        public int wakeMinute; // Minute to wake system

        public int startHour; // New conversion start hour
        public int startMinute; // New conversion start minute

        public int stopHour; // New conversion stop hour
        public int stopMinute; // New conversion stop minute

        public string daysOfWeek; // Days of week to start new conversions and wake up system

        public int minimumAge; // Minimum number of days to keep the file before converting

        public int maxConcurrentJobs; // Max no of simutaneous conversions

        public bool logJobs; // Enable job logs
        public int logLevel; // Amount of details of logs
        public int logKeepDays; // number of days to keep the logs

        public bool deleteOriginal; // Delete original file after successful conversion
        public bool archiveOriginal; // Archive original file after successful conversion
        public bool deleteConverted; // Delete converted file when source file is deleted

        public bool allowSleep; // Allow system to enter sleep during active conversion

        public bool sendEmail; // Send emails on various events
        public EMailOptions eMailSettings; // Settings used for sending eMails

        public string locale; // Locale to be used

        public string tempWorkingPath; // Temp folder
        public string archivePath; // Archive folder
        public bool spaceCheck; // Check for enough empty space
        public int hangTimeout; // Timeout for apps console output before they are determined as hung
        public int pollPeriod; // Polling period for scanning for new files in the monitor tasks
        public string processPriority; // Priority of the applications
        public bool engineRunning; // Last state of the MCEBuddy engine

        public int localServerPort; // Port of the local MCEBuddy server engine, to host the service and enable UPnP
        public bool uPnPEnable; // UPnP support

        public override string ToString()
        {
            string allOpts = "";

            allOpts += "Wake Hour -> " + wakeHour.ToString(CultureInfo.InvariantCulture) + "\n";
            allOpts += "Wake Minute -> " + wakeMinute.ToString(CultureInfo.InvariantCulture) + "\n";
            allOpts += "Start Hour -> " + startHour.ToString(CultureInfo.InvariantCulture) + "\n";
            allOpts += "Start Minute -> " + startMinute.ToString(CultureInfo.InvariantCulture) + "\n";
            allOpts += "Stop Hour -> " + stopHour.ToString(CultureInfo.InvariantCulture) + "\n";
            allOpts += "Stop Minute -> " + stopMinute.ToString(CultureInfo.InvariantCulture) + "\n";
            allOpts += "Days of Week -> " + daysOfWeek + "\n";
            allOpts += "Minimum Age -> " + minimumAge.ToString(CultureInfo.InvariantCulture) + "\n";
            allOpts += "Max Concurrent Jobs -> " + maxConcurrentJobs.ToString(CultureInfo.InvariantCulture) + "\n";
            allOpts += "Enable Job Logs -> " + logJobs.ToString(CultureInfo.InvariantCulture) + "\n";
            allOpts += "Log Level -> " + logLevel.ToString(CultureInfo.InvariantCulture) + "\n";
            allOpts += "Delete Original -> " + deleteOriginal.ToString(CultureInfo.InvariantCulture) + "\n";
            allOpts += "Archive Original -> " + archiveOriginal.ToString(CultureInfo.InvariantCulture) + "\n";
            allOpts += "Sync Converted -> " + deleteConverted.ToString(CultureInfo.InvariantCulture) + "\n";
            allOpts += "Allow Sleep During Conversions -> " + allowSleep.ToString(CultureInfo.InvariantCulture) + "\n";
            allOpts += "Send eMails -> " + sendEmail.ToString(CultureInfo.InvariantCulture) + "\n";
            allOpts += "eMail settings -> " + eMailSettings.ToString() + "\n";
            allOpts += "Locale -> " + locale + "\n";
            allOpts += "Temp Working Path -> " + tempWorkingPath + "\n";
            allOpts += "Archive Folder -> " + archivePath + "\n";
            allOpts += "Space Check -> " + spaceCheck.ToString(CultureInfo.InvariantCulture) + "\n";
            allOpts += "App Hang Timeout -> " + hangTimeout.ToString(CultureInfo.InvariantCulture) + "\n";
            allOpts += "Scan New Files Poll Period -> " + pollPeriod.ToString(CultureInfo.InvariantCulture) + "\n";
            allOpts += "Process Priority -> " + processPriority + "\n";
            allOpts += "Engine Running -> " + engineRunning.ToString(CultureInfo.InvariantCulture) + "\n";
            allOpts += "Local Server Port -> " + localServerPort + "\n";
            allOpts += "UPnP Enabled -> " + uPnPEnable.ToString(CultureInfo.InvariantCulture) + "\n";

            return allOpts;
        }

        /// <summary>
        /// Clones the current object and returns a new instance of the object
        /// </summary>
        /// <returns>New clone object</returns>
        public GeneralOptions Clone()
        {
            GeneralOptions clone = (GeneralOptions)this.MemberwiseClone();
            clone.eMailSettings = this.eMailSettings.Clone(); // Clone this object as MemberwiseClone only does a shallow copy
            return clone;
        }
    }
}
