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
    public class ConvertWithHandbrake : ConvertBase
    {
        protected const int DEFAULT_BIT_RATE = 1500;
        protected const double DRC = 2.5; // Dynamic Range Compression (0 to 4, 2.5 is a good value)

        public ConvertWithHandbrake(ConversionJobOptions conversionOptions, string tool, ref VideoInfo videoFile, ref JobStatus jobStatus, Log jobLog, ref Scanner commercialScan)
            : base(conversionOptions, tool, ref videoFile, ref jobStatus, jobLog, ref commercialScan)
        {

        }

        protected override bool IsPresetWidth()
        {
            // Get the profile conversion width
            string scale = ParameterValue("-w");
            if (!MCEBuddy.Globals.GlobalDefs.IsNullOrWhiteSpace(scale))
                return true;
            else
                return false;
        }

        protected override void SetTrim()
        {
            // Set the start trim
            if (_startTrim != 0)
                ParameterValueReplaceOrInsert("--start-at", "duration:" + _startTrim.ToString(System.Globalization.CultureInfo.InvariantCulture));

            // Set the end trim (calculate from reducing from video length)
            if (_endTrim != 0)
            {
                // Handbrake can specify duration of encoding, i.e. encoding_duration = stopTime - startTime
                // startTime = startTrim, stopTime = video_duration - endTrim
                int encDuration = (((int)_videoFile.Duration) - _endTrim) - (_startTrim); // by default _startTrim is 0
                ParameterValueReplaceOrInsert("--stop-at", "duration:" + encDuration.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        protected override void SetAspectRatio()
        {
            // Nothing for Handbrake
        }

        protected override void SetPreDRC()
        {
            // Nothing, handbrake supports postDRC
        }

        protected override void SetPostDRC()
        {
            ParameterValueReplaceOrInsert("-D", DRC.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        protected override void SetVolume()
        {
            ParameterValueReplaceOrInsert("--gain", _volume.ToString("#0.0", System.Globalization.CultureInfo.InvariantCulture)); // it has to be a floating point number in DB
        }

        protected override void SetBitrateAndQuality()
        {
            if (!ConstantQuality) // Constant quality does not need to be updated since it's constant quality irrespective of resolution
            {
                string bitrateToken = "-b";
                string bitrateVal = ParameterValue("-b");
                if (bitrateVal == "")
                {
                    bitrateVal = ParameterValue("-br");
                    if (bitrateVal == "") return;
                    bitrateToken = "-br";
                }

                int bitrate;
                if (!int.TryParse(bitrateVal, out bitrate))
                {
                    bitrate = DEFAULT_BIT_RATE;
                }

                bitrate = (int) ((double) bitrate*(double) _quality);
                ParameterValueReplaceOrInsert(bitrateToken, bitrate.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        protected override void SetResize()
        {
            if (!_cmdParams.Contains("anamorphic"))
            {
                _cmdParams += " --loose-anamorphic";
            }
            ParameterValueReplaceOrInsert("-X", _maxWidth.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        protected override void SetCrop()
        {
            // ToDO Handbrake does an autocrop on the video, so we don't need mencoder crop
            // If skipCropping is defined then we need to disable AutoCropping
            if (_skipCropping)
            {
                ParameterValueReplaceOrInsert("--crop", "0:0:0:0");
                _jobLog.WriteEntry(this, Localise.GetPhrase("Skipping video cropping"), Log.LogEntryType.Information);
            }
            else
                _jobLog.WriteEntry(this, Localise.GetPhrase("Handbrake auto video cropping"), Log.LogEntryType.Information);
        }

        protected override bool ConstantQuality
        {
            get
            {
                if (ParameterValue("-q") != "") return true;
                return false;
            }
        }

        protected override void SetAudioLanguage()
        {
            if (_videoFile.AudioTrack == -1)
                _jobLog.WriteEntry(this, Localise.GetPhrase("Cannot get Audio and Video stream details, continuing with default Audio Language selection"), Log.LogEntryType.Warning);
            else
            {
                // We need to compensate for zero channel audio tracks, which are ignored by handbrake
                // Cycle through and skip over zero channel tracks until we get the track we need
                int audioTrack = 1; // Baseline audio track for Handbrake is 1
                for (int i = 0; i < _videoFile.FFMPEGStreamInfo.AudioTracks; i++)
                {
                    if (_videoFile.AudioTrack == i) // We have reached the selected audio track limit
                        break;

                    if (_videoFile.FFMPEGStreamInfo.MediaInfo.AudioInfo[i].Channels != 0) // skip over 0 channel tracks until we reach the selected audio track
                        audioTrack++;
                }

                if (MCEBuddy.Globals.GlobalDefs.IsNullOrWhiteSpace(ParameterValue("-a"))) // don't override Audio track selection from profile
                    ParameterValueReplaceOrInsert("-a", audioTrack.ToString(System.Globalization.CultureInfo.InvariantCulture)); // Select the Audiotrack we had isolated earlier (1st Audio track is 1, FFMPEGStreamInfo is 0 based)
                else
                    _jobLog.WriteEntry(this, Localise.GetPhrase("User has specified Audio language selection in profile, continuing without Audio Language selection"), Log.LogEntryType.Warning);
            }
        }

        protected override void SetAudioChannels()
        {
            if (_2ChannelAudio && !_audioParams.Contains(" copy")) // copy is not compatible with -6
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Requested to limit Audio Channels to 2"), Log.LogEntryType.Information);
                ParameterValueReplaceOrInsert("-6", "stereo"); // force 2 channel audio
            }
            else if (!_audioParams.Contains(" copy") && (ParameterValue("-6") == "")) // check if 2 channel audio is fixed and no audio channel information is specified in audio params
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Did not find Audio Channel information, auto settings channels"), Log.LogEntryType.Information);

                if (_videoFile.AudioChannels == 6)
                    ParameterValueReplaceOrInsert("-6", "6ch");
                else
                    ParameterValueReplaceOrInsert("-6", "auto");
            }
            else
                _jobLog.WriteEntry(this, Localise.GetPhrase("Skipping over requested to set audio channel information either due to COPY codec or audio parameters already contains channel directive"), Log.LogEntryType.Warning);
        }

        protected override void SetInputFileName()
        {
            _cmdParams = "-i " + Util.FilePaths.FixSpaces(SourceVideo) + " " + _cmdParams.Trim();
        }

        protected override void SetOutputFileName()
        {
            _cmdParams = _cmdParams.Trim() + " -o " + Util.FilePaths.FixSpaces(_convertedFile);
        }

        protected override bool ConvertWithTool()
        {
            if (_2Pass == true)
                _cmdParams += " -2";

            var hb = new Handbrake(_cmdParams, ref _jobStatus, _jobLog);
            _jobStatus.CurrentAction = Localise.GetPhrase("Converting video file");

            hb.Run();
            if (!hb.Success) // something didn't complete or went wrong, don't check for % since sometimes handbrake shows less than 90%
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Handbrake conversion failed"), Log.LogEntryType.Error);
                _jobStatus.ErrorMsg = "Handbrake conversion failed";
                return false;
            }

            return (hb.Success);
        }
    }
}