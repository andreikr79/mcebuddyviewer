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
    public class ConvertWithMencoder : ConvertBase
    {
        private const int DEFAULT_X264_QUALITY = 10;
        private const int DEFAULT_XVID_QUALITY = 4;
        private const int DEFAULT_LAVC_QUALITY = 4;

        private const int DEFAULT_X264_BITRATE = 1800;
        private const int DEFAULT_XVID_BITRATE = 2000;
        private const int DEFAULT_LAVC_BITRATE = 2000;
        private bool mEncoderEDLSkip = false;
        private bool commercialSkipCut = false;
        private bool _fixCorruptedRemux = false;
        private string _extractCC = "";
        private const double DRC = 0.8; // Dynamic Range Compression to 80%

        public ConvertWithMencoder(ConversionJobOptions conversionOptions, string tool, ref VideoInfo videoFile, ref JobStatus jobStatus, Log jobLog, ref Scanner commercialScan, bool fixCorruptedRemux)
            : base(conversionOptions, tool, ref videoFile, ref  jobStatus, jobLog, ref commercialScan)
        {
            //Check if MEncoder EDL Removal has been disabled at conversion time
            Ini ini = new Ini(GlobalDefs.ProfileFile);
            mEncoderEDLSkip = ini.ReadBoolean(conversionOptions.profile, "MEncoderEDLSkip", false);
            commercialSkipCut = (conversionOptions.commercialSkipCut || (ini.ReadBoolean(conversionOptions.profile, "CommercialSkipCut", false))); // commercial skipcutting can be defined in Task or Profile
            _fixCorruptedRemux = fixCorruptedRemux;
            _extractCC = conversionOptions.extractCC;
            if (fixCorruptedRemux)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Corrupted video was fixed with Remux, enabling MEncoderEDLSkip to skip EDL cutting during encoding"), Log.LogEntryType.Information);
                mEncoderEDLSkip = true;
            }

            if (!String.IsNullOrEmpty(conversionOptions.extractCC)) // If Closed Caption extraction is enabled, we don't use cut EDL using Mencoder during encoding, Mencoder has a bug which causes it to cut out of sync with the EDL file which throws the CC out of sync, it will be cut separately
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Closed Captions Enabled, skipping EDL cutting during encoding"), Log.LogEntryType.Information);
                mEncoderEDLSkip = true;
            }
            
            if ((_startTrim != 0) || (_endTrim != 0)) // If trimming is enabled skip cutting using EDL otherwise MEncoder messes it up
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Trimming Enabled, skipping EDL cutting during encoding"), Log.LogEntryType.Information);
                mEncoderEDLSkip = true;
            }
        }

        protected override void SetTrim()
        {
            // Set the start trim
            if (_startTrim != 0)
                ParameterValueReplaceOrInsert("-ss", _startTrim.ToString(System.Globalization.CultureInfo.InvariantCulture));

            // Set the end trim (calculate from reducing from video length)
            if (_endTrim != 0)
            {
                // Mencoder can specify duration of encoding, i.e. encoding_duration = stopTime - startTime
                // startTime = startTrim, stopTime = video_duration - endTrim
                int encDuration = (((int)_videoFile.Duration) - _endTrim) - (_startTrim); // by default _startTrim is 0
                ParameterValueReplaceOrInsert("-endpos", encDuration.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }                
        }

        protected override void SetAspectRatio()
        {
            // Nothing for Mencoder
        }

        protected override void SetPreDRC()
        {
            // Nothing, mencoder supports postDRC
        }

        protected override void SetPostDRC()
        {
            ParameterValueReplaceOrInsert("-a52drc", DRC.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        
        protected override void SetVolume()
        {
            ParameterSubValueReplaceOrInsert("-af", "volume", _volume.ToString("#0.0", System.Globalization.CultureInfo.InvariantCulture) + ":0"); // it has to be a floating point number in DB
        }

        private void SetBitRate(string cmd, string subCmd, int defaltBitrate)
        {
            string val = ParameterSubValue(cmd, subCmd);
            if (val != "")
            {
               int bitrate;
               if (!int.TryParse(val, out bitrate))
               {
                   bitrate = defaltBitrate;
               }
               bitrate = (int)(bitrate * _quality);
               ParameterSubValueReplace(cmd, subCmd, bitrate.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        private void SetConstantQuality( string cmd, string subCmd, int min, int max, int defaltQuality)
        {
            string val = ParameterSubValue(cmd, subCmd);
            if (val != "")
            {
                int cq;
                if (!int.TryParse(val, out cq))
                {
                    cq = defaltQuality;
                }
                cq = ConstantQualityValue(min, max, cq);
                ParameterSubValueReplace(cmd, subCmd, cq.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

        }

        protected override void SetQuality()
        {
            if (_quality == 1) return;
            
            // Try for the fixed bitrate options
            SetBitRate("x264encopts", "bitrate", DEFAULT_X264_BITRATE);
            SetBitRate("xvidencopts", "bitrate", DEFAULT_XVID_BITRATE);
            SetBitRate("lavcopts", "vbitrate", DEFAULT_LAVC_BITRATE);

            // Try for the constant quality options
            SetConstantQuality("x264encopts", "qp", 51, 1, DEFAULT_X264_QUALITY);
            SetConstantQuality("xvidencopts", "fixed_quant", 31, 1, DEFAULT_XVID_QUALITY);
            SetConstantQuality("lavcopts", "vqscale", 31, 1, DEFAULT_LAVC_QUALITY);
        }

        protected override void SetResize()
        {
            // Set the conversion profile width
            string scaleCmd = _maxWidth.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":-10";
            AddVideoFilter("scale", scaleCmd);
        }

        protected override void SetPreCrop()
        {
            // nothing PreCrop required
        }

        protected override void SetPostCrop()
        {
            if (!_skipCropping)
            {
                if (_videoFile.CropString != "")
                {
                    AddVideoFilter("crop", _videoFile.CropString);
                    _jobLog.WriteEntry(this, Localise.GetPhrase("MEncoder setting up video cropping"), Log.LogEntryType.Information);
                }
                else
                    _jobLog.WriteEntry(this, Localise.GetPhrase("MEncoder found no video cropping"), Log.LogEntryType.Information);
            }
            else
                _jobLog.WriteEntry(this, Localise.GetPhrase("MEncoder Skipping video cropping"), Log.LogEntryType.Information);
        }

        protected override bool ConstantQuality
        {
            get
            {
                if (ParameterSubValue("-x264encopts", "qp") != "") return true;
                if (ParameterSubValue("-xvidencopts", "fixed_quant") != "") return true;
                if (ParameterSubValue("-lavcopts", "vqscale") != "") return true;
                return false;
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

        protected override void SetOutputFileName()
        {
            // TODO: SetAdRemoval here causes the audio to out of sync slightly, so a compensation has been added to profile 0.185 seconds adjustment. Need to double check if there are any other issues (check -mc 0 and -noskip options if required)
            SetAdRemoval(); // EDL command just before the output file command, strip the commericals here itself since we are using Mencoder to avoid a duplication
            _cmdParams = _cmdParams.Trim() + " -o " + Util.FilePaths.FixSpaces(_convertedFile);
        }

        protected override void SetAudioLanguage()
        {
            if (_videoFile.AudioPID == -1)
                _jobLog.WriteEntry(this, Localise.GetPhrase("Cannot get Audio and Video stream details, continuing without Audio Language selection"), Log.LogEntryType.Warning);
            else
                ParameterValueReplaceOrInsert("-aid", (_videoFile.AudioPID).ToString(System.Globalization.CultureInfo.InvariantCulture)); // Select the Audio track PID we had isolated earlier
        }

        protected override void SetInputFileName()
        {
            _cmdParams = Util.FilePaths.FixSpaces(SourceVideo) + " " + _cmdParams.Trim();
            //SetLanguage(); // language after input file - not required since we are using Audio Language
            //SetAudioDelay();   // -delay seems to fail
        }

        private void SetLanguage()
        {
            string langParam = " -alang ";
            if (Localise.TwoLetterISO() != "en")
            {
                langParam += Localise.TwoLetterISO() + ",en";
            }
            else
            {
                langParam += "en";
            }
            _cmdParams = _cmdParams.Trim() + langParam;
            _jobLog.WriteEntry(this, Localise.GetPhrase("Mencoder: Setting language parameter") + " " + langParam, Log.LogEntryType.Information);
        }

        private void SetAdRemoval()
        {
            if (!mEncoderEDLSkip)
            {
                // Mencoder exception hack to speed things up - remove the ads at transcode time
                _jobLog.WriteEntry(this, Localise.GetPhrase("Mencoder: Checking if advertisement removal is required"), Log.LogEntryType.Information);
                if (_commercialScan != null & !_videoFile.AdsRemoved) // check if Commercial Stripping has been enabled AND not done since this function is called for all conversions
                {
                    if (commercialSkipCut) // do not video removal if we are asked to skip cutting: ie. preserve the EDL file
                        _jobLog.WriteEntry(this, Localise.GetPhrase("MEncoder: Requested to skip cutting commercial"), Log.LogEntryType.Information);
                    else if (_commercialScan.CommercialsFound && File.Exists(_commercialScan.EDLFile))
                    {
                        /*if (Util.FilePaths.CleanExt(SourceVideo) == ".ts")
                        {
                            _jobLog.WriteEntry(this, Localise.GetPhrase("MEncoder: .TS source filed detected, using -mc 0 option for commercial skipping"), Log.LogEntryType.Debug);
                            _cmdParams = _cmdParams.Trim() + " -mc 0 -hr-edl-seek -edl " + Util.FilePaths.FixSpaces(_commercialScan.EDLFile); // use -mc 0 otherwise the audio goes out of sync: http://www.mplayerhq.hu/DOCS/HTML/en/menc-feat-dvd-mpeg4.html
                        }
                        else
                        {
                            _jobLog.WriteEntry(this, Localise.GetPhrase("MEncoder: Non .TS source filed detected, skipping -mc 0 option for commercial skipping"), Log.LogEntryType.Debug);
                            _cmdParams = _cmdParams.Trim() + " -hr-edl-seek -edl " + Util.FilePaths.FixSpaces(_commercialScan.EDLFile); // hr-edl-seek causes the audio to go out of sync sometimes, do not use
                        }*/
                        _cmdParams = _cmdParams.Trim() + " -edl " + Util.FilePaths.FixSpaces(_commercialScan.EDLFile); // keeping it simple for 95% of the videos, need to figure out exception conditions

                        _removedAds = true;
                        _jobLog.WriteEntry(this, Localise.GetPhrase("Mencoder setting advertisement removal with EDL file") + " : " + _commercialScan.EDLFile, Log.LogEntryType.Information);
                    }
                    else
                        _jobLog.WriteEntry(this, Localise.GetPhrase("Mencoder EDL commercial removal during conversion, EDL File not found, skipping commercial removal through MEncoder"), Log.LogEntryType.Warning);
                }
            }
            else
                _jobLog.WriteEntry(this, Localise.GetPhrase("Mencoder EDL commercial removal disabled during conversion"), Log.LogEntryType.Information);
        }

        private void SetAudioDelay()
        {
            if (_audioDelay != "skip") // Check if the user doesn't want us to fix the Audio Delay
            {
                double netAudioDelay = _toolAudioDelay; // Account for the user Audio Delay 
                if (netAudioDelay != 0) // MEncoder hack to speed things up and fix AudioDelay while encoding itself
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Mencoder enabling Audio Delay correction during conversion ") + netAudioDelay.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Information);
                    //_cmdParams = _cmdParams.Trim() + " -delay " + _videoFile.AudioDelay.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    ParameterValueReplaceOrInsert("-delay", netAudioDelay.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    _videoFile.AudioDelaySet = true;
                }
                else
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Mencoder net Audio Delay correction is 0, skipping during conversion"), Log.LogEntryType.Information);
            }
            else
                _jobLog.WriteEntry(this, Localise.GetPhrase("Mencoder Audio Delay correction disabled during conversion"), Log.LogEntryType.Information);
        }

        protected override void SetAudioChannels()
        {
            if (_2ChannelAudio) // Fix output to 2 channels
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Requested to limit Audio Channels to 2"), Log.LogEntryType.Information);
                ParameterValueReplaceOrInsert("-channels", "2");
            }
            else if (_videoFile.AudioChannels > 0 && !_audioParams.Contains("copy") && !_audioParams.Contains("-channels ")) // don't override the Audio Params if specified
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Did not find Audio Channel information, settings channels to") + " " + _videoFile.AudioChannels.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Information);
                ParameterValueReplaceOrInsert("-channels", _videoFile.AudioChannels.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        protected override bool ConvertWithTool()
        {
            Mencoder me;

            //Check if threads are specified else add multithreaded decoding, lavdopts supports a max of 8 threads
            if (ParameterSubValue("-lavdopts", "threads") == "")
            {
                ParameterSubValueReplaceOrInsert("-lavdopts", "threads", Math.Min(8, Environment.ProcessorCount).ToString(System.Globalization.CultureInfo.InvariantCulture));
                _jobLog.WriteEntry(this, Localise.GetPhrase("Adding decoding threaded support for") + " " + Environment.ProcessorCount + " Processors", Log.LogEntryType.Information);
            }
            else
                _jobLog.WriteEntry(this, Localise.GetPhrase("Decoding threaded support enabled within profile parameters"), Log.LogEntryType.Debug);

            //Check for use of threads with lavcopts and add multithreaded support if not there, max 8 threads supported
            if (ParameterValue("-ovc") == "lavc")
            {
                if (ParameterSubValue("-lavcopts", "threads") == "")
                {
                    ParameterSubValueReplaceOrInsert("-lavcopts", "threads", Math.Min(8, Environment.ProcessorCount).ToString(System.Globalization.CultureInfo.InvariantCulture));
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Adding lavc threaded support for") + " " + Environment.ProcessorCount + " Processors", Log.LogEntryType.Information);
                }
                else
                    _jobLog.WriteEntry(this, Localise.GetPhrase("lavc threaded support present"), Log.LogEntryType.Debug);
            }

            //Check for use of threads with x264encopts and add auto multithreaded support if not there, 0 threads = auto
            if (ParameterValue("-ovc") == "x264")
            {
                if(ParameterSubValue("-x264encopts", "threads") == "")
                {
                    ParameterSubValueReplaceOrInsert("-x264encopts", "threads", "0");
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Adding x264enc auto threaded support"), Log.LogEntryType.Information);
                }
                else
                    _jobLog.WriteEntry(this, Localise.GetPhrase("x264enc threaded support present"), Log.LogEntryType.Debug);
            }

            //Check for use of threads with xvidencopts and add auto multithreaded support if not there, 0 Thread = Auto
            if (ParameterValue("-ovc") == "xvidenc")
            {
                if (ParameterSubValue("-xvidencopts", "threads") == "")
                {
                    ParameterSubValueReplaceOrInsert("-xvidencopts", "threads", "0");
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Adding xvidenc threaded support for") + " " + Environment.ProcessorCount + " Processors", Log.LogEntryType.Information);
                }
                else
                    _jobLog.WriteEntry(this, Localise.GetPhrase("xvidenc threaded support present"), Log.LogEntryType.Debug);
            }

            // Check to see if there was a corrupted video file that was fixed through Remux, then disable skipping since MEncoder does not skip well with fixed files causing audio to go out of sync
            if (_fixCorruptedRemux)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Found Remuxed fixed corrupted video, disabling video skipping"), Log.LogEntryType.Information);
                ParameterValueReplaceOrInsert("-ss", "0");
            }

            if (_2Pass == true)
            {
                string param = "";
                string subPararm = "";

                if (_cmdParams.Contains( "x264encopts"))
                {
                    param = "-x264encopts";
                    subPararm = "pass";
                }
                else if (_cmdParams.Contains("xvidencopts"))
                {
                    param = "-xvidencopts";
                    subPararm = "pass";
                }
                else if (_cmdParams.Contains("lavcopts"))
                {
                    param = "-lavcopts";
                    subPararm = "vpass";
                }

                if (param != "")
                {
                    // 1s Pass
                    string baseParam = _cmdParams;
                    string passLog = Path.Combine(_workingPath, "MCEBuddy2Pass.log");
                    ParameterSubValueReplaceOrInsert(param, subPararm, "1");
                    ParameterSubValueReplaceOrInsert(param, "turbo", "");
                    ParameterValueReplace("-o", "NUL");

                    _jobStatus.CurrentAction = Localise.GetPhrase("Converting video file - Pass 1");

                    me = new Mencoder(_cmdParams + " -passlogfile " + Util.FilePaths.FixSpaces(passLog), ref _jobStatus, _jobLog, false);
                    me.Run();
                    if (!me.Success)
                    {
                        _jobLog.WriteEntry(this, Localise.GetPhrase("Mencoder Pass 1 conversion failed"), Log.LogEntryType.Error);
                        _jobStatus.ErrorMsg = "Mencoder Pass 1 conversion failed";
                        return false;
                    }

                    // 2nd Pass
                    _cmdParams = baseParam;
                    ParameterSubValueReplaceOrInsert(param, subPararm, "2");

                    _jobStatus.CurrentAction = Localise.GetPhrase("Converting video file - Pass 2");

                    me = new Mencoder(_cmdParams + " -passlogfile " + Util.FilePaths.FixSpaces(passLog), ref _jobStatus, _jobLog, false);
                    me.Run();
                    if (!me.Success)
                    {
                        _jobLog.WriteEntry(this, Localise.GetPhrase("Mencoder Pass 2 conversion failed"), Log.LogEntryType.Error);
                        _jobStatus.ErrorMsg = "Mencoder Pass 2 conversion failed";
                        return false;
                    }
                }
                else
                {
                    _jobStatus.CurrentAction = Localise.GetPhrase("Converting video file");

                    me = new Mencoder(_cmdParams, ref _jobStatus, _jobLog, false);
                    me.Run();
                    if (!me.Success)
                    {
                        _jobLog.WriteEntry(this, Localise.GetPhrase("Mencoder 2 pass no param conversion failed"), Log.LogEntryType.Error);
                        _jobStatus.ErrorMsg = "Mencoder 2 pass no param conversion failed";
                        return false;
                    }
                }
            }
            else
            {
                _jobStatus.CurrentAction = Localise.GetPhrase("Converting video file");
                me = new Mencoder(_cmdParams, ref _jobStatus, _jobLog, false);
                me.Run();
                if (!me.Success)
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Mencoder conversion failed"), Log.LogEntryType.Error);
                    _jobStatus.ErrorMsg = "Mencoder conversion failed";
                    return false;
                }
            }
            return (me.Success);

        }

    }
}
