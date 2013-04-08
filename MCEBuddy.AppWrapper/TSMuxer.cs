﻿using System;
using System.IO;
using System.Collections.Generic;
using System.Text;

using MCEBuddy.Globals;
using MCEBuddy.Util;

namespace MCEBuddy.AppWrapper
{
    public class TSMuxer : AppWrapper.Base
    {
        private const string APP_PATH = "tsmuxer\\tsmuxer.exe";

        public TSMuxer(string Parameters, ref JobStatus jobStatus, Log jobLog)
            : base(Parameters, APP_PATH, ref jobStatus, jobLog)
        {
            _success = false;
        }

        protected override void OutputHandler(object sendingProcess, System.Diagnostics.DataReceivedEventArgs ConsoleOutput)
        {
            string StdOut;
            int StartPos = 0, EndPos = 0;
            float Perc;

            base.OutputHandler(sendingProcess, ConsoleOutput);
            if (ConsoleOutput.Data == null) return;

            if (!String.IsNullOrEmpty(ConsoleOutput.Data))
            {
                StdOut = ConsoleOutput.Data;
                if (StdOut.Contains("%") && StdOut.Contains("complete"))
                {
                    EndPos = StdOut.IndexOf("%");
                    float.TryParse(StdOut.Substring(StartPos, EndPos - StartPos).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out Perc);
                    _jobStatus.PercentageComplete = Perc;
                    UpdateETAByPercentageComplete();
                }

                if (StdOut.Contains("Mux successful complete."))
                {
                    _success = true;
                }
            }
        }
    }
}
