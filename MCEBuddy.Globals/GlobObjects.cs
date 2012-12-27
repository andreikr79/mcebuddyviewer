using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using System.ServiceModel;
using System.Security.Permissions;
using Microsoft.Win32;

namespace MCEBuddy.Globals
{
    public static class GlobalDefs
    {
        public static Random random = new System.Random();

        public static string GetAppPath()
        {
            string CurrentPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().GetModules()[0].FullyQualifiedName);
            try
            {
                //try to find in Program Files
                if (Directory.Exists(Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles), "MCEBuddy2x")))
                {
                    // Found standart path in program files
                    CurrentPath = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles), "MCEBuddy2x");
                }
                else
                {
                    //standart path not found. Try Found Service Path in registry
                    RegistryKey buddykey = Registry.LocalMachine;
                    buddykey = buddykey.OpenSubKey(@"SYSTEM\CurrentControlSet\services\MCEBuddy2x");
                    if (buddykey.GetValue("ImagePath") != null)
                    {
                        // found service in registry. Get Path from service image path
                        CurrentPath = Path.GetDirectoryName((string)buddykey.GetValue("ImagePath"));
                    }
                    else
                    {
                        CurrentPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().GetModules()[0].FullyQualifiedName);
                    }
                }
            }
            catch (Exception e)
            {
                CurrentPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().GetModules()[0].FullyQualifiedName);
            }
            return CurrentPath;
        }

        volatile public static bool Shutdown = false;
        volatile public static bool Active = false;
        volatile public static bool Suspend = false;

        public const int NOTIFY_ICON_TIP_TIMEOUT = 1; // Timeout for Ballon tip in system tray
        public const int CURRENT_QUEUE_SCROLL_PERIOD = 150; // Scroll timer for moving items in the queue
        public const int ACCEPTABLE_COMPLETION = 85; //What % of the process are accepted as complete in the output handlers, typically there is a small buffer to it doesn't show 100% even though it's completed
        public const double MINIMUM_MERGED_FILE_THRESHOLD = 0.7; // Minimum size of merged file as a % of cut files during commercial removals
        public const int PIPE_TIMEOUT = 60; // Communication pipe message timeout in seconds
        public const string MCEBUDDY_ARCHIVE = "MCEBuddyArchive";
        public const string DefaultCCOffset = "3.1"; // Default offset in seconds, this has to be kept in sync with -ss parameter in the profiles video encoding parameters
        public const int SMTP_TIMEOUT = 60 * 1000; // Smtp time out in ms
        public const string DEFAULT_VIDEO_FILE_TYPES = "*.dvr-ms;*.wtv;*.asf;*.avi;*.divx;*.dv;*.flv;*.gxf;*.m1v;*.m2v;*.m2ts;*.m4v;*.mkv;*.mov;*.mp2;*.mp4;*.mpeg;*.mpeg1;*.mpeg2;*.mpeg4;*.mpg;*.mts;*.mxf;*.ogm;*.ts;*.vob;*.wmv;*.tp";
        public static DateTime NO_BROADCAST_TIME = DateTime.Parse("1900-01-01T00:00:00Z");
        public const string MCEBUDDY_SERVER_NAME = "localhost";
        public const string MCEBUDDY_SERVER_PORT = "23332";
        public const string MCEBUDDY_WEB_SOAP_PIPE = "http://" + MCEBUDDY_SERVER_NAME + ":" + MCEBUDDY_SERVER_PORT + "/MCEBuddy2x";
        public const string MCEBUDDY_LOCAL_NAMED_PIPE = "net.pipe://" + MCEBUDDY_SERVER_NAME + "/MCEBuddy2x";
        public const int GUI_REFRESH_PERIOD = 200; // GUI refresh period in ms
        public const int LOCAL_ENGINE_POLL_PERIOD = 300; // Poll the engine for status on local machine
        public const int REMOTE_ENGINE_POLL_PERIOD = 1000; // Poll the engine for status on remote machine over TCP/IP
        public const int UPNP_POLL_PERIOD = 30 * 60 * 1000; // Poll UPnP device every 'x' minutes for port mappings
        public const BasicHttpSecurityMode MCEBUDDY_PIPE_SECURITY = BasicHttpSecurityMode.None; // Security for Pipe communication using WCF

        public const string MCEBUDDY_DOCUMENTATION = @"HBnyuNYWVJ2Z6OTMWsOWowWox0aJrBKwXzpfmUpciMsr3T3eXpPJOxkuymhVNiWy";
        public const string MCEBUDDY_HOME_PAGE = @"HBnyuNYWVJ2Z6OTMWsOWo2cRktGYe8PktLkJMayRhNI=";
        public const string MCEBUDDY_DOWNLOAD_NEW_VERSION = @"HBnyuNYWVJ2Z6OTMWsOWo1NS0H0aQzXXwu6lm5NdHGL9y3hHxeAOBVf2ktbYaW32";
        public static string[] MCEBUDDY_CHECK_NEW_VERSION = { @"QAuka3atGmUjD0FwLUr79ldA3+xCl7HoOzfOjhg/inr7Rn5fm1uom2wGvDC2T1UD",
                                                             @"QAuka3atGmUjD0FwLUr79ldA3+xCl7HoOzfOjhg/inp4J+U8xTorW8EyMj6l4A0I",
                                                             @"HBnyuNYWVJ2Z6OTMWsOWo1NS0H0aQzXXwu6lm5NdHGL9y3hHxeAOBVf2ktbYaW32",
                                                             @"HBnyuNYWVJ2Z6OTMWsOWowWox0aJrBKwXzpfmUpciMvWBooGv1A9HY4omZkQBeei",
                                                             @"HBnyuNYWVJ2Z6OTMWsOWo6NaekSaHlPv7kYiEay3twBjELtp1/LJ1N1+5A8/5EjW0CIMgg5zfHaIiyZi18zX8A==",
                                                             @"HBnyuNYWVJ2Z6OTMWsOWo6NaekSaHlPv7kYiEay3twDOIva8QqLoKp916q13XVVlH8456LM/vu153oPwTHVf0A=="
                                                           };

        //public static string AppPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().GetModules()[0].FullyQualifiedName);
        //public static string CachePath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().GetModules()[0].FullyQualifiedName), "cache");
        //public static string ConfigPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().GetModules()[0].FullyQualifiedName), "config");
        //public static string LogPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().GetModules()[0].FullyQualifiedName), "log");
        //public static string LocalisationPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().GetModules()[0].FullyQualifiedName), "localisation");

        public static string AppPath = GetAppPath();
        public static string CachePath = Path.Combine(GetAppPath(), "cache");
        public static string ConfigPath = Path.Combine(GetAppPath(), "config");
        public static string LogPath = Path.Combine(GetAppPath(), "log");
        public static string LocalisationPath = Path.Combine(GetAppPath(), "localisation");

        public static string ConfigFile = Path.Combine(ConfigPath, "mcebuddy.conf"); // Store MCEBuddy settings carried over between upgrades
        public static string ProfileFile = Path.Combine(ConfigPath, "profiles.conf"); // Store the conversion base profiles
        public static string HistoryFile = Path.Combine(ConfigPath, "history."); // Store the history of conversions and status
        public static string TempSettingsFile = Path.Combine(ConfigPath, "temp.");  // Temp settings not carried over between upgrades
        public static string ManualQueueFile = Path.Combine(ConfigPath, "manualqueue.");
        public static string AppLogFile = Path.Combine(LogPath, "mcebuddy.log");

        public const string DEFAULT_WINVISTA_AND_LATER_PATH = @"C:\Users\Public\Recorded TV";
        public const string DEFAULT_WINXP_PATH = @"C:\Documents and Settings\All Users\Documents\Recorded TV\";

        public static ProcessPriorityClass Priority = ProcessPriorityClass.Normal;
        public static PriorityTypes IOPriority = PriorityTypes.NORMAL_PRIORITY_CLASS;

        public static List<string[]> profilesSummary; // A 2 dimensional string List containing the Profile name and Description for all profiles in profiles.conf
        public static bool showAnalyzerInstalled; // Is showanalyzer installed on engine system

        public static bool IsNullOrWhiteSpace(string value)
        {
            return String.IsNullOrEmpty(value) || value.Trim().Length == 0;
        }
    }
}
