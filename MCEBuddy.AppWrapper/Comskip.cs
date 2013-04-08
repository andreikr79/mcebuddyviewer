using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using MCEBuddy.Globals;
using MCEBuddy.Util;

namespace MCEBuddy.AppWrapper
{
    public class Comskip: Base
    {
        private const string APP_PATH = "comskip\\comskip.exe";
        private const string OLD_APP_PATH = "comskip\\comskip_old.exe";
        private bool _firstRun = true;
        private float _lastPerc = 0;
        private bool _newVersion = false; // The new version of Comskip does not show %
        private float _duration = 0; // total length of the video in seconds

        public Comskip(string Parameters, float SecondsDuration, ref JobStatus jobStatus, Log jobLog, bool oldVersion)
            : base(Parameters, (oldVersion ? OLD_APP_PATH : APP_PATH), ref jobStatus, jobLog)
        {
            _success = true; //output handlers don't look for any true, only false can be set by process issues
            _duration = SecondsDuration; // Length of the video in seconds
        }

        public Comskip(string Path, string Parameters, float SecondsDuration, ref JobStatus jobStatus, Log jobLog)
            : base(false, Parameters, Path, ref jobStatus, jobLog)
        {
            _success = true; //output handlers don't look for any true, only false can be set by process issues
            _duration = SecondsDuration; // Length of the video in seconds
        }

        protected override void OutputHandler(object sendingProcess, System.Diagnostics.DataReceivedEventArgs ConsoleOutput)
        {
            string StdOut;
            int StartPos, EndPos;
            float perc;

            base.OutputHandler(sendingProcess, ConsoleOutput);
            if (String.IsNullOrEmpty(ConsoleOutput.Data)) return;

            StdOut = ConsoleOutput.Data;

            if (StdOut.Contains("Duration: N/A"))
                _newVersion = true; // This is the new version of comskip, we don't get % so we need to reverse calculate it

            if (_newVersion) // New version of Comskip
            {
                if (_duration <= 0) // Incase we don't get duration information
                {
                    _jobStatus.CurrentAction = Localise.GetPhrase("Comskip advertisement scan - Pass 1");
                    _jobStatus.PercentageComplete = 0;
                }
                else if (StdOut.Contains(":") && StdOut.Contains("%"))
                {
                    StartPos = 0;
                    EndPos = StdOut.IndexOf("-") - 1;
                    TimeSpan time;
                    TimeSpan.TryParse(StdOut.Substring(StartPos, EndPos - StartPos), out time);
                    float timeProcessed = (float)time.TotalSeconds;
                    perc = timeProcessed / _duration * 100;

                    if (_firstRun) //sometime Comskip makes 2 passes, but sometimes only 1
                    {
                        _jobStatus.CurrentAction = Localise.GetPhrase("Comskip advertisement scan - Pass 1");
                        if (perc < _lastPerc)
                        {
                            _firstRun = false;
                        }
                        else
                        {
                            _lastPerc = perc;
                            _jobStatus.PercentageComplete = perc;
                            UpdateETAByPercentageComplete();
                        }
                    }
                    else
                    {
                        _jobStatus.CurrentAction = Localise.GetPhrase("Comskip advertisement scan - Pass 2");
                        _jobStatus.PercentageComplete = perc;
                        UpdateETAByPercentageComplete();
                    }
                }
            }
            else if (StdOut.Contains("%")) // Old version of Comskip
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

                if (float.TryParse(StdOut.Substring(StartPos, EndPos - StartPos).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out perc))
                {
                    if (_firstRun) //sometime Comskip makes 2 passes, but sometimes only 1
                    {
                        _jobStatus.CurrentAction = Localise.GetPhrase("Comskip advertisement scan - Pass 1");
                        if (perc < _lastPerc)
                        {
                            _firstRun = false;
                        }
                        else
                        {
                            _lastPerc = perc;
                            _jobStatus.PercentageComplete = perc;
                            UpdateETAByPercentageComplete();
                        }
                    }
                    else
                    {
                        _jobStatus.CurrentAction = Localise.GetPhrase("Comskip advertisement scan - Pass 2");
                        _jobStatus.PercentageComplete = perc;
                        UpdateETAByPercentageComplete();
                    }
                }
            }
        }
    }
}
