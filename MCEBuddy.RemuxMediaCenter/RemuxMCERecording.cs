using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

using MCEBuddy.AppWrapper;
using MCEBuddy.Globals;
using MCEBuddy.Util;
using MCEBuddy.VideoProperties;

namespace MCEBuddy.RemuxMediaCenter
{
    public class RemuxMCERecording
    {
        private string _RecordingFile;
        private string _RemuxedFile;
        private ExtractWithGraph _extract;
        protected JobStatus _jobStatus;
        protected Log _jobLog;
        private string _destinationPath;
        private string _requestedAudioLanguage = "";
        private bool _useRemuxsupp = false;
        private bool _allowH264CopyRemuxing = false;
        private bool _forceWTVStreamsRemuxing = false;
        private string _tivoMAKKey = "";

        public RemuxMCERecording(ConversionJobOptions cjo, ref JobStatus jobStatus, Log jobLog)
        {
            _jobStatus = jobStatus;
            _jobLog = jobLog;
            _RecordingFile = cjo.sourceVideo;
            _destinationPath = cjo.workingPath;
            _requestedAudioLanguage = cjo.audioLanguage;
            _tivoMAKKey = cjo.tivoMAKKey;

            if (Util.FilePaths.CleanExt(_RecordingFile) == ".ts") // Handle TS files difference since they will have the same namess
                _RemuxedFile = Path.Combine(_destinationPath, Path.GetFileNameWithoutExtension(_RecordingFile) + "-REMUXED.ts");
            else
                _RemuxedFile = Path.Combine(_destinationPath, Path.GetFileNameWithoutExtension(_RecordingFile) + ".ts");

            // Read various profile parameters
            Ini configProfileIni = new Ini(GlobalDefs.ProfileFile);
            _useRemuxsupp = configProfileIni.ReadBoolean(cjo.profile, "UseWTVRemuxsupp", false); // Some videos fail with FFMPEG remuxing and Mencoder encoding (use remuxsupp for remuxing there)
            _jobLog.WriteEntry(this, "Force Remuxsupp (UseWTVRemuxsupp) : " + _useRemuxsupp.ToString(CultureInfo.InvariantCulture), Log.LogEntryType.Debug);

            _forceWTVStreamsRemuxing = configProfileIni.ReadBoolean(cjo.profile, "ForceWTVStreamsRemuxing", false); // Use Streams remuxing for DVRMS and WTV files
            _jobLog.WriteEntry(this, "Force Streams Remuxing : " + _forceWTVStreamsRemuxing.ToString(CultureInfo.InvariantCulture), Log.LogEntryType.Debug);

            _allowH264CopyRemuxing = configProfileIni.ReadBoolean(cjo.profile, "AllowH264CopyRemuxing", false); // Allow H.264 files to be remuxed into TS without recoding to MPEG2
            _jobLog.WriteEntry(this, "Allow H264 Copy Remuxing : " + _allowH264CopyRemuxing.ToString(CultureInfo.InvariantCulture), Log.LogEntryType.Debug);

            if (_allowH264CopyRemuxing)
                if (cjo.commercialRemoval == CommercialRemovalOptions.Comskip)
                    _jobLog.WriteEntry(this, "AllowH264CopyRemuxing is NOT compatible with the bundled Comskip and will cause the conversion to FAIL. Use ShowAnalyzer or Comskip Donator version or no commercial removal", Log.LogEntryType.Warning);
        }

        public string RemuxedFile
        {
            get { return _RemuxedFile; }
        }

        /// <summary>
        /// Fixed the name of the remuxed file to remove the -REMUXED at the end of it if the original file is a TS file
        /// </summary>
        private void FixTSRemuxedFileName()
        {
            if (Util.FilePaths.CleanExt(_RecordingFile) != ".ts")
                return;

            string fixedRemuxedFileName = Path.Combine(_destinationPath, Path.GetFileNameWithoutExtension(_RecordingFile) + ".ts");
            Util.FileIO.TryFileDelete(fixedRemuxedFileName);
            File.Move(_RemuxedFile, fixedRemuxedFileName);
            _RemuxedFile = fixedRemuxedFileName;
        }

        /// <summary>
        /// Used to Remux WTV and DVRMS files to MPEG TS files
        /// </summary>
        /// <returns></returns>
        public bool Remux()
        {
            Util.FileIO.TryFileDelete(RemuxedFile);

            if (Util.FilePaths.CleanExt(_RecordingFile) == ".tivo") // Check for TiVO files
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Attempting to decode and remuxed TiVO file"), Log.LogEntryType.Information);
                _jobStatus.CurrentAction = Localise.GetPhrase("Remuxing TiVO file");
                if (!RemuxTiVO())
                {
                    _jobLog.WriteEntry(this, "Unable to remux TiVo files", Log.LogEntryType.Error);
                    _jobStatus.ErrorMsg = "Unable to remux TiVO";
                    return false;
                }
                else
                    return true;
            }

            // If it's DVRMS first try the special version of FFMPEG, if that fails fall back to regular FFMPEG below
            if (Util.FilePaths.CleanExt(_RecordingFile) == ".dvr-ms")
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Attempting directshow based DVRMS remux, extracting streams"), Log.LogEntryType.Information);
                _jobStatus.CurrentAction = Localise.GetPhrase("Extracting streams");

                // Try a streams based remux
                if (DumpAndRemuxStreams())
                    return true; // this is pretty good, try first
                else
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Streams remuxing failed, trying DVRMS Remuxing"), Log.LogEntryType.Warning);
                    _jobStatus.CurrentAction = Localise.GetPhrase("DVRMS Remuxing");

                    if (RemuxDVRMSFFmpeg()) // Special FFMPEG for DVRMS succeded to create a ts mpeg file
                        return true;
                }
            }
            else if (Util.FilePaths.CleanExt(_RecordingFile) == ".wtv")
            {
                if (_forceWTVStreamsRemuxing) // if we are using H264 copy remuxing then first try DirectShow based streams remuxing
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Force Streams Remuxing, attempting directshow based remux, extracting streams"), Log.LogEntryType.Information);
                    _jobStatus.CurrentAction = Localise.GetPhrase("Extracting streams");
                    if (DumpAndRemuxStreams())
                        return true;
                }

                //TODO: Temp fix, sometimes FFMPEG is not able to read WTV Audio and Video streams, in which case we resort back to ReMuxSupp. Can be removed once FFMPEG is fixed, Ticket #2133
                FFmpegMediaInfo mediaInfo = new FFmpegMediaInfo(_RecordingFile, ref _jobStatus, _jobLog);
                mediaInfo.Run();
                if (mediaInfo.Success && !mediaInfo.ParseError)
                {
                    if ((mediaInfo.AudioTracks < 1) || mediaInfo.MediaInfo.VideoInfo.Stream == -1) // Either no Audio or Video
                        _useRemuxsupp = true;
                }
                else
                    _useRemuxsupp = true; // Can't read the file, could be another issue

                if (_useRemuxsupp)
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Byte stream remuxing"), Log.LogEntryType.Information);
                    _jobStatus.CurrentAction = Localise.GetPhrase("Byte stream remuxing");
                    // Otherwise try a direct byte mux
                    if (RemuxWTVRaw())
                    {
                        return true; //remuxsupp for wtv succeded to create a ts mpeg file
                    }
                }
            }

            //continue to try other fallback methods, try Ffmpeg
            _jobLog.WriteEntry(this, Localise.GetPhrase("Fast Remuxing"), Log.LogEntryType.Information);
            _jobStatus.CurrentAction = Localise.GetPhrase("Fast Remuxing");
            if (RemuxFFmpeg()) //trying as a fallback directly conversion from WTV/DVRMS to TS using FFMPEG, no framerate conversion
            {
                _jobStatus.ErrorMsg = ""; // Reset error message since the fallback option has suceeded
                FixTSRemuxedFileName(); // Fix the file name if source file is TS
                return true;
            }

            // One last try for WTV files, if FFMPEG fails (still not 100% stable)
            if (Path.GetExtension(_RecordingFile).ToLower() == ".wtv") // Try remuxing at the byte level next with remuxsupp
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Byte stream remuxing"), Log.LogEntryType.Information);
                _jobStatus.CurrentAction = Localise.GetPhrase("Byte stream remuxing");
                // Otherwise try a direct byte mux
                if (RemuxWTVRaw())
                {
                    return true; //remuxsupp for wtv succeded to create a ts mpeg file
                }
            }

            // Extract the audio and video streams to files
            _jobLog.WriteEntry(this, Localise.GetPhrase("Attempting directshow based remux, extracting streams"), Log.LogEntryType.Information);
            _jobStatus.CurrentAction = Localise.GetPhrase("Extracting streams");
            bool res = DumpAndRemuxStreams();
            if (!res)
            {
                _jobLog.WriteEntry(this, "DirectShow unable to remux streams", Log.LogEntryType.Error);
                _jobStatus.ErrorMsg = "DirectShow unable to remux";
                return false;
            }

            return true;
        }


        private bool RemuxTiVO()
        {
            Util.FileIO.TryFileDelete(RemuxedFile);

            _jobLog.WriteEntry(this, "Remuxing TiVO file using TiVODecode", Log.LogEntryType.Information);

            string tivoRemuxParams = "";
            string mpgFile = Path.Combine(_destinationPath, Path.GetFileNameWithoutExtension(_RecordingFile) + ".mpg"); // Intermediate file from TiVODecode

            if (String.IsNullOrWhiteSpace(_tivoMAKKey))
            {
                _jobLog.WriteEntry(this, "No TiVO MAK key found", Log.LogEntryType.Error);
                _jobStatus.ErrorMsg = "No TiVO MAK key found";
                return false;
            }

            tivoRemuxParams += "-m " + _tivoMAKKey + " -o " + Util.FilePaths.FixSpaces(mpgFile) + " " + Util.FilePaths.FixSpaces(_RecordingFile);
            TiVODecode tivoDecode = new TiVODecode(tivoRemuxParams, ref _jobStatus, _jobLog);
            tivoDecode.Run();

            if ((Util.FileIO.FileSize(mpgFile) <= 0) || !tivoDecode.Success)
            {
                Util.FileIO.TryFileDelete(mpgFile);
                _jobLog.WriteEntry(this, "TiVODecode failed to remux to mpg", Log.LogEntryType.Error);
                _jobStatus.ErrorMsg = "TiVODecode unable to remux";
                return false;
            }

            _jobLog.WriteEntry(this, "Remuxing TiVO MPG file to TS", Log.LogEntryType.Information);

            // Use the FFMPEGRemux to remux the MPG to TS
            _RecordingFile = mpgFile; // repoint the file
            if (!RemuxFFmpeg())
            {
                Util.FileIO.TryFileDelete(mpgFile);
                Util.FileIO.TryFileDelete(_RemuxedFile);
                _jobLog.WriteEntry(this, "TiVO ffmpeg failed to remux mpg to ts", Log.LogEntryType.Error);
                _jobStatus.ErrorMsg = "TiVO ffmpeg unable to remux";
                return false;
            }

            return RemuxedFileOK();
        }

        /// <summary>
        /// Uses RemuxSupp for Remux WTV files to TS files
        /// Checks for 0 channel audio and re-remuxes it with the selected language
        /// </summary>
        /// <returns>Success or Failure</returns>
        private bool RemuxWTVRaw()
        {
            string RemuxToolParameters = "";

            RemuxToolParameters = "-i " + Util.FilePaths.FixSpaces(_RecordingFile) + " -o " + Util.FilePaths.FixSpaces(_RemuxedFile) + " -all"; // copy all Audio Streams, we'll select them later

            string tempFileName = "";
            if (Util.Text.ContainsUnicode(_RecordingFile))
            {
                // Fix for Unicode source WTV files
                string baseName = Path.Combine(_destinationPath, "TemporaryRemuxFile");
                tempFileName = baseName + Path.GetExtension(_RecordingFile);
                _RemuxedFile = baseName + ".ts";
                try
                {
                    File.Copy(_RecordingFile, tempFileName);
                }
                catch (Exception ex)
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Unable to copy temporary remux file") + "\n\r" + ex.Message, Log.LogEntryType.Error);
                    _jobStatus.ErrorMsg = "Unable to copy Temp remux file";
                    return false;
                }

                if (_requestedAudioLanguage == "")
                    RemuxToolParameters = "-i " + Util.FilePaths.FixSpaces(tempFileName) + " -o " + Util.FilePaths.FixSpaces(_RemuxedFile) + " -lang " + Localise.ThreeLetterISO();
                else
                    RemuxToolParameters = "-i " + Util.FilePaths.FixSpaces(tempFileName) + " -o " + Util.FilePaths.FixSpaces(_RemuxedFile) + " -all"; // Copy all audio streams
            }

            RemuxSupp remuxsupp = new RemuxSupp(RemuxToolParameters, ref _jobStatus, _jobLog);
            remuxsupp.Run();

            if (tempFileName != "") Util.FileIO.TryFileDelete(tempFileName);

            if (!(remuxsupp.Success && RemuxedFileOK())) //remux succedded and file exists and no zero channel audio track
            {
                if (CheckForNoneOrZeroChannelAudioTrack(RemuxedFile))
                {
                    // Found a 0 channel audio, now try once more with just the language required
                    _jobLog.WriteEntry(this, Localise.GetPhrase("RemuxSupp found 0 channel audio, trying again with language selection") + " : " + Localise.ThreeLetterISO(), Log.LogEntryType.Warning);
                    _jobStatus.CurrentAction = Localise.GetPhrase("Re-ReMuxing due to audio error");

                    RemuxToolParameters = "-i " + Util.FilePaths.FixSpaces(_RecordingFile) + " -o " + Util.FilePaths.FixSpaces(_RemuxedFile) + " -lang " + Localise.ThreeLetterISO();

                    tempFileName = "";
                    if (Util.Text.ContainsUnicode(_RecordingFile))
                    {
                        // Fix for Unicode source WTV files
                        string baseName = Path.Combine(_destinationPath, "TemporaryRemuxFile");
                        tempFileName = baseName + Path.GetExtension(_RecordingFile);
                        _RemuxedFile = baseName + ".ts";
                        try
                        {
                            Util.FileIO.TryFileDelete(tempFileName); // incase it exists from before
                            File.Copy(_RecordingFile, tempFileName);
                        }
                        catch (Exception ex)
                        {
                            _jobStatus.PercentageComplete = 0;
                            _jobLog.WriteEntry(this, Localise.GetPhrase("Unable to copy temporary remux file") + "\n\r" + ex.Message, Log.LogEntryType.Error);
                            _jobStatus.ErrorMsg = "Unable to copy Temp remux file";
                            return false;
                        }

                        RemuxToolParameters = "-i " + Util.FilePaths.FixSpaces(tempFileName) + " -o " + Util.FilePaths.FixSpaces(_RemuxedFile) + " -lang " + Localise.ThreeLetterISO();
                    }

                    remuxsupp = new RemuxSupp(RemuxToolParameters, ref _jobStatus, _jobLog);
                    remuxsupp.Run();
                    
                    if (tempFileName != "") Util.FileIO.TryFileDelete(tempFileName);

                    if (!(remuxsupp.Success && RemuxedFileOK())) //remux succedded and file exists and no zero channel audio
                    {
                        _jobStatus.PercentageComplete = 0;
                        _jobStatus.ErrorMsg = Localise.GetPhrase("ReMuxxSupp failed");
                        _jobLog.WriteEntry(this, (_jobStatus.ErrorMsg), Log.LogEntryType.Error);
                        return false;
                    }
                }
                else
                {
                    _jobStatus.ErrorMsg = Localise.GetPhrase("ReMuxxSupp failed");
                    _jobLog.WriteEntry(this, (_jobStatus.ErrorMsg), Log.LogEntryType.Error);
                    return false;
                }
            }
            
            // All's good
            return true;
        }

        /// <summary>
        /// Remux the DVRMS file directly to TSMPEG using a special FFMPEG
        /// </summary>
        /// <returns>Success or Failure</returns>
        private bool RemuxDVRMSFFmpeg()
        {
            string profile = "DVRMSRemux"; // Reading the remux parameters from the file
            string ffmpegParams = "";
            Ini configProfileIni = new Ini(GlobalDefs.ConfigFile);

            // Threads 0 causes an error in some streams, avoid
            _jobLog.WriteEntry(this, Localise.GetPhrase("DVRMS file, using special FFMPEG to remux"), Log.LogEntryType.Information);

            // First try with regular FFMPEG and GenPTs, since the DVRMS tends to created corrupted files
            _jobStatus.CurrentAction = Localise.GetPhrase("Fast Remuxing");

            _jobLog.WriteEntry(this, Localise.GetPhrase("Reading DVRMS Remux parameters"), Log.LogEntryType.Information);

            // First try to copy all the streams directly (read parmeters for copy profile)
            string baseRemuxParams = configProfileIni.ReadString(profile, "Remux", "");

            if (String.IsNullOrWhiteSpace(baseRemuxParams)) // Have we used up all the CopyRemux profiles, then we're done here - try something else
            {
                _jobLog.WriteEntry(Localise.GetPhrase("DVRMS Remuxing disabled in config file, no parameters"), Log.LogEntryType.Error);
                Util.FileIO.TryFileDelete(RemuxedFile);
                _jobStatus.ErrorMsg = "DVRMS ReMux, no paramters in config file";
                return false;
            }

            // Check for input file placeholders and substitute
            if (baseRemuxParams.Contains("-i <source>")) // Check if we have a input file placeholder (useful if we need to put commands before -i)
            {
                baseRemuxParams = baseRemuxParams.Replace("-i <source>", "-i " + Util.FilePaths.FixSpaces(_RecordingFile) + " ");
                ffmpegParams = "-fflags +genpts -y " + baseRemuxParams + " " + Util.FilePaths.FixSpaces(RemuxedFile);
            }
            else
                ffmpegParams = "-fflags +genpts -y -i " + Util.FilePaths.FixSpaces(_RecordingFile) + " " + baseRemuxParams + " " + Util.FilePaths.FixSpaces(RemuxedFile); // DO NOT USE -async 1 with COPY

            FFmpeg ffmpeg = new FFmpeg(ffmpegParams, ref _jobStatus, _jobLog);
            ffmpeg.Run();

            if (!ffmpeg.Success || !RemuxedFileOK()) //check of file is created, outputhandler reports success (Percentage not requires since Success is more accurate)
            {
                _jobLog.WriteEntry(Localise.GetPhrase("DVRMS ReMux using FFMPEG GenPTS failed at") + " " + _jobStatus.PercentageComplete.ToString(CultureInfo.InvariantCulture) + "%. Retrying using special DVRMS FFMpeg", Log.LogEntryType.Warning);

                // DVR-MS supports only one audio stream
                ffmpegParams = ffmpegParams.Replace("-fflags +genpts", ""); // don't need genpts for special build dvrms ffmpeg

                ffmpeg = new FFmpeg(ffmpegParams, true, ref _jobStatus, _jobLog); // use special build dvrms-ffmpeg
                ffmpeg.Run();

                if (!ffmpeg.Success || !RemuxedFileOK()) //check of file is created, outputhandler reports success (Percentage not requires since Success is more accurate)
                {
                    _jobLog.WriteEntry(Localise.GetPhrase("DVRMS ReMux using FFMPEG failed at") + " " + _jobStatus.PercentageComplete.ToString(CultureInfo.InvariantCulture) + "%", Log.LogEntryType.Error);
                    Util.FileIO.TryFileDelete(RemuxedFile);
                    _jobStatus.ErrorMsg = "DVRMS ReMux using FFMPEG failed";
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Remux the source recorded file using the specified base parameters, but also check if the source file (or Remux file) has any zero channel audio streams in it.
        /// If so it tries to remux using FFMPEG and the given parameters but compensating to keep only one audio channel
        /// It also checks to see if it can locate the user identified audio language within the source recorded file if required
        /// </summary>
        /// <param name="baseRemuxParams">Base parameters used for remuxing</param>
        /// <param name="FPS">Frame rate for the Recorded file</param>
        /// <param name="fixRemuxedFile">True if Remuxed file file needs to be FIXED for Zero Channel Audio, false if Recorded file needs to be checked and then fixed</param>
        /// <returns>True if there are no zero channel audio tracks or on a successful remux</returns>
        private bool FFMPEGRemuxZeroChannelFix(string baseRemuxParams, float FPS, bool fixRemuxedFile=false)
        {
            FFmpegMediaInfo ffmpegStreamInfo;
            bool autoFPS = false; // Used to check if Auto FPS was used

            _jobLog.WriteEntry(this, "Verifying " + (fixRemuxedFile ? "Remuxed" : "Recorded") + " file audio streams for Zero Channel Audio", Log.LogEntryType.Debug);

            // Read the Audio Stream Info to isolate the correct tracks if possible
            ffmpegStreamInfo = new FFmpegMediaInfo(_RecordingFile, ref _jobStatus, _jobLog);
            ffmpegStreamInfo.Run();

            // Compensate for FFMPEG bug #2227 where the mjpeg is identified as a video stream hence breaking -map 0:v, rather replace 'v' with the actual video stream number
            if (ffmpegStreamInfo.Success && !ffmpegStreamInfo.ParseError)
            {
                if (baseRemuxParams.Contains("-map 0:v"))
                    baseRemuxParams = baseRemuxParams.Replace("-map 0:v", "-map 0:" + ffmpegStreamInfo.MediaInfo.VideoInfo.Stream.ToString(CultureInfo.InvariantCulture)); // replace 0:v with the actual video stream number
            }
            else
            {
                _jobLog.WriteEntry(this, "Error reading audio streams, removing support for audio and video stream selection", Log.LogEntryType.Warning);
                baseRemuxParams = Regex.Replace(baseRemuxParams, @"-map 0:.", ""); // Remove all patterns like -map 0:v or -map 0:4 since we cannot read ffmpeg stream info
            }

            // We have a 0 channel audio we try to compensate for it by selecting the appropriate audio channel
            if (CheckForNoneOrZeroChannelAudioTrack((fixRemuxedFile ? RemuxedFile : _RecordingFile)))
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Found 0 channel audio while remuxing, re-remuxing using a single audio track"), Log.LogEntryType.Information);
                _jobStatus.CurrentAction = Localise.GetPhrase("Re-ReMuxing due to audio error");

                // DO NOT USE MAP ALL commands, we only need to copy one audio and one video stream
                baseRemuxParams = Regex.Replace(baseRemuxParams, @"-map 0:.", ""); // Remove all patterns like -map 0:v or -map 0:4

                if (ffmpegStreamInfo.Success && !ffmpegStreamInfo.ParseError)
                {
                    // Audio Lanauge - find the best Audio channel and try to get language support if possible
                    bool foundLang = false;
                    int audioChannels = 0;
                    int audioStream = 0;
                    int videoStream = 0;
                    bool selectedAudioImpaired = false;

                    if (!String.IsNullOrEmpty(_requestedAudioLanguage))
                    {
                        for (int i = 0; i < ffmpegStreamInfo.AudioTracks; i++)
                        {
                            // Language selection check, if the user has picked a specific language code, look for it
                            // If we find a match, we look the one with the highest number of channels in it
                            if ((ffmpegStreamInfo.MediaInfo.AudioInfo[i].Language.ToLower() == _requestedAudioLanguage) && (ffmpegStreamInfo.MediaInfo.AudioInfo[i].Channels > 0))
                            {
                                if (foundLang)
                                    if (!( // take into account impaired tracks (since impaired tracks typically have no audio)
                                        ((ffmpegStreamInfo.MediaInfo.AudioInfo[i].Channels > audioChannels) && !ffmpegStreamInfo.MediaInfo.AudioInfo[i].Impaired) || // PREFERENCE to non-imparied Audio tracks with the most channels
                                        ((ffmpegStreamInfo.MediaInfo.AudioInfo[i].Channels > audioChannels) && selectedAudioImpaired) || // PREFERENCE to Audio tracks with most channels if currently selected track is impaired
                                        (!ffmpegStreamInfo.MediaInfo.AudioInfo[i].Impaired && selectedAudioImpaired) // PREFER non impaired audio over currently selected impaired
                                        ))
                                            continue; // we have found a lang match, now we are looking for more channels only now

                                audioChannels = ffmpegStreamInfo.MediaInfo.AudioInfo[i].Channels;
                                audioStream = ffmpegStreamInfo.MediaInfo.AudioInfo[i].Stream; // store the stream number for the selected audio channel
                                string audioCodec = ffmpegStreamInfo.MediaInfo.AudioInfo[i].AudioCodec;
                                int audioTrack = i; // Store the audio track number we selected
                                string audioLanguage = ffmpegStreamInfo.MediaInfo.AudioInfo[i].Language.ToLower(); // this is what we selected
                                foundLang = true; // We foudn the language we were looking for
                                _jobLog.WriteEntry(this, Localise.GetPhrase("Found Audio Language match for language") + " " + _requestedAudioLanguage.ToUpper() + ", " + Localise.GetPhrase("Audio Stream") + " " + audioStream.ToString(CultureInfo.InvariantCulture) + ", " + Localise.GetPhrase("Audio Track") + " " + audioTrack.ToString(CultureInfo.InvariantCulture) + ", " + Localise.GetPhrase("Channels") + " " + audioChannels.ToString(CultureInfo.InvariantCulture) + ", " + Localise.GetPhrase("Codec") + "->" + audioCodec + ", Audio Impaired->" + selectedAudioImpaired.ToString(), Log.LogEntryType.Debug);
                            }

                            // Store the video information (there's only 1 video per file)
                            videoStream = ffmpegStreamInfo.MediaInfo.VideoInfo.Stream;
                        }
                    }

                    // If we have a found a suitable language, select it else let FFMPEG select it automatically
                    if (foundLang)
                        baseRemuxParams += " -map 0:" + audioStream.ToString(CultureInfo.InvariantCulture) + " -map 0:" + videoStream.ToString(CultureInfo.InvariantCulture); // Select the Audiotrack we had isolated earlier
                    else
                        _jobLog.WriteEntry(this, "No audio language match found, letting encoder choose best audio language", Log.LogEntryType.Warning);
                }
                else
                    _jobLog.WriteEntry(this, "Error reading audio streams, letting encoder choose best audio language", Log.LogEntryType.Warning);
            }

            // Build the command line to remux the file now
            string ffmpegParams = "";

            // Check for auto frame rate and replace with video framerate
            if (baseRemuxParams.Contains("-r auto"))
            {
                if (FPS != 0)
                {
                    _jobLog.WriteEntry(this, "Detected Auto FPS request, setting frame rate to " + FPS.ToString(CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
                    baseRemuxParams = baseRemuxParams.Replace("-r auto", "-r " + FPS.ToString(CultureInfo.InvariantCulture));
                    autoFPS = true;
                }
                else
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Cannot read frame rate from file, skipping frame rate adjustment"), Log.LogEntryType.Warning);
                    baseRemuxParams = baseRemuxParams.Replace("-r auto", ""); // no framerate since we can't read it
                    autoFPS = false;
                }
            }
            else
                autoFPS = false;

            // Check for input file placeholders and substitute
            if (baseRemuxParams.Contains("-i <source>")) // Check if we have a input file placeholder (useful if we need to put commands before -i)
            {
                baseRemuxParams = baseRemuxParams.Replace("-i <source>", "-i " + Util.FilePaths.FixSpaces(_RecordingFile) + " ");
                ffmpegParams = "-y " + baseRemuxParams + " " + Util.FilePaths.FixSpaces(RemuxedFile);
            }
            else
                ffmpegParams = "-y -i " + Util.FilePaths.FixSpaces(_RecordingFile) + " " + baseRemuxParams + " " + Util.FilePaths.FixSpaces(RemuxedFile); // DO NOT USE -async 1 with COPY

            FFmpeg ffmpeg = new FFmpeg(ffmpegParams, ref _jobStatus, _jobLog);
            ffmpeg.Run();
            if (!ffmpeg.Success || !RemuxedFileOK()) //check of file is created, outputhandler reports success (Percentage not requires since Success is more accurate)
            {
                // We have a 0 channel audio in remuxed file we try to compensate for it
                if (CheckForNoneOrZeroChannelAudioTrack(RemuxedFile))
                    if (!fixRemuxedFile) // avoid infinite loop, fix remuxed file only if we started out checking the recorded file
                        return FFMPEGRemuxZeroChannelFix(baseRemuxParams, FPS, true); // Call ZeroChannelAudioFix this time to fix the remuxed file

                // Otherwise it might an error related to genpts
                if (!ffmpegParams.Contains("genpts")) // Possible that some combinations used prior to calling this already have genpts in the command line
                {
                    _jobLog.WriteEntry("ZeroChannelCheckRemuxing failed, retying using GenPts", Log.LogEntryType.Warning);

                    // genpt is required sometimes when -ss is specified before the inputs file, see ffmpeg ticket #2054
                    ffmpegParams = "-fflags +genpts " + ffmpegParams;
                    ffmpeg = new FFmpeg(ffmpegParams, ref _jobStatus, _jobLog);
                    ffmpeg.Run();
                }

                if (!ffmpeg.Success || !RemuxedFileOK()) //check of file is created, outputhandler reports success (Percentage not requires since Success is more accurate)
                {
                    // We have a 0 channel audio in remuxed file we try to compensate for it
                    if (CheckForNoneOrZeroChannelAudioTrack(RemuxedFile))
                        if (!fixRemuxedFile) // avoid infinite loop, fix remuxed file only if we started out checking the recorded file
                            return FFMPEGRemuxZeroChannelFix(baseRemuxParams, FPS, true); // Call ZeroChannelAudioFix this time to fix the remuxed file

                    _jobLog.WriteEntry(Localise.GetPhrase("0 Channel ReMux using FFMPEG failed at") + " " + _jobStatus.PercentageComplete.ToString(CultureInfo.InvariantCulture) + "%", Log.LogEntryType.Error);
                    Util.FileIO.TryFileDelete(RemuxedFile);
                    _jobStatus.ErrorMsg = "0 Channel ReMux using FFMPEG failed";
                    return false;
                }
            }

            // Remux succeeded, check for Dropped or Duplicate packets due to incorrect FPS
            _jobLog.WriteEntry("Average rate of dropped frames :" + " " + ffmpeg.AverageDropROC.ToString("#0.00", CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
            _jobLog.WriteEntry("Average rate of duplicate frames :" + " " + ffmpeg.AverageDupROC.ToString("#0.00", CultureInfo.InvariantCulture), Log.LogEntryType.Debug);

            // Read ReMux Parameters from Config Profile
            Ini configProfileIni = new Ini(GlobalDefs.ConfigFile);
            string profile = "FFMpegBackupRemux"; // This is where the Fallback Remux parameters are stored
            
            // Read the Drop frame threshhold
            double dropThreshold = double.Parse(configProfileIni.ReadString(profile, "RemuxDropThreshold", "3.0"), CultureInfo.InvariantCulture);

            // Read the Duplicate frame threshhold
            double dupThreshold = double.Parse(configProfileIni.ReadString(profile, "RemuxDuplicateThreshold", "3.0"), CultureInfo.InvariantCulture);

            if ((ffmpeg.AverageDropROC > dropThreshold) || (ffmpeg.AverageDupROC > dupThreshold))
            {
                if (autoFPS) // Check if we used AutoFPS and also if this isn't a going into an infinite loop
                    _jobLog.WriteEntry(Localise.GetPhrase("Remuxed file has too many dropped or duplicate frames, try to manually set the frame rate. Auto FPS used ->") + " " + FPS.ToString(CultureInfo.InvariantCulture), Log.LogEntryType.Warning);
                else
                    _jobLog.WriteEntry(Localise.GetPhrase("Remuxed file has too many dropped or duplicate frames, check/set the manual remux frame rate"), Log.LogEntryType.Warning);
            }

            return true; // All done here
        }

        /// <summary>
        /// Remux the WTV file directly to TSMPEG using FFMPEG
        /// Uses 3 levels of remuxing, stream copy, video transcode and video+audio transcode with support for Auto FPS detection or manual FPS override
        /// Sets the fixCorruptedRemux flag is it falls back to transcodes the video and audio using remux
        /// </summary>
        /// <returns>Success or Failure</returns>
        private bool RemuxFFmpeg()
        {
            // TODO: WTV comes in mpeg2 and h.264 formats, we need to convert output to mpeg2ts format for comskip to work (comskip does not current support h.264, when it does we can do a directly copy)
            // Some WTV/DVRMS files are corrupted/malformed where the video DTS and PTS stamps are out of sync which causes FFMPEG to fail stream copying. The solution is to transcode which creates new timestamps and fixes the problems. Slower but effective.
            //string ffmpegParams = "-y -i " + Util.FilePaths.FixSpaces(_RecordingFile) + " -map 0:a -map 0:v -vcodec copy -acodec copy " + Util.FilePaths.FixSpaces(RemuxedFile);
            float FPS = 0;
            bool skipCopyRemux = false; // Do we need to skip stream copy

            // Read ReMux Parameters from Config Profile
            Ini configProfileIni = new Ini(GlobalDefs.ConfigFile);
            string profile = "FFMpegBackupRemux"; // This is where the Fallback Remux parameters are stored

            // MediaInfo is more reliable than FFMPEG but it doesn't always succeed
            _jobLog.WriteEntry(this, "Reading MediaInfo from " + _RecordingFile, Log.LogEntryType.Debug);
            MediaInfoDll mi = new MediaInfoDll();

            try
            {
                mi.Open(_RecordingFile);

                mi.Option("Inform", "Video; %FrameRate%");
                float.TryParse(mi.Inform(), NumberStyles.Float, CultureInfo.InvariantCulture, out FPS);
                _jobLog.WriteEntry(this, "Video FPS : " + FPS.ToString(CultureInfo.InvariantCulture), Log.LogEntryType.Debug);

            }
            catch (Exception ex)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Error reading media information using MediaInfo") + " " + ex.Message, Log.LogEntryType.Warning);
                FPS = 0; // Reset it
            }

            _jobLog.WriteEntry(this, "Reading FFMPEG info from " + _RecordingFile, Log.LogEntryType.Debug);

            FFmpegMediaInfo ffmpegStreamInfo = new FFmpegMediaInfo(_RecordingFile, ref _jobStatus, _jobLog);
            ffmpegStreamInfo.Run();

            if (FPS == 0) // MediaInfo did not succeed
            {
                if (ffmpegStreamInfo.Success && !ffmpegStreamInfo.ParseError)
                    FPS = ffmpegStreamInfo.MediaInfo.VideoInfo.FPS; // Get the FPS
                else
                    _jobLog.WriteEntry(this, "ERROR reading FFMPEG Media info from " + _RecordingFile + ", disabling AutoFPS support", Log.LogEntryType.Warning);
            }

            // First check if the video is MPEG2VIDEO, else we need to move on to Slow Remux which will convert the video to MPEG2VIDEO
            // If it's MPEG2VIDEO, we can stream copy it directly
            if (ffmpegStreamInfo.Success && !ffmpegStreamInfo.ParseError)
            {
                if ((ffmpegStreamInfo.MediaInfo.VideoInfo.VideoCodec.ToLower() != "mpeg2video") && !_allowH264CopyRemuxing) // If we remuxing H264 in TS Remuxing, then don't skip copy remuxing
                {
                    _jobLog.WriteEntry(this, "Video does not contain MPEG2VIDEO, skipping to slow remux. Video Codec found -> " + ffmpegStreamInfo.MediaInfo.VideoInfo.VideoCodec.ToLower(), Log.LogEntryType.Debug);
                    skipCopyRemux = true;
                }
            }

            if (!skipCopyRemux)
            {
                int profileCount = 0;
                while (true) // We do this in a loop until all MPEG2 CopyRemux profiles have been consumed
                {
                    // Check for Multiple Audio Streams
                    // Copy all streams if there is an audio selection specified and we'll extract it later (make sure you use the -map 0:a command to copy ALL audio stream, -acodec copy copies only 1 audio stream withou the map command)
                    // Use coptyb and copyts to avoid invalid DTS errors
                    // Threads 0 causes an error in some streams, avoid
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Copy remux loop ") + profileCount.ToString(CultureInfo.InvariantCulture), Log.LogEntryType.Information);

                    // First try to copy all the streams directly (read parmeters for copy profile)
                    string copyRemuxParams = configProfileIni.ReadString(profile, "CopyRemux" + profileCount.ToString(CultureInfo.InvariantCulture), "");

                    if (String.IsNullOrWhiteSpace(copyRemuxParams)) // Have we used up all the CopyRemux profiles, then we're done here - try something else
                        break;

                    // Some files have zero channel audio tracks in them, if so then copying all audio track will fails
                    // So we check for these tracks and run the remux
                    if (FFMPEGRemuxZeroChannelFix(copyRemuxParams, FPS))
                        break; // We're good, all done

                    profileCount++; // try the next one
                }
            }

            if (!RemuxedFileOK() || skipCopyRemux) //check of file is created, also check if we need to skip the copy remux to slow remux
            {
                _jobStatus.PercentageComplete = 100; // reset it to try again
                _jobStatus.ErrorMsg = ""; // reset it to try again
                _jobStatus.CurrentAction = Localise.GetPhrase("Slow Remuxing");

                int profileCount = 0;
                while (true) // Now loop through all the SlowRemux profiles until we succeed or fail
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Slow remux loop ") + profileCount.ToString(CultureInfo.InvariantCulture), Log.LogEntryType.Information);

                    // Now try to copy audio and transcode video (read parameters for Slow Remux profile)
                    string slowRemuxParams = configProfileIni.ReadString(profile, "SlowRemux"+profileCount.ToString(CultureInfo.InvariantCulture), "");

                    if (String.IsNullOrWhiteSpace(slowRemuxParams)) // Have we used up all the SlowRemux profiles, then we're done here
                        break;

                    // Some files have zero channel audio tracks in them, if so then copying all audio track will fails
                    // So we check for these tracks and run the remux
                    if (FFMPEGRemuxZeroChannelFix(slowRemuxParams, FPS))
                        break; // We're good, all done

                    profileCount++; // Move onto the next profile iteration
                }

                if (!RemuxedFileOK()) //check of file is created
                {
                    _jobLog.WriteEntry(Localise.GetPhrase("Slow Remux using FFMPEG failed at") + " " + _jobStatus.PercentageComplete.ToString(CultureInfo.InvariantCulture) + "%", Log.LogEntryType.Error);
                    Util.FileIO.TryFileDelete(RemuxedFile);
                    _jobStatus.ErrorMsg = "Slow Remux using FFMPEG failed";
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Remux with adjustments and extracted raw stream parts individually using ffmpeg
        /// </summary>
        /// <returns>Success or Failure</returns>
        private bool RemuxRawPartsFFmpeg()
        {
            Util.FileIO.TryFileDelete(RemuxedFile); // Start clean

            // Audio Delay on the original file
            float audioDelay = VideoProperties.RawFile.AudioDelay(_RecordingFile);
            _jobLog.WriteEntry("Audio Delay : " + audioDelay.ToString(CultureInfo.InvariantCulture), Log.LogEntryType.Debug);

            string vcodec = VideoProperties.RawFile.VideoFormat(_extract.VideoPart).ToLower();
            if (String.IsNullOrWhiteSpace(vcodec))
            {
                _jobStatus.ErrorMsg = "No Video Codec detected in raw stream";
                _jobLog.WriteEntry(_jobStatus.ErrorMsg, Log.LogEntryType.Error);
                return false;
            }

            _jobLog.WriteEntry("Video Codec detected : " + vcodec, Log.LogEntryType.Debug);
            /*if ((vcodec == "avc") && (!_allowH264CopyRemuxing)) // Accept H264 direct remuxing only if allowed
            {
                _jobStatus.ErrorMsg = "FFMpegParts incompatible settings, H264 video detected";
                _jobLog.WriteEntry(_jobStatus.ErrorMsg, Log.LogEntryType.Error);
                return false;
            }*/ // There is no other recourse so mux it

            // FPS of the video
            float fps = VideoProperties.RawFile.FPS(_extract.VideoPart);
            if (fps == 0)
            {
                _jobStatus.ErrorMsg = "No FPS detected in raw stream";
                _jobLog.WriteEntry(_jobStatus.ErrorMsg, Log.LogEntryType.Error);
                return false;
            }

            // Generate the timestamps, framerate and specify input format since it's raw video and ffmpeg won't recognize it
            string FFmpegParams = "-fflags +genpts -y -r " + fps.ToString(CultureInfo.InvariantCulture);

            if (audioDelay == 0)
                _jobLog.WriteEntry("Skipping Audio Delay correction, cannnot read", Log.LogEntryType.Warning);

            if (audioDelay > 0) // positive is used on 1st unput
                FFmpegParams += " -itsoffset " + audioDelay.ToString(CultureInfo.InvariantCulture);

            if (vcodec == "avc")
                FFmpegParams += " -f h264";
            else
                FFmpegParams += " -f mpegvideo";
            
            FFmpegParams += " -i " + Util.FilePaths.FixSpaces(_extract.VideoPart); // Video

            if (audioDelay < 0) // Negative is used on second input
                FFmpegParams += " -itsoffset " + (-1 * audioDelay).ToString(CultureInfo.InvariantCulture);

            for (int i = 0; i < _extract.AudioParts.Count; i++)
                FFmpegParams += " -i " + Util.FilePaths.FixSpaces(_extract.AudioParts[i]); // Audio streams

            // Copy all Streams
            FFmpegParams += " -acodec copy -vcodec copy -f mpegts " + Util.FilePaths.FixSpaces(RemuxedFile);

            FFmpeg ffmpeg = new FFmpeg(FFmpegParams, ref _jobStatus, _jobLog);
            ffmpeg.Run();

            if (!ffmpeg.Success) // process was terminated or failed
            {
                _jobStatus.ErrorMsg = "FFmpeg Remux Parts failed";
                _jobLog.WriteEntry(this, _jobStatus.ErrorMsg, Log.LogEntryType.Error);
                return false;
            }

            return RemuxedFileOK(); // check the output for success command and file exists
        }

        /// <summary>
        /// Put the extracting raw streams back together in TS format using TsMuxer
        /// </summary>
        /// <returns>True if successful</returns>
        private bool RemuxRawTSMuxer()
        {
            Util.FileIO.TryFileDelete(RemuxedFile); // Start clean

            // Audio Delay on the original file
            float audioDelay = VideoProperties.RawFile.AudioDelay(_RecordingFile);
            _jobLog.WriteEntry("Audio Delay : " + audioDelay.ToString(CultureInfo.InvariantCulture), Log.LogEntryType.Debug);

            // FPS of the video
            float fps = VideoProperties.RawFile.FPS(_extract.VideoPart);
            if (fps == 0)
            {
                _jobStatus.ErrorMsg = "No FPS detected in raw stream";
                _jobLog.WriteEntry(_jobStatus.ErrorMsg, Log.LogEntryType.Error);
                return false;
            }

            // Setup the TSMuxer - http://forum.doom9.org/archive/index.php/t-142559.html
            string MetaFile = MCEBuddy.Util.FilePaths.GetFullPathWithoutExtension(RemuxedFile) + ".meta";
            string MetaFileContents = "MUXOPT --no-pcr-on-video-pid --new-audio-pes --vbr --vbv-len=500\n";

            // Setup the Video
            string vcodec = VideoProperties.RawFile.VideoFormat(_extract.VideoPart).ToLower();
            if (String.IsNullOrWhiteSpace(vcodec))
            {
                _jobStatus.ErrorMsg = "No Video Codec detected in raw stream";
                _jobLog.WriteEntry(_jobStatus.ErrorMsg, Log.LogEntryType.Error);
                return false;
            }

            _jobLog.WriteEntry("Video Codec detected : " + vcodec, Log.LogEntryType.Debug);
            if (vcodec == "avc")
            {
                /*if (!_allowH264CopyRemuxing)
                {
                    _jobStatus.ErrorMsg = "TsMuxer incompatible settings, H264 video detected";
                    _jobLog.WriteEntry(_jobStatus.ErrorMsg, Log.LogEntryType.Error);
                    return false;
                }*/ // There is no other recourse so mux it

                MetaFileContents += "V_MPEG4/ISO/AVC, " + Util.FilePaths.FixSpaces(_extract.VideoPart) + ", fps=" + fps.ToString(CultureInfo.InvariantCulture) + ", insertSEI, contSPS";
            }
            else
            {
                MetaFileContents += "V_MPEG-2, " + Util.FilePaths.FixSpaces(_extract.VideoPart) + ", fps=" + fps.ToString(CultureInfo.InvariantCulture);
            }

            // Setup the Audio streams
            foreach (string AudioPart in _extract.AudioParts)
            {
                string acodec = VideoProperties.RawFile.AudioFormat(AudioPart).ToLower();
                if (String.IsNullOrWhiteSpace(acodec))
                {
                    _jobStatus.ErrorMsg = "No Audio Codec detected in raw stream";
                    _jobLog.WriteEntry(_jobStatus.ErrorMsg, Log.LogEntryType.Error);
                    return false;
                }

                _jobLog.WriteEntry("Audio Stream Codec detected : " + acodec, Log.LogEntryType.Debug);

                if (acodec.Contains("ac3") || acodec.Contains("ac-3"))
                    MetaFileContents += "\nA_AC3";
                else if (acodec.Contains("aac"))
                    MetaFileContents += "\nA_AAC";
                else if (acodec.Contains("dts"))
                    MetaFileContents += "\nA_DTS";
                else
                    MetaFileContents += "\nA_MP3";

                MetaFileContents += ", " + Util.FilePaths.FixSpaces(AudioPart);

                if (audioDelay == 0)
                    _jobLog.WriteEntry("Skipping Audio Delay correction, cannnot read", Log.LogEntryType.Warning);
                else
                    MetaFileContents += ", timeshift=" + (-1 * audioDelay).ToString(CultureInfo.InvariantCulture) + "s";

                MetaFileContents += "\n";
            }

            try
            {
                _jobLog.WriteEntry(this, "Writing TsMuxer Meta commands : \r\n" + MetaFileContents + "\r\nTo : " + MetaFile, Log.LogEntryType.Debug);
                System.IO.File.WriteAllText(MetaFile, MetaFileContents);
            }
            catch (Exception e)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("ReMuxTsMuxer failed to write Metafile...") + "\r\nError : " + e.ToString(), Log.LogEntryType.Error);
                _jobStatus.ErrorMsg = "ReMuxTsMuxer failed";
                return false;
            }

            AppWrapper.TSMuxer tsmuxer = new TSMuxer(Util.FilePaths.FixSpaces(MetaFile) + " " + Util.FilePaths.FixSpaces(RemuxedFile), ref _jobStatus, _jobLog);
            tsmuxer.Run();

            Util.FileIO.TryFileDelete(MetaFile);

            if (!tsmuxer.Success) // process was terminated or failed
            {
                _jobStatus.ErrorMsg = "ReMuxTsMuxer failed";
                _jobLog.WriteEntry(this, _jobStatus.ErrorMsg, Log.LogEntryType.Error);
                return false;
            }

            return RemuxedFileOK();
        }

        /// <summary>
        /// Extract the Audio and Video Streams from the file using Windows DirectShow Graph Filter and then remux using TSMuxer and FFMPEG.
        /// Currently only support extracting 1 audio stream, so a good option for DVRMS (backup for WTV)
        /// </summary>
        /// <returns>True if successful in Remuxing to TS</returns>
        private bool DumpAndRemuxStreams()
        {
            if (!((Util.FilePaths.CleanExt(_RecordingFile) == ".wtv") || (Util.FilePaths.CleanExt(_RecordingFile) == ".dvr-ms")))
            {
                _jobStatus.ErrorMsg = "DirectShow Streams remuxing only supported for WTV and DVR-MS files";
                _jobLog.WriteEntry(this, _jobStatus.ErrorMsg, Log.LogEntryType.Error);
                return false;
            }

            try
            {
                _jobLog.WriteEntry("Extracting with Graph....", Log.LogEntryType.Debug);
                _extract = new ExtractWithGraph(_RecordingFile, _destinationPath, ref _jobStatus);
            }
            catch (Exception e)
            {
                try
                {
                    if (_extract != null) _extract.Dispose();
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Error Extracting with Graph....") + "\r\nError : " + e.ToString(), Log.LogEntryType.Error, true);
                }
                catch (Exception e1)
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Error disposing objects after graph failure....") + "\r\nError : " + e1.ToString(), Log.LogEntryType.Error, true);
                }

                _jobStatus.ErrorMsg = "Error Extracting Graph";
                return false;
            }

            try
            {
                _jobLog.WriteEntry("Building with Graph....", Log.LogEntryType.Debug);
                _extract.BuildGraph(); // TODO: Currently this only returns 1 audio stream and doesn't extract other audio streams
            }
            catch (Exception e)
            {
                try
                {
                    _extract.DeleteParts();
                    _extract.Dispose();
                    _jobStatus.ErrorMsg = Localise.GetPhrase("Error building graph");
                    _jobLog.WriteEntry(this, (_jobStatus.ErrorMsg + "\r\nError : " + e.ToString()), Log.LogEntryType.Error);
                    return false;
                }
                catch
                {
                    _jobStatus.ErrorMsg = Localise.GetPhrase("Error building graph");
                    _jobLog.WriteEntry(this, (_jobStatus.ErrorMsg), Log.LogEntryType.Error);
                    return false;
                }
            }

            // Atleast 1 audio and 1 video channel should be there
            if (String.IsNullOrWhiteSpace(_extract.VideoPart) || (_extract.AudioParts.Count < 1))
            {
                try
                {
                    _extract.DeleteParts();
                    _extract.Dispose();
                    _jobStatus.ErrorMsg = Localise.GetPhrase("Graph invalid number of video/audio streams");
                    _jobLog.WriteEntry(this, (_jobStatus.ErrorMsg), Log.LogEntryType.Error);
                    return false;
                }
                catch (Exception e1)
                {
                    _jobStatus.ErrorMsg = Localise.GetPhrase("Invalid Graph audio/video streams disposing");
                    _jobLog.WriteEntry(this, (_jobStatus.ErrorMsg) + "\r\nError : " + e1.ToString(), Log.LogEntryType.Error);
                    return false;
                }
            }

            try
            {
                _jobLog.WriteEntry("Running with Graph....", Log.LogEntryType.Debug);
                _extract.RunGraph();
            }
            catch (Exception e)
            {
                try
                {
                    _extract.DeleteParts();
                    _extract.Dispose();
                    _jobStatus.ErrorMsg = Localise.GetPhrase("Error running graph");
                    _jobLog.WriteEntry(this, (_jobStatus.ErrorMsg + "\r\nError : " + e.ToString()), Log.LogEntryType.Error);
                    return false;
                }
                catch (Exception e1)
                {
                    _jobStatus.ErrorMsg = Localise.GetPhrase("Error running graph");
                    _jobLog.WriteEntry(this, (_jobStatus.ErrorMsg) + "\r\nError : " + e1.ToString(), Log.LogEntryType.Error);
                    return false;
                }
            }

            // Atleast 1 audio and 1 video channel should be there
            if ((!_extract.SuccessfulExtraction))
            {
                try
                {
                    _extract.DeleteParts();
                    _extract.Dispose();
                    _jobStatus.ErrorMsg = Localise.GetPhrase("Error extracting streams using Graph");
                    _jobLog.WriteEntry(this, (_jobStatus.ErrorMsg), Log.LogEntryType.Error);
                    return false;
                }
                catch (Exception e1)
                {
                    _jobStatus.ErrorMsg = Localise.GetPhrase("Error extracting streams disposing");
                    _jobLog.WriteEntry(this, (_jobStatus.ErrorMsg) + "\r\nError : " + e1.ToString(), Log.LogEntryType.Error);
                    return false;
                }
            }

            // Dispose the Graph object
            try
            {
                _jobLog.WriteEntry("Disposing Graph....", Log.LogEntryType.Debug);
                _extract.Dispose();
            }
            catch (Exception e)
            {
                _extract.DeleteParts();
                _jobStatus.ErrorMsg = Localise.GetPhrase("Error disposing graph");
                _jobLog.WriteEntry(this, (_jobStatus.ErrorMsg + "\r\nError : " + e.ToString()), Log.LogEntryType.Error);
                return false;
            }

            // Put the streams back together in TS format
            // Check to see if we can use FFmpegParts to do the job
            _jobLog.WriteEntry(this, Localise.GetPhrase("Muxing streams with TsMuxer"), Log.LogEntryType.Information);
            _jobStatus.CurrentAction = Localise.GetPhrase("Muxing streams");
            if (!RemuxRawTSMuxer())
            {
                _jobStatus.ErrorMsg = Localise.GetPhrase("TsMuxer Streams Muxing failed");
                _jobLog.WriteEntry(this, (_jobStatus.ErrorMsg), Log.LogEntryType.Error);

                // Otherwise try tsMuxer
                _jobLog.WriteEntry(this, Localise.GetPhrase("Fallback muxing streams with FFMpegParts"), Log.LogEntryType.Information);
                _jobStatus.CurrentAction = Localise.GetPhrase("Fallback muxing streams");
                if (!RemuxRawPartsFFmpeg())
                {
                    _extract.DeleteParts(); // Clean up
                    _jobStatus.ErrorMsg = Localise.GetPhrase("Fallback Remux Streams FFMpegParts failed");
                    _jobLog.WriteEntry(this, (_jobStatus.ErrorMsg), Log.LogEntryType.Error);
                    return false;
                }
            }

            _jobStatus.ErrorMsg = ""; // All done
            _extract.DeleteParts(); // Clean up

            return true;
        }

        /// <summary>
        /// Checkes the Remuxed file for the following:
        /// 1. File exists
        /// 2. There are no Zero Channel Audio Tracks
        /// 3. File size is reasonable
        /// </summary>
        /// <returns>True if all good, false if any of the above fail</returns>
        private bool RemuxedFileOK()
        {
            if (!File.Exists(RemuxedFile))
            {
                _jobStatus.ErrorMsg = Localise.GetPhrase("ReMuxer failed to create remux file");
                _jobLog.WriteEntry(this, (_jobStatus.ErrorMsg), Log.LogEntryType.Error);
                return false;
            }
            else
            {
                try
                {
                    FileInfo remuxFi = new FileInfo(RemuxedFile);
                    FileInfo originalFi = new FileInfo(_RecordingFile);
                    _jobLog.WriteEntry(this, "Original file size [KB] -> " + (originalFi.Length / 1024).ToString("N", CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
                    _jobLog.WriteEntry(this, "Remuxed file size [KB] -> " + (remuxFi.Length / 1024).ToString("N", CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
                    if (remuxFi.Length <= 0)
                    {
                        // The remuxed file is too small
                        Util.FileIO.TryFileDelete(RemuxedFile);
                        _jobStatus.ErrorMsg = Localise.GetPhrase("Remux file is zero length");
                        _jobLog.WriteEntry(this, (_jobStatus.ErrorMsg), Log.LogEntryType.Error);
                        return false;
                    }
                }
                catch
                {
                    _jobStatus.ErrorMsg = Localise.GetPhrase("Unable to get remux file size");
                    _jobLog.WriteEntry(this, (_jobStatus.ErrorMsg), Log.LogEntryType.Error);
                    return false;
                }

                // Finally - Check for 0 channel audio stream in Remuxed file
                if (CheckForNoneOrZeroChannelAudioTrack(RemuxedFile))
                    return false;

                return true; // it worked!
            }
        }

        /// <summary>
        /// Checks if the specified file has any zero channel audio tracks or no audio tracks
        /// </summary>
        /// <param name="file">Full path to file to check</param>
        /// <returns>True if there are any zero channel audio tracks or No Audio Tracks or failure to read the audio streams</returns>
        private bool CheckForNoneOrZeroChannelAudioTrack(string file)
        {
            _jobLog.WriteEntry(this, "Checking for 0 Channel Audio Tracks in " + file, Log.LogEntryType.Debug);

            // Check for 0 channel audio stream in Remuxed file
            FFmpegMediaInfo ffmpegStreamInfo = new FFmpegMediaInfo(file, ref _jobStatus, _jobLog);
            ffmpegStreamInfo.Run();
            if (ffmpegStreamInfo.Success && !ffmpegStreamInfo.ParseError)
            {
                if (ffmpegStreamInfo.ZeroChannelAudioTrackCount > 0)
                {
                    _jobStatus.ErrorMsg = "Found 0 channel audio track in remuxed file";
                    _jobLog.WriteEntry(this, _jobStatus.ErrorMsg, Log.LogEntryType.Warning);
                    return true;
                }

                if (ffmpegStreamInfo.AudioTracks == 0)
                {
                    _jobStatus.ErrorMsg = "Found No audio tracks in remuxed file";
                    _jobLog.WriteEntry(this, _jobStatus.ErrorMsg, Log.LogEntryType.Warning);
                    return true;
                }
            }
            else
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Unable to read FFMPEG MediaInfo to verify audio streams"), Log.LogEntryType.Error);
                _jobStatus.ErrorMsg = "Unable to read ReMux FFMPEG media info";
                return true;
            }

            return false; // good
        }
    }
}
