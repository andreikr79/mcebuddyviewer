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
    public class RemuxExt
    {
        private JobStatus _jobStatus;
        private Log _jobLog;
        private string _remuxTo = "";
        private string _extension = "";
        private string _convertedFile = "";
        private VideoInfo _videoFile;

        public RemuxExt(string convertedFile, ref VideoInfo videoFile, ref JobStatus jobStatus, Log jobLog, string remuxTo)
        {
            _jobLog = jobLog;
            _jobStatus = jobStatus;
            _convertedFile = convertedFile;
            _videoFile = videoFile;
            _remuxTo = remuxTo.ToLower();
            _extension = Path.GetExtension(convertedFile).ToLower();

            _jobLog.WriteEntry(this, "Remux To : " + _remuxTo, Log.LogEntryType.Debug);
            _jobLog.WriteEntry(this, "Extension : " + _extension, Log.LogEntryType.Debug);
        }

        public bool RemuxFile()
        {
            _jobStatus.PercentageComplete = 100; //all good to start with
            _jobStatus.ETA = "";
            bool ret = true;

            if (MCEBuddy.Globals.GlobalDefs.IsNullOrWhiteSpace(_remuxTo)) return ret;

            if (_remuxTo[0] != '.') _remuxTo = "." + _remuxTo;  // Just in case someone does something dumb like forget the leading "."

            if (_remuxTo == _extension)
            {
                _jobLog.WriteEntry(this, "Remuxing SKIPPED, cannot remux to same extension", Log.LogEntryType.Warning);
                return ret;
            }

            _jobStatus.CurrentAction = Localise.GetPhrase("Remuxing to") + " " + _remuxTo;

            if (MP4File(_remuxTo)) // to MPEG4 formats
            {
                if (_extension == ".avi") // AVI to MPEG4
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Remuxing video using MP4BoxRemuxAvito") + " " + _remuxTo, Log.LogEntryType.Debug);
                    ret = MP4BoxRemuxAvi();
                }
                else
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Remuxing video using FFMpeg to") + " " + _remuxTo, Log.LogEntryType.Debug);
                    ret = FfmpegRemux(); // First try ffmpeg, mp4box doesn't always work on all ts.h264 to mp4
                    if (!ret)
                    {
                        _jobLog.WriteEntry(this, Localise.GetPhrase("FFMPEG Remuxing failed, Remuxing video using MP4BoxRemux to") + " " + _remuxTo, Log.LogEntryType.Debug);
                        ret = MP4BoxRemux();
                    }
                }
            }
            else
                ret = false;

            if (!ret)// default and on failure try FFMPEG
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Remuxing video using FFMpeg to") + " " + _remuxTo, Log.LogEntryType.Debug);
                ret = FfmpegRemux();
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
            FFmpeg ffmpeg = new FFmpeg(ffmpegParams, ref _jobStatus, _jobLog);
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

            string parameters = "-o " + FilePaths.FixSpaces(RemuxedTempFile) + " " + FilePaths.FixSpaces(_convertedFile);
            MKVMerge mkvMerge = new MKVMerge(parameters, ref _jobStatus, _jobLog);

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
            string Parameters = " -keep-sys -keep-all";

            //Check for Null FPS (bug with MediaInfo for some .TS files)
            if (_videoFile.Fps == 0)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Mp4BoxRemuxAVI FPS 0 reported by video file - non compliant video file, skipping adding to parameter"), Log.LogEntryType.Warning);
                Parameters += " -add " + FilePaths.FixSpaces(_convertedFile) +
                              " -new " + FilePaths.FixSpaces(RemuxedTempFile);
            }
            else
            {
                Parameters += " -fps " + _videoFile.Fps.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                              " -add " + FilePaths.FixSpaces(_convertedFile) +
                              " -new " + FilePaths.FixSpaces(RemuxedTempFile);
            }

            MP4Box mp4Box = new MP4Box(Parameters, ref _jobStatus, _jobLog);
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

            // Video
            string Parameters = " -keep-sys -aviraw video " + FilePaths.FixSpaces(_convertedFile);

            _jobStatus.CurrentAction = Localise.GetPhrase("Remuxing to") + " " + _remuxTo.ToLower() + " " + Localise.GetPhrase("Part") + " 1";

            MP4Box mp4Box = new MP4Box(Parameters, ref _jobStatus, _jobLog);
            mp4Box.Run();
            if (!mp4Box.Success || _jobStatus.PercentageComplete < GlobalDefs.ACCEPTABLE_COMPLETION) // check for completion of job
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("MP4Box Remux AVI failed at") + " " + _jobStatus.PercentageComplete.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Error);
                return false;
            }

            // Audio
            Parameters = " -keep-all -keep-sys -aviraw audio " + FilePaths.FixSpaces(_convertedFile);

            _jobStatus.CurrentAction = Localise.GetPhrase("Remuxing to") + " " + _remuxTo.ToLower() + " " + Localise.GetPhrase("Part") + " 2";

            mp4Box = new MP4Box(Parameters, ref _jobStatus, _jobLog);
            mp4Box.Run();
            if (!mp4Box.Success || _jobStatus.PercentageComplete < GlobalDefs.ACCEPTABLE_COMPLETION) //check for completion of job
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Mp4BoxRemuxAVI failed at") + " " + _jobStatus.PercentageComplete.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Error);
                return false;
            }

            // Check if streams are extracted
            if ((File.Exists(audioStream)) && (File.Exists(videoStream)))
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("MP4Box remux avi moving file"), Log.LogEntryType.Information);
                Util.FileIO.TryFileDelete(newAudioStream);
                try
                {
                    File.Move(audioStream, newAudioStream);
                }
                catch (Exception e)
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Unable to move remuxed stream") + " " + audioStream + " to " + newAudioStream + "\r\nError : " + e.ToString(), Log.LogEntryType.Error);
                    _jobStatus.PercentageComplete = 0;
                    _jobStatus.ErrorMsg = "Unable to move muxed stream";
                    return false;
                }

                string mergeParameters = " -keep-sys -keep-all";

                //Check for Null FPS (bug with MediaInfo for some .TS files)
                if (_videoFile.Fps == 0)
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Mp4BoxRemuxAVI FPS 0 reported by video file - non compliant video file, skipping adding to parameter"), Log.LogEntryType.Warning);
                    mergeParameters += " -add " + FilePaths.FixSpaces(videoStream) +
                                       " -add " + FilePaths.FixSpaces(newAudioStream) +
                                       " -new " + FilePaths.FixSpaces(RemuxedTempFile);
                }
                else
                {
                    mergeParameters += " -fps " + _videoFile.Fps.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                                       " -add " + FilePaths.FixSpaces(videoStream) +
                                       " -add " + FilePaths.FixSpaces(newAudioStream) +
                                       " -new " + FilePaths.FixSpaces(RemuxedTempFile);
                }


                _jobStatus.CurrentAction = Localise.GetPhrase("Remuxing to") + " " + _remuxTo.ToLower() + " " + Localise.GetPhrase("Part") + " 3";

                mp4Box = new MP4Box(mergeParameters, ref _jobStatus, _jobLog);
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
                Util.FileIO.TryFileDelete(_convertedFile);
                _convertedFile = newName;
            }
            catch (Exception e)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Unable to move remuxed file") + " " + RemuxedTempFile + "\r\nError : " + e.ToString(), Log.LogEntryType.Error);
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
