using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using MCEBuddy.Globals;
using MCEBuddy.Util;

namespace MCEBuddy.AppWrapper
{
    public class TiVODecode : AppWrapper.Base
    {
        private const string APP_PATH = "tivo\\tivodecode.exe";

        public TiVODecode(string Parameters, ref JobStatus jobStatus, Log jobLog)
            : base(Parameters, APP_PATH, ref jobStatus, jobLog)
        {
            _success = false;
        }

        protected override void OutputHandler(object sendingProcess, System.Diagnostics.DataReceivedEventArgs ConsoleOutput)
        {
            string StdOut;

            base.OutputHandler(sendingProcess, ConsoleOutput);
            if (ConsoleOutput.Data == null) return;

            if (!String.IsNullOrEmpty(ConsoleOutput.Data))
            {
                StdOut = ConsoleOutput.Data;

                if (StdOut.Contains("End of File"))
                {
                    _success = true;
                }
            }
        }

        public override void Run()
        {
            _HangPeriod = 0; // disable process hang detection for TivoDecode since there is no output and can appear to hang for large files
            base.Run();
        }
    }
}