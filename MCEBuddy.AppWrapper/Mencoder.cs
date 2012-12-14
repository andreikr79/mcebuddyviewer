using System;
using System.Collections.Generic;
using System.IO;

using MCEBuddy.Globals;
using MCEBuddy.Util;

namespace MCEBuddy.AppWrapper
{
    public class Mencoder : AppWrapper.Base 
    {
        private const string APP_PATH = "mencoder\\mencoder.exe";
        private const string OLD_APP_PATH = "mencoder\\mencoder_cut.exe";
        private bool _oldVersion = false;

        public Mencoder(string Parameters, ref JobStatus jobStatus, Log jobLog, bool oldVersion)
            : base(Parameters, (oldVersion ? OLD_APP_PATH : APP_PATH), ref jobStatus, jobLog)
        {
            _success = false; //Mencoder looks for a +ve output in the handlers to ensure success, so we start with a false
            _oldVersion = oldVersion;
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
                if ((StdOut.Contains("%)")) && (StdOut.Contains("(")))
                {
                    EndPos = StdOut.IndexOf("%)");
                    for (StartPos = EndPos - 1; StartPos > -1; StartPos--)
                    {
                        if (StdOut[StartPos] == '(')
                        {
                            StartPos++;
                            break;
                        }
                    }
                    if (float.TryParse(StdOut.Substring(StartPos, EndPos - StartPos).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out perc))
                    {
                        if (perc > _jobStatus.PercentageComplete) // Sometimes Mencoder keeps jumping % complete, this to prevent it
                            _jobStatus.PercentageComplete = perc;
                    }
                }
                if ((StdOut.Contains("Trem:")) && (StdOut.Contains("min")))
                {
                    string ETAStr = "";
                    for (int idx = StdOut.IndexOf("Trem:") + "Trem".Length + 1; idx < StdOut.Length - 1; idx++)
                    {
                        if (char.IsNumber(StdOut[idx]))
                        {
                            ETAStr += StdOut[idx];
                        }
                        else if (char.IsWhiteSpace(StdOut[idx]))
                        {
                        }
                        else
                        {
                            break;
                        }
                    }
                    int ETAVal = 0;
                    int.TryParse(ETAStr, out ETAVal);
                    int Hours = ETAVal / 60;
                    int Minutes = ETAVal - (Hours * 60);
                    UpdateETA(Hours, Minutes, 0);
                }

                if (_oldVersion) // Old Version of Mencoder uses a different output for success
                {
                    if ((StdOut.Contains("Writing AVI index")) || (StdOut.Contains("Flushing video frames")))
                    {
                        _success = true;
                    }
                }
                else if ((StdOut.Contains("Writing index")) || (StdOut.Contains("Flushing video frames")))
                {
                    _success = true;
                }
            }
        }

        public override void Run()
        {
            base.Run();

            //Check to see if it completed succesfully
            if (!_success) //We do not check % complete here since it is contextual (e.g. cutting doesn't show 100%, conversion does)
            {
                _jobLog.WriteEntry(Localise.GetPhrase("Conversion or cutting failed at") + " " + _jobStatus.PercentageComplete.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%", Log.LogEntryType.Error);
                _jobStatus.ErrorMsg = "Mencoder conversion or cutting failed";
            }
            else if (_oldVersion && _jobStatus.PercentageComplete == 0)
                _jobStatus.PercentageComplete = 100; // Old version of Mencoder does not always update % correctly, so to avoid failure we set to 100 on success
        }
    }
}
