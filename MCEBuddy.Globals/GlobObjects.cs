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
using System.Runtime.InteropServices;

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

        public const string SEGMENT_CUT_OFFSET_GOP_COMPENSATE = "3.2"; // average number of second to compensate for GOP alignment when cutting segments (used to Subtitle Alignment)
        public const double MINIMUM_SEGMENT_LENGTH = 5; // Number of seconds each file segment will be when cutting files during commercial removal
        public const int NOTIFY_ICON_TIP_TIMEOUT = 1; // Timeout for Ballon tip in system tray
        public const int CURRENT_QUEUE_SCROLL_PERIOD = 150; // Scroll timer for moving items in the queue
        public const int ACCEPTABLE_COMPLETION = 85; //What % of the process are accepted as complete in the output handlers, typically there is a small buffer to it doesn't show 100% even though it's completed
        public const double MINIMUM_MERGED_FILE_THRESHOLD = 0.7; // Minimum size of merged file as a % of cut files during commercial removals
        public const int PIPE_TIMEOUT = 60; // Communication pipe message timeout in seconds
        public const string MCEBUDDY_ARCHIVE = "MCEBuddyArchive";
        public const string MCEBUDDY_EVENT_LOG_SOURCE = "MCEBuddy2x"; // name of application source in System event log
        public const string DefaultCCOffset = "2.5"; // Default offset in seconds, this has to be kept in sync with -ss parameter in the profiles video encoding parameters
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

        // Custom GetFolderPath. For 3.5 framework compatibility
        public enum SpecialFolderType
        {
            Desktop = 0x0000,        // <desktop>
            Internet = 0x0001,        // Internet Explorer (icon on desktop)
            Programs = 0x0002,        // Start Menu\Programs
            Controls = 0x0003,        // My Computer\Control Panel
            Printers = 0x0004,        // My Computer\Printers
            Personal = 0x0005,        // My Documents
            Favorites = 0x0006,        // <user name>\Favorites
            Startup = 0x0007,        // Start Menu\Programs\Startup
            Recent = 0x0008,        // <user name>\Recent
            SendTo = 0x0009,        // <user name>\SendTo
            BitBucket = 0x000a,        // <desktop>\Recycle Bin
            StartMenu = 0x000b,        // <user name>\Start Menu
            MyDocuments = Personal,      //  Personal was just a silly name for My Documents
            MyMusic = 0x000d,        // "My Music" folder
            MyVideo = 0x000e,        // "My Videos" folder
            DesktopDirectory = 0x0010,        // <user name>\Desktop
            Drives = 0x0011,        // My Computer
            Network = 0x0012,        // Network Neighborhood (My Network Places)
            NetHood = 0x0013,        // <user name>\nethood
            Fonts = 0x0014,        // windows\fonts
            Templates = 0x0015,
            CommonStartMenu = 0x0016,        // All Users\Start Menu
            CommonPrograms = 0X0017,        // All Users\Start Menu\Programs
            CommonStartup = 0x0018,        // All Users\Startup
            CommonDesktopDirectory = 0x0019,        // All Users\Desktop
            AppData = 0x001a,        // <user name>\Application Data
            PrintHood = 0x001b,        // <user name>\PrintHood
            LocalAppData = 0x001c,        // <user name>\Local Settings\Applicaiton Data (non roaming)
            AltStartup = 0x001d,        // non localized startup
            CommonAltStartup = 0x001e,        // non localized common startup
            CommonFavorites = 0x001f,
            InternetCache = 0x0020,
            Cookies = 0x0021,
            History = 0x0022,
            CommonAppData = 0x0023,        // All Users\Application Data
            Windows = 0x0024,        // GetWindowsDirectory()
            System = 0x0025,        // GetSystemDirectory()
            ProgramFiles = 0x0026,        // C:\Program Files
            MyPictures = 0x0027,        // C:\Program Files\My Pictures
            Profile = 0x0028,        // USERPROFILE
            SystemX86 = 0x0029,        // x86 system directory on RISC
            ProgramFilesX86 = 0x002a,        // x86 C:\Program Files on RISC
            ProgramFilesCommon = 0x002b,        // C:\Program Files\Common
            ProgramFilesCommonX86 = 0x002c,        // x86 Program Files\Common on RISC
            CommonTemplates = 0x002d,        // All Users\Templates
            CommonDocuments = 0x002e,        // All Users\Documents
            CommonAdminTools = 0x002f,        // All Users\Start Menu\Programs\Administrative Tools
            AdminTools = 0x0030,        // <user name>\Start Menu\Programs\Administrative Tools
            Connections = 0x0031,        // Network and Dial-up Connections
            CommonMusic = 0x0035,        // All Users\My Music
            CommonPictures = 0x0036,        // All Users\My Pictures
            CommonVideo = 0x0037,        // All Users\My Video
            Resources = 0x0038,        // Resource Direcotry
            ResourcesLocalized = 0x0039,        // Localized Resource Direcotry
            CommonOemLinks = 0x003a,        // Links to All Users OEM specific apps
            CDBurning = 0x003b,        // USERPROFILE\Local Settings\Application Data\Microsoft\CD Burning
            ComputersNearMe = 0x003d,        // Computers Near Me (computered from Workgroup membership)
        }
        [DllImport("shell32.dll")]
        static extern int SHGetFolderPath(IntPtr hwndOwner, int nFolder, IntPtr hToken,
           uint dwFlags, [Out] StringBuilder pszPath);
        static public string GetFolderPath(SpecialFolderType spfolder)
        {
            StringBuilder sb = new StringBuilder();
            SHGetFolderPath(IntPtr.Zero, (int)spfolder, IntPtr.Zero, 0, sb);
            return sb.ToString();
        }

        public static string ConfigFile = Path.Combine(ConfigPath, "mcebuddy.conf"); // Store MCEBuddy settings carried over between upgrades
        public static string ProfileFile = Path.Combine(ConfigPath, "profiles.conf"); // Store the conversion base profiles
        public static string HistoryFile = Path.Combine(ConfigPath, "history."); // Store the history of conversions and status
        public static string TempSettingsFile = Path.Combine(GetFolderPath(SpecialFolderType.CommonDocuments), "temp.");  // Temp settings not carried over between upgrades
        public static string ManualQueueFile = Path.Combine(ConfigPath, "manualqueue.");
        public static string AppLogFile = Path.Combine(LogPath, "mcebuddy.log");

        public const string DEFAULT_WINVISTA_AND_LATER_PATH = @"C:\Users\Public\Recorded TV";
        public const string DEFAULT_WINXP_PATH = @"C:\Documents and Settings\All Users\Documents\Recorded TV\";

        public static ProcessPriorityClass Priority = ProcessPriorityClass.Normal;
        public static PriorityTypes IOPriority = PriorityTypes.NORMAL_PRIORITY_CLASS;

        public static List<string[]> profilesSummary; // A 2 dimensional string List containing the Profile name and Description for all profiles in profiles.conf
        public static bool showAnalyzerInstalled; // Is showanalyzer installed on engine system

        public static string[] supportFilesExt = { ".edl", ".srt", ".xml", ".nfo", ".arg" }; // All extra files generated/supported by MCEBuddy along with converted/original file

        public static bool IsNullOrWhiteSpace(string value)
        {
            return String.IsNullOrEmpty(value) || value.Trim().Length == 0;
        }
    }
}
