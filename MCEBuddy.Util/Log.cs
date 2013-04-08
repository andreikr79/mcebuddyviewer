using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.IO;

using MCEBuddy.Globals;

namespace MCEBuddy.Util
{
    public class Log : IDisposable
    {
        /// <summary>
        /// Default global Log level for all log files
        /// </summary>
        public static Log.LogEntryType LogLevel = Log.LogEntryType.Information; // Log Level
        
        /// <summary>
        /// This it the global instance of the MCEBuddy log
        /// </summary>
        public static Log AppLog = new Log(Log.LogDestination.Null); // Global AppLog

        public enum LogEntryType
        {
            Error,
            Warning,
            Information,
            Debug
        } ;

        public enum LogDestination
        {
            Null,
            Console,
            LogFile
        };

        private LogDestination _logDestination = LogDestination.Null;

        private StreamWriter _logStream = null;

        public Log(LogDestination logDestination, string LogFile)
        {
            if (logDestination != LogDestination.LogFile)
            {
                throw new ArgumentException("Log file must be specified only for log destination");
            }
            if (String.IsNullOrEmpty(LogFile))
            {
                throw new ArgumentException("Log file name must be specified");
            }
            _logDestination = LogDestination.LogFile;

            // Log to a file
            try
            {
                _logStream = new StreamWriter(LogFile, false, Encoding.Unicode);
                _logStream.AutoFlush = true;
                _logDestination = LogDestination.LogFile;
            }
            catch (Exception)
            {
                // Logging is not app critical, so do exception thrown
                // Can't dump to file so it goes to the bit bucket
            }
        }

        public Log(LogDestination logDestination)
        {
            if (logDestination == LogDestination.LogFile)
            {
                throw new ArgumentException("Log file destination must supply file name");
            }

            _logDestination = logDestination;
        }

        public LogDestination Destination
        {
            get { return _logDestination; }
        }


        public Log()
        {
            // Log to the console
            _logDestination = LogDestination.Console;
        }

        public void WriteEntry(object obj, string entryText, LogEntryType entryType, bool timeStamp)
        {
            WriteEntry(obj.GetType().ToString(), entryText, entryType, timeStamp);
        }

        public void WriteEntry(object obj, string entryText, LogEntryType entryType)
        {
            WriteEntry(obj.GetType().ToString(), entryText, entryType, true );
        }

        public void WriteEntry(string entryHeader, string entryText, LogEntryType entryType)
        {
            WriteEntry(entryHeader, entryText, entryType, false);
        }

        public void WriteEntry(string entryHeader, string entryText, LogEntryType entryType, bool timeStamp)
        {
            if (_logDestination == LogDestination.Null) return;

            if (entryType <= Log.LogLevel)
            {
                string logLines = "";
                if (entryType < LogEntryType.Debug)
                {
                    // do it the hard way so I can refactor for internationalisation later
                    switch (entryType)
                    {
                        case LogEntryType.Error:
                            {
                                logLines += Localise.GetPhrase("ERROR") + "> ";
                                break;
                            }
                        case LogEntryType.Warning:
                            {
                                logLines += Localise.GetPhrase("WARNING") + "> ";
                                break;
                            }
                        case LogEntryType.Information:
                            {
                                logLines += Localise.GetPhrase("INFORMATION") + "> ";
                                break;
                            }
                    }

                }
                if (timeStamp)
                {
                    entryHeader = DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture) + " " + entryHeader;
                }
                if (!String.IsNullOrEmpty(entryHeader))
                {
                    logLines += entryHeader + " --> " + entryText;
                }
                else
                {
                    logLines = "--> " + entryText;
                }
                if (_logDestination == LogDestination.Console)
                {
                    Console.WriteLine(logLines);
                }
                else if (_logDestination == LogDestination.LogFile)
                {
                    try
                    {
                        _logStream.WriteLine(logLines);
                    }
                    catch (Exception)
                    {
                        // Logging is not app critical, so do exception thrown
                        // Can't dump to file so it goes to the bit bucket
                    }

                }

            }
        }

        public void WriteEntry(string entryText, LogEntryType entryType)
        {
            WriteEntry("", entryText, entryType);
        }

        public void Flush()
        {
            if (_logStream != null)
            {
                try
                {
                    _logStream.Flush();
                }
                catch (Exception)
                { }
            }
        }

        public void Close()
        {
            if (_logDestination == LogDestination.LogFile)
            {
                if (_logStream != null)
                {
                    try
                    {
                        _logStream.Flush();
                        _logStream.Close();
                        _logStream = null; //mark it out
                    }
                    catch (Exception)
                    { }
                }
            }
        }

        public void Dispose()
        {
            WriteEntry(this, Localise.GetPhrase("Log Dispose function called..."), Log.LogEntryType.Warning);
            Close();
        }

        /// <summary>
        /// Writes an entry to the event log but takes care of any exceptions (like source not found or permission issues)
        /// </summary>
        /// <param name="message">Message to write to log</param>
        /// <param name="type">Type of message</param>
        public static void WriteSystemEventLog(string message, EventLogEntryType type)
        {
            try
            {
                EventLog.WriteEntry(GlobalDefs.MCEBUDDY_EVENT_LOG_SOURCE, message, type);
            }
            catch { }
        }
    }

}
