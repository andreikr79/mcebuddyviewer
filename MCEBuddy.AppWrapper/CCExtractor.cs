using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using MCEBuddy.Globals;
using MCEBuddy.Util;

namespace MCEBuddy.AppWrapper
{
    public class CCExtractor : AppWrapper.Base
    {
        private const string APP_PATH = "ccextractor\\ccextractorwin.exe";

        public CCExtractor(string Parameters, ref JobStatus jobStatus, Log jobLog)
            : base(Parameters, APP_PATH, ref jobStatus, jobLog)
        {
            _success = false; //CCExtractor looks for success criteria
        }

        protected override void OutputHandler(object sendingProcess, System.Diagnostics.DataReceivedEventArgs ConsoleOutput)
        {
            string StdOut;
            float perc;

            base.OutputHandler(sendingProcess, ConsoleOutput);
            if (ConsoleOutput.Data == null) return;

            if (!String.IsNullOrEmpty(ConsoleOutput.Data))
            {
                StdOut = ConsoleOutput.Data;
                if (StdOut.Contains("%") && StdOut.Contains("|") && StdOut.Contains(":"))
                {
                    string[] details = StdOut.Split('|');

                    // Update the % completion
                    string percStr = details[0].Substring(0, details[0].IndexOf('%'));
                    float.TryParse(percStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out perc);
                    _jobStatus.PercentageComplete = perc;
                    UpdateETAByPercentageComplete();
                }

                // Success Criteria
                if (StdOut.Contains("Done, processing time"))
                    _success = true;
            }
        }
    }
}
