using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

using MCEBuddy.Globals;
using MCEBuddy.Util;
using MCEBuddy.Configuration;

namespace MCEBuddy.AppWrapper
{
    public class Base
    {
        protected string _ApplicationPath;
        protected string _Parameters;
        protected TimeSpan _ExecutionTime;
        protected int _HangPeriod;
        protected DateTime _LastTick;
        protected DateTime _StartTime;
        protected bool _success = false;
        protected JobStatus _jobStatus;
        protected Log _jobLog;
        private bool _customApp = false;
        private ProcessPriorityClass lastPriority = GlobalDefs.Priority;
        private bool isSuspended = false;

        public Base(string parameters, string appPath, ref JobStatus jobStatus, Log jobLog)
        {
            _ApplicationPath = Path.Combine(GlobalDefs.AppPath, appPath);
            Parameters = parameters;
            _jobStatus = jobStatus;
            _jobLog = jobLog;

            _HangPeriod = MCEBuddyConf.GlobalMCEConfig.GeneralOptions.hangTimeout; // Default period to detect hang due to no output activity on console - 300 seconds
        }

        public Base(string parameters, string appPath, ref JobStatus jobStatus, Log jobLog, bool customApp, bool defaultSuccess)
        {
            if (customApp)
            {
                _ApplicationPath = appPath;
                _customApp = customApp;
            }
            else
                _ApplicationPath = Path.Combine(GlobalDefs.AppPath, appPath);

            Parameters = parameters;
            _jobStatus = jobStatus;
            _jobLog = jobLog;
            _success = defaultSuccess;
        }

        protected virtual void OutputHandler(object sendingProcess, DataReceivedEventArgs ConsoleOutput)
        {
            if (_HangPeriod > 0 ) _LastTick = DateTime.Now;

            if (String.IsNullOrEmpty(ConsoleOutput.Data))
            {
                //_jobLog.WriteEntry(this, Localise.GetPhrase("OUTPUT: ConsoleOuput received NULL data"), Log.LogEntryType.Debug);
                return;
            }
            _jobLog.WriteEntry( ConsoleOutput.Data, Log.LogEntryType.Debug);
        }

        public int HangPeriod
        {
            get { return _HangPeriod; }
            set { _HangPeriod = value; }
        }

        public string Parameters
        {
            get { return _Parameters; }
            set { _Parameters = value; }
        }

        public bool Success
        {
            get { return _success; }
        }

        protected void UpdateETAByPercentageComplete()
        {
            if (_jobStatus.PercentageComplete > 2)
            {
                float percComplete = ((float)_jobStatus.PercentageComplete / (float)100  );
                DateTime currentTime = DateTime.Now;
                TimeSpan spanTimeElapsed = currentTime.Subtract(_StartTime);
                float secsToGo = (float)spanTimeElapsed.TotalSeconds / percComplete - (float)spanTimeElapsed.TotalSeconds;
                TimeSpan ts = TimeSpan.FromSeconds((int)secsToGo);
                UpdateETA(ts.Hours, ts.Minutes, ts.Seconds);
            }
        }

        protected void UpdateETA(int Hours, int Minutes, int Seconds)
        {
            string ETADisplay = "";
            if (Hours == 1) ETADisplay += Hours.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + Localise.GetPhrase("hour")+ " ";
            if (Hours > 1) ETADisplay += Hours.ToString(System.Globalization.CultureInfo.InvariantCulture) + " "+ Localise.GetPhrase("hours") + " ";
            if (Minutes == 1) ETADisplay += Minutes.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + Localise.GetPhrase("minute") + " ";
            if (Minutes > 1) ETADisplay += Minutes.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + Localise.GetPhrase("minutes")+ " ";
            if (Seconds == 1) ETADisplay += Seconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + Localise.GetPhrase("second")+ " ";
            if (Seconds > 1) ETADisplay += Seconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + Localise.GetPhrase("seconds")+ " ";
            _jobStatus.ETA = ETADisplay.Trim();
        }

        public virtual void Run()
        {
            //_success = false; //initial state to be defined by each inheriting class depending on it's handlers, we only update error conditions in this routine
            Process Proc;
            DateTime StartTime;

            // Last thing to check, for custom commands, this will log an error and exit
            if (!File.Exists(_ApplicationPath))
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Application File Not Found") + " : " + _ApplicationPath, Log.LogEntryType.Debug);
                _jobStatus.ErrorMsg = "Application File Not Found";
                _jobStatus.PercentageComplete = 0;
                _success = false;
                return;
            }

            if (_Parameters.Length > 8192)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Warning - command parameters exceeding 8192 characters.  This may fail on some systems such as Windows XP"), Log.LogEntryType.Warning);
            }

            // Reset any progress counters
            _jobStatus.PercentageComplete = 0;
            _jobStatus.ETA = Localise.GetPhrase("Working") + "..."; //some processes like ReMuxSupp don't update perc, put a default message

            // Check if job has been cancelled, needed for some long running functions called Base
            if (_jobStatus.Cancelled)
            {
                _jobLog.WriteEntry(Localise.GetPhrase("Job cancelled, killing process"), Log.LogEntryType.Error);
                _jobStatus.ErrorMsg = "Job cancelled, killing process";
                _success = false; //process has been terminated
                return;
            }

            //Set up the process
            Proc = new Process();
            Proc.StartInfo.FileName = _ApplicationPath;
            Proc.StartInfo.Arguments = _Parameters;
            if (_customApp)
            {
                Proc.StartInfo.CreateNoWindow = false; // for custom apps we create a window
                Proc.StartInfo.WindowStyle = ProcessWindowStyle.Normal;
            }
            else
            {
                Proc.StartInfo.CreateNoWindow = true;
                Proc.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            }
            Proc.StartInfo.WorkingDirectory = Path.GetDirectoryName(_ApplicationPath);
            Proc.StartInfo.RedirectStandardOutput = true;
            Proc.StartInfo.RedirectStandardError = true;
            Proc.StartInfo.UseShellExecute = false; // always false else error handlers break
            Proc.OutputDataReceived += new DataReceivedEventHandler(OutputHandler);
            Proc.ErrorDataReceived += new DataReceivedEventHandler(OutputHandler);
            StartTime = DateTime.Now;
            if (_HangPeriod > 0) _LastTick = DateTime.Now;

            _jobLog.WriteEntry(this, Localise.GetPhrase("Launching process") + " " + _ApplicationPath, Log.LogEntryType.Debug);
            _jobLog.WriteEntry(this, Localise.GetPhrase("Process arguments") + " " + _Parameters, Log.LogEntryType.Debug);
            //Reset the counters - duplicate
            //_jobStatus.ETA = "";
            //_jobStatus.PercentageComplete = 0;

            //Start the process
            _StartTime = DateTime.Now;
            try
            {
                Proc.Start(); // First start the process else we get an exception

                lastPriority = GlobalDefs.Priority; // Set it up
                Proc.PriorityClass = GlobalDefs.Priority; // Set the CPU Priority
                IOPriority.SetPriority(Proc.Handle, GlobalDefs.IOPriority); // Set the IO Priority
                if (GlobalDefs.IOPriority == PriorityTypes.IDLE_PRIORITY_CLASS) // If we set to IDLE IO Priority we need to set the background mode begin (only valid on CURRENT process and all CHILD process inherit)
                    IOPriority.SetPriority(PriorityTypes.PROCESS_MODE_BACKGROUND_BEGIN); 

                Proc.BeginOutputReadLine();
                Proc.BeginErrorReadLine();
            }
            catch (Exception Ex)
            {
                _jobLog.WriteEntry( Ex.Message, Log.LogEntryType.Error);
                _success = false; //process has been terminated
                return; // Exit now or we get an exception
            }

            //Wait for an end
            while ((!Proc.HasExited))
            {
                try
                {
                    if (!isSuspended && GlobalDefs.Suspend) // Check if process has to be suspended (if not already)
                    {
                        IOPriority.SuspendProcess(Proc.Id);
                        isSuspended = true;
                    }

                    if (isSuspended && !GlobalDefs.Suspend) // Check if we need to resume the process
                    {
                        isSuspended = false;
                        IOPriority.ResumeProcess(Proc.Id);
                    }

                    if (lastPriority != GlobalDefs.Priority) // Check if the priority was changed and if so update it
                    {
                        lastPriority = GlobalDefs.Priority;
                        Proc.PriorityClass = GlobalDefs.Priority;
                        if (GlobalDefs.IOPriority == PriorityTypes.IDLE_PRIORITY_CLASS) // If we set to IDLE IO Priority we need to set the background mode begin (only valid on CURRENT process and all CHILD process inherit)
                        {
                            IOPriority.SetPriority(Proc.Handle, GlobalDefs.IOPriority); // First set the CPU priority
                            IOPriority.SetPriority(PriorityTypes.PROCESS_MODE_BACKGROUND_BEGIN);
                        }
                        else
                        {
                            IOPriority.SetPriority(PriorityTypes.PROCESS_MODE_BACKGROUND_END);
                            IOPriority.SetPriority(Proc.Handle, GlobalDefs.IOPriority); // Set CPU priority after restoring the scheduling priority
                        }
                    }
                }
                catch { } //incase process exits in the background - not an issue, just avoid a crash

                if (_jobStatus.Cancelled) // if job has been cancelled kill the process
                {
                    try
                    {
                        Proc.Kill();
                        _jobLog.WriteEntry(Localise.GetPhrase("Job cancelled, killing process"), Log.LogEntryType.Error);
                        _jobStatus.ErrorMsg = "Job cancelled, killing process";
                        _success = false; //process has been terminated
                    }
                    catch (Exception Ex)
                    {
                        _jobLog.WriteEntry( Ex.Message, Log.LogEntryType.Warning);
                        _jobLog.WriteEntry(Localise.GetPhrase("Job cancelled, unable to kill process"), Log.LogEntryType.Error);
                        _jobStatus.ErrorMsg = "Job cancelled, unable to kill process";
                        _success = false; //process has been terminated
                    }
                    break;
                }

                if (isSuspended) _LastTick = DateTime.Now; // Since during suspension there will be no output it shouldn't terminate the process

                if ((_HangPeriod > 0) && (DateTime.Now > _LastTick.AddSeconds(_HangPeriod)))
                {
                    _jobLog.WriteEntry(Localise.GetPhrase("No response from process for ") + _HangPeriod + Localise.GetPhrase(" seconds, process likely hung - killing it"), Log.LogEntryType.Error);
                    _success = false;
                    break;
                }

                System.Threading.Thread.Sleep(100); // sleep and check again
            }

            if (!_jobStatus.Cancelled)
                System.Threading.Thread.Sleep(2000);    //Allow the last set of messages to be written

            if (!Proc.HasExited)
            {
                try
                {
                    Proc.Kill();
                    _jobLog.WriteEntry( Localise.GetPhrase("Process hung"), Log.LogEntryType.Error);
                    _jobStatus.ErrorMsg = "Process hung";
                    _success = false; //process has been terminated
                }
                catch (Exception Ex)
                {
                    _jobLog.WriteEntry( Localise.GetPhrase("Unable to terminate process") + "\n" + Ex.Message, Log.LogEntryType.Error);
                    _jobStatus.ErrorMsg = "Unable to terminate process";
                    _success = false; //process has been terminated
                }
            }

            _ExecutionTime = DateTime.Now - StartTime;

            //Close out
            try
            {
                Proc.Close();
            }
            catch (Exception Ex)
            {
                _jobLog.WriteEntry(  Localise.GetPhrase("Unable to cleanly close process")+ "\n" + Ex.Message, Log.LogEntryType.Error);
            }
            Proc.Dispose();
            Proc = null;

            if (!_jobStatus.Cancelled)
                System.Threading.Thread.Sleep(100);    //Allow for the process to be flush any pending messages
        }
    }
}
