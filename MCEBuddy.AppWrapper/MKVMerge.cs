using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using MCEBuddy.Globals;
using MCEBuddy.Util;

namespace MCEBuddy.AppWrapper
{
    public class MKVMerge : AppWrapper.Base
    {
        private const string APP_PATH = "MKVMerge\\MKVMerge.exe";

        public MKVMerge(string Parameters, ref JobStatus jobStatus, Log jobLog)
            : base(Parameters, APP_PATH, ref jobStatus, jobLog)
        {
            _success = false;
        }

        protected override void OutputHandler(object sendingProcess, System.Diagnostics.DataReceivedEventArgs ConsoleOutput)
        {
            string StdOut;
            int StartPos, EndPos;
            float perc;

            base.OutputHandler(sendingProcess, ConsoleOutput);
            if (ConsoleOutput.Data == null) return;

            if (!String.IsNullOrEmpty(ConsoleOutput.Data))
            {
                StdOut = ConsoleOutput.Data;

                if (StdOut.Contains("Progress:") && StdOut.Contains("%")) // look for progress
                {
                    EndPos = StdOut.IndexOf("%");
                    StartPos = StdOut.IndexOf("Progress:") + "Progress:".Length;
                    float.TryParse(StdOut.Substring(StartPos, EndPos - StartPos).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out perc);
                    _jobStatus.PercentageComplete = perc;
                    UpdateETAByPercentageComplete();
                }

                if (StdOut.Contains("Muxing took")) // look for  success criteria
                    _success = true;
            }
        }
    }
}
