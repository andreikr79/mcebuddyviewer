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
    public class ConvertWithFfmpeg : ConvertBase
    {
        private const int DEFAULT_BIT_RATE = 1500000;
        private const double DRC = 0.8; // 80% Dynamic Range Compression (DRC)

        public ConvertWithFfmpeg(ConversionJobOptions conversionOptions, string tool, ref VideoInfo videoFile, ref JobStatus jobStatus, Log jobLog, ref Scanner commercialScan)
            : base(conversionOptions, tool, ref videoFile, ref jobStatus, jobLog, ref commercialScan)
        {

        }

        protected override bool IsPresetWidth()
        {
            // Get the profile conversion width
            string scale = ParameterSubValue("-vf", "scale");
            if (!String.IsNullOrWhiteSpace(scale))
                return true;
            else
                return false;
        }

        protected override void SetTrim()
        {
            // Set the start trim
            if (_startTrim != 0)
                ParameterValueReplaceOrInsert("-ss", _startTrim.ToString(System.Globalization.CultureInfo.InvariantCulture));

            // Set the end trim (calculate from reducing from video length)
            if (_endTrim != 0)
            {
                // FFMPEG can specify duration of encoding, i.e. encoding_duration = stopTime - startTime
                // startTime = startTrim, stopTime = video_duration - endTrim
                int encDuration = (((int)_videoFile.Duration) - _endTrim) - (_startTrim); // by default _startTrim is 0
                ParameterValueReplaceOrInsert("-t", encDuration.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        protected override void SetPostDRC()
        {
            // Nothing, ffmpeg supports preDRC
        }

        protected override void SetPreDRC() // ffmpeg needs to setup this parameter before the inputs file because it applies to decoding the input
        {
            ParameterValueReplaceOrInsert("-drc_scale", DRC.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        protected override void SetVolume()
        {
            AddAudioFilter("volume", _volume.ToString("#0.0", System.Globalization.CultureInfo.InvariantCulture) + "dB"); // it has to be a floating point number in dB
        }

        protected override void SetBitrateAndQuality()
        {
            if (!ConstantQuality) // Constant quality does not need to be updated since it's constant quality irrespective of resolution
            {
                string bitrateVal = ParameterValue("-b");
                if (bitrateVal == "") return;

                bitrateVal = bitrateVal.Replace("k", "000");
                bitrateVal = bitrateVal.Replace("m", "000000");
                bitrateVal = bitrateVal.Replace("g", "000000000");
                bitrateVal = bitrateVal.Replace("K", "000");
                bitrateVal = bitrateVal.Replace("M", "000000");
                bitrateVal = bitrateVal.Replace("G", "000000000");
                int bitrate;
                if (!int.TryParse(bitrateVal, out bitrate))
                {
                    bitrate = DEFAULT_BIT_RATE;
                }

                bitrate = (int)((double)bitrate * (double)_quality);
                ParameterValueReplaceOrInsert("-b", bitrate.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        protected override void SetResize()
        {
            int VideoWidth = _videoFile.Width;
            if ((_videoFile.CropWidth > 0) && (_videoFile.CropWidth < _videoFile.Width))
                VideoWidth = _videoFile.CropWidth; // Use the cropped width if present

            int VideoHeight = _videoFile.Height;
            if ((_videoFile.CropHeight > 0) && (_videoFile.CropHeight < _videoFile.Height))
                VideoHeight = _videoFile.CropHeight; // Use the cropped height if present

            // FFMEG required Width to be divisible by 16 and height to be divisible by 8 (this is post cropping height and width that needs to be scaled)
            int newWidth = VideoWidth;
            if (newWidth > _maxWidth)
                newWidth = _maxWidth; // We use the less of two, MaxWidth of post cropping video width
            newWidth = Util.MathLib.RoundOff(newWidth, 16);

            int newHeight = VideoHeight * newWidth / VideoWidth;
            newHeight = Util.MathLib.RoundOff(newHeight, 8);

            AddVideoFilter("scale", newWidth.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + newHeight.ToString(System.Globalization.CultureInfo.InvariantCulture));
            //AddVideoFilter("scale", newWidth.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":-1"); // set the maxWidth and let FFMPEG autoscale the height keeping the same aspect ratio (libxvid has weird restrictions on the ratio) - AUTOSCALE DOES NOT ROUND OFF CAUSING FFMPEG TO FAIL DUE TO INVALID PIXEL HEIGHTS
        }

        protected override void SetCrop()
        {
            if (!_skipCropping)
            {
                if (!String.IsNullOrEmpty(_videoFile.CropString))
                {
                    AddVideoFilter("crop", _videoFile.CropString);
                    _jobLog.WriteEntry(this, Localise.GetPhrase("FFMpeg setting up cropping"), Log.LogEntryType.Information);
                }
                else
                    _jobLog.WriteEntry(this, Localise.GetPhrase("FFMpeg found no video cropping"), Log.LogEntryType.Information);
            }
            else
                _jobLog.WriteEntry(this, Localise.GetPhrase("FFMpeg Skipping video cropping"), Log.LogEntryType.Information);

        }

        protected override void SetAspectRatio()
        {
            // LibXVid is very finicky and doesn't handle cropping well so we need to set the SAR aspect ration otherwise it mucks it up and fails
            // setsar and setdar should come at the end of the filter chain else it fails
            if (_videoParams.ToLower().Contains("libxvid"))
            {
                if (!String.IsNullOrEmpty(_videoFile.FFMPEGStreamInfo.MediaInfo.VideoInfo.SAR))
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Found LibXVid encoder, cropping detected, setting SAR aspect ratio") + " " + _videoFile.FFMPEGStreamInfo.MediaInfo.VideoInfo.SAR, Log.LogEntryType.Information);
                    //ParamaterValueReplaceOrInsert("-aspect", _videoFile.FFMPEGStreamInfo.MediaInfo.VideoInfo.DAR); // Set the DAR otherwise it fails sometimes while cropping as libxvid is very sensitive to PAR distortion
                    AddVideoFilter("setsar", _videoFile.FFMPEGStreamInfo.MediaInfo.VideoInfo.SAR); // Set the SAR otherwise it fails sometimes while cropping as libxvid is very sensitive to SAR distortion
                }
                else
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Found LibXVid encoder, cropping detected, cannot read input SAR aspect ratio, skipping setting SAR - could fail video conversion") + " " + _videoFile.FFMPEGStreamInfo.MediaInfo.VideoInfo.SAR, Log.LogEntryType.Warning);
            }
        }

        protected override bool ConstantQuality
        {
            get { return (ParameterValue("-qscale") != ""); }
        }

        protected override void SetInputFileName() // general parameters already setup, now add the input filename details
        {
            _cmdParams = _cmdParams.Trim() + " -y -i " + Util.FilePaths.FixSpaces(SourceVideo);
        }

        protected override void SetOutputFileName() // general + input + video + audio setup, now add the output filename
        {
            _cmdParams = _cmdParams.Trim() + " " + Util.FilePaths.FixSpaces(_convertedFile);
        }

        protected override void SetAudioLanguage()
        {
            if (_videoFile.AudioStream == -1 || _videoFile.VideoStream == -1)
                _jobLog.WriteEntry(this, Localise.GetPhrase("Cannot get Audio and Video stream details, continuing with default Audio Language selection"), Log.LogEntryType.Warning);
            else if (!_cmdParams.Contains("-map")) // don't override the audio channel selection if the user has specified it in the profile
                _cmdParams += " -map 0:" + _videoFile.AudioStream.ToString(System.Globalization.CultureInfo.InvariantCulture) + " -map 0:" + _videoFile.VideoStream.ToString(System.Globalization.CultureInfo.InvariantCulture); // Select the Audiotrack we had isolated earlier
            else
                _jobLog.WriteEntry(this, Localise.GetPhrase("User has specified Audio language selection in profile, continuing without Audio Language selection"), Log.LogEntryType.Warning);
        }

        protected override void SetAudioChannels()
        {
            if (_2ChannelAudio && !_audioParams.Contains(" copy")) // Fix output to 2 channels, copy is not compatible with -ac
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Requested to limit Audio Channels to 2"), Log.LogEntryType.Information);
                ParameterValueReplaceOrInsert("-ac", "2");
            }
            else if ((_videoFile.AudioChannels > 0) && !_audioParams.Contains(" copy") && (ParameterValue("-ac") == "")) // Don't override what's given in the audio params
            {
                if (_audioParams.Contains("libvo_aacenc") && (ParameterValue("-ac") == "")) // Allow for AAC channel override
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("FFMPEG AACEnc Audio Codec detected, settings Audio Channels to 2"), Log.LogEntryType.Information);
                    ParameterValueReplaceOrInsert("-ac", "2"); // libvo_aacenc does not support > 2 channels as of 3/7/12 for FFMPEG
                }
                else
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("No Audio Channel information detected, settings Audio Channels to") + " " + _videoFile.AudioChannels.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Information);
                    ParameterValueReplaceOrInsert("-ac", _videoFile.AudioChannels.ToString(System.Globalization.CultureInfo.InvariantCulture)); // except ac3 nothing else supports > 2 channels as of 3/7/12 for FFMPEG
                }

                if (!(_audioParams.Contains("ac3") || _audioParams.Contains("ac-3") || _audioParams.Contains("libvo_aacenc")) && (_videoFile.AudioChannels > 2)) // AC3 support, AAC taken care of above
                    _jobLog.WriteEntry(this, Localise.GetPhrase("AC3 Codec not detected, FFMPEG may not support >2 channels for other audio codecs. May lead to failure of conversion"), Log.LogEntryType.Warning);
            }
            else
                _jobLog.WriteEntry(this, Localise.GetPhrase("Skipping over requested to set audio channel information either due to COPY codec or audio parameters already contains channel directive"), Log.LogEntryType.Warning);
        }

        protected override bool ConvertWithTool()
        {
            FFmpeg ff;

            if (_2Pass == true)
            {
                // 1st Pass
                string baseParam = _cmdParams; // save the base command params
                string passLog = Path.Combine(_workingPath, "MCEBuddy2Pass.log");

                // We replicate -Turbo option from Mencoder to speed up the 1st pass, i.e. set subq and framerefs to 1
                // TODO: Need to compensate for other encoders apart from x264
                if (_cmdParams.Contains("x264opts"))
                {
                    // Replace these values if they exist since we can't delete them
                    ParameterSubValueReplace("-x264opts", "subq", "1");
                    ParameterSubValueReplace("-x264opts", "ref", "1");
                }

                // Insert these generic values that will be passed to all encoders
                ParameterValueReplaceOrInsert("-subq", "1");
                ParameterValueReplaceOrInsert("-refs", "1");

                // Speed up the 1st pass, no file required
                _cmdParams = _cmdParams.Replace(Util.FilePaths.FixSpaces(_convertedFile), ""); // We don't need file on 1st pass, will compensate for output file in next file with -f
                ParameterValueReplaceOrInsert("-f", "rawvideo NUL"); // require for NUL output - at the end preferably, but NUL must come after -f rawvideo

                _jobStatus.CurrentAction = Localise.GetPhrase("Converting video file - Pass 1");
                ff = new FFmpeg(_cmdParams + " -pass 1 -passlogfile " + Util.FilePaths.FixSpaces(passLog), ref _jobStatus, _jobLog);
                ff.Run();
                if (!ff.Success) // Do not check for % completion as it is not reported sometimes
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("FFMpeg Pass 1 conversion failed"), Log.LogEntryType.Error);
                    _jobStatus.ErrorMsg = "FFMpeg Pass 1 conversion failed";
                    return false;
                }

                // Reset the command line for the 2nd pass
                _cmdParams = baseParam;

                _jobStatus.CurrentAction = Localise.GetPhrase("Converting video file - Pass 2");
                ff = new FFmpeg(_cmdParams + " -pass 2 -passlogfile " + Util.FilePaths.FixSpaces(passLog), ref _jobStatus, _jobLog);
                ff.Run();
                if (!ff.Success) // Do not check for % completion as it is not reported sometimes
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("FFMpeg Pass 2 conversion failed"), Log.LogEntryType.Error);
                    _jobStatus.ErrorMsg = "FFMpeg Pass 2 conversion failed";
                    return false;
                }
            }
            else
            {
                _jobStatus.CurrentAction = Localise.GetPhrase("Converting video file");
                ff = new FFmpeg(_cmdParams, ref _jobStatus, _jobLog);
                ff.Run();
                if (!ff.Success) // Do not check for % completion as it is not reported sometimes
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("FFMpeg conversion failed"), Log.LogEntryType.Error);
                    _jobStatus.ErrorMsg = "FFMpeg conversion failed";
                    return false;
                }
            }
            return (ff.Success);
        }
    }
}
