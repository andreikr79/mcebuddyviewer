using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

using MCEBuddy.Globals;
using MCEBuddy.Util;
using MCEBuddy.AppWrapper;
using MCEBuddy.VideoProperties;

namespace MCEBuddy.Transcode
{
    public class Remux
    {
        private JobStatus _jobStatus;
        private Log _jobLog;
        private string _remuxTo = "";
        private string _extension = "";
        private string _convertedFile = "";
        private VideoInfo _videoFile;

        public Remux(string convertedFile, ref VideoInfo videoFile, ref JobStatus jobStatus, Log jobLog, string remuxTo, string extension)
        {
            _jobLog = jobLog;
            _jobStatus = jobStatus;
            _convertedFile = convertedFile;
            _videoFile = videoFile;
            _remuxTo = remuxTo.ToLower();
            _extension = extension.ToLower();

            _jobLog.WriteEntry(this, "Remux To : " + _remuxTo, Log.LogEntryType.Debug);
            _jobLog.WriteEntry(this, "Extension : " + _extension, Log.LogEntryType.Debug);
        }

        public bool RemuxFile()
        {
            _jobStatus.PercentageComplete = 100; //all good to start with
            _jobStatus.ETA = "";
            bool ret = true;

            if (String.IsNullOrEmpty(_remuxTo)) return ret;

            if (_remuxTo[0] != '.') _remuxTo = "." + _remuxTo;  // Just in case someone does something dumb like forget the leading "."

            _jobStatus.CurrentAction = Localise.GetPhrase("Remuxing to") + " " + _remuxTo;

            if ((_remuxTo == ".avi") || (_remuxTo == ".wtv")) // AVI or WTV
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Remuxing video using FFMpeg to") + " " + _remuxTo, Log.LogEntryType.Debug);
                ret = FfmpegRemux();
            }
            else if (_remuxTo == ".mkv") // MKV
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Remuxing video using MKVMerge to") + " " + _remuxTo, Log.LogEntryType.Debug);
                ret = MKVRemux();
            }
            else if (MP4File(_remuxTo)) // MPEG4 formats
            {
                if (MP4File(_extension)) // MPEG4 to MPEG4
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Remuxing video using MP4BoxRemux to") + " " + _remuxTo, Log.LogEntryType.Debug);
                    ret = MP4BoxRemux();
                }
                else if (_extension == ".avi") // MPEG4 to AVI
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Remuxing video using MP4BoxRemuxAvito") + " " + _remuxTo, Log.LogEntryType.Debug);
                    ret = MP4BoxRemuxAvi();
                }
            }
            else
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Unable to remux file") + " " + Path.GetFileName(_convertedFile) + " " + _extension + "->" + _remuxTo + " - " + Localise.GetPhrase("unsupported remux container path"), Log.LogEntryType.Error);
                _jobStatus.PercentageComplete = 0;
                _jobStatus.ErrorMsg = "Unable to remux file";
                return false;
            }
            _jobLog.WriteEntry(this, Localise.GetPhrase("Finished video file remuxing, file size [KB]") + " " + (Util.FileIO.FileSize(_convertedFile) / 1024).ToString("N", System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);

            return ret;
        }

        /// <summary>
        /// Remuxes the converted file to the specified extension/format using FFMPEG.
        /// (Optionally) If a new Audio Stream file is specified, the audio from the new file is taken and video from the converted file
        /// </summary>
        /// <param name="newAudioStream"></param>
        /// <returns>True is successful</returns>
        public bool FfmpegRemux(string newAudioStream = "")
        {
            Util.FileIO.TryFileDelete(RemuxedTempFile);
            string ffmpegParams = "-y -i " + FilePaths.FixSpaces(_convertedFile);
            if (!String.IsNullOrEmpty(newAudioStream))
                ffmpegParams += " -i " + FilePaths.FixSpaces(newAudioStream) + " -map 0:v -map 1:a"; // Take audio stream from new file and video from the converted file
            else
                ffmpegParams += " -map 0:a -map 0:v";
            ffmpegParams += " -acodec copy -vcodec copy " + FilePaths.FixSpaces(RemuxedTempFile);
            var ffmpeg = new FFmpeg(ffmpegParams, ref _jobStatus, _jobLog);
            ffmpeg.Run();
            if (!ffmpeg.Success) // something went wrong (FFMPEG % complettion is not reliable, do not check)
            {
                _jobLog.WriteEntry("FFMpegRemux failed, retying using GenPts", Log.LogEntryType.Warning);

                // genpt is required sometimes when -ss is specified before the inputs file, see ffmpeg ticket #2054
                ffmpegParams = "-fflags +genpts " + ffmpegParams;
                ffmpeg = new FFmpeg(ffmpegParams, ref _jobStatus, _jobLog);
                ffmpeg.Run();

                if (!ffmpeg.Success) //check of file is created, outputhandler reports success (Percentage not requires since Success is more accurate)
                {
                    _jobStatus.ErrorMsg = Localise.GetPhrase("ffmpeg remux failed");
                    _jobLog.WriteEntry(this, (_jobStatus.ErrorMsg), Log.LogEntryType.Error);
                    _jobStatus.PercentageComplete = 0;
                    return false;
                }
            }

            _jobLog.WriteEntry(this, Localise.GetPhrase("FFMPEG ReMux moving file"), Log.LogEntryType.Information);
            return ReplaceTempRemuxed();
        }

        private bool MKVRemux()
        {
            Util.FileIO.TryFileDelete(RemuxedTempFile);
            MKVMerge mkvMerge = new MKVMerge("", ref _jobStatus, _jobLog);

            mkvMerge.Parameters = "-o " + FilePaths.FixSpaces(RemuxedTempFile) + " " + FilePaths.FixSpaces(_convertedFile);

            mkvMerge.Run();
            if (!mkvMerge.Success)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("MKVMerge failed"), Log.LogEntryType.Error);
                _jobStatus.ErrorMsg = Localise.GetPhrase("MKVMerge failed");
                _jobStatus.PercentageComplete = 0;
                return false;
            }

            _jobLog.WriteEntry(this, Localise.GetPhrase("MKVMerge remux moving file"), Log.LogEntryType.Information);
            return ReplaceTempRemuxed();
        }

        private bool MP4BoxRemux()
        {
            Util.FileIO.TryFileDelete(RemuxedTempFile);
            MP4Box mp4Box = new MP4Box("", ref _jobStatus, _jobLog);

            //Check for Null FPS (bug with MediaInfo for some .TS files)
            if (_videoFile.Fps == 0)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Mp4BoxRemuxAVI FPS 0 reported by video file - non compliant video file, skipping adding to parameter"), Log.LogEntryType.Warning);
                mp4Box.Parameters = " -add " + FilePaths.FixSpaces(_convertedFile) +
                                    " -new " + FilePaths.FixSpaces(RemuxedTempFile);
            }
            else
            {
                mp4Box.Parameters = "-fps " + _videoFile.Fps.ToString(System.Globalization.CultureInfo.InvariantCulture) + " -add " + FilePaths.FixSpaces(_convertedFile) +
                                    " -new " + FilePaths.FixSpaces(RemuxedTempFile);
            }

            mp4Box.Run();
            if (!mp4Box.Success || _jobStatus.PercentageComplete < GlobalDefs.ACCEPTABLE_COMPLETION)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("MP4BoxRemux failed"), Log.LogEntryType.Error);
                _jobStatus.ErrorMsg = Localise.GetPhrase("MP4BoxRemux failed");
                _jobStatus.PercentageComplete = 0;
                return false;
            }

            _jobLog.WriteEntry(this, Localise.GetPhrase("MP4Box remux moving file"), Log.LogEntryType.Information);
            return ReplaceTempRemuxed();
        }

        private bool MP4BoxRemuxAvi()
        {
            string fileNameBase = Util.FilePaths.GetFullPathWithoutExtension(_convertedFile);
            string audioStream = fileNameBase + "_audio.raw";
            string newAudioStream = fileNameBase + "_audio.aac";
            string videoStream = fileNameBase + "_video.h264";

            Util.FileIO.TryFileDelete(RemuxedTempFile);
            MP4Box mp4Box = new MP4Box("", ref _jobStatus, _jobLog);
            mp4Box.Parameters = "-aviraw video " + FilePaths.FixSpaces(_convertedFile);

            _jobStatus.CurrentAction = Localise.GetPhrase("Remuxing to") + " " + _remuxTo.ToLower() + " " + Localise.GetPhrase("Part") + " 1";
            
            mp4Box.Run();
            if (!mp4Box.Success || _jobStatus.PercentageComplete < GlobalDefs.ACCEPTABLE_COMPLETION) // check for completion of job
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("MP4Box Remux AVI failed at") + " " + _jobStatus.PercentageComplete.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Error);
                return false;
            }

            mp4Box.Parameters = "-aviraw audio " + FilePaths.FixSpaces(_convertedFile);

            _jobStatus.CurrentAction = Localise.GetPhrase("Remuxing to") + " " + _remuxTo.ToLower() + " " + Localise.GetPhrase("Part") + " 2";
            
            mp4Box.Run();
            if (!mp4Box.Success || _jobStatus.PercentageComplete < GlobalDefs.ACCEPTABLE_COMPLETION) //check for completion of job
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Mp4BoxRemuxAVI failed at") + " " + _jobStatus.PercentageComplete.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Error);
                return false;
            }

            if ((File.Exists(audioStream)) && (File.Exists(videoStream)))
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("MP4Box remux avi moving file"), Log.LogEntryType.Information);
                Util.FileIO.TryFileDelete(newAudioStream);
                try
                {
                    File.Move(audioStream, newAudioStream);
                }
                catch (Exception)
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Unable to move remuxed stream") + " " + audioStream + " to " + newAudioStream, Log.LogEntryType.Error);
                    _jobStatus.PercentageComplete = 0;
                    _jobStatus.ErrorMsg = "Unable to move muxed stream";
                    return false;
                }

                //Check for Null FPS (bug with MediaInfo for some .TS files)
                if (_videoFile.Fps == 0)
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Mp4BoxRemuxAVI FPS 0 reported by video file - non compliant video file, skipping adding to parameter"), Log.LogEntryType.Warning);
                    mp4Box.Parameters = " -add " + FilePaths.FixSpaces(videoStream) +
                                        " -add " + FilePaths.FixSpaces(newAudioStream) + " -new " +
                                        FilePaths.FixSpaces(RemuxedTempFile);
                }
                else
                {
                    mp4Box.Parameters = "-fps " + _videoFile.Fps.ToString(System.Globalization.CultureInfo.InvariantCulture) + " -add " + FilePaths.FixSpaces(videoStream) +
                                        " -add " + FilePaths.FixSpaces(newAudioStream) + " -new " +
                                        FilePaths.FixSpaces(RemuxedTempFile);
                }


                _jobStatus.CurrentAction = Localise.GetPhrase("Remuxing to") + " " + _remuxTo.ToLower() + " " + Localise.GetPhrase("Part") + " 3";
                
                mp4Box.Run();
                if (!mp4Box.Success || _jobStatus.PercentageComplete < GlobalDefs.ACCEPTABLE_COMPLETION) // check for completion
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Mp4BoxRemuxAVI with FPS conversion failed"), Log.LogEntryType.Error);
                    return false;
                }

                Util.FileIO.TryFileDelete(videoStream);
                Util.FileIO.TryFileDelete(newAudioStream);

                _jobLog.WriteEntry(this, Localise.GetPhrase("MP4Box remux AVI trying to move remuxed file"), Log.LogEntryType.Information);
                return ReplaceTempRemuxed();
            }
            else
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("MP4Box Remux AVI of") + " " + _convertedFile + " " + Localise.GetPhrase("failed.  Extracted video and audio streams not found."), Log.LogEntryType.Error);
                _jobStatus.PercentageComplete = 0;
                _jobStatus.ErrorMsg = "Remux failed, extracted video streams not found";
                return false;
            }
        }

        private bool ReplaceTempRemuxed()
        {
            string newName = Util.FilePaths.GetFullPathWithoutExtension(_convertedFile) + _remuxTo;
            _jobLog.WriteEntry(this, Localise.GetPhrase("Changing rexumed file names") + " \nOriginalFile : " + _convertedFile + " \nReMuxedFile : " + RemuxedTempFile + " \nConvertedFile : " + newName, Log.LogEntryType.Debug);
            try
            {
                File.Move(RemuxedTempFile, newName);

                if (_convertedFile != _videoFile.OriginalFileName) // e.g. TS files are not remuxed and worked on directly on the original source files
                    Util.FileIO.TryFileDelete(_convertedFile);

                _convertedFile = newName;
            }
            catch (Exception)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Unable to move remuxed file") + " " + RemuxedTempFile, Log.LogEntryType.Error);
                _jobStatus.ErrorMsg = "Unable to move remux file";
                _jobStatus.PercentageComplete = 0;
                return false;
            }

            return true;
        }

        private string RemuxedTempFile
        {
            get
            {
                return Util.FilePaths.GetFullPathWithoutExtension(_convertedFile) + "_REMUX" + _remuxTo;
            }
        }

        private bool MP4File(string ext)
        {
            if (ext.ToLower() == ".mp4") return true;
            if (ext.ToLower() == ".m4v") return true;
            return false;
        }

        public string RemuxedFile
        { get { return _convertedFile; } }
    }
}
