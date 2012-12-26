using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using MCEBuddy.Globals;
using MCEBuddy.Util;

namespace MCEBuddy.AppWrapper
{
    public class AtomicParsley : AppWrapper.Base
    {
        private const string APP_PATH = "AtomicParsley\\AtomicParsley.exe";

        public AtomicParsley(string Parameters, ref JobStatus jobStatus, Log jobLog)
            : base(Parameters, APP_PATH, ref jobStatus, jobLog)
        {
            _success = false; //Atomic Parsley handlers look for a true condition in the output so we start with false
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
                if (StdOut.Contains("Progress:") && StdOut.Contains("%"))
                {
                    EndPos = StdOut.IndexOf("%");
                    for (StartPos = EndPos - 1; StartPos > -1; StartPos--)
                    {
                        if (!char.IsNumber(StdOut[StartPos]))
                        {
                            StartPos++;
                            break;
                        }
                    }
                    float.TryParse(StdOut.Substring(StartPos, EndPos - StartPos).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out Perc);
                    _jobStatus.PercentageComplete = Perc;
                }

                if (StdOut.Contains("Finished writing") || StdOut.Contains("completed")) // new ones say finished wirting, updating says completed
                {
                    _success = true;
                }
            }
        }
    }
}
