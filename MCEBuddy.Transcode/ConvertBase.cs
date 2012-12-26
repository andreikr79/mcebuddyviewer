using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MCEBuddy.AppWrapper;
using MCEBuddy.Util;
using MCEBuddy.Util.Combinatorics;

using MCEBuddy.Globals;
using MCEBuddy.VideoProperties;
using MCEBuddy.CommercialScan;

namespace MCEBuddy.Transcode
{
    public abstract class ConvertBase
    {
        protected const int DEFAULT_VIDEO_WIDTH = 720;

        protected string _generalParams = "";
        protected string _videoParams = "";
        protected string _audioParams = "";
        protected string _extension = "";
        protected string _remuxTo = "";
        protected string _cmdParams = "";
        protected double _toolAudioDelay = 0;
        protected string _audioDelay = "";

        protected string _audioStream = "";
        protected string _convertedFile = "";
        protected string _workingPath = "";

        protected VideoInfo _videoFile;
        protected Scanner _commercialScan;
        protected int _maxWidth = 1000000;
        protected double _quality = 1;
        protected bool _fixedResolution = false;
        protected bool _skipCropping = false;
        protected bool _2Pass = false;
        protected bool _2ChannelAudio = false;
        protected bool _drc = false;
        protected double _volume = 1;
        protected int _startTrim = 0;
        protected int _endTrim = 0;

        protected char _separator = ':';
        protected bool _caseSensitive = true;

        protected bool _unsupported = false;
        protected bool _error = false;
        protected bool _removedAds = false;

        protected JobStatus _jobStatus;
        protected Log _jobLog;

        protected string _replaceWithOriginalName = ""; // Keep track incase of filename conflict

        protected ConvertBase(ConversionJobOptions conversionOptions, string tool, ref VideoInfo videoFile, ref JobStatus jobStatus, Log jobLog, ref Scanner commercialScan)
        {
            //Setup log and status
            _jobStatus = jobStatus;
            _jobLog = jobLog;
            
            // Check first up to see if the source video uses an unsupported combination for this profile
            // Container, Video Codec, Audio Codec and whether it was originally a Media Center recording or not
            _videoFile = videoFile;
            _commercialScan = commercialScan;
            if (CheckUnsupported(conversionOptions.profile, tool)) return;


            // Set the input params and get the standard settings
            _maxWidth = conversionOptions.maxWidth;
            _quality = conversionOptions.qualityMultiplier;
            _volume = conversionOptions.volumeMultiplier;
            _drc = conversionOptions.drc;
            _startTrim = conversionOptions.startTrim;
            _endTrim = conversionOptions.endTrim;

            Ini ini = new Ini(GlobalDefs.ProfileFile);
            if (ini.ReadBoolean(conversionOptions.profile, "2ChannelAudio", false))
                jobLog.WriteEntry("Profile override - fixing output to Stereo Audio", Log.LogEntryType.Debug);

            _2ChannelAudio = conversionOptions.stereoAudio || ini.ReadBoolean(conversionOptions.profile, "2ChannelAudio", false); // Fix output to 2 channels (from profile)
            _fixedResolution = ini.ReadBoolean(conversionOptions.profile, "FixedResolution", false);
            _skipCropping = (conversionOptions.disableCropping || (ini.ReadBoolean(conversionOptions.profile, "SkipCropping", false))); // Cropping can be disabled in the profile or in the Conversion Task GUI settings
            _2Pass = ini.ReadBoolean(conversionOptions.profile, "2pass", false);
            _generalParams = ini.ReadString(conversionOptions.profile, tool + "-general", "");
            _videoParams = ini.ReadString(conversionOptions.profile, tool + "-video", "");
            _audioParams = ini.ReadString(conversionOptions.profile, tool + "-audio", "");
            _extension = _videoFile.Extension = ini.ReadString(conversionOptions.profile, tool + "-ext", "").ToLower().Trim();
            _remuxTo = _videoFile.RemuxTo = ini.ReadString(conversionOptions.profile, tool + "-remuxto", "").ToLower().Trim();
            _audioDelay = ini.ReadString(conversionOptions.profile, tool + "-audiodelay", "skip").ToLower().Trim();

            if (_audioDelay == "auto") // Use the audio delay specified in the file
                _toolAudioDelay = videoFile.AudioDelay;
            else if (_audioDelay != "skip")
                double.TryParse(_audioDelay, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _toolAudioDelay);

            if (conversionOptions.audioOffset != 0) // Conversion options Audio Delay takes priority over profile Audio Delay
                _toolAudioDelay = conversionOptions.audioOffset; 

            // Audio select the AC3 audio option if the source video has AC3)
            if (((videoFile.AudioCodec == "ac-3") || (videoFile.AudioCodec == "ac3")) && (ini.ReadString(conversionOptions.profile, tool + "-audioac3", "") != ""))
            {
                _audioParams = ini.ReadString(conversionOptions.profile, tool + "-audioac3", _audioParams);
            }

            // E-AC3 test option if the source video has E-AC3
            if (videoFile.AudioCodec == "e-ac-3" || _videoFile.AudioCodec != "eac3")
            {
                _audioParams = ini.ReadString(conversionOptions.profile, tool + "-audioeac3", _audioParams);
                if ((_audioParams == "") && (tool == "mencoder"))
                {
                    _audioParams = "-noaudio ";
                }
            }

            //Set the destination paths
            _workingPath = conversionOptions.workingPath;

            //Check the work path
            Util.FilePaths.CreateDir(_workingPath);

            if (Path.GetFileName(SourceVideo) == (Path.GetFileNameWithoutExtension(videoFile.OriginalFileName) + _extension)) // check if the input filename and output filename are identical, we need to compensate to avoid conflict
            {
                string outputFileName = Path.GetFileNameWithoutExtension(videoFile.OriginalFileName) + "-temp" + _extension;
                _convertedFile = Path.Combine(_workingPath, outputFileName);
                _replaceWithOriginalName = Path.Combine(_workingPath, Path.GetFileNameWithoutExtension(videoFile.OriginalFileName) + _extension);
            }
            else
            {
                string outputFileName = Path.GetFileNameWithoutExtension(videoFile.OriginalFileName) + _extension;
                _convertedFile = Path.Combine(_workingPath, outputFileName);
            }

        }

        private void AddCombinations(ref List<string> comboList)
        {
            List<string> originalList = new List<string>(comboList);

            for (int combinationCount = 2; combinationCount <= originalList.Count; combinationCount++)
            {
                var combos = new Util.Combinatorics.Combinations<string>(originalList, combinationCount);
                foreach (IList<string> combo in combos)
                {
                    var permutations = new Util.Combinatorics.Permutations<string>(combo); // Make all possible permutations now
                    foreach (IList<string> permutation in permutations)
                    {
                        string newPermutation = "";
                        foreach (string s in permutation)
                        {
                            if (newPermutation == "")
                                newPermutation = s;
                            else
                                newPermutation += "+" + s;
                        }

                        if (!comboList.Contains(newPermutation))
                            comboList.Add(newPermutation);
                    }
                }
            }
        }

        protected bool CheckUnsupported(string profile, string tool)
        {
            _jobLog.WriteEntry(this, Localise.GetPhrase("Checking for Unsupported profile for container / codec combination"), Log.LogEntryType.Information);
            Ini ini = new Ini(GlobalDefs.ProfileFile);
            string unsupportedCombinations = ini.ReadString(profile, tool + "-unsupported", "").ToLower().Trim();

            if (unsupportedCombinations != "")
            {
                List<string> videoItems = new List<string>();

                // Use properties for ORIGINAL video (not remuxed file, since the user is referring to original video properties, remuxed is always MPEG2 and TS)
                // Derive all possible combinations from the source video (video, audio and extension)
                videoItems.Add(_videoFile.OriginalVideoFFMPEGStreamInfo.MediaInfo.VideoInfo.VideoCodec);
                foreach (MediaInfo.Audio audioInfo in _videoFile.OriginalVideoFFMPEGStreamInfo.MediaInfo.AudioInfo) // check all audio codecs found
                    videoItems.Add(audioInfo.AudioCodec);
                videoItems.Add(_videoFile.OriginalFileExtension.ToLower().Replace(".", ""));

                AddCombinations(ref videoItems);

                // Get the unsupported list and check to see if there are any matches
                string[] unsupported = unsupportedCombinations.Split(',');
                foreach (string combo in unsupported)
                {
                    string c = combo.ToLower().Trim();
                    if (videoItems.Contains(c))
                    {
                        _jobLog.WriteEntry(this, 
                            Localise.GetPhrase("Unsupported profile for container / codec combination") + " " + c + " " + profile,
                            Log.LogEntryType.Warning);
                        _unsupported = true;
                        return true;
                    }
                }
            }
            return false;
        }

        protected void ConvertEAC3()
        {
            _jobStatus.PercentageComplete = 100; //all good by default
            _jobStatus.ETA = "";

            if (_videoFile.AudioCodec != "e-ac-3" || _videoFile.AudioCodec != "eac3") return;
            // Only supports MP4, MKV and AVI
            if ((_extension != ".mp4") && (_extension != ".mkv") && (_extension != ".avi")) return;

            _jobStatus.CurrentAction = Localise.GetPhrase("Converting E-AC3");
            _jobLog.WriteEntry(this, Localise.GetPhrase("Converting E-AC3"), Log.LogEntryType.Information);

            // Convert EAC3 file
            string eac3toParams;
            string audiop = _audioParams.ToLower().Trim();
            if (audiop.Contains("faac") || audiop.Contains("libfaac") || audiop.Contains("aac") || audiop.Contains("libvo_aacenc"))
            {
                _audioStream = Util.FilePaths.GetFullPathWithoutExtension(_videoFile.OriginalFileName) + "_AUDIO.mp4";
                eac3toParams = Util.FilePaths.FixSpaces(_videoFile.OriginalFileName) + " " + Util.FilePaths.FixSpaces(_audioStream) + " -384";
            }
            else
            {
                _audioStream = Util.FilePaths.GetFullPathWithoutExtension(_videoFile.OriginalFileName) + "_AUDIO.ac3";
                eac3toParams = Util.FilePaths.FixSpaces(_videoFile.OriginalFileName) + " " + Util.FilePaths.FixSpaces(_audioStream) + " -384";
            }

            Util.FileIO.TryFileDelete(_audioStream);
            
            var eac3to = new AppWrapper.Eac3To(eac3toParams, ref _jobStatus, _jobLog);
            eac3to.Run();
            if (!eac3to.Success)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("E-AC3 conversion unsuccessful"), Log.LogEntryType.Error);
                _jobStatus.ErrorMsg = "E-AC3 conversion unsuccessful";
                _jobStatus.PercentageComplete = 0;
                return; // something went wrong
            }

            // Mux into destination 
            if ((_extension == ".mp4") || (_extension == ".m4v"))
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Muxing E-AC3 using MP4Box"), Log.LogEntryType.Information);
                string mp4boxParams = "-add " + FilePaths.FixSpaces(_audioStream) + " " + FilePaths.FixSpaces(_convertedFile);
                _jobStatus.PercentageComplete = 0; //reset
                _jobStatus.ETA = "";
                var mp4box = new MP4Box(mp4boxParams, ref _jobStatus, _jobLog);
                mp4box.Run();

                Util.FileIO.TryFileDelete(_audioStream);
                if (!mp4box.Success || _jobStatus.PercentageComplete < GlobalDefs.ACCEPTABLE_COMPLETION) // check for incomplete output or process issues
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("E-AC3 muxing using MP4Box failed at") + " " + _jobStatus.PercentageComplete.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Error);
                    _jobStatus.ErrorMsg = "E-AC3 muxing using MP4Box failed";
                    _jobStatus.PercentageComplete = 0; // something went wrong with the process
                    return;
                }
            }
            else if (_extension == ".mkv")
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Muxing E-AC3 using MKVMerge"), Log.LogEntryType.Information);
                string remuxedFile = Util.FilePaths.GetFullPathWithoutExtension(_convertedFile) + "_REMUX.mkv";
                Util.FileIO.TryFileDelete(remuxedFile);
                string mkvmergeParams = FilePaths.FixSpaces(_convertedFile) + " " + FilePaths.FixSpaces(_audioStream) + " -o " + FilePaths.FixSpaces(remuxedFile);
                _jobStatus.PercentageComplete = 0; //reset
                _jobStatus.ETA = "";
                var mkvmerge = new MKVMerge(mkvmergeParams, ref _jobStatus, _jobLog);
                mkvmerge.Run();
                Util.FileIO.TryFileDelete(_convertedFile);
                if (!mkvmerge.Success)
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Muxing E-AC3 using MKVMerge failed"), Log.LogEntryType.Error);
                    _jobStatus.ErrorMsg = "Muxing E-AC3 using MKVMerge failed";
                    _jobStatus.PercentageComplete = 0; // something went wrong with the process
                    return;
                }

                try
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Moving MKVMerge muxed E-AC3"), Log.LogEntryType.Information);
                    File.Move(remuxedFile, _convertedFile);
                    _jobStatus.PercentageComplete = 100; //proxy for job done since mkvmerge doesn't report
                    _jobStatus.ETA = "";
                }
                catch (Exception)
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Unable to move MKVMerge remuxed E-AC3 file") + " " + remuxedFile, Log.LogEntryType.Error);
                    _jobStatus.PercentageComplete = 0;
                    _jobStatus.ErrorMsg = "Unable to move MKVMerge remuxed E-AC3 file";
                    return;
                }
                Util.FileIO.TryFileDelete(_audioStream);
            }
            else
            {
                _jobStatus.PercentageComplete = 0; //reset
                _jobStatus.ETA = "";
                _jobLog.WriteEntry(this, Localise.GetPhrase("Muxing E-AC3 using FFMPEGRemux"), Log.LogEntryType.Information);
                Remux remuxFile = new Remux(_convertedFile, ref _videoFile, ref _jobStatus, _jobLog, _remuxTo, _extension);
                if (remuxFile.FfmpegRemux(_audioStream))
                {
                    _convertedFile = remuxFile.RemuxedFile;
                }
                else
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Error Muxing E-AC3 using FFMPEGRemux"), Log.LogEntryType.Error);
                    _jobStatus.PercentageComplete = 0;
                    _jobStatus.ErrorMsg = "Error Muxing E-AC3 using FFMPEGRemux";
                    return;
                }
                Util.FileIO.TryFileDelete(_audioStream);
            }
            _jobLog.WriteEntry(this, Localise.GetPhrase("Finished EAC3 conversion, file size [KB]") + " " + (Util.FileIO.FileSize(_convertedFile) / 1024).ToString("N", System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
        }

        protected bool FixAudioDelay()
        {
            bool ret = true;
            string encoderParams;

            _jobStatus.PercentageComplete = 100; //all good to start with
            _jobStatus.ETA = "";

            if (_videoFile.AudioDelaySet || _toolAudioDelay == 0) return ret; //It's already been done (probably by mencoder) or been requested to skip

            double audioDelay = _toolAudioDelay;

            if (audioDelay != 0)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Fixing Audio Delay, Detected :") + " " + _videoFile.AudioDelay.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", Manual Adj : " + _toolAudioDelay.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Information);
                
                string ext = Path.GetExtension(_convertedFile).Trim().ToLower();
                string fixedFile = Util.FilePaths.GetFullPathWithoutExtension(_convertedFile) + "_AVFIX" + Path.GetExtension(_convertedFile);
                Util.FileIO.TryFileDelete(fixedFile);

                _jobStatus.CurrentAction = Localise.GetPhrase("Correcting audio delay");

                switch (ext)
                {
                    case ".ts":
                    case ".wmv":
                    case ".mp4":
                    case ".m4v":
                            if (audioDelay > 0) // If Audio is ahead of the video skip seconds from the 1st input file and remap to output
                            {
                                encoderParams = "-y -i " + Util.FilePaths.FixSpaces(_convertedFile) + " -ss " +
                                                audioDelay.ToString(System.Globalization.CultureInfo.InvariantCulture) + " -i " + Util.FilePaths.FixSpaces(_convertedFile) +
                                                " -map 1:0 -map 0:1 -acodec copy -vcodec copy " +
                                                Util.FilePaths.FixSpaces(fixedFile.ToString(System.Globalization.CultureInfo.InvariantCulture));

                                _jobLog.WriteEntry(this, "Fixing +ve Audio Delay using FFMPEG", Log.LogEntryType.Debug);
                            
                            } // if audio is behind the video skip seconds from the 2nd input file and remap to ouput (keeping the audio shift positive)
                            else
                            {
                                encoderParams = "-y " + " -ss " + (audioDelay * -1).ToString(System.Globalization.CultureInfo.InvariantCulture) + " -i " +
                                                Util.FilePaths.FixSpaces(_convertedFile) + " -i " +
                                                Util.FilePaths.FixSpaces(_convertedFile) +
                                                " -map 1:0 -map 0:1 -acodec copy -vcodec copy " +
                                                Util.FilePaths.FixSpaces(fixedFile.ToString(System.Globalization.CultureInfo.InvariantCulture));

                                _jobLog.WriteEntry(this, "Fixing -ve Audio Delay using FFMPEG", Log.LogEntryType.Debug);
                            }

                            var ffmpeg = new FFmpeg(encoderParams, ref _jobStatus, _jobLog);
                            ffmpeg.Run();
                            if (!ffmpeg.Success || (FileIO.FileSize(fixedFile) <= 0)) // Do not check for % completion since FFMPEG doesn't always report a % for this routine for some reason
                            {
                                _jobLog.WriteEntry("Fixing Audio Delay failed, retying using GenPts", Log.LogEntryType.Warning);

                                // genpt is required sometimes when -ss is specified before the inputs file, see ffmpeg ticket #2054
                                encoderParams = "-fflags +genpts " + encoderParams;
                                ffmpeg = new FFmpeg(encoderParams, ref _jobStatus, _jobLog);
                                ffmpeg.Run();

                                if (!ffmpeg.Success || (FileIO.FileSize(fixedFile) <= 0)) //check of file is created, outputhandler reports success (Percentage not requires since Success is more accurate)
                                {
                                    _jobStatus.PercentageComplete = 0; // if the file wasn't completely converted the percentage will be low so no worries
                                    _jobStatus.ErrorMsg = Localise.GetPhrase("Fix AudioSync Failed for") + " " + ext;
                                    _jobLog.WriteEntry(this, (_jobStatus.ErrorMsg), Log.LogEntryType.Error);
                                    return false;
                                }
                            }

                            break;

                    case ".mpg":
                            encoderParams = Util.FilePaths.FixSpaces(_convertedFile) + " -of mpeg -ovc copy -oac copy -delay " + audioDelay.ToString(System.Globalization.CultureInfo.InvariantCulture) + " -o " + Util.FilePaths.FixSpaces(fixedFile.ToString(System.Globalization.CultureInfo.InvariantCulture)); // avoid using threads since we are copying to increase stability
                            _jobLog.WriteEntry(this, "Fixing Audio Delay using MEncoder with Parameters: " + encoderParams, Log.LogEntryType.Debug);
                            Mencoder mencoder = new Mencoder(encoderParams, ref _jobStatus, _jobLog, true); // Use old version of Mencoder for MPG and TS
                            mencoder.Run();
                            if (!mencoder.Success) // something failed or was incomplete, do not check for % completion as Mencoder looks fro success criteria
                            {
                                _jobStatus.PercentageComplete = 0;
                                _jobStatus.ErrorMsg = Localise.GetPhrase("Fix AudioSync failed for") + " " + ext;
                                _jobLog.WriteEntry(this, (_jobStatus.ErrorMsg), Log.LogEntryType.Error);
                                return false;
                            }

                            break;

                    case ".avi":
                            encoderParams = Util.FilePaths.FixSpaces(_convertedFile) + " -oac copy -ovc copy -ni -delay " + audioDelay.ToString(System.Globalization.CultureInfo.InvariantCulture) + " -o " + Util.FilePaths.FixSpaces(fixedFile.ToString(System.Globalization.CultureInfo.InvariantCulture)); // avoid using threads since we are copying to increase stability

                            _jobLog.WriteEntry(this, "Fixing Audio Delay using MEncoder with Parameters: " + encoderParams, Log.LogEntryType.Debug);
                            Mencoder mencoderAVI = new Mencoder(encoderParams, ref _jobStatus, _jobLog, false);
                            mencoderAVI.Run();
                            if (!mencoderAVI.Success) // something failed or was incomplete, do not check for % completion as Mencoder looks fro success criteria
                            {
                                _jobStatus.PercentageComplete = 0;
                                _jobStatus.ErrorMsg = Localise.GetPhrase("Fix AudioSync failed for") + " " + ext;
                                _jobLog.WriteEntry(this, (_jobStatus.ErrorMsg), Log.LogEntryType.Error);
                                return false;
                            }

                            break;

                    default:
                            _jobLog.WriteEntry(this, Localise.GetPhrase("Unable to correct audio aync.  Supported options are mp4, m4v and avi files") + " Requested " + ext, Log.LogEntryType.Error);
                            _jobStatus.ErrorMsg = "Unable to correct AudioSync, unsupported format";
                            _jobStatus.PercentageComplete = 0;
                            return false;
                }
                
                Util.FileIO.TryFileDelete(_convertedFile);
                try
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Fix Audio Delay trying to move fixed file"), Log.LogEntryType.Information);
                    File.Move(fixedFile, _convertedFile);
                }
                catch (Exception)
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Unable to move audio sync corrected file") + " " + fixedFile, Log.LogEntryType.Error);
                    _jobStatus.ErrorMsg = "Unable to move audio sync file";
                    _jobStatus.PercentageComplete = 0;
                    return false;
                }
                _jobLog.WriteEntry(this, Localise.GetPhrase("Finished Audio Delay Correction, file size [KB]") + " " + (Util.FileIO.FileSize(_convertedFile) / 1024).ToString("N", System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
            }
            else
                _jobLog.WriteEntry(this, Localise.GetPhrase("Fix Audio Delay, net correction 0, skipping correction"), Log.LogEntryType.Information);

            return ret;
        }


        protected void AddParameter(string parameter)
        {
            _cmdParams = (_cmdParams + " " + parameter).Trim();
        }

        private void SetResizeBitRateQuality()
        {
            if (ConstantQuality) return;

            if (_fixedResolution && (PresetVideoWidth != 0)) return; // The size has been hardcoded into the paramters and bitrate optimized, no need to recalculate here

            // Set the quality multiplier if you are using fixed bitrate
            int VideoWidth = _videoFile.Width;

            if ((_videoFile.CropWidth != 0) && (_videoFile.CropWidth != _videoFile.Width))
            {
                VideoWidth = _videoFile.CropWidth;
            }

            // If we are scaling down, then reduce the quality multiplier
            if (VideoWidth > _maxWidth ) VideoWidth = _maxWidth;

            _quality = _quality * (float)VideoWidth / (float)DEFAULT_VIDEO_WIDTH;
        }

        public bool Convert() // THE MAIN CONVERSION ROUTINE
        {
            if (_unsupported) return false;
            
            _jobLog.WriteEntry(this, "Main conversion routine DEBUG", Log.LogEntryType.Debug);
            _jobLog.WriteEntry(this, "Source Video File : " + _videoFile.SourceVideo, Log.LogEntryType.Debug);
            _jobLog.WriteEntry(this, "Extension : " + _extension, Log.LogEntryType.Debug);
            _jobLog.WriteEntry(this, "Remux To : " + _remuxTo, Log.LogEntryType.Debug);
            _jobLog.WriteEntry(this, "2 Pass : " + _2Pass.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
            _jobLog.WriteEntry(this, "Fixed Resolution : " + _fixedResolution.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
            _jobLog.WriteEntry(this, "Skip Cropping : " + _skipCropping.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
            _jobLog.WriteEntry(this, "Video Audio Delay : " + _videoFile.AudioDelay.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
            _jobLog.WriteEntry(this, "Manual Audio Delay : " + _toolAudioDelay.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
            _jobLog.WriteEntry(this, "Skip Audio Delay : " + _audioDelay, Log.LogEntryType.Debug);
            _jobLog.WriteEntry(this, "Max Width : " + _maxWidth.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
            _jobLog.WriteEntry(this, "Quality : " + _quality.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
            _jobLog.WriteEntry(this, "Audio Track : " + _videoFile.AudioTrack.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
            _jobLog.WriteEntry(this, "Start Trim : " + _startTrim.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
            _jobLog.WriteEntry(this, "Stop Trim : " + _endTrim.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
            _jobLog.WriteEntry(this, "Working Path : " + _workingPath, Log.LogEntryType.Debug);
            _jobLog.WriteEntry(this, "Output File : " + _convertedFile, Log.LogEntryType.Debug);
            _jobLog.WriteEntry(this, "Temp Output File : " + _replaceWithOriginalName, Log.LogEntryType.Debug);
            _jobLog.WriteEntry(this, Localise.GetPhrase("Source video file, file size [KB]") + " " + (Util.FileIO.FileSize(SourceVideo) / 1024).ToString("N", System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);

            //WAY TO BUILD THE COMMAND LINE
            // GeneralParameters + InputFile + VideoOptions + AudioOptions + OutputFile
            
            _jobLog.WriteEntry(this, Localise.GetPhrase("Setting up General conversion parameters :") + " " + _generalParams, Log.LogEntryType.Information);
            AddParameter(_generalParams); // General Parameters

            // Set the DRC before the input file for some encoders like ffmpeg
            if (_drc)
            {
                if (_videoFile.AudioCodec == "ac-3" || _videoFile.AudioCodec == "ac3") // DRC only applies to AC3 audio
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Setting up PreDRC"), Log.LogEntryType.Information);

                    if (_audioParams.ToLower().Contains("copy"))
                        _jobLog.WriteEntry(this, Localise.GetPhrase("Copy Audio stream detected, skipping DRC settings"), Log.LogEntryType.Warning);
                    else
                        SetPreDRC(); // do this before we BEFORE setting the input filename
                }
                else
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Non AC3 Source Audio, DRC not applicable"), Log.LogEntryType.Information);
            }

            _jobLog.WriteEntry(this, Localise.GetPhrase("Setting up input file name parameters"), Log.LogEntryType.Information);
            SetInputFileName(); // Input Filename

            _jobLog.WriteEntry(this, Localise.GetPhrase("Setting up video conversion parameters :") + " " + _videoParams, Log.LogEntryType.Information);
            AddParameter(_videoParams); // Video Parameters

            // Get the value of the preset width before we start modifying
            GetPresetWidth();

            // TRIM for TS, WTV and DVRMS is now done before Commercial Scan in the main conversion job, we do the rest here
            // Set the start and end trim parameters (after the video parameters)
            if (Path.GetExtension(SourceVideo.ToLower()) != ".ts")
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Setting up trim parameters"), Log.LogEntryType.Information);
                SetTrim(); // Set Trim Parameters
            }

            // Set the pre cropping BEFORE scaling, filter chain rule - ffmpeg breaks otherwise
            _jobLog.WriteEntry(this, Localise.GetPhrase("Setting up pre video crop parameters"), Log.LogEntryType.Information);
            SetPreCrop(); // Set Crop Parameters

            //Set the scaling
            if (!_fixedResolution)
            {
                int VideoWidth = _videoFile.Width;

                _jobLog.WriteEntry(this, Localise.GetPhrase("Checking if video resizing required"), Log.LogEntryType.Information);
                if ((_videoFile.CropWidth > 0) && (_videoFile.CropWidth < _videoFile.Width))
                {
                    VideoWidth = _videoFile.CropWidth;
                }
                // If we do not need to scale, don't
                if (VideoWidth > _maxWidth)
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Setting up video resize parameters"), Log.LogEntryType.Information);
                    SetResize(); // Set Resize parameters
                }
            }
            else
                _jobLog.WriteEntry(this, Localise.GetPhrase("Fixed resolution video, no resizing"), Log.LogEntryType.Information);

            // Set post cropping AFTER scaling, filter chain rule - mencoder breaks otherwise
            _jobLog.WriteEntry(this, Localise.GetPhrase("Setting up post video crop parameters"), Log.LogEntryType.Information);
            SetPostCrop(); // Set Crop Parameters

            // Sometimes we need to set the Aspect Ratio as the last parameter in the video filter chain (e.g. with libxvid)
            _jobLog.WriteEntry(this, Localise.GetPhrase("Setting up aspect ratio if required"), Log.LogEntryType.Information);
            SetAspectRatio();

            // Set the quality 
            _jobLog.WriteEntry(this, Localise.GetPhrase("Setting up bitrate and quality parameters"), Log.LogEntryType.Information);
            SetResizeBitRateQuality();
            if (_quality <= 0.1) _quality = 0.1;
            if (ConstantQuality && (_quality > 2)) _quality = 2;
            SetQuality(); // Quality Parameters

            _jobLog.WriteEntry(this, Localise.GetPhrase("Setting up audio conversion parameters :") + " " + _audioParams, Log.LogEntryType.Information);
            if (_videoFile.AudioCodec != "e-ac-3" || _videoFile.AudioCodec != "eac3")
            {
                AddParameter(_audioParams); // Audio Parameters

                // Select the audio language specified by the user if multiple audio languages exist
                if ((!String.IsNullOrEmpty(_videoFile.RequestedAudioLanguage)) && (_videoFile.FFMPEGStreamInfo.AudioTracks > 1)) // check if we were requested to isolate a language manually and if there is more than one audio track
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Selecting Audio Track :") + " " + _videoFile.AudioTrack.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Information);
                    SetAudioLanguage(); // Select the right Audio Track if required, do this before we AFTER setting the audio parameters
                }
                else
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Skipping over Audio Track selection, no language request or only one Audio Track found"), Log.LogEntryType.Information);

                // Set the volume
                if (_volume != 0) // volume is in dB (0dB is no change)
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Setting up volume adjustment :") + " " + _volume.ToString("#0.0", System.Globalization.CultureInfo.InvariantCulture) + "dB", Log.LogEntryType.Information);

                    if (_audioParams.ToLower().Contains("copy"))
                        _jobLog.WriteEntry(this, Localise.GetPhrase("Copy Audio stream detected, skipping volume settings"), Log.LogEntryType.Warning);
                    else
                        SetVolume(); // do this before we AFTER setting the audio parameters
                }

                // Set the DRC with the remaining audio options for most encoders (except ffmpeg)
                if (_drc)
                {
                    if (_videoFile.AudioCodec == "ac-3" || _videoFile.AudioCodec == "ac3") // DRC only applies to AC3 audio
                    {
                        _jobLog.WriteEntry(this, Localise.GetPhrase("Setting up PostDRC"), Log.LogEntryType.Information);

                        if (_audioParams.ToLower().Contains("copy"))
                            _jobLog.WriteEntry(this, Localise.GetPhrase("Copy Audio stream detected, skipping DRC settings"), Log.LogEntryType.Warning);
                        else
                            SetPostDRC(); // do this before we BEFORE setting the input filename
                    }
                    else
                        _jobLog.WriteEntry(this, Localise.GetPhrase("Non AC3 Source Audio, DRC not applicable"), Log.LogEntryType.Information);
                }
            }
            else
                _jobLog.WriteEntry(this, Localise.GetPhrase("Skipping audio conversion paramters, E-AC3 detected"), Log.LogEntryType.Information);

            //Set audio channels
            _jobLog.WriteEntry(this, Localise.GetPhrase("Setting up Audio channels"), Log.LogEntryType.Information);
            if (_videoFile.AudioCodec != "e-ac-3" || _videoFile.AudioCodec != "eac3") SetAudioChannels(); // Multi channel Audio
            else _jobLog.WriteEntry(this, Localise.GetPhrase("Skipping Audio channels, E-AC3 detected"), Log.LogEntryType.Information);
            
            //Set the output file names
            _jobLog.WriteEntry(this, Localise.GetPhrase("Setting up Output filename"), Log.LogEntryType.Information);
            SetOutputFileName();

            //Convert the video - MAIN ONE
            if (!_jobStatus.Cancelled)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Converting the video - Main conversion"), Log.LogEntryType.Information);
                bool ret = ConvertWithTool();
                _jobLog.WriteEntry(this, Localise.GetPhrase("Conversion: Percentage Complete") + " " + _jobStatus.PercentageComplete.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
                if (!ret) // with unsuccessful or incomplete
                {
                    _jobStatus.ErrorMsg = Localise.GetPhrase("Conversion of video failed");
                    _jobLog.WriteEntry(this, (_jobStatus.ErrorMsg), Log.LogEntryType.Error);
                    return false;
                }
                _jobLog.WriteEntry(this, Localise.GetPhrase("Finished video conversion, file size [KB]") + " " + (Util.FileIO.FileSize(_convertedFile) / 1024).ToString("N", System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
            }

            // EAC3 exception handling
            if (!_jobStatus.Cancelled)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Checking EAC3 Audio conversion"), Log.LogEntryType.Information);
                ConvertEAC3();
                _jobLog.WriteEntry(this, Localise.GetPhrase("EAC3 conversion: Percentage Complete") + " " + _jobStatus.PercentageComplete.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
                if (_jobStatus.PercentageComplete == 0) // Check for total failure as some component like FFMPEG dont' return a correct %
                {
                    _jobStatus.ErrorMsg = Localise.GetPhrase("Conversion of EAC3 failed");
                    _jobLog.WriteEntry(this, (_jobStatus.ErrorMsg), Log.LogEntryType.Error);
                    return false;
                }
            }

            // Set the audio delay post conversion
            if (!_jobStatus.Cancelled)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Correcting Audio Delay if required"), Log.LogEntryType.Information);
                bool ret = FixAudioDelay();
                _jobLog.WriteEntry(this, Localise.GetPhrase("Fix Audio Delay: Percentage Complete") + " " + _jobStatus.PercentageComplete.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
                if (!ret)
                {
                    _jobStatus.ErrorMsg = Localise.GetPhrase("Fix AudioSync failed");
                    _jobLog.WriteEntry(this, (_jobStatus.ErrorMsg), Log.LogEntryType.Error);
                    return false;
                }
            }

            // Remux if required
            if (!_jobStatus.Cancelled)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Remuxing video if required"), Log.LogEntryType.Information);

                Remux remuxVideo = new Remux(_convertedFile, ref _videoFile, ref _jobStatus, _jobLog, _remuxTo, _extension);
                bool ret = remuxVideo.RemuxFile();
                _jobLog.WriteEntry(this, Localise.GetPhrase("Conversion Remux: Percentage Complete") + " " + _jobStatus.PercentageComplete.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
                if (!ret) // Check for total failure as some component like FFMPEG dont' return a correct %
                {
                    _jobStatus.ErrorMsg = Localise.GetPhrase("Remux failed");
                    _jobLog.WriteEntry(this, (_jobStatus.ErrorMsg), Log.LogEntryType.Error);
                    return false;
                }
                _convertedFile = remuxVideo.RemuxedFile;
            }

            // Check if the filename conflict took place and we need to rename the file
            if (!String.IsNullOrEmpty(_replaceWithOriginalName))
            {
                try
                {
                    FileIO.TryFileDelete(_replaceWithOriginalName); // Delete if the file with the replacement name exists (sometime with .TS file and TS profiles this happens)
                    File.Move(_convertedFile, _replaceWithOriginalName);
                    _convertedFile = _replaceWithOriginalName;
                }
                catch (Exception e)
                {
                    _jobStatus.ErrorMsg = Localise.GetPhrase("Unable to rename file after conversion");
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Unable to rename file after conversion"), Log.LogEntryType.Error);
                    _jobLog.WriteEntry(this, "Error -> " + e.ToString(), Log.LogEntryType.Error);
                    return false;
                }
            }

            if (!_jobStatus.Cancelled)
            {
                if (_removedAds) // We have successfully removed ad's
                    _videoFile.AdsRemoved = true;
                return true;
            }
            else
            {
                _jobStatus.ErrorMsg = Localise.GetPhrase("Job cancelled, Aborting conversion");
                _jobLog.WriteEntry(this, Localise.GetPhrase("Job cancelled, Aborting conversion"), Log.LogEntryType.Error);
                return false;
            }
        }


        private bool ParamaterValueStartEnd(string cmd, out int start, out int length)
        {
            start = -1;
            length = -1;

            cmd = cmd + " ";

            int idx = -1;
            if (!_caseSensitive)
            {
                idx = _cmdParams.ToLower().IndexOf(cmd.ToLower());
            }
            else
            {
                idx = _cmdParams.IndexOf(cmd);
            }

            // Find the start of the parameter
            if (idx < 0) return false;
            idx = idx + cmd.Length;
            while (idx < _cmdParams.Length)
            {
                if (!char.IsWhiteSpace(_cmdParams[idx])) break;
                idx++;
            }
            // Found the start of the parameter

            // Find the end of the parameter
            int endidx = -1;
            bool inQuotes = false;
            for (int i = idx; i < _cmdParams.Length; i++)
            {
                if ((char.IsWhiteSpace(_cmdParams[i])) && (!inQuotes))
                {
                    endidx = i;
                    break;
                }
                else if (_cmdParams[i] == '\"')
                {
                    inQuotes = !inQuotes;
                }
            }
            if (!inQuotes)
            {
                // Found a valid parameter
                if (endidx == -1) endidx = _cmdParams.Length;
                start = idx;
                length = endidx - idx;
                return (length > 0);
            }
            return false;
        }

        protected string ParameterValue(string cmd)
        {
            int start, length;

            if (ParamaterValueStartEnd(cmd, out start, out length))
            {
                return _cmdParams.Substring(start, length);
            }
            else return "";

        }

        protected bool ParameterValueReplace(string cmd, string newValue)
        {
            int start, length;

            if (ParamaterValueStartEnd(cmd, out start, out length))
            {
                string newCmdLine = _cmdParams.Substring(0, start - 1).Trim();
                newCmdLine += " " + newValue;
                newCmdLine += " " + _cmdParams.Substring(start + length).Trim();
                _cmdParams = newCmdLine;
                return true;
            }
            else return false;
        }

        protected void ParameterValueReplaceOrInsert(string cmd, string newValue)
        {
            if (!ParameterValueReplace(cmd, newValue))
            {
                _cmdParams += " " + cmd + " " + newValue;
            }
        }
        private bool ParameterSubValueStartEnd(string cmd, string subcmd, out int start, out int length)
        {
            start = -1;
            length = -1;
            int paramStart = -1;
            int paramLength = -1;
            string param = "";

            // Get the parameter value and the start/length 
            // TODO Regex this all once I get some time (hah!)

            if (ParamaterValueStartEnd(cmd, out paramStart, out paramLength))
            {
                param = _cmdParams.Substring(paramStart, paramLength);
            }
            else return false;

            if (!_caseSensitive)
            {
                subcmd = subcmd.ToLower();
            }

            // Hack for mencoder -vf as it varies from everything else
            string[] subParams;
            if (cmd == "-vf")
            {
                subParams = param.Split(',');
            }
            else
            {
                subParams = param.Split(_separator);
            }
            string subStr = "";
            foreach (string s in subParams)
            {
                string s2 = "";
                if (_caseSensitive)
                {
                    s2 = s;
                }
                else
                {
                    s2 = s.ToLower();
                }
                if (s2.Contains("="))
                {
                    s2 = s2.Substring(0, s2.IndexOf("="));
                }
                if (s2 == subcmd)
                {
                    subStr = s;
                    break;
                }
            }
            if (subStr != "")
            {
                start = paramStart + param.IndexOf(subStr);
                if (subStr.Contains("="))
                {
                    start += subStr.IndexOf("=") + 1;
                    length = subStr.Length - subStr.IndexOf("=") - 1;
                }
                else
                {
                    length = subStr.Length;
                }
                if (start > _cmdParams.Length - 1) start = _cmdParams.Length - 1;
                // TODo Start + length check
                return true;
            }
            else
            {
                return false;
            }


        }

        protected string ParameterSubValue(string cmd, string subCmd)
        {
            int start, length;

            if (ParameterSubValueStartEnd(cmd, subCmd, out start, out length))
            {
                return _cmdParams.Substring(start, length);
            }
            else return "";
        }

        protected bool ParameterSubValueReplace(string cmd, string subCmd, string newValue)
        {
            int start, length;

            if (ParameterSubValueStartEnd(cmd, subCmd, out start, out length))
            {
                string newCmdLine = _cmdParams.Substring(0, start);
                newCmdLine += newValue;
                newCmdLine += _cmdParams.Substring(start + length);
                _cmdParams = newCmdLine;
                return true;
            }
            else return false;
        }

        protected void ParameterSubValueReplaceOrInsert(string cmd, string subCmd, string newValue)
        {
            if (!ParameterSubValueReplace(cmd, subCmd, newValue))
            {
                // Hack for mencoder -vf and -af as it varies from everything else
                string paramValue;
                string value = "=" + newValue;
                if (newValue == "") value = "";
                if (cmd == "-vf" || cmd == "-af")
                {
                    if (ParameterValue(cmd) != "") // avoid the last comma if this is a new key
                        paramValue = ParameterValue(cmd) + "," + subCmd + value; // place at the end
                    else
                        paramValue = subCmd + value;
                }
                else
                {
                    paramValue = subCmd + value;
                    if(ParameterValue(cmd) != "") // avoid the last separator if this is a new key
                        paramValue = ParameterValue(cmd) + _separator + paramValue; // place at the end
                }
                ParameterValueReplaceOrInsert(cmd, paramValue);
            }
        }

        protected int ConstantQualityValue(int lowQuality, int highQuality, int qualDefault)
        {
            if (_quality == 1) return qualDefault;

            int qualBoundary;
            double qualMult;

            // Chooose the boundary value to head to - remember highQuality  can be the LOWEST OR THE HIGHEST value 
            if (_quality < 1)
            {
                qualBoundary = lowQuality;
                qualMult = _quality;
            }
            else
            {
                qualBoundary = highQuality;
                qualMult = _quality - 1;
            }

            return (int)((qualBoundary - qualDefault) * (qualMult)) + qualDefault;
        }

        protected void AddVideoFilter(string subCmd, string parameter)
        {
            // First try replacing an existing filter
            if (ParameterSubValueReplace("-vf", subCmd, parameter)) return;

            // OK, so the filter is not there, so work out where to insert it in the filter chain and then add it;
            string newFilter = subCmd + "=" + parameter;
            if (!_cmdParams.Contains("-vf"))
            {
                ParameterSubValueReplaceOrInsert("-vf", subCmd, parameter);
            }
            else
            {
                string vfParams = ParameterValue("-vf");
                if (vfParams.Contains("softskip"))
                {
                    vfParams = vfParams.Insert(vfParams.IndexOf("softskip") + "softskip".Length, "," + newFilter);
                    ParameterValueReplace("-vf", vfParams);
                }
                else if (vfParams.Contains("pullup"))
                {
                    vfParams = vfParams.Insert(vfParams.IndexOf("pullup") + "pullup".Length, "," + newFilter);
                    ParameterValueReplace("-vf", vfParams);
                }
                else
                    ParameterSubValueReplaceOrInsert("-vf", subCmd, parameter);
            }
        }

        protected void AddAudioFilter(string subCmd, string parameter)
        {
            // First try replacing an existing filter
            if (ParameterSubValueReplace("-af", subCmd, parameter)) return;

            // OK, so the filter is not there, so work out where to insert it in the filter chain and then add it;
            string newFilter = subCmd + "=" + parameter;
            if (!_cmdParams.Contains("-af"))
            {
                ParameterSubValueReplaceOrInsert("-af", subCmd, parameter);
            }
            else
            {
                // TODO: Any special filter rules/toolchain rules for audio filters?
                ParameterSubValueReplaceOrInsert("-af", subCmd, parameter);
            }
        }

        protected string SourceVideo
        {
            get { return _videoFile.SourceVideo; }
        }

        public bool Error
        {
            get { return _error; }
        }

        public bool Unsupported
        {
            get { return _unsupported; }
        }

        public string ConvertedFile
        {
            get { return _convertedFile; }
        }

        protected abstract bool ConvertWithTool();

        protected abstract void SetPreCrop();

        protected abstract void SetPostCrop();

        protected abstract void SetQuality();

        protected abstract void SetResize();

        protected abstract void SetInputFileName();

        protected abstract void SetOutputFileName();

        protected abstract void SetAudioChannels();

        protected abstract void SetAudioLanguage();

        protected abstract bool ConstantQuality { get; }

        protected abstract int PresetVideoWidth { get; }

        protected abstract void SetVolume();

        protected abstract void SetPreDRC();

        protected abstract void SetPostDRC();

        protected abstract void SetAspectRatio();

        protected abstract void SetTrim();

        protected abstract void GetPresetWidth();
    }
}
