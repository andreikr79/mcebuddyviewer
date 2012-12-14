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

        public ConvertWithFfmpeg(ConversionJobOptions conversionOptions, string tool, ref VideoInfo videoFile, ref JobStatus jobStatus, Log jobLog, ref Scanner commercialScan, bool fixCorruptedRemux)
            : base(conversionOptions, tool, ref videoFile, ref jobStatus, jobLog, ref commercialScan)
        {

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
            //TODO: ffmpeg documentation says it supports dB through the -af volume=x.ydB buy it fails, need to check back when it's fixed
            //For now use the old -vol <int64> command, so convert the input dB to a %. The base for ffmpeg is 256 (ie 100%), scale accordingly
            // % = 10 ^ (dB/10)
            double percentageValue = Math.Pow(10, (_volume / 10)); // convert dB to %
            long volumeScale = (long) (percentageValue * 256); //scale the based 256
            ParameterValueReplaceOrInsert("-vol", volumeScale.ToString("#0", System.Globalization.CultureInfo.InvariantCulture)); // has to be a int64
        }

        protected override void SetQuality()
        {
            if (ConstantQuality)
            {
                int qualityVal = ConstantQualityValue(31, 1, 3);
                ParameterValueReplaceOrInsert("-qscale", qualityVal.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            else
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
            // FFMEG required Width to be divisible by 16 and height to be divisible by 8
            int newHeight = _videoFile.Height * _maxWidth / _videoFile.Width;
            //newHeight = Util.MathLib.RoundOff(newHeight, 8);
            newHeight = ((int)(newHeight / 8 + 0.5)) * 8;
            AddVideoFilter("scale", _maxWidth.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + newHeight.ToString(System.Globalization.CultureInfo.InvariantCulture));
            //AddVideoFilter("scale", _maxWidth.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":-1"); // set the maxWidth and let FFMPEG autoscale the height keeping the same aspect ratio (libxvid has weird restrictions on the ratio) - AUTOSCALE DOES NOT ROUND OFF CAUSING FFMPEG TO FAIL DUE TO INVALID PIXEL HEIGHTS
        }

        protected override void SetPostCrop()
        {
            // Nothing PreCrop for FFMPEG
        }

        protected override void SetPreCrop()
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
            get
            {
                return (ParameterValue("-qscale") != "");
            }
        }

        protected override int DefaultVideoWidth
        {
            get
            {
                // Get the profile conversion width
                int defaultWidth = DEFAULT_VIDEO_WIDTH;
                string scale = ParameterSubValue("-vf", "scale");
                if (scale.Contains(":"))
                {
                    int.TryParse(scale.Split(':')[0], out defaultWidth);
                }
                if (defaultWidth < 1) defaultWidth = DEFAULT_VIDEO_WIDTH;
                return defaultWidth;
            }
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
                _jobLog.WriteEntry(this, Localise.GetPhrase("Cannot get Audio and Video stream details, continuing without Audio Language selection"), Log.LogEntryType.Warning);
            else
                _cmdParams += " -map 0:" + _videoFile.AudioStream.ToString(System.Globalization.CultureInfo.InvariantCulture) + " -map 0:" + _videoFile.VideoStream.ToString(System.Globalization.CultureInfo.InvariantCulture); // Select the Audiotrack we had isolated earlier
        }

        protected override void SetAudioChannels()
        {
            if (_2ChannelAudio) // Fix output to 2 channels
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
        }

        protected override bool ConvertWithTool()
        {
            FFmpeg ff;

            if (_2Pass == true)
            {
                string passLog = Path.Combine(_workingPath, "MCEBuddy2Pass.log");
                _jobStatus.CurrentAction = Localise.GetPhrase("Converting video file - Pass 1");
                ff = new FFmpeg(_cmdParams + " -pass 1 -passlogfile " + Util.FilePaths.FixSpaces(passLog), ref _jobStatus, _jobLog);
                ff.Run();
                if (!ff.Success) // Do not check for % completion as it is not reported sometimes
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("FFMpeg Pass 1 conversion failed"), Log.LogEntryType.Error);
                    _jobStatus.ErrorMsg = "FFMpeg Pass 1 conversion failed";
                    return false;
                }

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
