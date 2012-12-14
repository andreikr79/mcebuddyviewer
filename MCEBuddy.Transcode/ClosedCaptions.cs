using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

using MCEBuddy.Util;
using MCEBuddy.Globals;
using MCEBuddy.AppWrapper;

namespace MCEBuddy.Transcode
{
    public class ClosedCaptions
    {
        private string _profile;
        private JobStatus _jobStatus;
        private Log _jobLog;
        private string extractedSRTFile = "";

        public ClosedCaptions(string profile, ref JobStatus jobStatus, Log jobLog)
        {
            _profile = profile;
            _jobLog = jobLog;
            _jobStatus = jobStatus;
        }

        public string SRTFile
        { get { return extractedSRTFile; } }

        public bool Extract(string sourceFile, string workingPath, string ccOptions, int startTrim, int endTrim, double ccOffset)
        {
            _jobLog.WriteEntry(this, Localise.GetPhrase("Extracting Closed Captions as SRT file"), Log.LogEntryType.Information);
            _jobLog.WriteEntry(this, "Source File : " + sourceFile, Log.LogEntryType.Debug);
            _jobLog.WriteEntry(this, "Working Path " + workingPath, Log.LogEntryType.Debug);
            _jobLog.WriteEntry(this, "CC Field : " + ccOptions.Split(',')[0], Log.LogEntryType.Debug);
            _jobLog.WriteEntry(this, "CC Channel : " + ccOptions.Split(',')[1], Log.LogEntryType.Debug);
            _jobLog.WriteEntry(this, "Start Trim : " + startTrim.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
            _jobLog.WriteEntry(this, "Stop Trim : " + endTrim.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
            _jobLog.WriteEntry(this, "Offset : " + ccOffset.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);

            if (String.IsNullOrEmpty(ccOptions))
                return true; // nothing to do, accidentally called

            // Output SRT file has to be working directory, will be copied to output afterwards
            extractedSRTFile = Path.Combine(workingPath, Path.GetFileNameWithoutExtension(sourceFile)) + ".srt";
            string ccExtractorParams = "";

            // CCOptions are encoded as field,channel
            string field = ccOptions.Split(',')[0];
            string channel = ccOptions.Split(',')[1];

            ccExtractorParams += " -" + field; // Field is -1 or -2

            if (channel == "2")
                ccExtractorParams += " -cc2"; // By default is Channel 1, there is no parameter for it

            // Adjust for any offset required during extraction (opposite direction, so -ve)
            if (ccOffset != 0)
                ccExtractorParams += " -delay " + (-ccOffset * 1000).ToString(System.Globalization.CultureInfo.InvariantCulture); // ccOffset is in seconds while -delay required milliseconds

            // Get the length of the video, needed to calculate end point
            FFmpegMediaInfo ffmpegStreamInfo = new FFmpegMediaInfo(sourceFile, ref _jobStatus, _jobLog);
            double Duration;
            ffmpegStreamInfo.Run();
            if (ffmpegStreamInfo.Success && !ffmpegStreamInfo.ParseError)
            {
                // Converted file should contain only 1 audio stream
                Duration = ffmpegStreamInfo.MediaInfo.VideoInfo.Duration;
                _jobLog.WriteEntry(this, Localise.GetPhrase("Video duration") + " : " + Duration.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Information);

                if (Duration == 0)
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Video duration 0"), Log.LogEntryType.Error);
                    return false;
                }
            }
            else
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Cannot read video duration"), Log.LogEntryType.Error);
                return false;
            }

            // Set the start trim time
            if (startTrim != 0)
                ccExtractorParams += " -startat " + TimeSpan.FromSeconds((double)startTrim).ToString();

            // Set the end trim time
            if (endTrim != 0)
            {
                // startTime = startTrim, stopTime = video_duration - endTrim
                int encDuration = (((int)Duration) - endTrim) - (startTrim); // by default _startTrim is 0

                ccExtractorParams += " -endat " + TimeSpan.FromSeconds((double)encDuration).ToString();
            }

            // Set the input file
            ccExtractorParams += " " + Util.FilePaths.FixSpaces(sourceFile);

            // set output file
            ccExtractorParams += " -o " + Util.FilePaths.FixSpaces(extractedSRTFile);

            // Run the command
            CCExtractor ccExtractor = new CCExtractor(ccExtractorParams, ref _jobStatus, _jobLog);
            ccExtractor.Run();

            if (!ccExtractor.Success) // check for termination
            {
                _jobLog.WriteEntry(Localise.GetPhrase("CCExtractor was terminated"), Log.LogEntryType.Error);
                return false;
            }

            _jobLog.WriteEntry(this, Localise.GetPhrase("Extracted closed captions to") + " " + extractedSRTFile, Log.LogEntryType.Information);

            return true;
        }

        public bool EDLTrim(string edlFile, string srtFile, double ccOffset)
        {
            _jobLog.WriteEntry(this, Localise.GetPhrase("Syncing SRT file with EDL file"), Log.LogEntryType.Information);
            _jobLog.WriteEntry(this, "EDL File " + edlFile, Log.LogEntryType.Debug);
            _jobLog.WriteEntry(this, "SRT File " + srtFile, Log.LogEntryType.Debug);
            _jobLog.WriteEntry(this, "Offset : " + ccOffset.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);

            if (String.IsNullOrEmpty(srtFile) || String.IsNullOrEmpty(edlFile))
                return true; // nothing to do

            if (!File.Exists(srtFile) || !File.Exists(edlFile) || FileIO.FileSize(srtFile) == 0 || FileIO.FileSize(edlFile) == 0)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("SRT/EDL file does not exist or is 0 bytes in size"), Log.LogEntryType.Warning);
                return true; // nothing to do
            }

            // Taken from MACHYY1's srt_edl_cutter.ps1 (translated from PS1 to C#)
            List<List<double>> edl_array = new List<List<double>>();
            List<List<string>> srt_array = new List<List<string>>();

            // Read the EDL File
            try
            {
                System.IO.StreamReader edlS = new System.IO.StreamReader(edlFile);
                string line;
                double edl_start_keep = 0, edl_end_keep = 0, cut_start_time = 0, cut_end_time = 0, edl_offset = ccOffset;
                string[] fields;
                List<double> edl_line;

                while ((line = edlS.ReadLine()) != null)
                {
                    fields = line.Split('\t');
                    int actionType = Int32.Parse(fields[2]);
                    if (actionType == 0)
                    {
                        cut_start_time = double.Parse(fields[0], System.Globalization.CultureInfo.InvariantCulture);
                        cut_end_time = double.Parse(fields[1], System.Globalization.CultureInfo.InvariantCulture);

                        if (cut_start_time == 0)
                        {
                            edl_start_keep = cut_end_time;
                        }
                        else if (cut_start_time > edl_start_keep)
                        {
                            edl_offset += edl_start_keep - edl_end_keep;
                            edl_end_keep = cut_start_time;

                            edl_line = new List<double>();
                            edl_line.Add(edl_start_keep);
                            edl_line.Add(edl_end_keep);
                            edl_line.Add(edl_offset);
                            edl_array.Add(edl_line);

                            edl_start_keep = cut_end_time;
                        }
                    }
                }

                edl_offset += edl_start_keep - edl_end_keep;

                edl_line = new List<double>();
                edl_line.Add(edl_start_keep);
                edl_line.Add((double)9999999999); // till end of file
                edl_line.Add(edl_offset);
                edl_array.Add(edl_line);

                edlS.Close();
                edlS.Dispose();
            }
            catch (Exception e)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Error processing EDL file") + " " + e.ToString(), Log.LogEntryType.Error);
                return false;
            }

            // Read the SRT File
            try
            {
                System.IO.StreamReader srtS = new System.IO.StreamReader(srtFile);
                string line;
                int edl_array_position = 0;
                int sequence = 1, temp;
                int line_text_num = 1;
                string line_text_1 = "";
                string line_text_2 = "";
                double srt_start_time = 0, srt_end_time = 0;
                List<string> srt_line;
                string[] fields;

                while ((line = srtS.ReadLine()) != null)
                {
                    if (line == "") //blank line - so write the info to an array
                    {
                        if (sequence > 0)
                        {
                            if (srt_start_time > edl_array[edl_array_position][1]) // in the cut area
                                edl_array_position++;
                            else if (srt_start_time >= edl_array[edl_array_position][0] && srt_start_time <= edl_array[edl_array_position][1]) // record to keep
                            {
                                srt_line = new List<string>();
                                srt_line.Add(sequence.ToString());
                                srt_line.Add(seconds_to_hhmmss(srt_start_time - edl_array[edl_array_position][2]));
                                srt_line.Add(seconds_to_hhmmss(srt_end_time - edl_array[edl_array_position][2]));
                                srt_line.Add(line_text_1);
                                srt_line.Add(line_text_2);
                                srt_array.Add(srt_line);
                                sequence++;
                            }

                            srt_start_time = srt_end_time = 0;
                            line_text_1 = line_text_2 = "";
                            line_text_num = 1;
                        }
                    }
                    else if (int.TryParse(line, out temp))
                        continue; // do nothing here
                    else if (line.Contains(" --> "))
                    {
                        line = line.Replace(" --> ", "\t");
                        fields = line.Split('\t');
                        srt_start_time = hhmmss_to_seconds(fields[0]);
                        srt_end_time = hhmmss_to_seconds(fields[1]);
                    }
                    else if (line != "")
                    {
                        if (line_text_num == 1)
                        {
                            line_text_1 = line;
                            line_text_num = 2;
                        }
                        else if (line_text_num == 2)
                        {
                            line_text_2 = line;
                        }
                    }
                    else
                        throw new System.ArgumentException("Invalid SRT file format");
                }

                srtS.Close();
                srtS.Dispose();
            }
            catch (Exception e)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Error processing SRT file") + " " + e.ToString(), Log.LogEntryType.Error);
                return false;
            }

            // Delete the original SRT file
            FileIO.TryFileDelete(srtFile);

            try
            {
                // Write the new SRT file
                StreamWriter srtWrite = new System.IO.StreamWriter(srtFile);
                foreach (List<string> srt_line in srt_array)
                {
                    srtWrite.WriteLine(srt_line[0]);
                    srtWrite.WriteLine(srt_line[1] + " --> " + srt_line[2]);
                    srtWrite.WriteLine(srt_line[3]);
                    if (srt_line[4] != "") srtWrite.WriteLine(srt_line[4]); // no need to write a blank line
                    srtWrite.WriteLine("");
                }
                srtWrite.Flush();
                srtWrite.Close();
                srtWrite.Dispose();
            }
            catch (Exception e)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Error writing to SRT file") + " " + e.ToString(), Log.LogEntryType.Error);
                return false;
            }

            return true;
        }

        private double hhmmss_to_seconds(string hhmmss)
        {
            double hour = 3600 * double.Parse(hhmmss.Substring(0, 2), System.Globalization.CultureInfo.InvariantCulture);
            double minute = 60 * double.Parse(hhmmss.Substring(3, 2), System.Globalization.CultureInfo.InvariantCulture);
            double second = double.Parse(hhmmss.Substring(6, 2), System.Globalization.CultureInfo.InvariantCulture);
            double millisecond = double.Parse(hhmmss.Substring(9, 3), System.Globalization.CultureInfo.InvariantCulture) / 1000;

            double secondsTime = hour + minute + second + millisecond;

            return secondsTime;
        }

        private static string FormatTimeSpan(TimeSpan span, bool showSign)
        {
            string sign = String.Empty;
            if (showSign && (span > TimeSpan.Zero))
                sign = "+";

            return sign +
                   span.Hours.ToString("00") + ":" +
                   span.Minutes.ToString("00") + ":" +
                   span.Seconds.ToString("00") + "," +
                   span.Milliseconds.ToString("000");
        }


        private string seconds_to_hhmmss(double seconds)
        {
            TimeSpan st = TimeSpan.FromSeconds(seconds);
            if (seconds < 0)
                //return st.ToString(@"\-hh\:mm\:ss\,fff", System.Globalization.CultureInfo.InvariantCulture); //-00:25:30,978
                return FormatTimeSpan(st, false);
            else
                return FormatTimeSpan(st, false);
                //return st.ToString(@"hh\:mm\:ss\,fff", System.Globalization.CultureInfo.InvariantCulture); //00:25:30,978
        }
    }
}
