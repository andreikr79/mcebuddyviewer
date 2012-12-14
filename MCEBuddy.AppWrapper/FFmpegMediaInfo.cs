using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

using MCEBuddy.Globals;
using MCEBuddy.Util;

namespace MCEBuddy.AppWrapper
{
    // This class collects information about the Video file using the output of FFMPEG (Video and Audio information)
    public class FFmpegMediaInfo : AppWrapper.Base
    {
        private const string APP_PATH = "ffmpeg\\ffmpeg.exe";
        private MediaInfo mediaInfo;
        private int audioTracks = 0; // keep count of how many audio there are reported
        private int zeroChannelTracks = 0; // keep count of how many zero bit rate audio tracks are reported
        private int subtitleTracks = 0; // keep count of subtitle tracks
        private bool parseError = false;
        private bool parseCompleted = false;

        // Various bit sample format supported by FFMPEG
        private static string[,] sampleFmts = {
                                        {"u8","8"},
                                        {"s16","16"},
                                        {"s32","32"},
                                        {"flt","32"},
                                        {"dbl","64"},
                                        {"u8p","8"},
                                        {"s16p","16"},
                                        {"s32p","32"},
                                        {"fltp","32"},
                                        {"dblp","64"}
                                   };

        private static string[,] channelStrings = {
                                                      {"7.1","8"},
                                                      {"8 channel","8"},
                                                      {"6.1","7"},
                                                      {"7 channel","7"},
                                                      {"5.1","6"},
                                                      {"6 channel", "6"}, // since we doing a Contains we are looking for channel and not channels
                                                      {"5.0","5"},
                                                      {"5 channel","5"},
                                                      {"4.0","4"},
                                                      {"4 channel","4"},
                                                      {"3.0","3"},
                                                      {"3 channel","3"},
                                                      {"2.1","3"},
                                                      {"2.0","2"},
                                                      {"2 channel","2"},
                                                      {"stereo","2"},
                                                      {"1 channel","1"},
                                                      {"mono","1"},
                                                      {"0 channel","0"},
                                                      {"no audio","0"}
                                                  };

        public bool ParseError
        {
            get { return parseError; }
        }

        public int AudioTracks
        {
            get { return audioTracks; }
        }

        public int SubtitleTracks
        {
            get { return subtitleTracks; }
        }

        public int ZeroBitrateTracks
        {
            get { return zeroChannelTracks; }
        }

        public MediaInfo MediaInfo
        {
            get { return mediaInfo; }
        }

        public FFmpegMediaInfo (string fileName, ref JobStatus jobStatus, Log jobLog)
            : base(fileName, APP_PATH, ref jobStatus, jobLog )
        {
            mediaInfo = new MediaInfo();
            mediaInfo.VideoInfo = new MediaInfo.Video(); // We have only 1 video track per file, audio/subtitle tracks are created and added as detected
            _success = true; // information always suceeds unless we find an error in the output
            _Parameters = " -i " + MCEBuddy.Util.FilePaths.FixSpaces(fileName); // create the format for run the command
        }

        // Convert the string to time in seconds
        private float TimeStringToSecs(string timeString)
        {
            // Cater for different cuilds fo ffmpeg and their varying output
            if (timeString.Contains(":"))
            {
                float secs = 0;
                int mult = 1;
                string[] timeVals = timeString.Split(':');
                for (int i = timeVals.Length - 1; i >= 0; i--)
                {
                    float val = 0;
                    float.TryParse(timeVals[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out val);
                    secs += mult * val;
                    mult = mult * 60;
                }
                return secs;
            }
            else
            {
                float secs = 0;
                float.TryParse(timeString, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out secs);
                return secs;
            }
        }

        private int NthIndex(string String, char Character, int Index)
        {
            int count = 0;
            for (int i = 0; i < String.Length; i++)
            {
                if (String[i] == Character)
                {
                    count++;
                    if (count == Index)
                        return i;
                }
            }
            return -1;
        }

        private int CheckFmts(string fmt)
        {
            for (int i = 0; i < sampleFmts.Length / 2; i++)
            {
                if (fmt.Contains(sampleFmts[i, 0]))
                    return int.Parse(sampleFmts[i, 1]); // Find bits per sample
            }

            return -1;
        }

        private int CheckChannels(string fmt)
        {
            for (int i = 0; i < channelStrings.Length / 2; i++)
            {
                if (fmt.Contains(channelStrings[i, 0]))
                    return int.Parse(channelStrings[i, 1]); // Channel information
            }

            return -1;
        }

        protected override void OutputHandler(object sendingProcess, System.Diagnostics.DataReceivedEventArgs ConsoleOutput)
        {
            string StdOut;

            base.OutputHandler(sendingProcess, ConsoleOutput);
            if (ConsoleOutput.Data == null) return;

            if (!String.IsNullOrEmpty(ConsoleOutput.Data))
            {
                StdOut = ConsoleOutput.Data;

                /*  SAMPLE OUTPUTS of FFMPEG
                 * 
                 *   Duration: 00:28:13.87, start: 44.031933, bitrate: 14735 kb/s
                 *   Stream #0:0(und): Video: h264 (High) (avc1 / 0x31637661), yuv420p, 688x352 [SAR 8:9 DAR 172:99], 1720 kb/s, SAR 611:688 DAR 611:352, 29.97 fps, 29.97 tbr, 120k tbn, 59.94 tbc
                 *   Stream #0:1[0x2b]: Video: mpeg2video (Main), yuv420p, 1920x1080 [SAR 1:1 DAR 16:9], 65000 kb/s, 1018.98 fps, 59.94 tbr, 10000k tbn, 59.94 tbc
                 *   Stream #0:1[0x12]: Video: mpeg2video (Main), yuv420p, 720x480 [SAR 8:9 DAR 4:3], 44.96 fps, 29.97 tbr, 10000k tbn, 59.94 tbc
                 *   Stream #0:2(eng): Video: mpeg2video (Main) (DVR  / 0x20525644), yuv420p, 1920x1080 [SAR 1:1 DAR 16:9], 17098 kb/s, 30.30 fps, 29.97 tbr, 1k tbn, 59.94 tbc
                 *   Stream #0:0: Video: mpeg4 (Simple Profile) (XVID / 0x44495658), yuv420p, 640x464 [SAR 1:1 DAR 40:29], 23.98 tbr, 23.98 tbn, 23.98 tbc
                 *   Stream #0:1: Video: mpeg4 (Advanced Simple Profile) (xvid / 0x64697678), yuv420p, 720x400 [SAR 83:91 DAR 747:455], SAR 595:603 DAR 119:67, 29.97 fps, 29.97 tbr, 29.97 tbn, 30k tbc
                 *   Stream #0:0: Video: msmpeg4 (DIV3 / 0x33564944), yuv420p, 352x288, 29.97 tbr, 29.97 tbn, 29.97 tbc
                 *   Stream #0:1[0x1e0]: Video: mpeg1video, yuv420p, 352x240 [SAR 200:219 DAR 880:657], 1150 kb/s, 29.97 fps, 29.97 tbr, 90k tbn, 29.97 tbc
                 *   Stream #0:0(und): Video: h264 (Constrained Baseline) (avc1 / 0x31637661), yuv420p, 640x464 [SAR 159:160 DAR 159:116], 1753 kb/s, 23.98 fps, 90k tbr, 90k tbn, 180k tbc
                 *   Stream #0:0(eng): Video: h264 (High), yuv420p, 1920x800, SAR 1:1 DAR 12:5, 23.98 fps, 23.98 tbr, 1k tbn, 47.95 tbc (default)
                 *   Stream #0:1: Video: mpeg2video (DVR  / 0x20525644), yuv420p, 720x480 [SAR 8:9 DAR 4:3], q=2-31, 8250 kb/s, 31.57 fps, 90k tbn, 29.97 tbc
                 *   Stream #0:0[0x1100]: Video: mpeg2video (Main) (2000 / 0x0002), yuv420p, 720x576 SAR 64:45 DAR 16:9, 15000 kb/s, 25.84 fps, 25 tbr, 90k tbn, 50 tbc
                 *   Stream #0:2[0x2c](spa): Audio: ac3, 48000 Hz, stereo, s16, 192 kb/s
                 *   Stream #0:0[0x2a](eng): Audio: ac3, 48000 Hz, 5.1(side), s16, 384 kb/s
                 *   Stream #0:1(eng): Audio: dts (DTS-ES), 48000 Hz, 7 channels (FL+FR+FC+LFE+BC+SL+SR), s16, 1536 kb/s (default)
                 *   Stream #0:2[0x13]: Audio: mp2 (P[0][0][0] / 0x0050), 48000 Hz, stereo, s16, 384 kb/s
                 *   Stream #0:1[0x843]: Audio: mp1, 0 channels, s16
                 *   Stream #0:0: Audio: ac3 ([0] [0][0] / 0x2000), 48000 Hz, stereo, s16, 192 kb/s
                 *   Stream #0:0(eng): Audio: ac3, 48000 Hz, 5.1(side), s16, 384 kb/s
                 *   Stream #0:1: Audio: mp3 (U[0][0][0] / 0x0055), 12000 Hz, stereo, s16, 32 kb/s
                 *   Stream #0:0[0x11]: Subtitle: dvb_teletext
                 *   Stream #0:3[0x2d](eng): Subtitle: dvb_teletext
                 */
                try
                {
                    // Parsing the Video section - we break this up into 4 sections, <Stream #0> <1[0x2b](eng)> <Video> <the rest...>
                    if (StdOut.Contains("Stream #0:") && StdOut.Contains("Video"))
                    {
                        string section1 = StdOut.Substring(0, NthIndex(StdOut, ':', 1)); // Header, <Stream #0> - JUNK
                        string section2 = StdOut.Substring(NthIndex(StdOut, ':', 1) + 1, NthIndex(StdOut, ':', 2) - NthIndex(StdOut, ':', 1) - 1); // <1[0x2b](eng)>
                        string section3 = StdOut.Substring(NthIndex(StdOut, ':', 2) + 1, NthIndex(StdOut, ':', 3) - NthIndex(StdOut, ':', 2) - 1); // <Video> - JUNK
                        string section4 = StdOut.Substring(NthIndex(StdOut, ':', 3) + 1); // <the rest...>

                        // Parse Section 2 to extract the stream and PID
                        if (section2.Contains('['))
                        {
                            mediaInfo.VideoInfo.Stream = int.Parse(section2.Substring(0, section2.IndexOf('['))); // Video stream
                            string PID = section2.Substring(section2.IndexOf('x') + 1, section2.IndexOf(']') - (section2.IndexOf('x') + 1));
                            mediaInfo.VideoInfo.PID = Convert.ToInt32(PID, 16); // get the video PID
                        }
                        else if (section2.Contains('('))
                            mediaInfo.VideoInfo.Stream = int.Parse(section2.Substring(0, section2.IndexOf('('))); // Video stream
                        else
                            mediaInfo.VideoInfo.Stream = int.Parse(section2.Substring(0)); // Video stream

                        // Parse section 4 to extract the remaining information
                        // First parse the codec
                        string codecString = section4.Substring(0, section4.IndexOf(','));
                        if (codecString.Contains("("))
                            mediaInfo.VideoInfo.VideoCodec = codecString.Substring(0, codecString.IndexOf('(')).Trim(); // Video codec
                        else
                            mediaInfo.VideoInfo.VideoCodec = codecString.Substring(0).Trim(); // Video codec

                        // Now parse the rest
                        string restString = section4.Substring(section4.IndexOf(',') + 1);
                        foreach (string subsection in restString.Split(','))
                        {
                            if (subsection.Contains("fps"))
                                mediaInfo.VideoInfo.FPS = float.Parse(subsection.Substring(0, subsection.IndexOf("fps")).Trim(), System.Globalization.CultureInfo.InvariantCulture); // fps always comes before tbr, if tbr is corrupted we end up using fps

                            else if (subsection.Contains("tbr")) // tbr wins over fps when possible (more accurate estimation, tbr always comes after fps)
                            {
                                if (!subsection.Contains("k")) // Sometime FFMPEG reports a faulty tbr e.g. 90k
                                    mediaInfo.VideoInfo.FPS = float.Parse(subsection.Substring(0, subsection.IndexOf("tbr")).Trim(), System.Globalization.CultureInfo.InvariantCulture); // FPS - use tbr since this is the most accurate
                            }

                            else if (subsection.Contains("kb/s"))
                                mediaInfo.VideoInfo.BitRate = int.Parse(subsection.Substring(0, subsection.IndexOf("kb/s")).Trim()) * 1000; // Bit Rate

                            else if (subsection.Contains("[")) // SAR/DAR with [] and resolutions
                            {
                                mediaInfo.VideoInfo.Width = int.Parse(subsection.Substring(0, subsection.IndexOf('x')).Trim()); // Width
                                mediaInfo.VideoInfo.Height = int.Parse(subsection.Substring(subsection.IndexOf('x') + 1, subsection.IndexOf('[') - (subsection.IndexOf('x') + 1)).Trim()); // Height

                                int sar = subsection.IndexOf("SAR");
                                int dar = subsection.IndexOf("DAR");
                                mediaInfo.VideoInfo.SAR = subsection.Substring(sar + 3, dar - (sar + 3)).Trim();
                                mediaInfo.VideoInfo.DAR = subsection.Substring(dar + 3, subsection.IndexOf(']') - (dar + 3)).Trim();
                            }

                            else if (subsection.Contains("SAR") && !subsection.Contains("[")) // SAR/DAR without []
                            {
                                // Sometimes the resolution lies with the SAR/DAR without []
                                if (subsection.Contains("x"))
                                {
                                    mediaInfo.VideoInfo.Width = int.Parse(subsection.Substring(0, subsection.IndexOf('x')).Trim()); // Width
                                    mediaInfo.VideoInfo.Height = int.Parse(subsection.Substring(subsection.IndexOf('x') + 1, subsection.IndexOf('S') - (subsection.IndexOf('x') + 1)).Trim()); // Height
                                }

                                // SAR/DAR inside [] take preference to those without []
                                if ((String.IsNullOrEmpty(mediaInfo.VideoInfo.SAR)) || (String.IsNullOrEmpty(mediaInfo.VideoInfo.DAR)))
                                {
                                    int sar = subsection.IndexOf("SAR");
                                    int dar = subsection.IndexOf("DAR");
                                    mediaInfo.VideoInfo.SAR = subsection.Substring(sar + 3, dar - (sar + 3)).Trim();
                                    mediaInfo.VideoInfo.DAR = subsection.Substring(dar + 3).Trim();
                                }
                            }

                            else if (subsection.Contains("x") && !subsection.Contains("SAR")) // standalone without SAR/DAR
                            {
                                mediaInfo.VideoInfo.Width = int.Parse(subsection.Substring(0, subsection.IndexOf('x')).Trim()); // Width
                                mediaInfo.VideoInfo.Height = int.Parse(subsection.Substring(subsection.IndexOf('x') + 1).Trim()); // Height
                            }

                            else if (subsection.Contains("tbn")) { } // capture all events else they will end up in format

                            else if (subsection.Contains("tbc")) { } // capture all events else they will end up in format

                            else if (subsection.Contains("q=")) { } // capture all events else they will end up in format

                            else
                                mediaInfo.VideoInfo.Format = subsection.Trim(); // Video format
                        }

                        _jobLog.WriteEntry(this, "Video stream = " + mediaInfo.VideoInfo.Stream.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
                        _jobLog.WriteEntry(this, "Video codec = " + mediaInfo.VideoInfo.VideoCodec, Log.LogEntryType.Debug);
                        _jobLog.WriteEntry(this, "Video format = " + mediaInfo.VideoInfo.Format, Log.LogEntryType.Debug);
                        _jobLog.WriteEntry(this, "Video width = " + mediaInfo.VideoInfo.Width.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
                        _jobLog.WriteEntry(this, "Video height = " + mediaInfo.VideoInfo.Height.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
                        _jobLog.WriteEntry(this, "Video SAR = " + mediaInfo.VideoInfo.SAR, Log.LogEntryType.Debug);
                        _jobLog.WriteEntry(this, "Video DAR = " + mediaInfo.VideoInfo.DAR, Log.LogEntryType.Debug);
                        _jobLog.WriteEntry(this, "Video bit rate = " + mediaInfo.VideoInfo.BitRate.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
                        _jobLog.WriteEntry(this, "Video FPS = " + mediaInfo.VideoInfo.FPS.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
                        _jobLog.WriteEntry(this, "Video PID = " + mediaInfo.VideoInfo.PID.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
                    }
                }
                catch (Exception ex)
                {
                    _jobLog.WriteEntry(this, "Error parsing Video. String : " + StdOut + "\n" + ex.ToString(), Log.LogEntryType.Error);
                    parseError = true;
                }

                try
                {
                    //Parse the Duration details
                    if (StdOut.Contains("Duration:") && StdOut.Contains(",") && (mediaInfo.VideoInfo.Duration < 1))
                    {
                        int StartPos = StdOut.IndexOf("Duration:") + "Duration:".Length;
                        int EndPos = StdOut.IndexOf(",", StartPos);
                        mediaInfo.VideoInfo.Duration = TimeStringToSecs(StdOut.Substring(StartPos, EndPos - StartPos));
                        _jobLog.WriteEntry(this, "Video duration = " + mediaInfo.VideoInfo.Duration.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);

                        // Capture the bitrate also, if video shows it later it will be overwritten
                        StartPos = StdOut.IndexOf("bitrate:") + "bitrate:".Length;
                        EndPos = StdOut.IndexOf("kb/s", StartPos);
                        mediaInfo.VideoInfo.BitRate = int.Parse(StdOut.Substring(StartPos, EndPos - StartPos));
                        _jobLog.WriteEntry(this, "Overall bit rate = " + mediaInfo.VideoInfo.BitRate.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
                    }
                }
                catch (Exception ex)
                {
                    _jobLog.WriteEntry(this, "Error parsing Duration. String:" + StdOut + "\n" + ex.ToString(), Log.LogEntryType.Error);
                    parseError = true;
                }

                try
                {
                    // Parse the Audio Stream details (Multiple audio tracks per file)
                    if (StdOut.Contains("Stream #0:") && StdOut.Contains(",") && StdOut.Contains("Audio"))
                    {
                        Array.Resize(ref mediaInfo.AudioInfo, audioTracks + 1); // Increase the array size
                        mediaInfo.AudioInfo[audioTracks] = new MediaInfo.Audio(); // Create a new Audio object for each stream we find
                        string[] parseChunk = StdOut.Split(':');

                        if (parseChunk[1].Contains('[')) // not all files contain the PID
                        {
                            mediaInfo.AudioInfo[audioTracks].Stream = int.Parse(parseChunk[1].Substring(0, parseChunk[1].IndexOf('['))); // Audio Stream
                            string PID = parseChunk[1].Substring(parseChunk[1].IndexOf('x') + 1, parseChunk[1].IndexOf(']') - (parseChunk[1].IndexOf('x') + 1));
                            mediaInfo.AudioInfo[audioTracks].PID = Convert.ToInt32(PID, 16); // get the stream PID
                        }
                        else if (parseChunk[1].Contains('(')) // some return only lang without PID
                            mediaInfo.AudioInfo[audioTracks].Stream = int.Parse(parseChunk[1].Substring(0, parseChunk[1].IndexOf('('))); // Audio Stream
                        else
                            mediaInfo.AudioInfo[audioTracks].Stream = int.Parse(parseChunk[1].Substring(0)); // Audio Stream

                        if (parseChunk[1].Contains('(') && parseChunk[1].Contains(')')) // Not all outputs contains the lanuage
                            mediaInfo.AudioInfo[audioTracks].Language = parseChunk[1].Substring(parseChunk[1].IndexOf('(') + 1, 3); // 3 char language code

                        string[] parseAudio = parseChunk[3].Split(',');
                        if (parseAudio[0].Contains('('))
                            mediaInfo.AudioInfo[audioTracks].AudioCodec = parseAudio[0].Substring(0, parseAudio[0].IndexOf('(')).Trim();
                        else
                            mediaInfo.AudioInfo[audioTracks].AudioCodec = parseAudio[0].Trim();

                        // Parse the rest
                        string parseRest = parseChunk[3].Substring(parseChunk[3].IndexOf(',') + 1);

                        foreach (string section in parseRest.Split(','))
                        {

                            if (section.Contains("Hz")) // check for sampling rate
                                mediaInfo.AudioInfo[audioTracks].Rate = int.Parse(section.Substring(0, section.IndexOf("Hz")).Trim()); // Sample rate

                            else if (section.Contains("kb/s")) // check for bitrate
                                mediaInfo.AudioInfo[audioTracks].BitRate = int.Parse(section.Substring(0, section.IndexOf("kb/s")).Trim()) * 1000; // Bit Rate

                            else if (CheckFmts(section.Trim()) != -1) // check for bits per sample
                                mediaInfo.AudioInfo[audioTracks].SamplingBits = CheckFmts(section.Trim()); // Bits per sample

                            else // We are left with audio channels
                            {
                                // TODO: Need to add support for more audio channel types, where to get the list??
                                // Audio channels
                                if (CheckChannels(section.Trim().ToLower()) != -1)
                                {
                                    // We need to keep track of 0 channel tracks for some encoders
                                    if (CheckChannels(section.Trim().ToLower()) == 0)
                                    {
                                        _jobLog.WriteEntry(this, "0 Audio Channels reported", Log.LogEntryType.Warning);
                                        zeroChannelTracks++; // keep track of this
                                    }

                                    mediaInfo.AudioInfo[audioTracks].Channels = CheckChannels(section.Trim().ToLower()); // Store the channel information
                                }
                                else
                                {
                                    parseError = true;
                                    _jobLog.WriteEntry(this, "Unrecognized Audio Channels : " + StdOut, Log.LogEntryType.Warning);
                                }
                            }
                        }

                        _jobLog.WriteEntry(this, "Audio Track = " + audioTracks.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
                        _jobLog.WriteEntry(this, "Audio stream = " + mediaInfo.AudioInfo[audioTracks].Stream.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
                        _jobLog.WriteEntry(this, "Audio language = " + mediaInfo.AudioInfo[audioTracks].Language, Log.LogEntryType.Debug);
                        _jobLog.WriteEntry(this, "Audio Codec = " + mediaInfo.AudioInfo[audioTracks].AudioCodec, Log.LogEntryType.Debug);
                        _jobLog.WriteEntry(this, "Audio Sampling Rate = " + mediaInfo.AudioInfo[audioTracks].Rate.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
                        _jobLog.WriteEntry(this, "Audio channels = " + mediaInfo.AudioInfo[audioTracks].Channels.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
                        _jobLog.WriteEntry(this, "Audio Bits per sample = " + mediaInfo.AudioInfo[audioTracks].SamplingBits.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
                        _jobLog.WriteEntry(this, "Audio Bit Rate = " + mediaInfo.AudioInfo[audioTracks].BitRate.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
                        _jobLog.WriteEntry(this, "Audio PID = " + mediaInfo.AudioInfo[audioTracks].PID.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);

                        audioTracks++; // We are done with this track
                    }
                }
                catch (Exception ex)
                {
                    _jobLog.WriteEntry(this, "Error parsing Audio. String:" + StdOut + "\n" + ex.ToString(), Log.LogEntryType.Error);
                    parseError = true;
                }

                try
                {
                    // Parse the Subtitle Stream details (Multiple audio tracks per file)
                    if (StdOut.Contains("Stream #0:") && StdOut.Contains("Subtitle"))
                    {
                        Array.Resize(ref mediaInfo.SubtitleInfo, subtitleTracks + 1); // Increase the array size
                        mediaInfo.SubtitleInfo[subtitleTracks] = new MediaInfo.Subtitle(); // Create a new Subtitle object for each stream we find
                        string[] parseChunk = StdOut.Split(':');

                        if (parseChunk[1].Contains('[')) // not all contain PID
                        {
                            mediaInfo.SubtitleInfo[subtitleTracks].Stream = int.Parse(parseChunk[1].Substring(0, parseChunk[1].IndexOf('['))); // Subtitle Stream
                            string PID = parseChunk[1].Substring(parseChunk[1].IndexOf('x') + 1, parseChunk[1].IndexOf(']') - (parseChunk[1].IndexOf('x') + 1));
                            mediaInfo.SubtitleInfo[subtitleTracks].PID = Convert.ToInt32(PID, 16); // get the stream PID
                        }
                        else if (parseChunk[1].Contains('(')) // some return only lang without PID
                            mediaInfo.SubtitleInfo[subtitleTracks].Stream = int.Parse(parseChunk[1].Substring(0, parseChunk[1].IndexOf('('))); // Subtitle Stream
                        else
                            mediaInfo.SubtitleInfo[subtitleTracks].Stream = int.Parse(parseChunk[1].Substring(0)); // Subtitle Stream

                        if (parseChunk[1].Contains('(') && parseChunk[1].Contains(')')) // Not all outputs contains the lanuage
                            mediaInfo.SubtitleInfo[subtitleTracks].Language = parseChunk[1].Substring(parseChunk[1].IndexOf('(') + 1, 3); // 3 char language code

                        mediaInfo.SubtitleInfo[subtitleTracks].Name = parseChunk[3].Trim(); // Name of the subtitle

                        _jobLog.WriteEntry(this, "Subtitle track = " + subtitleTracks.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
                        _jobLog.WriteEntry(this, "Subtitle stream = " + mediaInfo.SubtitleInfo[subtitleTracks].Stream.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
                        _jobLog.WriteEntry(this, "Subtitle language = " + mediaInfo.SubtitleInfo[subtitleTracks].Language, Log.LogEntryType.Debug);
                        _jobLog.WriteEntry(this, "Subtitle name = " + mediaInfo.SubtitleInfo[subtitleTracks].Name, Log.LogEntryType.Debug);
                        _jobLog.WriteEntry(this, "Subtitle PID = " + mediaInfo.SubtitleInfo[subtitleTracks].PID, Log.LogEntryType.Debug);

                        subtitleTracks++; // We are done with this track
                    }
                }
                catch (Exception ex)
                {
                    _jobLog.WriteEntry(this, "Error parsing Subtitles. String:" + StdOut + "\n" + ex.ToString(), Log.LogEntryType.Error);
                    parseError = true;
                }

                if (StdOut.Contains("At least one output file must be specified"))
                    parseCompleted = true; // FFMPEG has finished processing the file
            }
        }

        public override void Run()
        {
            base.Run();

            int i = 0;
            while (!parseCompleted && (i++ < 20))
                Thread.Sleep(500); // Wait for messages to be flushed and parsing completed or until 10 seconds (incase of a failure to read)
        }
    }
}
