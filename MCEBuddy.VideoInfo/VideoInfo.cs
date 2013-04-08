using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using MCEBuddy.AppWrapper;
using MCEBuddy.Globals;
using MCEBuddy.Util;

namespace MCEBuddy.VideoProperties
{
    public class VideoInfo
    {
        // External Parameters set by other functions related to video properties
        public string ConversionTool = "";
        public bool AdsRemoved = false;
        public bool AudioDelaySet = false;
        public string Extension = "";
        public string RemuxTo = "";

        // Parameters that are updated at runtime and don't need reinitialization
        protected JobStatus _jobStatus;
        protected Log _jobLog;
        private string _EDLFile;
        private string _requestedAudioLanguage; // Language that we were requested to look for
        private string _originalFileName;
        private string _remuxedFileName;
        private bool _skipCropping;
        private FFmpegMediaInfo _originalFileFFmpegStreamInfo;
        private FFmpegMediaInfo _ffmpegStreamInfo;
        private bool _ignoreSuspend = false; // If called from UI, we ignore any suspend requests to avoid hanging

        // IMPORTANT ADD PARAMETERS TO RESET SECTION: VideoInfo Parameters to be reset each time Update is called - these are to be reset from scratch for a clean update by the Update function
        private bool _error;
        private string _videoCodec;
        private string _audioCodec;
        private float _audioDelay;
        private int _audioChannels;
        private float _fps;
        private int _height;
        private int _width;
        private float _duration;
        private int _cropHeight;
        private int _cropWidth;
        private string _cropString;
        private int _audioStream; // Stream number of the selected Audio Channel
        private int _audioTrack; // Audio track number of the selected Audio Channel (0 based reference, i.e. 0 indicates 1st audio)
        private int _videoStream; // Stream number for the video stream (there is only 1 video stream per file)
        private int _videoPID; // Video stream PID
        private int _audioPID; // Audio stream PID
        private string _audioLanguage; // Store the language we are selecting
        private bool _selectedAudioImpaired; // Is this audio selection impaired audio

        private void ResetParameters()
        {
            _error = false;
            _videoCodec = "";
            _audioCodec = "";
            _audioDelay = 0;
            _audioChannels = 0;
            _fps = 0;
            _height = 0;
            _width = 0;
            _duration = 0;
            _cropHeight = 0;
            _cropWidth = 0;
            _cropString = "";
            _audioStream = -1; // Stream number of the selected Audio Channel
            _audioTrack = -1; // Audio track number of the selected Audio Channel (0 based reference, i.e. 0 indicates 1st audio)
            _videoStream = -1; // Stream number for the video stream (there is only 1 video stream per file)
            _videoPID = -1; // Video stream PID
            _audioPID = -1; // Audio stream PID
            _audioLanguage = ""; // Store the language we are selecting
            _selectedAudioImpaired = false; // no imparied audio
        }

        /// <summary>
        /// Gets the audio / video information for a file, does not check cropping information
        /// </summary>
        /// <param name="videoFileName">Full path to the video file to get information about</param>
        /// <param name="ignoreSuspend">True if called from a GUI</param>
        public VideoInfo(string videoFileName, ref JobStatus jobStatus, Log jobLog, bool ignoreSuspend)
        {
            _ignoreSuspend = ignoreSuspend;
            UpdateVideoInfo("", videoFileName, "", "", "", ref jobStatus, jobLog);
        }

        public VideoInfo(string profile, string videoFileName, string remuxedFileName, string edlFile, string audioLanguage, ref JobStatus jobStatus, Log jobLog)
        {
            UpdateVideoInfo(profile, videoFileName, remuxedFileName, edlFile, audioLanguage, ref jobStatus, jobLog);
        }

        /// <summary>
        /// Updates the Video file properties structure, check for crop, audio and video information
        /// </summary>
        /// <param name="profile">Profile being used</param>
        /// <param name="videoFileName">Path to Original Source Video</param>
        /// <param name="remuxedFileName">Path to Remuxed video, else null or empty string</param>
        /// <param name="edlFile">Path to EDL file else null or empty string</param>
        /// <param name="audioLanguage">Audio Language</param>
        /// <param name="jobStatus">JobStatus</param>
        /// <param name="jobLog">JobLog</param>
        public void UpdateVideoInfo(string profile, string videoFileName, string remuxedFileName, string edlFile, string audioLanguage, ref JobStatus jobStatus, Log jobLog)
        {
            ResetParameters(); // Reset VideoInfo parameters

            _jobStatus = jobStatus;
            _jobLog = jobLog;
            _EDLFile = edlFile;
            _requestedAudioLanguage = audioLanguage;
            _originalFileName = videoFileName;
            _remuxedFileName = remuxedFileName;

            Ini ini = new Ini(GlobalDefs.ProfileFile);

            if (String.IsNullOrEmpty(profile))
                _skipCropping = true; // we don't need crop info
            else
                _skipCropping = ini.ReadBoolean(profile, "SkipCropping", false);

            string activeFileName = _originalFileName; // source file
            if (!String.IsNullOrEmpty(_remuxedFileName)) activeFileName = _remuxedFileName; // if there is a remux file, then the active conversion file is the remux file

            _jobLog.WriteEntry(this, "Reading MediaInfo from " + activeFileName, Log.LogEntryType.Information);
            MediaInfoDll mi = new MediaInfoDll();

            try
            {
                mi.Open(activeFileName);
                
                mi.Option("Inform", "General;%Video_Format_WithHint_List%");
                _videoCodec = mi.Inform().ToLower().Trim();
                jobLog.WriteEntry(this, "Video Codec : " + _videoCodec, Log.LogEntryType.Debug);

                mi.Option("Inform", "General;%Audio_Format_WithHint_List%");
                _audioCodec = mi.Inform().ToLower().Trim();
                jobLog.WriteEntry(this, "Audio Codec : " + _audioCodec, Log.LogEntryType.Debug); 
                
                mi.Option("Inform", "Video; %FrameRate%");
                float.TryParse(mi.Inform(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _fps);
                jobLog.WriteEntry(this, "Video FPS : " + _fps.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);

                mi.Option("Inform", "Video; %Width%");
                int.TryParse(mi.Inform(), out _width);
                jobLog.WriteEntry(this, "Video Width : " + _width.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);

                mi.Option("Inform", "Video; %Height%");
                int.TryParse(mi.Inform(), out _height);
                jobLog.WriteEntry(this, "Video Height : " + _height.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);

                int durationMs = -1;
                mi.Option("Inform", "Video; %Duration%");
                int.TryParse(mi.Inform(), out durationMs);
                if (durationMs > 0)
                {
                    _duration = (float)durationMs/1000;
                }
                jobLog.WriteEntry(this, "Video Duration : " + _duration.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);

                mi.Option("Inform", "Audio; %Video_Delay%");
                float.TryParse(mi.Inform(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _audioDelay);
                _audioDelay = (float)_audioDelay/1000;
                jobLog.WriteEntry(this, "Audio Delay : " + _audioDelay.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);

                // We don't get AudioChannel information here as it interfers with FFMPEG
                /*mi.Option("Inform", "Audio; %Channels%");
                int.TryParse(mi.Inform(), out _audioChannels);
                jobLog.WriteEntry(this, "Audio Channels : " + _audioChannels.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);*/
            }
            catch (Exception ex)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Error reading media information using MediaInfo") + " " + ex.Message, Log.LogEntryType.Warning);
                _error = true;
            }

            // Supplement with extracting Video and Audio information (sometimes MediaInfo fails) using FFMPEG and selected the Audio Language specified
            _jobLog.WriteEntry(this, Localise.GetPhrase("Supplementing Media information using FFMPEG"), Log.LogEntryType.Information);
            _ffmpegStreamInfo = new FFmpegMediaInfo(activeFileName, ref _jobStatus, _jobLog, _ignoreSuspend); // this may be called from the UI request
            _ffmpegStreamInfo.Run();
            if (_ffmpegStreamInfo.Success && !_ffmpegStreamInfo.ParseError)
            {
                //Video parameters
                _width = _ffmpegStreamInfo.MediaInfo.VideoInfo.Width;
                _height = _ffmpegStreamInfo.MediaInfo.VideoInfo.Height;
                if (_fps == 0) // use FPS from MediaInfo if available, more reliable
                    _fps = _ffmpegStreamInfo.MediaInfo.VideoInfo.FPS;
                else
                    _ffmpegStreamInfo.MediaInfo.VideoInfo.FPS = _fps; // Store the value from MediaInfo, more reliable
                _duration = _ffmpegStreamInfo.MediaInfo.VideoInfo.Duration;
                _videoCodec = _ffmpegStreamInfo.MediaInfo.VideoInfo.VideoCodec;

                // Audio parameters - find the best Audio channel for the selected language otherwise by default the encoder will select the best audio channel
                bool foundLang = false;
                if (!String.IsNullOrEmpty(_requestedAudioLanguage))
                {
                    for (int i = 0; i < _ffmpegStreamInfo.AudioTracks; i++)
                    {
                        // Language selection check, if the user has picked a specific language code, look for it
                        // If we find a match, we look the one with the highest number of channels in it
                        if ((_ffmpegStreamInfo.MediaInfo.AudioInfo[i].Language.ToLower() == _requestedAudioLanguage) && (_ffmpegStreamInfo.MediaInfo.AudioInfo[i].Channels > 0))
                        {
                            if (foundLang)
                                if (!( // take into account impaired tracks (since impaired tracks typically have no audio)
                                    ((_ffmpegStreamInfo.MediaInfo.AudioInfo[i].Channels > _audioChannels) && !_ffmpegStreamInfo.MediaInfo.AudioInfo[i].Impaired) || // PREFERENCE to non-imparied Audio tracks with the most channels
                                    ((_ffmpegStreamInfo.MediaInfo.AudioInfo[i].Channels > _audioChannels) && _selectedAudioImpaired) || // PREFERENCE to Audio tracks with most channels if currently selected track is impaired
                                    (!_ffmpegStreamInfo.MediaInfo.AudioInfo[i].Impaired && _selectedAudioImpaired) // PREFER non impaired audio over currently selected impaired
                                    ))
                                    continue; // we have found a lang match, now we are looking for more channels only now

                            _audioChannels = _ffmpegStreamInfo.MediaInfo.AudioInfo[i].Channels;
                            _audioStream = _ffmpegStreamInfo.MediaInfo.AudioInfo[i].Stream; // store the stream number for the selected audio channel
                            _audioCodec = _ffmpegStreamInfo.MediaInfo.AudioInfo[i].AudioCodec;
                            _audioPID = _ffmpegStreamInfo.MediaInfo.AudioInfo[i].PID; // Audio PID
                            _audioTrack = i; // Store the audio track number we selected
                            _audioLanguage = _ffmpegStreamInfo.MediaInfo.AudioInfo[i].Language.ToLower(); // this is what we selected
                            _selectedAudioImpaired = _ffmpegStreamInfo.MediaInfo.AudioInfo[i].Impaired; // Is this an imparied audio track?
                            foundLang = true; // We foudn the language we were looking for
                            _jobLog.WriteEntry(this, Localise.GetPhrase("Found Audio Language match for language") + " " + _requestedAudioLanguage.ToUpper() + ", " + Localise.GetPhrase("Audio Stream") + " " + _audioStream.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", " + Localise.GetPhrase("Audio Track") + " " + _audioTrack.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", " + Localise.GetPhrase("Channels") + " " + _audioChannels.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", " + Localise.GetPhrase("Codec") + " " + _audioCodec + " Impaired " + _selectedAudioImpaired.ToString(), Log.LogEntryType.Debug);
                        }
                    }

                    // Store the video information (there's only 1 video per file)
                    _videoStream = _ffmpegStreamInfo.MediaInfo.VideoInfo.Stream;
                    _videoPID = _ffmpegStreamInfo.MediaInfo.VideoInfo.PID; // video PID

                    if (!foundLang)
                        _jobLog.WriteEntry(this, Localise.GetPhrase("Could not find a match for selected Audio Language Code") + " " + _requestedAudioLanguage + ", letting encoder choose best audio language", Log.LogEntryType.Warning);
                    else
                        _jobLog.WriteEntry(this, Localise.GetPhrase("Selected Audio Stream") + " " + _audioStream.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", " + Localise.GetPhrase("Audio Track") + " " + _audioTrack.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", " + Localise.GetPhrase("Channels") + " " + _audioChannels.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", " + Localise.GetPhrase("Codec") + " " + _audioCodec + ", Impaired " + _selectedAudioImpaired.ToString(), Log.LogEntryType.Debug);
                }
                else
                {
                    // check if all audio streams have same codec, if so populate the field for later use during reading profiles
                    for (int i = 0; i < _ffmpegStreamInfo.AudioTracks; i++)
                    {
                        if (i == 0)
                            _audioCodec = _ffmpegStreamInfo.MediaInfo.AudioInfo[i].AudioCodec; // baseline the codec name
                        else if (_audioCodec != _ffmpegStreamInfo.MediaInfo.AudioInfo[i].AudioCodec)
                        {
                            _audioCodec = ""; // All codecs are not the same, reset it and let the encoder figure it out
                            break; // we're done here
                        }
                    }

                    _jobLog.WriteEntry(this, Localise.GetPhrase("Audio Stream") + " " + _audioStream.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", " + Localise.GetPhrase("Audio Track") + " " + _audioTrack.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", " + Localise.GetPhrase("Channels") + " " + _audioChannels.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", " + Localise.GetPhrase("Codec") + " " + _audioCodec + ", Impaired " + _selectedAudioImpaired.ToString(), Log.LogEntryType.Debug);
                    _jobLog.WriteEntry(this, "No audio language selected, letting encoder choose best audio language", Log.LogEntryType.Warning);
                }

                _error = false; // all good now
            }
            else
                _error = true;
            
            if (_error)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Unable to read media information using FFMPEG or MediaInfo"), Log.LogEntryType.Error);
                return;
            }

            // Get the video properties for the original video
            _jobLog.WriteEntry(this, "Reading Original File Media information", Log.LogEntryType.Information);
            _originalFileFFmpegStreamInfo = new FFmpegMediaInfo(_originalFileName, ref _jobStatus, _jobLog, _ignoreSuspend);
            _originalFileFFmpegStreamInfo.Run();
            if (!_originalFileFFmpegStreamInfo.Success || _originalFileFFmpegStreamInfo.ParseError)
                _jobLog.WriteEntry(this, Localise.GetPhrase("Unable to read media information using FFMPEG"), Log.LogEntryType.Warning);

            if (_skipCropping)
            {
                _jobLog.WriteEntry(this, "Skipping crop information", Log.LogEntryType.Information);
            }
            else
            {
                string orderSetting = ini.ReadString(profile, "order", "").ToLower().Trim();
                if (orderSetting != "handbrake") // if handbrake is being used (only handbrake and not combined with any backup encoder) then skip cropping as Handbrake does autocropping
                {
                    _jobLog.WriteEntry(this, "Getting crop information using MEncoder", Log.LogEntryType.Information);
                    MencoderCropDetect mencoderCropDetect = new MencoderCropDetect(activeFileName, _EDLFile, ref _jobStatus, jobLog);
                    mencoderCropDetect.Run();
                    if (!mencoderCropDetect.Success || _jobStatus.PercentageComplete < GlobalDefs.ACCEPTABLE_COMPLETION)
                    {
                        _jobLog.WriteEntry(Localise.GetPhrase("MEncoder Crop Detect Process Error - cropping will not take place"), Log.LogEntryType.Warning);
                        /*_error = true; // Crop detection failing is not a show stopper
                        return;*/
                    }
                    else if ((!String.IsNullOrEmpty(mencoderCropDetect.CropString)) && (mencoderCropDetect.CropHeight > 0) && (mencoderCropDetect.CropWidth > 0))
                    {
                        if ((mencoderCropDetect.CropWidth <= _width) && (mencoderCropDetect.CropHeight <= _height))
                        {
                            _cropWidth = Util.MathLib.RoundOff(mencoderCropDetect.CropWidth, 16); // Round width to the nearest multiple of 16
                            _cropHeight = Util.MathLib.RoundOff(mencoderCropDetect.CropHeight, 8); // Round height to the nearest multiple of 8
                            _cropString = mencoderCropDetect.GenerateCropString(_cropWidth, _cropHeight, mencoderCropDetect.CropStartX, mencoderCropDetect.CropStartY); // Get the new crop string
                            jobLog.WriteEntry(this, "Crop String : " + _cropString + ", Crop Height " + mencoderCropDetect.CropHeight + ", Crop Width " + mencoderCropDetect.CropWidth, Log.LogEntryType.Debug);
                        }
                        else
                            _jobLog.WriteEntry(this, Localise.GetPhrase("MEncoder Crop Detect Process error, Crop size > Video size, ignoring cropping. Crop Width:" + _cropWidth + " Crop Height:" + _cropHeight + " Video Width:" + _width + " Video Height:" + _height), Log.LogEntryType.Warning);
                    }
                    else
                        _jobLog.WriteEntry(this, Localise.GetPhrase("MEncoder Crop Detect Process - no cropping detected"), Log.LogEntryType.Information);
                }
                else
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Using Handbrake encoder exclusively, Handbrake with autodetect cropping during conversion"), Log.LogEntryType.Information);
            }
        }


        public string VideoCodec
        {
            get { return _videoCodec; }
        }

        public string AudioCodec
        {
            get { return _audioCodec; }
        }

        public float Fps
        {
            get { return _fps; }
        }

        public int Height
        {
            get { return _height; }
        }

        public int Width
        {
            get { return _width; }
        }

        public int CropHeight
        {
            get { return _cropHeight; }
        }

        public int CropWidth
        {
            get { return _cropWidth; }
        }

        public string CropString
        {
            get { return _cropString; }
        }

        public bool Error
        {
            get { return _error; }
        }

        public string EDLFile
        {
            get { return _EDLFile; }
        }

        public string OriginalFileName
        {
            get { return _originalFileName; }
        }

        public string OriginalFileExtension
        {
            get { return (Path.GetExtension(_originalFileName).Trim().ToLower()); }
        }

        public bool CropDetected
        {
            get { return ((_cropHeight > 0) && (_cropWidth > 0)); }
        }

        public float Duration
        {
            get { return _duration; }
        }

        public float AudioDelay
        {
            get { return _audioDelay; }
        }

        public string RemuxedFileName
        {
            get { return _remuxedFileName; }
        }

        /// <summary>
        /// This represents the source file active in use for conversion. If remuxing has been done, then it returns the remuxed file else the original file (or temp copied file)
        /// </summary>
        public string SourceVideo
        {
            get
            {
                if (!String.IsNullOrEmpty(_remuxedFileName)) return _remuxedFileName;
                return _originalFileName;
            }
        }

        public int AudioChannels
        {
            get { return _audioChannels; }
        }

        public int AudioStream
        {
            get { return _audioStream; }
        }

        public int AudioTrack
        {
            get { return _audioTrack; }
        }

        public int VideoStream
        {
            get { return _videoStream; }
        }

        public int AudioPID
        {
            get { return _audioPID; }
        }

        public int VideoPID
        {
            get { return _videoPID; }
        }

        public FFmpegMediaInfo FFMPEGStreamInfo
        {
            get { return _ffmpegStreamInfo; }
        }

        public FFmpegMediaInfo OriginalVideoFFMPEGStreamInfo
        {
            get { return _originalFileFFmpegStreamInfo; }
        }

        public string AudioLanguage
        {
            get { return _audioLanguage; }
        }

        public string RequestedAudioLanguage
        {
            get { return _requestedAudioLanguage; }
        }
    }
}
