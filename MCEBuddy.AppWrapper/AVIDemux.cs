using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using MCEBuddy.Globals;
using MCEBuddy.Util;

namespace MCEBuddy.AppWrapper
{
    public class AVIDemux : AppWrapper.Base
    {
        private const string APP_PATH = "AVIDemux\\AVIDemux_cli.exe";

        public AVIDemux(string Parameters, ref JobStatus jobStatus, Log jobLog)
            : base(Parameters, APP_PATH, ref jobStatus, jobLog)
        {
            _success = true;
        }

        protected override void OutputHandler(object sendingProcess, System.Diagnostics.DataReceivedEventArgs ConsoleOutput)
        {
            string StdOut;

            base.OutputHandler(sendingProcess, ConsoleOutput);
            if (ConsoleOutput.Data == null) return;

            if (!String.IsNullOrEmpty(ConsoleOutput.Data))
            {
                StdOut = ConsoleOutput.Data;

                // TODO: Need a reliable way to check the output for errors (* Error * - is NOT reliable, since it throws an error on success also)
            }
        }

        public override void Run()
        {
            base.Run();

            if (_success)
                _jobStatus.PercentageComplete = 100; // we are good
        }
    }
}
