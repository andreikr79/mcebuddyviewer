using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Globalization;

using MCEBuddy.Globals;
using MCEBuddy.Util;

namespace MCEBuddy.Engine
{
    public class CustomRename
    {
        /// <summary>
        /// This function is used to create a custom filename and path.
        /// This function throws an exception if an invalid rename pattern is provided
        /// </summary>
        /// <param name="customRenamePattern">The renaming pattern</param>
        /// <param name="newFileName">Reference to a string that will contain the new custom Filename</param>
        /// <param name="destinationPath">Reference to a string that will contains the new custom Path</param>
        /// <param name="sourceVideo">Path to Source Video file</param>
        /// <param name="metaData">Metadata for the Source Video file</param>
        /// <param name="jobLog">Log object</param>
        public static void CustomRenameFilename(string customRenamePattern, ref string newFileName, ref string destinationPath, string sourceVideo, VideoTags metaData, Log jobLog)
        {
            char[] renameBytes = customRenamePattern.ToCharArray();
            for (int i = 0; i < renameBytes.Length; i++)
            {
                switch (renameBytes[i])
                {
                    case '%':
                        string command = "";
                        while (renameBytes[++i] != '%')
                            command += renameBytes[i].ToString(System.Globalization.CultureInfo.InvariantCulture).ToLower();

                        string format = "";
                        switch (command)
                        {
                            case "airyear": // %ad% - original Air Year
                                if (metaData.OriginalBroadcastDateTime > GlobalDefs.NO_BROADCAST_TIME)
                                    newFileName += metaData.OriginalBroadcastDateTime.ToLocalTime().Year.ToString("0000", System.Globalization.CultureInfo.InvariantCulture);
                                else
                                    jobLog.WriteEntry(Localise.GetPhrase("Cannot find AirYear"), Log.LogEntryType.Warning);
                                break;

                            case "airmonth": // %ad% - original Air Month
                                if (metaData.OriginalBroadcastDateTime > GlobalDefs.NO_BROADCAST_TIME)
                                    newFileName += metaData.OriginalBroadcastDateTime.ToLocalTime().Month.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
                                else
                                    jobLog.WriteEntry(Localise.GetPhrase("Cannot find AirMonth"), Log.LogEntryType.Warning);
                                break;

                            case "airmonthshort": // %ad% - original Air Month abbreviation
                                if (metaData.OriginalBroadcastDateTime > GlobalDefs.NO_BROADCAST_TIME)
                                    newFileName += metaData.OriginalBroadcastDateTime.ToLocalTime().ToString("MMM"); // need to keep it culture sensitive here
                                else
                                    jobLog.WriteEntry(Localise.GetPhrase("Cannot find AirMonthShort"), Log.LogEntryType.Warning);
                                break;

                            case "airmonthlong": // %ad% - original Air Month full name
                                if (metaData.OriginalBroadcastDateTime > GlobalDefs.NO_BROADCAST_TIME)
                                    newFileName += metaData.OriginalBroadcastDateTime.ToLocalTime().ToString("MMMM"); // need to keep it culture sensitive here
                                else
                                    jobLog.WriteEntry(Localise.GetPhrase("Cannot find AirMonthLong"), Log.LogEntryType.Warning);
                                break;

                            case "airday": // %ad% - original Air Date
                                if (metaData.OriginalBroadcastDateTime > GlobalDefs.NO_BROADCAST_TIME)
                                    newFileName += metaData.OriginalBroadcastDateTime.ToLocalTime().Day.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
                                else
                                    jobLog.WriteEntry(Localise.GetPhrase("Cannot find AirDay"), Log.LogEntryType.Warning);
                                break;

                            case "airdayshort": // %ad% - original Air Day of week abbreviation
                                if (metaData.OriginalBroadcastDateTime > GlobalDefs.NO_BROADCAST_TIME)
                                    newFileName += metaData.OriginalBroadcastDateTime.ToLocalTime().ToString("ddd");  // need to keep it culture sensitive here
                                else
                                    jobLog.WriteEntry(Localise.GetPhrase("Cannot find AirDayShort"), Log.LogEntryType.Warning);
                                break;

                            case "airdaylong": // %ad% - original Air Day of week full name
                                if (metaData.OriginalBroadcastDateTime > GlobalDefs.NO_BROADCAST_TIME)
                                    newFileName += metaData.OriginalBroadcastDateTime.ToLocalTime().ToString("dddd");  // need to keep it culture sensitive here
                                else
                                    jobLog.WriteEntry(Localise.GetPhrase("Cannot find AirDayLong"), Log.LogEntryType.Warning);
                                break;

                            case "airhour": // %ad% - original Air Hour
                                if (metaData.OriginalBroadcastDateTime > GlobalDefs.NO_BROADCAST_TIME)
                                    newFileName += metaData.OriginalBroadcastDateTime.ToLocalTime().Hour.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
                                else
                                    jobLog.WriteEntry("Cannot find AirHour", Log.LogEntryType.Warning);
                                break;

                            case "airhourampm": // %ad% - original Air Hour AM/PM
                                if (metaData.OriginalBroadcastDateTime > GlobalDefs.NO_BROADCAST_TIME)
                                    newFileName += metaData.OriginalBroadcastDateTime.ToLocalTime().ToString("tt");
                                else
                                    jobLog.WriteEntry("Cannot find AirHourAMPM", Log.LogEntryType.Warning);
                                break;

                            case "airminute": // %ad% - original Air Minute
                                if (metaData.OriginalBroadcastDateTime > GlobalDefs.NO_BROADCAST_TIME)
                                    newFileName += metaData.OriginalBroadcastDateTime.ToLocalTime().Minute.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
                                else
                                    jobLog.WriteEntry("Cannot find AirMinute", Log.LogEntryType.Warning);
                                break;

                            case "recordyear": // %rd% - Record Year
                                if (metaData.RecordedDateTime > GlobalDefs.NO_BROADCAST_TIME)
                                    newFileName += metaData.RecordedDateTime.ToLocalTime().Year.ToString("0000", System.Globalization.CultureInfo.InvariantCulture);
                                else
                                {
                                    jobLog.WriteEntry(Localise.GetPhrase("Cannot find RecordYear using File Creation Date"), Log.LogEntryType.Warning);
                                    DateTime dt = Util.FileIO.GetFileCreationTime(sourceVideo);
                                    if (dt > GlobalDefs.NO_BROADCAST_TIME)
                                        newFileName += dt.Year.ToString("0000", System.Globalization.CultureInfo.InvariantCulture);
                                }
                                break;

                            case "recordmonth": // %rd% - Record Month
                                if (metaData.RecordedDateTime > GlobalDefs.NO_BROADCAST_TIME)
                                    newFileName += metaData.RecordedDateTime.ToLocalTime().Month.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
                                else
                                {
                                    jobLog.WriteEntry(Localise.GetPhrase("Cannot find RecordMonth using File Creation Date"), Log.LogEntryType.Warning);
                                    DateTime dt = Util.FileIO.GetFileCreationTime(sourceVideo);
                                    if (dt > GlobalDefs.NO_BROADCAST_TIME)
                                        newFileName += dt.Month.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
                                }
                                break;

                            case "recordmonthshort": // %rd% - Record Month abbreviation
                                if (metaData.RecordedDateTime > GlobalDefs.NO_BROADCAST_TIME)
                                    newFileName += metaData.RecordedDateTime.ToLocalTime().ToString("MMM"); // Need to keep it culture sensitive
                                else
                                {
                                    jobLog.WriteEntry(Localise.GetPhrase("Cannot find RecordMonthShort using File Creation Date"), Log.LogEntryType.Warning);
                                    DateTime dt = Util.FileIO.GetFileCreationTime(sourceVideo);
                                    if (dt > GlobalDefs.NO_BROADCAST_TIME)
                                        newFileName += dt.ToString("MMM");
                                }
                                break;

                            case "recordmonthlong": // %rd% - Record Month full name
                                if (metaData.RecordedDateTime > GlobalDefs.NO_BROADCAST_TIME)
                                    newFileName += metaData.RecordedDateTime.ToLocalTime().ToString("MMMM"); // Need to keep it culture sensitive
                                else
                                {
                                    jobLog.WriteEntry(Localise.GetPhrase("Cannot find RecordMonthLong using File Creation Date"), Log.LogEntryType.Warning);
                                    DateTime dt = Util.FileIO.GetFileCreationTime(sourceVideo);
                                    if (dt > GlobalDefs.NO_BROADCAST_TIME)
                                        newFileName += dt.ToString("MMMM");
                                }
                                break;

                            case "recordday": // %rd% - Record Day
                                if (metaData.RecordedDateTime > GlobalDefs.NO_BROADCAST_TIME)
                                    newFileName += metaData.RecordedDateTime.ToLocalTime().Day.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
                                else
                                {
                                    jobLog.WriteEntry(Localise.GetPhrase("Cannot find RecordDay using File Creation Date"), Log.LogEntryType.Warning);
                                    DateTime dt = Util.FileIO.GetFileCreationTime(sourceVideo);
                                    if (dt > GlobalDefs.NO_BROADCAST_TIME)
                                        newFileName += dt.Day.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
                                }
                                break;

                            case "recorddayshort": // %rd% - Record Day of week abbreviation
                                if (metaData.RecordedDateTime > GlobalDefs.NO_BROADCAST_TIME)
                                    newFileName += metaData.RecordedDateTime.ToLocalTime().ToString("ddd"); // Keep it culture sensitive
                                else
                                {
                                    jobLog.WriteEntry(Localise.GetPhrase("Cannot find RecordDayShort using File Creation Date"), Log.LogEntryType.Warning);
                                    DateTime dt = Util.FileIO.GetFileCreationTime(sourceVideo);
                                    if (dt > GlobalDefs.NO_BROADCAST_TIME)
                                        newFileName += dt.ToString("ddd");
                                }
                                break;

                            case "recorddaylong": // %rd% - Record Day of week full name
                                if (metaData.RecordedDateTime > GlobalDefs.NO_BROADCAST_TIME)
                                    newFileName += metaData.RecordedDateTime.ToLocalTime().ToString("dddd"); // Keep it culture sensitive
                                else
                                {
                                    jobLog.WriteEntry(Localise.GetPhrase("Cannot find RecordDayLong using File Creation Date"), Log.LogEntryType.Warning);
                                    DateTime dt = Util.FileIO.GetFileCreationTime(sourceVideo);
                                    if (dt > GlobalDefs.NO_BROADCAST_TIME)
                                        newFileName += dt.ToString("dddd");
                                }
                                break;

                            case "recordhour": // Record Hour
                                if (metaData.RecordedDateTime > GlobalDefs.NO_BROADCAST_TIME)
                                    newFileName += metaData.RecordedDateTime.ToLocalTime().Hour.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
                                else
                                {
                                    jobLog.WriteEntry(Localise.GetPhrase("Cannot find RecordHour using File Creation Date"), Log.LogEntryType.Warning);
                                    DateTime dt = Util.FileIO.GetFileCreationTime(sourceVideo);
                                    if (dt > GlobalDefs.NO_BROADCAST_TIME)
                                        newFileName += dt.Hour.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
                                }
                                break;

                            case "recordhourampm": // Record Hour AM/PM
                                if (metaData.RecordedDateTime > GlobalDefs.NO_BROADCAST_TIME)
                                    newFileName += metaData.RecordedDateTime.ToLocalTime().ToString("tt");
                                else
                                {
                                    jobLog.WriteEntry(Localise.GetPhrase("Cannot find RecordHourAMPM using File Creation Date"), Log.LogEntryType.Warning);
                                    DateTime dt = Util.FileIO.GetFileCreationTime(sourceVideo);
                                    if (dt > GlobalDefs.NO_BROADCAST_TIME)
                                        newFileName += dt.ToString("tt");
                                }
                                break;

                            case "recordminute": // Record minute
                                if (metaData.RecordedDateTime > GlobalDefs.NO_BROADCAST_TIME)
                                    newFileName += metaData.RecordedDateTime.ToLocalTime().Minute.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
                                else
                                {
                                    jobLog.WriteEntry(Localise.GetPhrase("Cannot find RecordMinute using File Creation Date"), Log.LogEntryType.Warning);
                                    DateTime dt = Util.FileIO.GetFileCreationTime(sourceVideo);
                                    if (dt > GlobalDefs.NO_BROADCAST_TIME)
                                        newFileName += dt.Minute.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
                                }
                                break;

                            case "originalfilename": // Name of the source file without the extension or path
                                newFileName += Path.GetFileNameWithoutExtension(sourceVideo);
                                break;

                            case "showname": // %sn% - Showname / title
                                newFileName += metaData.Title;
                                break;

                            case "episodename": // %en% - episode name / subtitle
                                if (!String.IsNullOrEmpty(metaData.SubTitle))
                                    newFileName += metaData.SubTitle;
                                else
                                    jobLog.WriteEntry(Localise.GetPhrase("Cannot find Episode Name"), Log.LogEntryType.Warning);
                                break;

                            case "network": // %en% - recorded channel name
                                if (!String.IsNullOrEmpty(metaData.Network))
                                    newFileName += metaData.Network;
                                else
                                    jobLog.WriteEntry(Localise.GetPhrase("Cannot find Network Channel Name"), Log.LogEntryType.Warning);
                                break;

                            case "season": // %ss%### - season no
                                format = "";
                                try
                                {
                                    if (renameBytes[i + 1] == '#')
                                    {
                                        while (renameBytes[++i] == '#')
                                            format += "0";

                                        --i; // adjust for last increment
                                    }
                                }
                                catch { } // this is normal incase it doesn't exist

                                if (metaData.Season != 0)
                                    newFileName += metaData.Season.ToString(format);
                                else
                                    jobLog.WriteEntry(Localise.GetPhrase("Cannot find Season No"), Log.LogEntryType.Warning);
                                break;

                            case "episode": // %ee%### - episode no
                                format = "";
                                try
                                {
                                    if (renameBytes[i + 1] == '#')
                                    {
                                        while (renameBytes[++i] == '#')
                                            format += "0";

                                        --i; // adjust for last increment
                                    }
                                }
                                catch { } // this is normal incase it doesn't exist

                                if (metaData.Episode != 0)
                                    newFileName += metaData.Episode.ToString(format);
                                else
                                    jobLog.WriteEntry(Localise.GetPhrase("Cannot find Episode No"), Log.LogEntryType.Warning);
                                break;

                            case "ismovie": // Special condition allowing for separate renaming if it's a movie or not
                                // FORMAT: %ismovie%<RenamePatternIfTrue,RenamePatternIfFalse>
                                string truePattern = "", falsePattern = "";
                                while (renameBytes[++i] != '<') ; // Skip until you get a <
                                while (renameBytes[++i] != ',') // Is it's a movie, rename pattern
                                    truePattern += renameBytes[i].ToString(CultureInfo.InvariantCulture);
                                while (renameBytes[++i] != '>') // Is it's NOT a movie, rename pattern
                                    falsePattern += renameBytes[i].ToString(CultureInfo.InvariantCulture);

                                // Now parse the rename pattern and we're done
                                if (metaData.IsMovie)
                                    CustomRenameFilename(truePattern, ref newFileName, ref destinationPath, sourceVideo, metaData, jobLog);
                                else
                                    CustomRenameFilename(falsePattern, ref newFileName, ref destinationPath, sourceVideo, metaData, jobLog);
                                break; // We're done

                            default:
                                jobLog.WriteEntry(Localise.GetPhrase("Invalid file naming format detected, skipping") + " : " + command, Log.LogEntryType.Warning); // We had an invalid format
                                break;
                        }
                        break;

                    case '\\':
                        if (!MCEBuddy.Globals.GlobalDefs.IsNullOrWhiteSpace(destinationPath) && (destinationPath.Substring(destinationPath.Length - 1) != "\\")) // First directory should not start with a '\' and there should not be any consecutive '\'
                            destinationPath += "\\";
                        destinationPath += Util.FilePaths.RemoveIllegalFilePathChars(newFileName);
                        newFileName = ""; // reset the new filename
                        break;

                    default:
                        if (!Util.FilePaths.IsIllegalFilePathChar(renameBytes[i])) // Only accept characters which are not illegal in file paths otherwise ignore
                            newFileName += renameBytes[i];
                        break;
                }
            }
        }
    }
}
