using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Linq;

using MCEBuddy.Globals;
using MCEBuddy.Util;

namespace MCEBuddy.AppWrapper
{
    public class FFmpeg : AppWrapper.Base
    {
        private const string APP_PATH = "ffmpeg\\ffmpeg.exe";
        private const string DVRMS_APP_PATH = "ffmpeg\\ffmpeg.dvrms.exe";
        private float _Duration = 0;
        private bool _muxError = false;
        private double[] dupArray = new double[0];
        private double lastDup = 0;
        private double[] dropArray = new double[0];
        private double lastDrop = 0;

        /// <summary>
        /// General FFMPEG to convert a file
        /// </summary>
        /// <param name="Parameters">Parameters to pass to FFMPEG</param>
        /// <param name="jobStatus">Reference to JobStatus</param>
        /// <param name="jobLog">JobLog</param>
        public FFmpeg(string Parameters, ref JobStatus jobStatus, Log jobLog)
            : base(Parameters, APP_PATH, ref jobStatus, jobLog )
        {
            _success = false; //ffmpeg looks for a +ve output so we have a false to begin with
        }

        /// <summary>
        /// FFMPEG to convert a DVRMS file
        /// </summary>
        /// <param name="Parameters">Parameters to pass to FFMPEG</param>
        /// <param name="DVRMS">Set to true if converting a DVRMS file</param>
        /// <param name="jobStatus">Reference to JobStatus</param>
        /// <param name="jobLog">JobLog</param>
        public FFmpeg(string Parameters, bool DVRMS, ref JobStatus jobStatus, Log jobLog)
            : base(Parameters, DVRMS_APP_PATH, ref jobStatus, jobLog)
        {
            _success = false; //ffmpeg looks for a +ve output so we have a false to begin with
        }

        public bool MuxError
        { get { return _muxError; } }

        /// <summary>
        /// Return the average rate of change in duplicate frames
        /// </summary>
        public double AverageDupROC
        {
            get
            {
                if (dupArray.Length == 0)
                    return 0;
                else
                    return dupArray.Average();
            } 
        }

        /// <summary>
        /// Return the average rate of change in dropped frames
        /// </summary>
        public double AverageDropROC
        {
            get
            {
                if (dropArray.Length == 0)
                    return 0;
                else
                    return dropArray.Average();
            }    
        }
        
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

        protected override void OutputHandler(object sendingProcess, System.Diagnostics.DataReceivedEventArgs ConsoleOutput)
        {
            string StdOut;
            int StartPos, EndPos;

            base.OutputHandler(sendingProcess, ConsoleOutput);
            if (ConsoleOutput.Data == null) return;

            if (!String.IsNullOrEmpty(ConsoleOutput.Data))
            {
                StdOut = ConsoleOutput.Data;
                if (StdOut.Contains("Duration:") && StdOut.Contains(",") && (_Duration < 1))
                {
                    _StartTime = DateTime.Now;
                    StartPos = StdOut.IndexOf("Duration:") + "Duration:".Length;
                    EndPos = StdOut.IndexOf(",", StartPos);
                    _Duration = TimeStringToSecs(StdOut.Substring(StartPos, EndPos - StartPos));
                    _jobLog.WriteEntry("Video duration=" + _Duration, Log.LogEntryType.Debug);
                }
                else if (StdOut.Contains("frame=") && StdOut.Contains("time="))
                {
                    StartPos = StdOut.IndexOf("time=") + "time=".Length;
                    EndPos = StdOut.IndexOf(" ", StartPos);
                    float timeCoded = TimeStringToSecs(StdOut.Substring(StartPos, EndPos - StartPos));
                    if (timeCoded > 0)
                    {
                        _jobStatus.PercentageComplete = (float)(timeCoded / _Duration * 100);
                        UpdateETAByPercentageComplete();
                    }

                }

                // Capture average rate of change in duplicate frames
                if (StdOut.Contains("dup="))
                {
                    StartPos = StdOut.IndexOf("dup=") + "dup=".Length;
                    EndPos = StdOut.IndexOf(" ", StartPos);

                    Array.Resize(ref dupArray, dupArray.Length + 1); // increase the array size by 1 to store new value
                    double dup = 0;
                    double.TryParse(StdOut.Substring(StartPos, EndPos - StartPos), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out dup);

                    dupArray[dupArray.Length - 1] = dup - lastDup; // We are storing the change in dup value to calcuate the average rate of change in duplicate packets
                    lastDup = dup;
                }

                // Capture average rate of change in dropped frames
                if (StdOut.Contains("drop="))
                {
                    StartPos = StdOut.IndexOf("drop=") + "drop=".Length;
                    EndPos = StdOut.IndexOf(" ", StartPos);

                    Array.Resize(ref dropArray, dropArray.Length + 1); // increase the array size by 1 to store new value
                    double drop = 0;
                    double.TryParse(StdOut.Substring(StartPos, EndPos - StartPos), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out drop);

                    dropArray[dropArray.Length - 1] = drop - lastDrop; // We are storing the change in dup value to calcuate the average rate of change in duplicate packets
                    lastDrop = drop;
                }

                if (StdOut.Contains("non monotonically increasing"))
                {
                    _muxError = true;
                }
                
                if ((StdOut.Contains("global headers")) && (StdOut.Contains("muxing overhead")))
                {
                    _success = true;
                }
            }

        }

    }
}
