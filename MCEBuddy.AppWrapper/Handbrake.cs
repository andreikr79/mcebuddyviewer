using System;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using MCEBuddy.Globals;
using MCEBuddy.Util;

namespace MCEBuddy.AppWrapper 
{
    public class Handbrake : Base
    {
        private const string APP_PATH = "handbrake\\handbrakecli.exe";

        public Handbrake(string Parameters, ref JobStatus jobStatus, Log jobLog)
            : base(Parameters, APP_PATH, ref jobStatus, jobLog)
        {
            _success = false; //Handbrake look for a +ve output in it's handlers so we start with a false
        }

        protected override void OutputHandler(object sendingProcess, System.Diagnostics.DataReceivedEventArgs ConsoleOutput)
        {
            string StdOut;
            int StartPos, EndPos;
            float Perc;

            base.OutputHandler(sendingProcess, ConsoleOutput);
            if (ConsoleOutput.Data == null) return;

            if (!String.IsNullOrEmpty(ConsoleOutput.Data))
            {
                StdOut = ConsoleOutput.Data;
                if (StdOut.Contains("Encoding:") && StdOut.Contains(",") && StdOut.Contains("%"))
                {
                    EndPos = StdOut.IndexOf("%");
                    for (StartPos = EndPos - 1; StartPos > -1; StartPos--)
                    {
                        if ((!char.IsNumber(StdOut[StartPos])) && (StdOut[StartPos] != '.') && (StdOut[StartPos] != ' '))
                        {
                            StartPos++;
                            break;
                        }
                    }
                    float.TryParse(StdOut.Substring(StartPos, EndPos - StartPos).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out Perc);
                    _jobStatus.PercentageComplete = Perc;

                    if (StdOut.Contains("ETA "))
                    {
                        string ETAStr = "";
                        for (int idx = StdOut.IndexOf("ETA "); idx < StdOut.Length - 1; idx++)
                        {
                            if (char.IsNumber(StdOut[idx])) ETAStr += StdOut[idx];
                        }
                        int ETAVal = 0;
                        int.TryParse(ETAStr, out ETAVal);
                        int Hours = ETAVal / 10000;
                        int Minutes = (ETAVal - (Hours * 10000)) / 100;
                        int Seconds = ETAVal - (Hours * 10000) - (Minutes * 100);
                        UpdateETA(Hours, Minutes, Seconds);
                    }
                }

                if (StdOut.Contains("task 1 of 2"))
                    _jobStatus.CurrentAction = Localise.GetPhrase("Converting video file - Pass 1");
                else if (StdOut.Contains("task 2 of 2"))
                    _jobStatus.CurrentAction = Localise.GetPhrase("Converting video file - Pass 2");

                if (StdOut.Contains("Rip done!") || StdOut.Contains("Encode done!"))
                {
                    _success = true;
                }
            }
        }
    }
}
