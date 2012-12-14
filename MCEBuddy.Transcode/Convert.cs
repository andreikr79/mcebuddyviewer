using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using MCEBuddy.AppWrapper;
using MCEBuddy.Globals;
using MCEBuddy.VideoProperties;
using MCEBuddy.Util;
using MCEBuddy.CommercialScan;

namespace MCEBuddy.Transcode
{
    public class Convert
    {
        private string _convertedFile = "";
        protected JobStatus _jobStatus;
        protected Log _jobLog;

        public Convert(ref JobStatus jobStatus, Log jobLog)
        {
            _jobStatus = jobStatus;
            _jobLog = jobLog;
        }

        public string ConvertedFile
        {
            get { return _convertedFile; }
        }

        /// <summary>
        /// Returns the final extension for the file to be converted
        /// </summary>
        /// <param name="conversionOptions">Conversion Options</param>
        /// <returns>Extension as specified in the Conversion Profile</returns>
        public static string GetConversionExtension(ConversionJobOptions conversionOptions)
        {
            Ini ini = new Ini(GlobalDefs.ProfileFile);
            string orderSetting = ini.ReadString(conversionOptions.profile, "order", "").ToLower().Trim();
            if (!orderSetting.Contains("mencoder")) orderSetting = orderSetting.Replace("me","mencoder");
            if (!orderSetting.Contains("handbrake")) orderSetting = orderSetting.Replace("hb", "handbrake");
            if (!orderSetting.Contains("ffmpeg")) orderSetting = orderSetting.Replace("ff", "ffmpeg");

            string[] tool = orderSetting.Split(',');

            // We can check the first tool since all tools will lead to the same final extension
            string extension = ini.ReadString(conversionOptions.profile, tool[0] + "-ext", "").ToLower().Trim();
            string remuxTo = ini.ReadString(conversionOptions.profile, tool[0] + "-remuxto", "").ToLower().Trim();

            if (!string.IsNullOrEmpty(remuxTo))
            {
                if (remuxTo[0] != '.') remuxTo = "." + remuxTo;  // Just in case someone does something dumb like forget the leading "."
                return remuxTo;
            }
            else
            {
                if (extension[0] != '.') extension = "." + extension;  // Just in case someone does something dumb like forget the leading "."
                return extension;
            }
        }

        public bool Run(ConversionJobOptions conversionOptions, ref VideoInfo videoFile, ref Scanner commercialScan, bool fixCorruptedRemux)
        {
            bool converted = false;
            Ini ini = new Ini(GlobalDefs.ProfileFile);
            string orderSetting = ini.ReadString(conversionOptions.profile, "order", "").ToLower().Trim();
            if (!orderSetting.Contains("mencoder")) orderSetting = orderSetting.Replace("me","mencoder");
            if (!orderSetting.Contains("handbrake")) orderSetting = orderSetting.Replace("hb", "handbrake");
            if (!orderSetting.Contains("ffmpeg")) orderSetting = orderSetting.Replace("ff", "ffmpeg");

            string[] order = orderSetting.Split(',');
            foreach (string tool in order)
            {
                switch (tool)
                {
                    case "copy":
                        {
                            _jobLog.WriteEntry(this, Localise.GetPhrase("Using special case COPY for converter"), Log.LogEntryType.Information);

                            // Special case, no real encoder, just ignore any recoding and assume the output = input file
                            ConvertWithCopy convertWithCopy = new ConvertWithCopy(conversionOptions, "copy", ref videoFile, ref _jobStatus, _jobLog, ref commercialScan, fixCorruptedRemux);
                            if (!convertWithCopy.Unsupported)
                            {
                                _jobLog.WriteEntry(this, Localise.GetPhrase("Converting with COPY"), Log.LogEntryType.Information);

                                bool ret = convertWithCopy.Convert();
                                if (ret && !convertWithCopy.Error)
                                {
                                    converted = true;
                                    _convertedFile = convertWithCopy.ConvertedFile;
                                    videoFile.ConversionTool = "copy";
                                }
                                else
                                {
                                    _jobLog.WriteEntry(this, Localise.GetPhrase("COPY did not convert successfully, using fallback if configured"), Log.LogEntryType.Error);
                                }
                            }

                            break;
                        }
                    case "mencoder":
                        {
                            ConvertWithMencoder convertWithMencoder = new ConvertWithMencoder(conversionOptions, "mencoder", ref videoFile, ref _jobStatus, _jobLog, ref commercialScan, fixCorruptedRemux);
                            if (!convertWithMencoder.Unsupported)
                            {
                                _jobLog.WriteEntry(this, Localise.GetPhrase("Converting with MEncoder"), Log.LogEntryType.Information);
                                
                                bool ret = convertWithMencoder.Convert();
                                if (ret && !convertWithMencoder.Error)
                                {
                                    converted = true;
                                    _convertedFile = convertWithMencoder.ConvertedFile;
                                    videoFile.ConversionTool = "mencoder";
                                }
                                else
                                {
                                    _jobLog.WriteEntry(this, Localise.GetPhrase("MEncoder did not convert successfully, using fallback if configured"), Log.LogEntryType.Error);
                                }
                            }
                            else
                                _jobLog.WriteEntry(this, Localise.GetPhrase("Unsupported MEncoder file formats"), Log.LogEntryType.Error);

                            break;
                        }
                    case "handbrake":
                        {
                            ConvertWithHandbrake convertWithHandbrake = new ConvertWithHandbrake(conversionOptions, "handbrake", ref videoFile, ref _jobStatus, _jobLog, ref commercialScan, fixCorruptedRemux);
                            if (!convertWithHandbrake.Unsupported)
                            {
                                _jobLog.WriteEntry(this, Localise.GetPhrase("Converting with Handbrake"), Log.LogEntryType.Information); 
                                
                                bool ret = convertWithHandbrake.Convert();
                                if (ret && !convertWithHandbrake.Error)
                                {
                                    converted = true;
                                    _convertedFile = convertWithHandbrake.ConvertedFile;
                                    videoFile.ConversionTool = "handbrake";
                                }
                                else
                                {
                                    _jobLog.WriteEntry(this, Localise.GetPhrase("Handbrake did not convert successfully, using fallback if configured"), Log.LogEntryType.Error);
                                }
                            }
                            else
                                _jobLog.WriteEntry(this, Localise.GetPhrase("Unsupported Handbrake file formats"), Log.LogEntryType.Error);

                            break;
                        }
                    case "ffmpeg":
                        {
                            ConvertWithFfmpeg convertWithFfmpeg = new ConvertWithFfmpeg(conversionOptions, "ffmpeg", ref videoFile, ref _jobStatus, _jobLog, ref commercialScan, fixCorruptedRemux);
                            if (!convertWithFfmpeg.Unsupported)
                            {
                                _jobLog.WriteEntry(this, Localise.GetPhrase("Converting with FFMpeg"), Log.LogEntryType.Information); 

                                bool ret = convertWithFfmpeg.Convert();
                                if (ret && !convertWithFfmpeg.Error)
                                {
                                    converted = true;
                                    _convertedFile = convertWithFfmpeg.ConvertedFile;
                                    videoFile.ConversionTool = "ffmpeg";
                                }
                                else
                                {
                                    _jobLog.WriteEntry(this, Localise.GetPhrase("FFMpeg did not convert successfully, using fallback if configured"), Log.LogEntryType.Error);
                                }
                            }
                            else
                                _jobLog.WriteEntry(this, Localise.GetPhrase("Unsupported FFMpeg file formats"), Log.LogEntryType.Error);

                            break;
                        }
                    default:
                        {
                            _jobLog.WriteEntry(Localise.GetPhrase("Unsupported converter"), Log.LogEntryType.Error); 
                            break;
                        }
                }
                if (converted || _jobStatus.Cancelled) break;
            }

            if (!converted)
                _jobLog.WriteEntry(this, Localise.GetPhrase("Unable to convert file") + " " + Path.GetFileName(videoFile.OriginalFileName) + " " + Localise.GetPhrase("using profile") + " " + conversionOptions.profile, Log.LogEntryType.Error);
            else
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Successfully converted file") + " " + Path.GetFileName(videoFile.OriginalFileName) + " " + Localise.GetPhrase("using profile") + " " + conversionOptions.profile, Log.LogEntryType.Debug); 

                //Reset the error message incase there was a fallback conversion the suceeded
                _jobStatus.ErrorMsg = "";
            }

            return converted;
        }
    }
}
