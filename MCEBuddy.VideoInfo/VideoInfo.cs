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
        private bool skipCropping;
        private FFmpegMediaInfo originalFileFFmpegStreamInfo;
        private FFmpegMediaInfo ffmpegStreamInfo;

        // VideoInfo Parameters to be reset each time Update is called - these are to be reset from scratch for a clean update by the Update function
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
        private int audioStream; // Stream number of the selected Audio Channel
        private int audioTrack; // Audio track number of the selected Audio Channel (0 based reference, i.e. 0 indicates 1st audio)
        private int videoStream; // Stream number for the video stream (there is only 1 video stream per file)
        private int videoPID; // Video stream PID
        private int audioPID; // Audio stream PID
        private string _audioLanguage; // Store the language we are selecting

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
            audioStream = -1; // Stream number of the selected Audio Channel
            audioTrack = -1; // Audio track number of the selected Audio Channel (0 based reference, i.e. 0 indicates 1st audio)
            videoStream = -1; // Stream number for the video stream (there is only 1 video stream per file)
            videoPID = -1; // Video stream PID
            audioPID = -1; // Audio stream PID
            _audioLanguage = ""; // Store the language we are selecting
        }

        public VideoInfo(string videoFileName, ref JobStatus jobStatus, Log jobLog)
        {
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
                skipCropping = true; // we don't need crop info
            else
                skipCropping = ini.ReadBoolean(profile, "SkipCropping", false);

            string activeFileName = videoFileName; // source file
            if (!String.IsNullOrEmpty(remuxedFileName)) activeFileName = remuxedFileName; // if there is a remux file, then the active conversion file is the remux file

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
            ffmpegStreamInfo = new FFmpegMediaInfo(activeFileName, ref _jobStatus, _jobLog);
            ffmpegStreamInfo.Run();
            if (ffmpegStreamInfo.Success && !ffmpegStreamInfo.ParseError)
            {
                //Video parameters
                _width = ffmpegStreamInfo.MediaInfo.VideoInfo.Width;
                _height = ffmpegStreamInfo.MediaInfo.VideoInfo.Height;
                if (_fps == 0) // use FPS from MediaInfo if available, more reliable
                    _fps = ffmpegStreamInfo.MediaInfo.VideoInfo.FPS;
                else
                    ffmpegStreamInfo.MediaInfo.VideoInfo.FPS = _fps; // Store the value from MediaInfo, more reliable
                _duration = ffmpegStreamInfo.MediaInfo.VideoInfo.Duration;
                _videoCodec = ffmpegStreamInfo.MediaInfo.VideoInfo.VideoCodec;

                // Audio parameters - find the best Audio channel
                bool foundLang = false;
                for (int i = 0; i < ffmpegStreamInfo.AudioTracks; i++)
                {
                    /* Quoting from FFMPEG documentation:
                        * By default ffmpeg includes only one stream of each type (video, audio, subtitle) present in the input files and adds them to each output file.
                        * It picks the "best" of each based upon the following criteria;
                        *      for video it is the stream with the highest resolution,
                        *      for audio the stream with the most channels,
                        *      for subtitle it’s the first subtitle stream.
                        * In the case where several streams of the same type rate equally, the lowest numbered stream is chosen.
                    */
                    if (ffmpegStreamInfo.MediaInfo.AudioInfo[i].Channels > _audioChannels && !foundLang) // we keep looking as long as didn't find a language match
                    {
                        _audioChannels = ffmpegStreamInfo.MediaInfo.AudioInfo[i].Channels;
                        audioStream = ffmpegStreamInfo.MediaInfo.AudioInfo[i].Stream; // store the stream number for the selected audio channel
                        _audioCodec = ffmpegStreamInfo.MediaInfo.AudioInfo[i].AudioCodec;
                        audioPID = ffmpegStreamInfo.MediaInfo.AudioInfo[i].PID; // Audio PID
                        audioTrack = i; // Store the audio track number we selected
                        _audioLanguage = ffmpegStreamInfo.MediaInfo.AudioInfo[i].Language.ToLower(); // this is what we selected
                    }

                    // Language selection check, if the user has picked a specific language code, look for it
                    // If we find a match, we look the one with the highest number of channels in it
                    if (ffmpegStreamInfo.MediaInfo.AudioInfo[i].Language.ToLower() == _requestedAudioLanguage)
                    {
                        if (foundLang)
                            if (ffmpegStreamInfo.MediaInfo.AudioInfo[i].Channels <= _audioChannels)
                                continue; // we have found a lang match, now we are looking for more channels only now

                        _audioChannels = ffmpegStreamInfo.MediaInfo.AudioInfo[i].Channels;
                        audioStream = ffmpegStreamInfo.MediaInfo.AudioInfo[i].Stream; // store the stream number for the selected audio channel
                        _audioCodec = ffmpegStreamInfo.MediaInfo.AudioInfo[i].AudioCodec;
                        audioPID = ffmpegStreamInfo.MediaInfo.AudioInfo[i].PID; // Audio PID
                        audioTrack = i; // Store the audio track number we selected
                        _audioLanguage = ffmpegStreamInfo.MediaInfo.AudioInfo[i].Language.ToLower(); // this is what we selected
                        foundLang = true; // We foudn the language we were looking for
                        _jobLog.WriteEntry(this, Localise.GetPhrase("Found Audio Language match for language") + " " + _requestedAudioLanguage.ToUpper() + ", " + Localise.GetPhrase("Audio Stream") + " " + audioStream.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", " + Localise.GetPhrase("Audio Track") + " " + audioTrack.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", " + Localise.GetPhrase("Channels") + " " + _audioChannels.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", " + Localise.GetPhrase("Codec") + " " + _audioCodec, Log.LogEntryType.Debug);
                    }

                    // Store the video information (there's only 1 video per file)
                    videoStream = ffmpegStreamInfo.MediaInfo.VideoInfo.Stream;
                    videoPID = ffmpegStreamInfo.MediaInfo.VideoInfo.PID; // video PID
                }

                if (!foundLang && (!String.IsNullOrEmpty(_requestedAudioLanguage)))
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Could not find a match for selected Audio Language Code") + " " + _requestedAudioLanguage, Log.LogEntryType.Warning);

                if (audioTrack != -1)
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Selected Audio Stream") + " " + audioStream.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", " + Localise.GetPhrase("Audio Track") + " " + audioTrack.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", " + Localise.GetPhrase("Channels") + " " + _audioChannels.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", " + Localise.GetPhrase("Codec") + " " + _audioCodec, Log.LogEntryType.Information);
                else
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Unable to read Audio Stream information using FFMPEG"), Log.LogEntryType.Warning);

                _error = false; // all good now
            }
            
            if (_error)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Unable to read media information using FFMPEG or MediaInfo"), Log.LogEntryType.Error);
                return;
            }

            // Get the video properties for the original video
            if (!String.IsNullOrEmpty(remuxedFileName))
                originalFileFFmpegStreamInfo = ffmpegStreamInfo; // it's the same file
            else
            {
                _jobLog.WriteEntry(this, "Reading Original File Media information", Log.LogEntryType.Information);
                originalFileFFmpegStreamInfo = new FFmpegMediaInfo(activeFileName, ref _jobStatus, _jobLog);
                originalFileFFmpegStreamInfo.Run();
                if (!originalFileFFmpegStreamInfo.Success || originalFileFFmpegStreamInfo.ParseError)
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Unable to read media information using FFMPEG"), Log.LogEntryType.Warning);
            }

            if (skipCropping)
            {
                _jobLog.WriteEntry(this, "Skipping crop information", Log.LogEntryType.Information);
            }
            else
            {
                string orderSetting = ini.ReadString(profile, "order", "").ToLower().Trim();
                if (orderSetting != "handbrake") // if handbrake is being used (only handbrake and not combined with any backup encoder) then skip cropping as Handbrake does autocropping
                {
                    _jobLog.WriteEntry(this, "Getting crop information using MEncoder", Log.LogEntryType.Information);
                    MencoderCropDetect mencoderCropDetect = new MencoderCropDetect(activeFileName, edlFile, ref _jobStatus, jobLog);
                    mencoderCropDetect.Run();
                    if (!mencoderCropDetect.Success || _jobStatus.PercentageComplete < GlobalDefs.ACCEPTABLE_COMPLETION)
                    {
                        _jobLog.WriteEntry(Localise.GetPhrase("MEncoder Crop Detect Process Error - cropping will not take place"), Log.LogEntryType.Warning);
                        /*_error = true; // Crop detection failing is not a show stopper
                        return;*/
                    }
                    else if ((!String.IsNullOrEmpty(mencoderCropDetect.CropString)) && (mencoderCropDetect.CropHeight > 0) && (mencoderCropDetect.CropWidth > 0))
                    {
                        _cropString = mencoderCropDetect.CropString;
                        _cropWidth = Util.MathLib.RoundOff(mencoderCropDetect.CropWidth, 2);    //Rounding to nearest 2
                        _cropHeight = Util.MathLib.RoundOff(mencoderCropDetect.CropHeight, 2);
                        jobLog.WriteEntry(this, "Crop String : " + _cropString + ", Crop Height " + _cropHeight + ", Crop Width " + _cropWidth, Log.LogEntryType.Debug);
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
            get { return audioStream; }
        }

        public int AudioTrack
        {
            get { return audioTrack; }
        }

        public int VideoStream
        {
            get { return videoStream; }
        }

        public int AudioPID
        {
            get { return audioPID; }
        }

        public int VideoPID
        {
            get { return videoPID; }
        }

        public FFmpegMediaInfo FFMPEGStreamInfo
        {
            get { return ffmpegStreamInfo; }
        }

        public FFmpegMediaInfo OriginalVideoFFMPEGStreamInfo
        {
            get { return originalFileFFmpegStreamInfo; }
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
