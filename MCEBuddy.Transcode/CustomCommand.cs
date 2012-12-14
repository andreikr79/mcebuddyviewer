using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

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
        public CustomCommand(string profile, string convertedFile, string sourceFile, string remuxFile, VideoTags metaData, ref JobStatus jobStatus, Log jobLog)
        {
            _profile = profile;
            _jobLog = jobLog;
            _jobStatus = jobStatus;
            _convertedFile = convertedFile;
            _sourceFile = sourceFile;
            _remuxFile = remuxFile;
            _metaData = metaData;

            Ini ini = new Ini(GlobalDefs.ProfileFile);
            commandPath = ini.ReadString(profile, "CustomCommandPath", "").ToLower().Trim();
            commandParameters = ini.ReadString(profile, "CustomCommandParameters", "");
            hangPeriod = ini.ReadInteger(profile, "CustomCommandHangPeriod", -1);
            customCommandCritical = ini.ReadBoolean(profile, "CustomCommandCritical", false); // NOTE: if customCommandCritical is TRUE will need to return false in case it's a failure
        }

        public bool Run()
        {
            if (commandPath == "")
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("No custom commands found"), Log.LogEntryType.Information);
                return true; // not a critical failure
            }

            if ((hangPeriod < 0) || (!File.Exists(commandPath)))
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Invalid custom command parameters") + " CommandPath = " + commandPath + " CommandParameters = " + commandParameters + " CommandHangPeriod = " + hangPeriod.ToString(System.Globalization.CultureInfo.InvariantCulture), (customCommandCritical ? Log.LogEntryType.Error : Log.LogEntryType.Warning));
                if (customCommandCritical)
                    _jobStatus.ErrorMsg = Localise.GetPhrase("Invalid custom command parameters"); // Set an error message on if we are failing the conversion
                return !customCommandCritical; // return the opposite of the critical (if it's true then return false)
            }

            _jobLog.WriteEntry(this, Localise.GetPhrase("Read custom command parameters") + " CommandPath = " + commandPath + " CommandParameters = " + commandParameters + " CommandHangPeriod = " + hangPeriod.ToString(System.Globalization.CultureInfo.InvariantCulture) + " CommandCritical = " + customCommandCritical.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);

            // Replace the converted file string if it exists in the commandParameters
            commandParameters = commandParameters.Replace("%convertedfile%", Util.FilePaths.FixSpaces(_convertedFile)); // Preserve case for parameters
            commandParameters = commandParameters.Replace("%sourcefile%", Util.FilePaths.FixSpaces(_sourceFile)); // Preserve case for parameters
            commandParameters = commandParameters.Replace("%remuxfile%", Util.FilePaths.FixSpaces(_remuxFile)); // Preserve case for parameters
            
            // Support for Metadata in custom parameters
            commandParameters = commandParameters.Replace("%originalfilepath%", Util.FilePaths.FixSpaces(Path.GetDirectoryName(_sourceFile))); // Preserve case for parameters
            commandParameters = commandParameters.Replace("%originalfilename%", Util.FilePaths.FixSpaces(Path.GetFileNameWithoutExtension(_sourceFile))); // Preserve case for parameters
            commandParameters = commandParameters.Replace("%showname%", Util.FilePaths.FixSpaces(_metaData.Title)); // Preserve case for parameters
            commandParameters = commandParameters.Replace("%genre%", Util.FilePaths.FixSpaces(_metaData.Genres != null ? (_metaData.Genres.Length > 0 ? _metaData.Genres[0] : "") : "")); // Preserve case for parameters
            commandParameters = commandParameters.Replace("%episodename%", Util.FilePaths.FixSpaces(_metaData.SubTitle)); // Preserve case for parameters
            commandParameters = commandParameters.Replace("%episodedescription%", Util.FilePaths.FixSpaces(_metaData.SubTitleDescription)); // Preserve case for parameters
            commandParameters = commandParameters.Replace("%network%", Util.FilePaths.FixSpaces(_metaData.Network)); // Preserve case for parameters
            commandParameters = commandParameters.Replace("%bannerfile%", Util.FilePaths.FixSpaces(_metaData.BannerFile)); // Preserve case for parameters
            commandParameters = commandParameters.Replace("%bannerurl%", Util.FilePaths.FixSpaces(_metaData.BannerURL)); // Preserve case for parameters
            commandParameters = commandParameters.Replace("%movieid%", Util.FilePaths.FixSpaces(_metaData.movieId)); // Preserve case for parameters
            commandParameters = commandParameters.Replace("%seriesid%", Util.FilePaths.FixSpaces(_metaData.seriesId)); // Preserve case for parameters
            commandParameters = commandParameters.Replace("%season%", Util.FilePaths.FixSpaces((_metaData.Season == 0 ? "" : _metaData.Season.ToString(System.Globalization.CultureInfo.InvariantCulture)))); // Preserve case for parameters
            commandParameters = commandParameters.Replace("%episode%", Util.FilePaths.FixSpaces((_metaData.Episode == 0 ? "" : _metaData.Episode.ToString(System.Globalization.CultureInfo.InvariantCulture)))); // Preserve case for parameters
            commandParameters = commandParameters.Replace("%ismovie%", Util.FilePaths.FixSpaces(_metaData.IsMovie.ToString(System.Globalization.CultureInfo.InvariantCulture))); // Preserve case for parameters
            commandParameters = commandParameters.Replace("%airyear%", Util.FilePaths.FixSpaces((_metaData.OriginalBroadcastDateTime > GlobalDefs.NO_BROADCAST_TIME) ? _metaData.OriginalBroadcastDateTime.ToLocalTime().Year.ToString("0000", System.Globalization.CultureInfo.InvariantCulture) : "")); // Preserve case for parameters
            commandParameters = commandParameters.Replace("%airmonth%", Util.FilePaths.FixSpaces((_metaData.OriginalBroadcastDateTime > GlobalDefs.NO_BROADCAST_TIME) ? _metaData.OriginalBroadcastDateTime.ToLocalTime().Month.ToString("00", System.Globalization.CultureInfo.InvariantCulture) : "")); // Preserve case for parameters
            commandParameters = commandParameters.Replace("%airday%", Util.FilePaths.FixSpaces((_metaData.OriginalBroadcastDateTime > GlobalDefs.NO_BROADCAST_TIME) ? _metaData.OriginalBroadcastDateTime.ToLocalTime().Day.ToString("00", System.Globalization.CultureInfo.InvariantCulture) : "")); // Preserve case for parameters
            commandParameters = commandParameters.Replace("%airhour%", Util.FilePaths.FixSpaces((_metaData.OriginalBroadcastDateTime > GlobalDefs.NO_BROADCAST_TIME) ? _metaData.OriginalBroadcastDateTime.ToLocalTime().Hour.ToString("00", System.Globalization.CultureInfo.InvariantCulture) : "")); // Preserve case for parameters
            commandParameters = commandParameters.Replace("%airminute%", Util.FilePaths.FixSpaces((_metaData.OriginalBroadcastDateTime > GlobalDefs.NO_BROADCAST_TIME) ? _metaData.OriginalBroadcastDateTime.ToLocalTime().Minute.ToString("00", System.Globalization.CultureInfo.InvariantCulture) : "")); // Preserve case for parameters
            commandParameters = commandParameters.Replace("%recordyear%", Util.FilePaths.FixSpaces((_metaData.RecordedDateTime > GlobalDefs.NO_BROADCAST_TIME) ? _metaData.RecordedDateTime.ToLocalTime().Year.ToString("0000", System.Globalization.CultureInfo.InvariantCulture) : Util.FileIO.GetFileCreationTime(_sourceFile).Year.ToString("0000", System.Globalization.CultureInfo.InvariantCulture))); // Preserve case for parameters
            commandParameters = commandParameters.Replace("%recordmonth%", Util.FilePaths.FixSpaces((_metaData.RecordedDateTime > GlobalDefs.NO_BROADCAST_TIME) ? _metaData.RecordedDateTime.ToLocalTime().Month.ToString("00", System.Globalization.CultureInfo.InvariantCulture) : Util.FileIO.GetFileCreationTime(_sourceFile).Month.ToString("00", System.Globalization.CultureInfo.InvariantCulture))); // Preserve case for parameters
            commandParameters = commandParameters.Replace("%recorddate%", Util.FilePaths.FixSpaces((_metaData.RecordedDateTime > GlobalDefs.NO_BROADCAST_TIME) ? _metaData.RecordedDateTime.ToLocalTime().Day.ToString("00", System.Globalization.CultureInfo.InvariantCulture) : Util.FileIO.GetFileCreationTime(_sourceFile).Day.ToString("00", System.Globalization.CultureInfo.InvariantCulture))); // Preserve case for parameters
            commandParameters = commandParameters.Replace("%recordhour%", Util.FilePaths.FixSpaces((_metaData.RecordedDateTime > GlobalDefs.NO_BROADCAST_TIME) ? _metaData.RecordedDateTime.ToLocalTime().Hour.ToString("00", System.Globalization.CultureInfo.InvariantCulture) : Util.FileIO.GetFileCreationTime(_sourceFile).Hour.ToString("00", System.Globalization.CultureInfo.InvariantCulture))); // Preserve case for parameters
            commandParameters = commandParameters.Replace("%recordminute%", Util.FilePaths.FixSpaces((_metaData.RecordedDateTime > GlobalDefs.NO_BROADCAST_TIME) ? _metaData.RecordedDateTime.ToLocalTime().Minute.ToString("00", System.Globalization.CultureInfo.InvariantCulture) : Util.FileIO.GetFileCreationTime(_sourceFile).Minute.ToString("00", System.Globalization.CultureInfo.InvariantCulture))); // Preserve case for parameters

            try
            {
                baseCommand = new Base(commandParameters, commandPath, ref _jobStatus, _jobLog, true, true); // send the absolute command path and by default success is true until process is terminated
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

            _jobLog.WriteEntry(this, Localise.GetPhrase("About to run custom command with parameters") + " CommandPath = " + commandPath + " CommandParameters = " + commandParameters + " CommandHangPeriod = " + hangPeriod.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);

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
