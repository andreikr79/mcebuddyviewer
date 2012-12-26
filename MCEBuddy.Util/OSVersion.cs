using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MCEBuddy.Util
{
    public static class OSVersion
    {
        /// <summary>
        /// List of known Windows Operating Systems
        /// </summary>
        public enum OS
        {
            UNKNOWN = 0,
            WIN_CE,
            WIN_3_1,
            WIN_95,
            WIN_98,
            WIN_ME,
            NT_3_51,
            NT_4_0,
            WIN_2000,
            WIN_XP,
            WIN_2003,
            WIN_VISTA_2008_SERVER,
            WIN_7_2008_SERVER_R2,
            WIN_8_2012_SERVER
        }

        /// <summary>
        /// Returns the current Operating System
        /// </summary>
        /// <returns>Operating System</returns>
        public static OS GetOSVersion()
        {
            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.Win32S:
                    return OS.WIN_3_1;

                case PlatformID.Win32Windows:
                    switch (Environment.OSVersion.Version.Minor)
                    {
                        case 0:
                            return OS.WIN_95;
                        case 10:
                            return OS.WIN_98;
                        case 90:
                            return OS.WIN_ME;
                        default:
                            return OS.UNKNOWN;
                    }

                case PlatformID.Win32NT:
                    switch (Environment.OSVersion.Version.Major)
                    {
                        case 3:
                            return OS.NT_3_51;
                        case 4:
                            return OS.NT_4_0;
                        case 5:
                            switch (Environment.OSVersion.Version.Minor)
                            {
                                case 0:
                                    return OS.WIN_2000;
                                case 1:
                                    return OS.WIN_XP;
                                case 2:
                                    return OS.WIN_2003;
                                default:
                                    return OS.UNKNOWN;
                            }
                        case 6:
                            switch (Environment.OSVersion.Version.Minor)
                            {
                                case 0:
                                    return OS.WIN_VISTA_2008_SERVER;
                                case 1:
                                    return OS.WIN_7_2008_SERVER_R2;
                                case 2:
                                    return OS.WIN_8_2012_SERVER;
                                default:
                                    return OS.UNKNOWN;
                            }
                        default:
                            return OS.UNKNOWN;
                    }

                case PlatformID.WinCE:
                    return OS.WIN_CE;

                default:
                    return OS.UNKNOWN;
            }
        } 
    }
}
