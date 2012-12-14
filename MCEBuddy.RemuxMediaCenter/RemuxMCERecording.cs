using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

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
        private bool fixCorruptedRemux = false;
        private string _audioLanguage = "";

        public RemuxMCERecording(string RecordingFile, string DestinationPath, string audioLanguage, ref JobStatus jobStatus, Log jobLog)
        {
            _RecordingFile = RecordingFile;
            _RemuxedFile = Path.Combine(DestinationPath, Path.GetFileNameWithoutExtension(_RecordingFile) + ".ts");
            _jobStatus = jobStatus;
            _jobLog = jobLog;
            _destinationPath = DestinationPath;
            _audioLanguage = audioLanguage;
        }

        public string RemuxedFile
        {
            get { return _RemuxedFile; }
        }

        public bool FixCorruptedRemux
        {
            get { return fixCorruptedRemux; }
        }



        /// <summary>
        /// Used to Remux WTV and DVRMS files to MPEG TS files
        /// </summary>
        /// <returns></returns>
        public bool Remux()
        {
            bool res = false;

            Util.FileIO.TryFileDelete(RemuxedFile);

            // If it's DVRMS first try the special version of FFMPEG, if that fails fall back to regular FFMPEG below
            if (Path.GetExtension(_RecordingFile).ToLower() == ".dvr-ms")
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("DVRMS ReMuxing"), Log.LogEntryType.Information);
                _jobStatus.CurrentAction = Localise.GetPhrase("DVRMS ReMuxing");
                // Otherwise try a direct byte mux
                if (RemuxDVRMSFFmpeg())
                {
                    return true; //Special FFMPEG for DVRMS succeded to create a ts mpeg file
                }
            }
            else if (Path.GetExtension(_RecordingFile).ToLower() == ".wtv") // Try remuxing at the byte level next with remuxsupp
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Byte stream remuxing"), Log.LogEntryType.Information);
                _jobStatus.CurrentAction = Localise.GetPhrase("Byte stream remuxing");
                // Otherwise try a direct byte mux
                if (RemuxWTVRaw())
                {
                    return true; //remuxsupp for wtv succeded to create a ts mpeg file
                }
            }

            //continue to try other fallback methods, try Ffmpeg
            _jobLog.WriteEntry(this, Localise.GetPhrase("ReMuxing using FFMPEG"), Log.LogEntryType.Information);
            _jobStatus.CurrentAction = Localise.GetPhrase("ReMuxing using FFMPEG");
            if (RemuxFFmpeg()) //trying as a fallback directly conversion from WTV/DVRMS to TS using FFMPEG, no framerate conversion
            {
                _jobStatus.ErrorMsg = ""; // Reset error message since the fallback option has suceeded
                return true;
            }

            //64 bit dump filter now!  Release the shackles!!
            //if (Environment.Is64BitOperatingSystem)
            //{
            //    _jobLog.WriteEntry(this, "Cannot stream remux under 64 bit windows.\rUnable to remux.", Log.LogEntryType.Error);
            //    return false;
            //}

            // Dump the streams to files
            _jobLog.WriteEntry(this, Localise.GetPhrase("Attempting directshow based remux, extracting streams"), Log.LogEntryType.Information);
            _jobStatus.CurrentAction = Localise.GetPhrase("Extracting streams");
            res = DumpStreams();
            if (!res)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Unable to extract streams, trying other methods"), Log.LogEntryType.Error);

                try
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Unable to remux") + " " + Path.GetFileName(_RecordingFile), Log.LogEntryType.Error);
                    _extract.DeleteParts();
                    _extract.Dispose();
                }
                catch
                {
                }
                _jobStatus.ErrorMsg = "Unable to remux";
                return false;
            }

            // Get the audio codec and FPS of the video
            string acodec = VideoProperties.RawFile.AudioFormat(_extract.AudioParts[0]);
            float fps = VideoProperties.RawFile.FPS(_extract.VideoPart);


            //Check to see if we can use TSMuxer to do the job
            if (acodec != "")
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Muxing streams with TSMuxer"), Log.LogEntryType.Information);
                _jobStatus.CurrentAction = Localise.GetPhrase("Muxing streams");
                res = RemuxTSMuxer(fps);
                if (res)
                {
                    try
                    {
                        _extract.DeleteParts();
                    }
                    catch
                    {
                    }

                    _jobStatus.ErrorMsg = ""; // Reset error message since the fallback option has suceeded
                    return true;
                }
            }

            // Otherwise try Ffmpeg
            _jobLog.WriteEntry(this, Localise.GetPhrase("Fallback remux with FFMPEG with FPS adjustment"), Log.LogEntryType.Information);
            _jobStatus.CurrentAction = Localise.GetPhrase("Fallback remux");
            if (RemuxPartsFFmpeg(fps))
            {
                try
                {
                    _extract.DeleteParts();
                }
                catch
                {
                }

                _jobStatus.ErrorMsg = ""; // Reset error message since the fallback option has suceeded
                return true;
            }
            else
            {
                _jobStatus.ErrorMsg = Localise.GetPhrase("Unable to remux");
                _jobLog.WriteEntry(this, (_jobStatus.ErrorMsg), Log.LogEntryType.Error);
            }

            try
            {
                _extract.DeleteParts();
            }
            catch
            {
            }
            return false;
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

                if (_audioLanguage == "")
                    RemuxToolParameters = "-i " + Util.FilePaths.FixSpaces(tempFileName) + " -o " + Util.FilePaths.FixSpaces(_RemuxedFile) + " -lang " + Localise.ThreeLetterISO();
                else
                    RemuxToolParameters = "-i " + Util.FilePaths.FixSpaces(tempFileName) + " -o " + Util.FilePaths.FixSpaces(_RemuxedFile) + " -all"; // Copy all audio streams
            }

            RemuxSupp remuxsupp = new RemuxSupp(RemuxToolParameters, ref _jobStatus, _jobLog);
            remuxsupp.Run();
            bool success = remuxsupp.Success; //check the results for an error
            if (tempFileName != "") Util.FileIO.TryFileDelete(tempFileName);
            success = success && (RemuxedFileOK()); //remux succedded and file exists
            if (success)
            {
                FFmpegMediaInfo ffmpegStreamInfo = new FFmpegMediaInfo(RemuxedFile, ref _jobStatus, _jobLog);
                bool zeroChannelAudio = false;
                ffmpegStreamInfo.Run();
                if (ffmpegStreamInfo.Success && !ffmpegStreamInfo.ParseError)
                {
                    // look for atleast audio stream with > 0 audio channels
                    for (int i = 0; i < ffmpegStreamInfo.AudioTracks; i++)
                    {
                        if (ffmpegStreamInfo.MediaInfo.AudioInfo[i].Channels == 0)
                        {
                            zeroChannelAudio = true;
                            break;
                        }
                    }
                }
                else
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Unable to read FFMPEG MediaInfo to verify remuxsupp audio streams"), Log.LogEntryType.Warning);

                if (zeroChannelAudio)
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
                    success = remuxsupp.Success; //check the results for an error
                    if (tempFileName != "") Util.FileIO.TryFileDelete(tempFileName);
                    success = success && RemuxedFileOK(); //remux succedded and file exists
                    if (success)
                    {
                        // If we still find a 0 channel audio, we fallback to FFMPEG now
                        ffmpegStreamInfo = new FFmpegMediaInfo(RemuxedFile, ref _jobStatus, _jobLog);
                        zeroChannelAudio = false;
                        ffmpegStreamInfo.Run();
                        if (ffmpegStreamInfo.Success && !ffmpegStreamInfo.ParseError)
                        {
                            // look for atleast audio stream with > 0 audio channels
                            for (int i = 0; i < ffmpegStreamInfo.AudioTracks; i++)
                            {
                                if (ffmpegStreamInfo.MediaInfo.AudioInfo[i].Channels == 0)
                                {
                                    zeroChannelAudio = true;
                                    break;
                                }
                            }
                        }
                        else
                            _jobLog.WriteEntry(this, Localise.GetPhrase("Unable to read FFMPEG MediaInfo to verify remuxsupp audio streams"), Log.LogEntryType.Warning);

                        // Check one last time for a 0 channel audio
                        if (zeroChannelAudio)
                        {
                            _jobStatus.PercentageComplete = 0;
                            _jobStatus.ErrorMsg = Localise.GetPhrase("ReMuxxSupp failed, found 0 channel audio");
                            _jobLog.WriteEntry(this, (_jobStatus.ErrorMsg), Log.LogEntryType.Error);
                            return false; // fall back to ffmpeg
                        }
                    }
                    else
                    {
                        _jobStatus.PercentageComplete = 0;
                        _jobStatus.ErrorMsg = Localise.GetPhrase("ReMuxxSupp failed");
                        _jobLog.WriteEntry(this, (_jobStatus.ErrorMsg), Log.LogEntryType.Error);
                        return false;
                    }
                }

                // All's good
                return true;
            }
            else
            {
                _jobStatus.ErrorMsg = Localise.GetPhrase("ReMuxxSupp failed");
                _jobLog.WriteEntry(this, (_jobStatus.ErrorMsg), Log.LogEntryType.Error);
                return false;
            }
        }

        /// <summary>
        /// Remux the DVRMS file directly to TSMPEG using a special FFMPEG
        /// </summary>
        /// <returns>Success or Failure</returns>
        private bool RemuxDVRMSFFmpeg()
        {
            // Threads 0 causes an error in some streams, avoid
            _jobLog.WriteEntry(this, Localise.GetPhrase("DVRMS file, using special FFMPEG to remux"), Log.LogEntryType.Information);

            // DVR-MS supports only one audio stream
            string ffmpegParams = "-y -i " + Util.FilePaths.FixSpaces(_RecordingFile) + " -vcodec copy -acodec copy -f mpegts " + Util.FilePaths.FixSpaces(RemuxedFile);

            FFmpeg ffmpeg = new FFmpeg(ffmpegParams, true, ref _jobStatus, _jobLog);
            ffmpeg.Run();

            if (!ffmpeg.Success || !RemuxedFileOK()) //check of file is created, outputhandler reports success (Percentage not requires since Success is more accurate)
            {
                _jobLog.WriteEntry(Localise.GetPhrase("DVRMS ReMux using FFMPEG failed at") + " " + _jobStatus.PercentageComplete.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%", Log.LogEntryType.Error);
                Util.FileIO.TryFileDelete(RemuxedFile);
                _jobStatus.ErrorMsg = "DVRMS ReMux using FFMPEG failed";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Checks if the ReMuxed the file has any zero channel audio streams in it.
        /// If so it tries to re-remux the file using FFMPEG and the given parameters but compensating to keep only one audio channel.
        /// </summary>
        /// <param name="remuxParams">Base parameters used for remuxing</param>
        /// <returns>True if there are no zero channel audio tracks or on a successful re-remux</returns>
        private bool ZeroChannelCheckRemuxFFMPEG(string remuxParams)
        {
            FFmpegMediaInfo ffmpegStreamInfo = new FFmpegMediaInfo(RemuxedFile, ref _jobStatus, _jobLog);
            bool zeroChannelAudio = false;
            ffmpegStreamInfo.Run();
            if (ffmpegStreamInfo.Success && !ffmpegStreamInfo.ParseError)
            {
                // look for atleast audio stream with 0 audio channels
                for (int i = 0; i < ffmpegStreamInfo.AudioTracks; i++)
                {
                    if (ffmpegStreamInfo.MediaInfo.AudioInfo[i].Channels == 0)
                    {
                        zeroChannelAudio = true;
                        break;
                    }
                }
            }
            else
                _jobLog.WriteEntry(this, Localise.GetPhrase("Unable to read FFMPEG MediaInfo to verify remuxsupp audio streams"), Log.LogEntryType.Warning);

            // We have a 0 channel audio we try to remux it again compensating for it
            if (zeroChannelAudio)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Found 0 channel audio while remuxing, re-remuxing using a single audio track"), Log.LogEntryType.Information);
                _jobStatus.CurrentAction = Localise.GetPhrase("Re-ReMuxing due to audio error");

                // DO NOT USE MAP commands, we only need to copy one audio and one video stream, let FFMPEG choose
                remuxParams = remuxParams.ToLower().Replace("-map 0:a", "");
                remuxParams = remuxParams.ToLower().Replace("-map 0:v", "");

                string ffmpegParams = "-y -i " + Util.FilePaths.FixSpaces(_RecordingFile) + " " + remuxParams + " " + Util.FilePaths.FixSpaces(RemuxedFile); // DO NOT USE -async 1 with COPY

                FFmpeg ffmpeg = new MCEBuddy.AppWrapper.FFmpeg(ffmpegParams, ref _jobStatus, _jobLog);
                ffmpeg.Run();
                if (!ffmpeg.Success || !RemuxedFileOK()) //check of file is created, outputhandler reports success (Percentage not requires since Success is more accurate)
                {
                    _jobLog.WriteEntry(Localise.GetPhrase("0 Channel ReMux using FFMPEG failed at") + " " + _jobStatus.PercentageComplete.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%", Log.LogEntryType.Error);
                    Util.FileIO.TryFileDelete(RemuxedFile);
                    _jobStatus.ErrorMsg = "0 Channel ReMux using FFMPEG failed";
                    return false;
                }
            }

            return true;
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
            string ffmpegParams;
            bool autoFPS = false; // Used to check if Auto FPS was used
            fixCorruptedRemux = false; // for managing nested loop conversions
            float FPS = 0;
            bool skipCopyRemux = false; // Do we need to skip stream copy
            FFmpeg ffmpeg = new MCEBuddy.AppWrapper.FFmpeg("", ref _jobStatus, _jobLog); // Dummy, success is false

            // Read ReMux Parameters from Config Profile
            Ini configProfileIni = new Ini(GlobalDefs.ConfigFile);
            string profile = "FFMpegBackupRemux"; // This is where the Fallback Remux parameters are stored

            // Read the Drop frame threashold
            double dropThreshold = double.Parse(configProfileIni.ReadString(profile, "RemuxDropThreshold", "3.0"), System.Globalization.CultureInfo.InvariantCulture);

            // Read the Duplicate frame threashold
            double dupThreshold = double.Parse(configProfileIni.ReadString(profile, "RemuxDuplicateThreshold", "3.0"), System.Globalization.CultureInfo.InvariantCulture);

            // MediaInfo is more reliable than FFMPEG but it doesn't always succeed
            _jobLog.WriteEntry(this, "Reading MediaInfo from " + _RecordingFile, Log.LogEntryType.Debug);
            MediaInfoDll mi = new MediaInfoDll();

            try
            {
                mi.Open(_RecordingFile);

                mi.Option("Inform", "Video; %FrameRate%");
                float.TryParse(mi.Inform(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out FPS);
                _jobLog.WriteEntry(this, "Video FPS : " + FPS.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);

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
            }

            // First check if the video is MPEG2VIDEO, else we need to move on to Slow Remux which will convert the video to MPEG2VIDEO
            // If it's MPEG2VIDEO, we can stream copy it directly
            if (ffmpegStreamInfo.Success && !ffmpegStreamInfo.ParseError)
            {
                if (ffmpegStreamInfo.MediaInfo.VideoInfo.VideoCodec.ToLower() != "mpeg2video")
                {
                    _jobLog.WriteEntry(this, "Video does not contain MPEG2VIDEO, skipping to slow remux. Video Codec found -> " + ffmpegStreamInfo.MediaInfo.VideoInfo.VideoCodec.ToLower(), Log.LogEntryType.Debug);
                    skipCopyRemux = true;
                }
            }

            if (!skipCopyRemux)
            {
                // Check for Multiple Audio Streams
                // Copy all streams if there is an audio selection specified and we'll extract it later (make sure you use the -map 0:a command to copy ALL audio stream, -acodec copy copies only 1 audio stream withou the map command)
                // Use coptyb and copyts to avoid invalid DTS errors
                // Threads 0 causes an error in some streams, avoid
                _jobLog.WriteEntry(this, Localise.GetPhrase("Non default language, copying all audio and video streams"), Log.LogEntryType.Information);

                // First try to copy all the streams directly (read parmeters for copy profile)
                string copyRemuxParams = configProfileIni.ReadString(profile, "CopyRemux", "-vcodec copy -acodec copy -map 0:a -map 0:v -f mpegts");

                // Check for auto frame rate and replace with video framerate
                if (copyRemuxParams.Contains("-r auto"))
                {
                    if (FPS != 0)
                    {
                        _jobLog.WriteEntry(this, "Detected Auto FPS request, setting frame rate to " + FPS.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
                        copyRemuxParams = copyRemuxParams.Replace("-r auto", "-r " + FPS.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        autoFPS = true;
                    }
                    else
                    {
                        _jobLog.WriteEntry(this, Localise.GetPhrase("Cannot read frame rate from file, skipping frame rate adjustment"), Log.LogEntryType.Warning);
                        copyRemuxParams = copyRemuxParams.Replace("-r auto", ""); // no framerate since we can't read it
                        autoFPS = false;
                    }
                }
                else
                    autoFPS = false;

                ffmpegParams = "-y -i " + Util.FilePaths.FixSpaces(_RecordingFile) + " " + copyRemuxParams + " " + Util.FilePaths.FixSpaces(RemuxedFile); // DO NOT USE -async 1 with COPY

                ffmpeg = new MCEBuddy.AppWrapper.FFmpeg(ffmpegParams, ref _jobStatus, _jobLog);
                ffmpeg.Run();

                if (ffmpeg.Success && RemuxedFileOK())
                {
                    // Check for a 0 channel audio, if so we need to recopy it with a single audio track
                    if (!ZeroChannelCheckRemuxFFMPEG(copyRemuxParams))
                        Util.FileIO.TryFileDelete(RemuxedFile); // Did not work, delete remux file and try something else
                }
            }

            if (!ffmpeg.Success || !RemuxedFileOK() || skipCopyRemux) //check of file is created, outputhandler reports success (Percentage not requires since Success is more accurate), also check if we need to skip the copy remux to slow remux
            {
                _jobStatus.PercentageComplete = 100; // reset it to try again
                _jobStatus.ErrorMsg = ""; // reset it to try again
                _jobStatus.CurrentAction = Localise.GetPhrase("Slow Remuxing video");

                // Now try to copy audio and transcode video (read parameters for Slow Remux profile)
                string slowRemuxParams = configProfileIni.ReadString(profile, "SlowRemux", "-vcodec mpeg2video -qscale 0 -r auto -acodec copy -map 0:a -map 0:v -f mpegts");
                
                // Check for auto frame rate and replace with video framerate
                if (slowRemuxParams.Contains("-r auto"))
                {
                    if (FPS != 0)
                    {
                        _jobLog.WriteEntry(this, "Detected Auto FPS request, setting frame rate to " + FPS.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
                        slowRemuxParams = slowRemuxParams.Replace("-r auto", "-r " + FPS.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        autoFPS = true;
                    }
                    else
                    {
                        _jobLog.WriteEntry(this, Localise.GetPhrase("Cannot read frame rate from file, skipping frame rate adjustment"), Log.LogEntryType.Warning);
                        slowRemuxParams = slowRemuxParams.Replace("-r auto", ""); // no framerate since we can't read it
                        autoFPS = false;
                    }
                }
                else
                    autoFPS = false;

                ffmpegParams = "-y -async 1 -i " + Util.FilePaths.FixSpaces(_RecordingFile) + " " + slowRemuxParams + " " + Util.FilePaths.FixSpaces(RemuxedFile);

                ffmpeg = new MCEBuddy.AppWrapper.FFmpeg(ffmpegParams, ref _jobStatus, _jobLog);
                ffmpeg.Run();
                
                if (ffmpeg.Success && RemuxedFileOK())
                {
                    // Check for a 0 channel audio, if so we need to recopy it with a single audio track
                    if (!ZeroChannelCheckRemuxFFMPEG(slowRemuxParams))
                        Util.FileIO.TryFileDelete(RemuxedFile); // Did not work, delete remux file and try something else
                }

                if (!ffmpeg.Success || !RemuxedFileOK()) //check of file is created, outputhandler reports success (Percentage not requires since Success is more accurate)
                {
                    _jobStatus.PercentageComplete = 100; // reset it to try again
                    _jobStatus.ErrorMsg = ""; // reset it to try again
                    _jobStatus.CurrentAction = Localise.GetPhrase("Remuxing corrupted video");

                    // Okay so the Audio PTS/DTS timestamps are corrupted too. Can't stream copy audio to transcode to create new timestamps (albiet loss of channel information)
                    _jobLog.WriteEntry(this, "Video corrupted, trying to remux corrupted video using FFMPEG", Log.LogEntryType.Information);

                    // Check for Multiple Audio Streams, copy all streams if there's an audio selection specified, we'll extract it later (read parameters for Corrupted Remux from profile)
                    string corruptedRemuxParams = configProfileIni.ReadString(profile, "CorruptedRemux", "-vcodec mpeg2video -qscale 0 -r auto -acodec ac3 -ab 256k -map 0:a -map 0:v -f mpegts");

                    // Check for auto frame rate and replace with video framerate
                    if (corruptedRemuxParams.Contains("-r auto"))
                    {
                        if (FPS != 0)
                        {
                            _jobLog.WriteEntry(this, "Detected Auto FPS request, setting frame rate to " + FPS.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
                            corruptedRemuxParams = corruptedRemuxParams.Replace("-r auto", "-r " + FPS.ToString(System.Globalization.CultureInfo.InvariantCulture));
                            autoFPS = true;
                        }
                        else
                        {
                            _jobLog.WriteEntry(this, Localise.GetPhrase("Cannot read frame rate from file, skipping frame rate adjustment"), Log.LogEntryType.Warning);
                            corruptedRemuxParams = corruptedRemuxParams.Replace("-r auto", ""); // no framerate since we can't read it
                            autoFPS = false;
                        }
                    }
                    else
                        autoFPS = false;

                    ffmpegParams = "-y -async 1 -i " + Util.FilePaths.FixSpaces(_RecordingFile) + " " + corruptedRemuxParams + " " + Util.FilePaths.FixSpaces(RemuxedFile);

                    ffmpeg = new MCEBuddy.AppWrapper.FFmpeg(ffmpegParams, ref _jobStatus, _jobLog);
                    ffmpeg.Run();

                    if (ffmpeg.Success && RemuxedFileOK())
                    {
                        // Check for a 0 channel audio, if so we need to recopy it with a single audio track
                        if (!ZeroChannelCheckRemuxFFMPEG(corruptedRemuxParams))
                            Util.FileIO.TryFileDelete(RemuxedFile); // Did not work, delete remux file and try something else
                    }


                    if (!ffmpeg.Success || !RemuxedFileOK()) //check of file is created, outputhandler reports success (Percentage not requires since Success is more accurate)
                    {
                        _jobLog.WriteEntry(Localise.GetPhrase("Slower ReMux using FFMPEG failed at") + " " + _jobStatus.PercentageComplete.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%", Log.LogEntryType.Error);
                        Util.FileIO.TryFileDelete(RemuxedFile);
                        _jobStatus.ErrorMsg = "Slower ReMux using FFMPEG failed";
                        return false;
                    }
                    else
                        fixCorruptedRemux = true; // We have fixed a corrupted video file through ReMux
                }
            }

            _jobLog.WriteEntry("Average rate of dropped frames :" + " " + ffmpeg.AverageDropROC.ToString("#0.00", System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
            _jobLog.WriteEntry("Average rate of duplicate frames :" + " " + ffmpeg.AverageDupROC.ToString("#0.00", System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
            
            // Remux succeeded, check for Dropped or Duplicate packets due to incorrect FPS
            if ((ffmpeg.AverageDropROC > dropThreshold) || (ffmpeg.AverageDupROC > dupThreshold))
            {
                if (autoFPS) // Check if we used AutoFPS and also if this isn't a going into an infinite loop
                    _jobLog.WriteEntry(Localise.GetPhrase("Remuxed file has too many dropped or duplicate frames, try to manually set the frame rate. Auto FPS used ->") + " " + FPS.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Warning);
                else
                    _jobLog.WriteEntry(Localise.GetPhrase("Remuxed file has too many dropped or duplicate frames, check/set the manual remux frame rate"), Log.LogEntryType.Warning);
            }

            return true;
        }

        /// <summary>
        /// Remux with adjustments and extracted stream parts individually using ffmpeg
        /// </summary>
        /// <param name="fps"></param>
        /// <returns>Success or Failure</returns>
        private bool RemuxPartsFFmpeg(float fps)
        {
            string FFmpegParams = "-y -r " + fps.ToString(System.Globalization.CultureInfo.InvariantCulture) + " -i " + _extract.VideoPart;
            for (int i = 0; i < _extract.AudioParts.Count; i++)
            {
                FFmpegParams += " -i " + _extract.AudioParts[i];
            }

            // Check for Multiple Audio Streams
            if (_audioLanguage != "") // Copy all streams, we'll extract it later
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Non default language, copying all audio and video streams"), Log.LogEntryType.Information);
                FFmpegParams += " -map 0:a -map 0:v -acodec copy -vcodec copy -copyts -copytb -1 -f mpegts " + Util.FilePaths.FixSpaces(RemuxedFile);
            }
            else
            {
                // use the locale to get the default language
                string defLang = Localise.ThreeLetterISO();

                FFmpegMediaInfo ffmpegStreamInfo = new FFmpegMediaInfo(_RecordingFile, ref _jobStatus, _jobLog);
                ffmpegStreamInfo.Run();
                if (ffmpegStreamInfo.Success && !ffmpegStreamInfo.ParseError)
                {
                    // Audio parameters - find the best Audio channel
                    bool foundLang = false;
                    int audioStream = -1;
                    int audioChannels = -1;
                    for (int i = 0; i < ffmpegStreamInfo.AudioTracks; i++)
                    {
                        // Default Language selection check
                        if (ffmpegStreamInfo.MediaInfo.AudioInfo[i].Language.ToLower() == defLang)
                        {
                            if (foundLang)
                                if (ffmpegStreamInfo.MediaInfo.AudioInfo[i].Channels <= audioChannels)
                                    continue; // we have found a lang match, now we are looking for more channels only now

                            audioChannels = ffmpegStreamInfo.MediaInfo.AudioInfo[i].Channels;
                            audioStream = ffmpegStreamInfo.MediaInfo.AudioInfo[i].Stream; // store the stream number for the selected audio channel
                            foundLang = true; // We foudn the language we were looking for
                            _jobLog.WriteEntry(this, Localise.GetPhrase("Found Default Audio Language match for language") + " " + defLang.ToUpper() + ", " + Localise.GetPhrase("Audio Stream") + " " + audioStream.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
                        }
                    }

                    if (foundLang)
                    {
                        FFmpegParams += " -map 0:" + audioStream.ToString(System.Globalization.CultureInfo.InvariantCulture); // map the audio stream
                        FFmpegParams += " -map 0:" + ffmpegStreamInfo.MediaInfo.VideoInfo.Stream.ToString(System.Globalization.CultureInfo.InvariantCulture); // map video stream
                    }
                    else
                        _jobLog.WriteEntry(this, Localise.GetPhrase("Cannot find default language audio, using default FFMPEG Audio selection"), Log.LogEntryType.Information);
                }
                else
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Unable to read media information, using default FFMPEG Audio selection"), Log.LogEntryType.Information);

                FFmpegParams += " -acodec copy -vcodec copy -copyts -copytb -1 -f mpegts " + Util.FilePaths.FixSpaces(RemuxedFile);
            }

            MCEBuddy.AppWrapper.FFmpeg ffmpeg = new MCEBuddy.AppWrapper.FFmpeg(FFmpegParams, ref _jobStatus, _jobLog);
            ffmpeg.Run();

            return ((ffmpeg.Success) && (RemuxedFileOK())); // check the output for success command and file exists
        }

        private bool RemuxTSMuxer(float fps)
        {
            string MetaFile = MCEBuddy.Util.FilePaths.GetFullPathWithoutExtension(RemuxedFile) + ".meta";
            string MetaFileContents = "MUXOPT --no-pcr-on-video-pid --new-audio-pes --vbr --vbv-len=500\n";
            string vcodec = VideoProperties.RawFile.VideoFormat(_extract.VideoPart);
            string acodec = VideoProperties.RawFile.AudioFormat(_extract.AudioParts[0]);
            if (vcodec == "avc")
            {
                MetaFileContents += "V_MPEG4/ISO/AVC, \"" + _extract.VideoPart + "\", fps=" + fps.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", insertSEI, contSPS";
            }
            else
            {
                MetaFileContents += "V_MPEG-2, \"" + _extract.VideoPart + "\", fps=" + fps.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            foreach (string AudioPart in _extract.AudioParts)
            {
                if (acodec.Contains("ac3") || acodec.Contains("ac-3"))
                {
                    MetaFileContents += "\nA_AC3";
                }
                else if (acodec.Contains("aac"))
                {
                    MetaFileContents += "\nA_AAC";
                }
                else
                {
                    MetaFileContents += "\nA_MP3";
                }
                MetaFileContents += ", \"" + AudioPart + "\"\n";
            }

            try
            {
                System.IO.File.WriteAllText(MetaFile, MetaFileContents);
            }
            catch (Exception)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("ReMuxTsMuxer failed to write Metafile..."), Log.LogEntryType.Error);
                _jobStatus.ErrorMsg = "ReMuxTsMuxer failed";
                return false;
                //throw;
            }

            AppWrapper.TSMuxer tsmuxer = new TSMuxer(Util.FilePaths.FixSpaces(MetaFile) + " " + Util.FilePaths.FixSpaces(RemuxedFile), ref _jobStatus, _jobLog);
            tsmuxer.Run();

            Util.FileIO.TryFileDelete(MetaFile);

            if (!tsmuxer.Success) // process was terminated
                return false;

            return RemuxedFileOK();
        }

        private bool DumpStreams()
        {
            try
            {
                _extract = new ExtractWithGraph(_RecordingFile, _destinationPath);
            }
            catch (Exception)
            {
                try
                {
                    if (_extract != null) _extract.Dispose();
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Error Extracting with Graph...."), Log.LogEntryType.Error, true);
                }
                catch (Exception)
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Error disposing objects after graph failure...."), Log.LogEntryType.Error, true);
                }

                _jobStatus.ErrorMsg = "Error Extracting Graph";
                return false;
            }

            try
            {
                _extract.BuildGraph();
            }
            catch (Exception)
            {
                try
                {
                    _extract.DeleteParts();
                    _extract.Dispose();
                    _jobStatus.ErrorMsg = Localise.GetPhrase("Error building graph");
                    _jobLog.WriteEntry(this, (_jobStatus.ErrorMsg), Log.LogEntryType.Error);
                    return false;
                }
                catch
                {
                    _jobStatus.ErrorMsg = Localise.GetPhrase("Error building graph");
                    _jobLog.WriteEntry(this, (_jobStatus.ErrorMsg), Log.LogEntryType.Error);
                    return false;
                }
            }

            try
            {
                _extract.RunGraph();
            }
            catch (Exception)
            {
                try
                {
                    _extract.DeleteParts();
                    _extract.Dispose();
                    _jobStatus.ErrorMsg = Localise.GetPhrase("Error running graph");
                    _jobLog.WriteEntry(this, (_jobStatus.ErrorMsg), Log.LogEntryType.Error);
                    return false;

                }
                catch
                {
                    _jobStatus.ErrorMsg = Localise.GetPhrase("Error running graph");
                    _jobLog.WriteEntry(this, (_jobStatus.ErrorMsg), Log.LogEntryType.Error);
                    return false;
                }
            }

            try
            {
                _extract.Dispose();
            }
            catch (Exception)
            {
                _jobStatus.ErrorMsg = Localise.GetPhrase("Error disposing graph");
                _jobLog.WriteEntry(this, (_jobStatus.ErrorMsg), Log.LogEntryType.Error);
                return false;

            }

            return true;
        }

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
                        _jobStatus.ErrorMsg = Localise.GetPhrase("Remuxfile is zero length");
                        _jobLog.WriteEntry(this, (_jobStatus.ErrorMsg), Log.LogEntryType.Error);
                        return false;
                    }
                    else
                    {
                        // Hey!  It worked
                        return true;
                    }
                }
                catch
                {
                    _jobStatus.ErrorMsg = Localise.GetPhrase("Remux file failed");
                    _jobLog.WriteEntry(this, (_jobStatus.ErrorMsg), Log.LogEntryType.Error);
                    return false;
                }
            }
        }
    }
}
