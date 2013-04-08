using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Globalization;

using MCEBuddy.Util;
using MCEBuddy.Globals;
using MCEBuddy.AppWrapper;
using MCEBuddy.MetaData;

namespace MCEBuddy.Transcode
{
    public class CustomCommand
    {
        private string _profile;
        private JobStatus _jobStatus;
        private Log _jobLog;
        private string commandPath = "";
        private string commandParameters = "";
        private int hangPeriod = -1;
        private Base baseCommand;
        private string _convertedFile;
        private string _sourceFile;
        private string _remuxFile;
        private bool customCommandCritical = false;
        private VideoTags _metaData;
        private string _edlFile;

        /// <summary>
        /// Used to execute custom commands after the conversion process is compelte just before the file is moved to the desination directory
        /// </summary>
        /// <param name="profile">Profile name</param>
        /// <param name="convertedFile">Full path to final converted file</param>
        /// <param name="sourceFile">Full path to original source file</param>
        /// <param name="remuxFile">Full path to intermediate remuxed file</param>
        /// <param name="metaData">Video metadata structure for source file</param>
        /// <param name="jobStatus">ref to JobStatus</param>
        /// <param name="jobLog">JobLog</param>
        public CustomCommand(string profile, string convertedFile, string sourceFile, string remuxFile, string edlFile, VideoTags metaData, ref JobStatus jobStatus, Log jobLog)
        {
            _profile = profile;
            _jobLog = jobLog;
            _jobStatus = jobStatus;
            _convertedFile = convertedFile;
            _sourceFile = sourceFile;
            _remuxFile = remuxFile;
            _edlFile = edlFile;
            _metaData = metaData;

            Ini ini = new Ini(GlobalDefs.ProfileFile);
            commandPath = ini.ReadString(profile, "CustomCommandPath", "").ToLower().Trim();
            commandParameters = ini.ReadString(profile, "CustomCommandParameters", "");
            hangPeriod = ini.ReadInteger(profile, "CustomCommandHangPeriod", -1);
            customCommandCritical = ini.ReadBoolean(profile, "CustomCommandCritical", false); // NOTE: if customCommandCritical is TRUE will need to return false in case it's a failure
        }

        public bool Run()
        {
            string translatedCommand = "";

            if (commandPath == "")
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("No custom commands found"), Log.LogEntryType.Information);
                return true; // not a critical failure
            }

            if ((hangPeriod < 0) || (!File.Exists(commandPath)))
            {
                if (hangPeriod < 0)
                    _jobLog.WriteEntry(this, "CustomCommandHangPeriod NOT specified!", Log.LogEntryType.Error);
                else
                    _jobLog.WriteEntry(this, "CustomCommandPath does NOT exist!", Log.LogEntryType.Error);

                _jobLog.WriteEntry(this, Localise.GetPhrase("Invalid custom command parameters") + " \nCustomCommandPath = " + commandPath + " \nCustomCommandParameters = " + commandParameters + " \nCustomCommandHangPeriod = " + hangPeriod.ToString(System.Globalization.CultureInfo.InvariantCulture) + " \nCustomCommandCritical = " + customCommandCritical.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Error);
                
                if (customCommandCritical)
                    _jobStatus.ErrorMsg = Localise.GetPhrase("Invalid custom command parameters"); // Set an error message on if we are failing the conversion
                
                return !customCommandCritical; // return the opposite of the critical (if it's true then return false)
            }

            _jobLog.WriteEntry(this, Localise.GetPhrase("Read custom command parameters") + " \nCommandPath = " + commandPath + " \nCommandParameters = " + commandParameters + " \nCommandHangPeriod = " + hangPeriod.ToString(System.Globalization.CultureInfo.InvariantCulture) + " \nCommandCritical = " + customCommandCritical.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);

            // SRT and EDl files are substitued if they exist otherwise they are ""
            string srtFile = Path.Combine(Path.GetDirectoryName(_convertedFile), (Path.GetFileNameWithoutExtension(_sourceFile) + ".srt")); // SRT file created by 3rd Party in temp working directory
            if (!File.Exists(srtFile))
                srtFile = "";
            string edlFile = _edlFile;
            if (!File.Exists(edlFile))
                edlFile = "";

            try
            {
                char[] commandBytes = commandParameters.ToCharArray();
                for (int i = 0; i < commandBytes.Length; i++)
                {
                    switch (commandBytes[i])
                    {
                        case '%':
                            string command = "";
                            while (commandBytes[++i] != '%')
                                command += commandBytes[i].ToString(System.Globalization.CultureInfo.InvariantCulture).ToLower();

                            string format = "";
                            switch (command)
                            {
                                case "convertedfile":
                                    translatedCommand += (_convertedFile); // Preserve case for parameters
                                    break;

                                case "sourcefile":
                                    translatedCommand += (_sourceFile); // Preserve case for parameters
                                    break;

                                case "remuxfile":
                                    translatedCommand += (_remuxFile); // Preserve case for parameters
                                    break;

                                case "workingpath":
                                    translatedCommand += (Path.GetDirectoryName(_convertedFile)); // Preserve case for parameters
                                    break;

                                case "srtfile":
                                    translatedCommand += (srtFile); // Preserve case for parameters
                                    break;

                                case "edlfile":
                                    translatedCommand += (edlFile); // Preserve case for parameters
                                    break;

                                case "originalfilepath":
                                    translatedCommand += (Path.GetDirectoryName(_sourceFile)); // Preserve case for parameters
                                    break;

                                case "originalfilename":
                                    translatedCommand += (Path.GetFileNameWithoutExtension(_sourceFile)); // Preserve case for parameters
                                    break;

                                case "showname":
                                    translatedCommand += (_metaData.Title); // Preserve case for parameters
                                    break;

                                case "genre":
                                    translatedCommand += (_metaData.Genres != null ? (_metaData.Genres.Length > 0 ? _metaData.Genres[0] : "") : ""); // Preserve case for parameters
                                    break;

                                case "episodename":
                                    translatedCommand += (_metaData.SubTitle); // Preserve case for parameters
                                    break;

                                case "episodedescription":
                                    translatedCommand += (_metaData.Description); // Preserve case for parameters
                                    break;

                                case "network":
                                    translatedCommand += (_metaData.Network); // Preserve case for parameters
                                    break;

                                case "bannerfile":
                                    translatedCommand += (_metaData.BannerFile); // Preserve case for parameters
                                    break;

                                case "bannerurl":
                                    translatedCommand += (_metaData.BannerURL); // Preserve case for parameters
                                    break;

                                case "movieid":
                                    translatedCommand += (_metaData.movieDBMovieId); // Preserve case for parameters
                                    break;

                                case "imdbmovieid":
                                    translatedCommand += (_metaData.imdbMovieId); // Preserve case for parameters
                                    break;

                                case "seriesid":
                                    translatedCommand += (_metaData.tvdbSeriesId); // Preserve case for parameters
                                    break;

                                case "season":
                                    format = "";
                                    try
                                    {
                                        if (commandBytes[i + 1] == '#')
                                        {
                                            while (commandBytes[++i] == '#')
                                                format += "0";

                                            --i; // adjust for last increment
                                        }
                                    }
                                    catch { } // this is normal incase it doesn't exist

                                    translatedCommand += ((_metaData.Season == 0 ? "" : _metaData.Season.ToString(format))); // Preserve case for parameters
                                    break;

                                case "episode":
                                    format = "";
                                    try
                                    {
                                        if (commandBytes[i + 1] == '#')
                                        {
                                            while (commandBytes[++i] == '#')
                                                format += "0";

                                            --i; // adjust for last increment
                                        }
                                    }
                                    catch { } // this is normal incase it doesn't exist

                                    translatedCommand += ((_metaData.Episode == 0 ? "" : _metaData.Episode.ToString(format))); // Preserve case for parameters
                                    break;

                                case "ismovie":
                                    translatedCommand += (_metaData.IsMovie.ToString(CultureInfo.InvariantCulture)); // Preserve case for parameters
                                    break;

                                case "airyear":
                                    translatedCommand += ((_metaData.OriginalBroadcastDateTime > GlobalDefs.NO_BROADCAST_TIME) ? _metaData.OriginalBroadcastDateTime.ToLocalTime().ToString("yyyy") : ""); // Preserve case for parameters
                                    break;

                                case "airmonth":
                                    translatedCommand += ((_metaData.OriginalBroadcastDateTime > GlobalDefs.NO_BROADCAST_TIME) ? _metaData.OriginalBroadcastDateTime.ToLocalTime().ToString("%M") : ""); // Preserve case for parameters
                                    break;

                                case "airmonthshort":
                                    translatedCommand += ((_metaData.OriginalBroadcastDateTime > GlobalDefs.NO_BROADCAST_TIME) ? _metaData.OriginalBroadcastDateTime.ToLocalTime().ToString("MMM") : ""); // Preserve case for parameters, culture sensitive
                                    break;

                                case "airmonthlong":
                                    translatedCommand += ((_metaData.OriginalBroadcastDateTime > GlobalDefs.NO_BROADCAST_TIME) ? _metaData.OriginalBroadcastDateTime.ToLocalTime().ToString("MMMM") : ""); // Preserve case for parameters, culture sensitive
                                    break;

                                case "airday":
                                    translatedCommand += ((_metaData.OriginalBroadcastDateTime > GlobalDefs.NO_BROADCAST_TIME) ? _metaData.OriginalBroadcastDateTime.ToLocalTime().ToString("%d") : ""); // Preserve case for parameters
                                    break;

                                case "airdayshort":
                                    translatedCommand += ((_metaData.OriginalBroadcastDateTime > GlobalDefs.NO_BROADCAST_TIME) ? _metaData.OriginalBroadcastDateTime.ToLocalTime().ToString("ddd") : ""); // Preserve case for parameters, culture sensitive
                                    break;

                                case "airdaylong":
                                    translatedCommand += ((_metaData.OriginalBroadcastDateTime > GlobalDefs.NO_BROADCAST_TIME) ? _metaData.OriginalBroadcastDateTime.ToLocalTime().ToString("dddd") : ""); // Preserve case for parameters, culture sensitive
                                    break;

                                case "airhour":
                                    translatedCommand += ((_metaData.OriginalBroadcastDateTime > GlobalDefs.NO_BROADCAST_TIME) ? _metaData.OriginalBroadcastDateTime.ToLocalTime().ToString("%h") : ""); // Preserve case for parameters
                                    break;

                                case "airhourampm":
                                    translatedCommand += ((_metaData.OriginalBroadcastDateTime > GlobalDefs.NO_BROADCAST_TIME) ? _metaData.OriginalBroadcastDateTime.ToLocalTime().ToString("tt") : ""); // Preserve case for parameters
                                    break;

                                case "airminute":
                                    translatedCommand += ((_metaData.OriginalBroadcastDateTime > GlobalDefs.NO_BROADCAST_TIME) ? _metaData.OriginalBroadcastDateTime.ToLocalTime().ToString("%m") : ""); // Preserve case for parameters
                                    break;

                                case "recordyear":
                                    translatedCommand += ((_metaData.RecordedDateTime > GlobalDefs.NO_BROADCAST_TIME) ? _metaData.RecordedDateTime.ToLocalTime().ToString("yyyy") : Util.FileIO.GetFileCreationTime(_sourceFile).ToString("yyyy")); // Preserve case for parameters
                                    break;

                                case "recordmonth":
                                    translatedCommand += ((_metaData.RecordedDateTime > GlobalDefs.NO_BROADCAST_TIME) ? _metaData.RecordedDateTime.ToLocalTime().ToString("%M") : Util.FileIO.GetFileCreationTime(_sourceFile).ToString("%M")); // Preserve case for parameters
                                    break;

                                case "recordmonthshort":
                                    translatedCommand += ((_metaData.RecordedDateTime > GlobalDefs.NO_BROADCAST_TIME) ? _metaData.RecordedDateTime.ToLocalTime().ToString("MMM") : Util.FileIO.GetFileCreationTime(_sourceFile).ToString("MMM")); // Preserve case for parameters, culture sensitive
                                    break;

                                case "recordmonthlong":
                                    translatedCommand += ((_metaData.RecordedDateTime > GlobalDefs.NO_BROADCAST_TIME) ? _metaData.RecordedDateTime.ToLocalTime().ToString("MMMM") : Util.FileIO.GetFileCreationTime(_sourceFile).ToString("MMMM")); // Preserve case for parameters, culture sensitive
                                    break;

                                case "recordday":
                                    translatedCommand += ((_metaData.RecordedDateTime > GlobalDefs.NO_BROADCAST_TIME) ? _metaData.RecordedDateTime.ToLocalTime().ToString("%d") : Util.FileIO.GetFileCreationTime(_sourceFile).ToString("%d")); // Preserve case for parameters
                                    break;

                                case "recorddayshort":
                                    translatedCommand += ((_metaData.RecordedDateTime > GlobalDefs.NO_BROADCAST_TIME) ? _metaData.RecordedDateTime.ToLocalTime().ToString("ddd") : Util.FileIO.GetFileCreationTime(_sourceFile).ToString("ddd")); // Preserve case for parameters, culture sensitive
                                    break;

                                case "recorddaylong":
                                    translatedCommand += ((_metaData.RecordedDateTime > GlobalDefs.NO_BROADCAST_TIME) ? _metaData.RecordedDateTime.ToLocalTime().ToString("dddd") : Util.FileIO.GetFileCreationTime(_sourceFile).ToString("dddd")); // Preserve case for parameters, culture sensitive
                                    break;

                                case "recordhour":
                                    translatedCommand += ((_metaData.RecordedDateTime > GlobalDefs.NO_BROADCAST_TIME) ? _metaData.RecordedDateTime.ToLocalTime().ToString("%h") : Util.FileIO.GetFileCreationTime(_sourceFile).ToString("%h")); // Preserve case for parameters
                                    break;

                                case "recordhourampm":
                                    translatedCommand += ((_metaData.RecordedDateTime > GlobalDefs.NO_BROADCAST_TIME) ? _metaData.RecordedDateTime.ToLocalTime().ToString("tt") : Util.FileIO.GetFileCreationTime(_sourceFile).ToString("tt")); // Preserve case for parameters
                                    break;

                                case "recordminute":
                                    translatedCommand += ((_metaData.RecordedDateTime > GlobalDefs.NO_BROADCAST_TIME) ? _metaData.RecordedDateTime.ToLocalTime().ToString("%m") : Util.FileIO.GetFileCreationTime(_sourceFile).ToString("%m")); // Preserve case for parameters
                                    break;

                                default:
                                    _jobLog.WriteEntry(Localise.GetPhrase("Invalid custom command format detected, skipping") + " : " + command, Log.LogEntryType.Warning); // We had an invalid format
                                    break;
                            }
                            break;

                        default:
                            translatedCommand += commandBytes[i];
                            break;
                    }
                }
            }
            catch (Exception e)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Invalid custom command. Error " + e.ToString()), (customCommandCritical ? Log.LogEntryType.Error : Log.LogEntryType.Warning));
                if (customCommandCritical)
                    _jobStatus.ErrorMsg = Localise.GetPhrase("Invalid custom command"); // Set an error message on if we are failing the conversion
                return !customCommandCritical; // return the opposite of the critical (if it's true then return false)
            }

            try
            {
                baseCommand = new Base(true, translatedCommand, commandPath, ref _jobStatus, _jobLog); // send the absolute command path and by default success is true until process is terminated and show Window
            }
            catch (FileNotFoundException)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Invalid custom command path") + " CommandPath = " + commandPath, (customCommandCritical ? Log.LogEntryType.Error : Log.LogEntryType.Warning));
                if (customCommandCritical)
                    _jobStatus.ErrorMsg = Localise.GetPhrase("Invalid custom command path"); // Set an error message on if we are failing the conversion
                return !customCommandCritical; // return the opposite of the critical (if it's true then return false)
            }

            // Set the hang detection period
            baseCommand.HangPeriod = hangPeriod;

            _jobLog.WriteEntry(this, Localise.GetPhrase("About to run custom command with parameters") + " \nCommandPath = " + commandPath + " \nCommandParameters = " + translatedCommand + " \nCommandHangPeriod = " + hangPeriod.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);

            // Run the custom command
            baseCommand.Run();

            // Check for hang/termination
            if (!baseCommand.Success)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Custom command hung, process was terminated"), (customCommandCritical ? Log.LogEntryType.Error : Log.LogEntryType.Warning));
                if (customCommandCritical)
                    _jobStatus.ErrorMsg = Localise.GetPhrase("Custom command hung, process was terminated");
                return !customCommandCritical; // return the opposite, see above
            }

            return true;
        }
    }
}
