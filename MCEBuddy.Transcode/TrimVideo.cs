using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

using MCEBuddy.Util;
using MCEBuddy.Globals;
using MCEBuddy.AppWrapper;

namespace MCEBuddy.Transcode
{
    public class TrimVideo
    {
        private JobStatus _jobStatus;
        private Log _jobLog;
        private string _profile;

        public TrimVideo(string profile, ref JobStatus jobStatus, Log jobLog)
        {
            _profile = profile;
            _jobLog = jobLog;
            _jobStatus = jobStatus;
        }

        public bool Trim(string sourceVideo, int startTrim, int endTrim)
        {
            _jobLog.WriteEntry(this, "Start Trim : " + startTrim.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
            _jobLog.WriteEntry(this, "Stop Trim : " + endTrim.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
            _jobLog.WriteEntry(this, "Video File : " + sourceVideo, Log.LogEntryType.Debug);

            if ((startTrim == 0) && (endTrim == 0))
                return true; // nothing to do here

            string tempFile = Util.FilePaths.GetFullPathWithoutExtension(sourceVideo) + "-temp" + Util.FilePaths.CleanExt(sourceVideo);

            // Get the length of the video, needed to calculate end point
            FFmpegMediaInfo ffmpegStreamInfo = new FFmpegMediaInfo(sourceVideo, ref _jobStatus, _jobLog);
            float Duration;
            ffmpegStreamInfo.Run();
            if (ffmpegStreamInfo.Success && !ffmpegStreamInfo.ParseError)
            {
                // Converted file should contain only 1 audio stream
                Duration = ffmpegStreamInfo.MediaInfo.VideoInfo.Duration;
                _jobLog.WriteEntry(this, Localise.GetPhrase("Video duration") + " : " + Duration.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Information);

                if (Duration == 0)
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Video duration 0"), Log.LogEntryType.Error);
                    return false;
                }
            }
            else
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Cannot read video duration"), Log.LogEntryType.Error);
                return false;
            }

            // dont' use threads here since we are copying video to improve stability
            string ffmpegParams = "-y";

            // Set the start trim before the input file to speed up the seeking
            if (startTrim != 0)
                ffmpegParams += " -ss " + startTrim.ToString(System.Globalization.CultureInfo.InvariantCulture);

            ffmpegParams += " -i " + Util.FilePaths.FixSpaces(sourceVideo);

            // Set the end trim (calculate from reducing from video length)
            if (endTrim != 0)
            {
                // FFMPEG can specify duration of encoding, i.e. encoding_duration = stopTime - startTime
                // startTime = startTrim, stopTime = video_duration - endTrim
                int encDuration = (((int)Duration) - endTrim) - (startTrim); // by default _startTrim is 0
                ffmpegParams += " -t " + encDuration.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            ffmpegParams += " -map 0:a -map 0:v -vcodec copy -acodec copy " + Util.FilePaths.FixSpaces(tempFile);

            FFmpeg ffmpeg = new FFmpeg(ffmpegParams, ref _jobStatus, _jobLog);
            ffmpeg.Run();

            if (!ffmpeg.Success || (FileIO.FileSize(tempFile) <= 0)) //check of file is created, outputhandler reports success (Percentage not requires since Success is more accurate)
            {
                _jobLog.WriteEntry("Trimming failed, retrying using GenPts", Log.LogEntryType.Warning);

                // genpt is required sometimes when -ss is specified before the inputs file, see ffmpeg ticket #2054
                ffmpegParams = "-fflags +genpts " + ffmpegParams;
                ffmpeg = new FFmpeg(ffmpegParams, ref _jobStatus, _jobLog);
                ffmpeg.Run();

                if (!ffmpeg.Success || (FileIO.FileSize(tempFile) <= 0)) //check of file is created, outputhandler reports success (Percentage not requires since Success is more accurate)
                {
                    _jobLog.WriteEntry(Localise.GetPhrase("Failed to trim video at") + " " + _jobStatus.PercentageComplete.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%", Log.LogEntryType.Error);
                    return false;
                }
            }

            // Replace the file
            if (File.Exists(tempFile))
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("TrimVideo trying to replace file") + " Source : " + sourceVideo + " Temp : " + tempFile, Log.LogEntryType.Debug);
                Util.FileIO.TryFileReplace(sourceVideo, tempFile);
            }
            else
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("TrimVideo cannot find temp file") + " " + tempFile, Log.LogEntryType.Error);
                _jobStatus.PercentageComplete = 0;
                return false;
            }

            return true; // All good here
        }
    }
}
