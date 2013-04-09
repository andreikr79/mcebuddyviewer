using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using MCEBuddy.Globals;
using MCEBuddy.Util;

namespace MCEBuddy.AppWrapper
{
    public class TiVOMetaExtract : AppWrapper.Base
    {
        private const string APP_PATH = "tivo\\tdcat.exe";

        public TiVOMetaExtract(string Parameters, ref JobStatus jobStatus, Log jobLog)
            : base(Parameters, APP_PATH, ref jobStatus, jobLog)
        {
            _success = true;
        }
    }
}
