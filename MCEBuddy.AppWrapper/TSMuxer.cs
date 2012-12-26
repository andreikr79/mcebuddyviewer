using System;
using System.IO;
using System.Collections.Generic;
using System.Text;

using MCEBuddy.Globals;
using MCEBuddy.Util;

namespace MCEBuddy.AppWrapper
{
    public class TSMuxer : AppWrapper.Base
    {
        protected bool _SafeExit = false;
        private const string APP_PATH = "tsmuxer\\tsmuxer.exe";

        public TSMuxer(string Parameters, ref JobStatus jobStatus, Log jobLog)
            : base(Parameters, APP_PATH, ref jobStatus, jobLog)
        {
            _success = true; // it doesn't look for anything so we assume it's good unless something in the base run process breaks
        }
    }
}
