using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using ShowAnalyzerLib;

using MCEBuddy.Globals;
using MCEBuddy.Util;
using MCEBuddy.AppWrapper;
using MCEBuddy.Configuration;
using MCEBuddy.VideoProperties;

namespace MCEBuddy.CommercialScan
{
    public class Scanner : EDL
    {
        private string _videoFileName;
        private bool _useShowAnalyzer = false;
        private JobStatus _jobStatus;
        private Log _jobLog;
        private string _profile;
        private ConversionJobOptions _convOptions;
        private string _customComskipPath = "";

        public Scanner(ConversionJobOptions conversionOptions, string videoFileName, bool useShowAnalyzer, float duration, ref JobStatus jobStatus, Log jobLog)
            : base(conversionOptions.profile, videoFileName, duration, "", ref jobStatus, jobLog)
        {
            _videoFileName = videoFileName;
            _useShowAnalyzer = useShowAnalyzer;
            _jobStatus = jobStatus;
            _jobLog = jobLog;
            _profile = conversionOptions.profile;
            _convOptions = conversionOptions;

            _customComskipPath = MCEBuddyConf.GlobalMCEConfig.GeneralOptions.comskipPath;
            if (!String.IsNullOrWhiteSpace(_customComskipPath))
                _jobLog.WriteEntry(this, "Using Custom Comskip Path -> " + MCEBuddyConf.GlobalMCEConfig.GeneralOptions.comskipPath, Log.LogEntryType.Information);
        }

        private void ScanWithSA()
        {
            bool isSuspended = false;

            try
            {
                _jobStatus.PercentageComplete = 0; //reset
                _jobStatus.ETA = "";

                int _HangPeriod = MCEBuddyConf.GlobalMCEConfig.GeneralOptions.hangTimeout; // Default period to detect hang due to no output activity on console - 300 seconds
                double _lastPercentage = 0;

                ShowAnalyzerLib.ShowAnalyzer sa = new ShowAnalyzer();
                string fileId = "";
                DateTime _LastTick = DateTime.Now; ;


                sa.AnalyzeFile(_videoFileName, "", ref fileId);
                //sa.ChangePriority(fileId, (int) GlobObjects.Priority); // set the priority to match the users's selection - not implemented yet, throwing exception
                bool completed = false;
                double percentage = 0;
                string status;
                while (!completed)
                {
                    if (_jobStatus.Cancelled)
                    {
                        //Cancel the running process to release file lock and free processor
                        sa.Abort(fileId);
                        _jobLog.WriteEntry(this, Localise.GetPhrase("Cancelled ShowAnalyzer, file ->") + " " + _videoFileName, Log.LogEntryType.Error);
                        Util.FileIO.TryFileDelete(EDLFilePath()); // Delete the EDL file since that is used to determine success
                        break;
                    }

                    sa.GetStatus(fileId, out percentage, out status);
                    _jobStatus.PercentageComplete = (float)(100 * percentage);

                    if (status == "done")
                    {
                        break;
                    }

                    if (!isSuspended && GlobalDefs.Suspend) // Check if process has to be suspended (if not already)
                    {
                        sa.Pause(fileId); // Suspend it
                        _jobLog.Flush(); // Flush the buffers
                        isSuspended = true;
                    }

                    if (isSuspended && !GlobalDefs.Suspend) // Check if we need to resume the process
                    {
                        isSuspended = false;
                        sa.Resume(fileId); // Resume it
                    }

                    if (isSuspended) _LastTick = DateTime.Now; // Since during suspension there will be no output it shouldn't terminate the process

                    // Check if Showanalyzer has hung
                    if (_lastPercentage != percentage) // It has progressed
                    {
                        if (_HangPeriod > 0) _LastTick = DateTime.Now;
                    }
                    else
                    {
                        if ((_HangPeriod > 0) && (DateTime.Now > _LastTick.AddSeconds(_HangPeriod)))
                        {
                            _jobLog.WriteEntry(Localise.GetPhrase("No response from process for ") + _HangPeriod + Localise.GetPhrase(" seconds, process likely hung - killing it"), Log.LogEntryType.Error);
                            sa.Abort(fileId); // Kill it
                            Util.FileIO.TryFileDelete(EDLFilePath()); // Delete the EDL file since that is used to determine success
                            break;
                        }
                    }

                    _lastPercentage = percentage; // track the last percentage
                    System.Threading.Thread.Sleep(100);
                }

                // Release the COM object and clean up otherwise it hangs later
                try
                {
                    sa.RemoveItemFromQueue(fileId);
                }
                catch { }

                sa = null;
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            catch (Exception Ex)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("ShowAnalyzer failed, file ->") + " " + _videoFileName, Log.LogEntryType.Error);
                _jobLog.WriteEntry(this, "Error -> " + Ex.ToString(), Log.LogEntryType.Debug);
            }
        }

        public static bool ShowAnalyzerInstalled()
        {
            try
            {
                ShowAnalyzerLib.ShowAnalyzer sa = new ShowAnalyzer();
                string reg = sa.GetRegistrationInfo();
                // TODO check for unregistered use - fail
                
                // Release the COM object and clean up otherwise it hangs later
                sa = null;
                GC.Collect();
                GC.WaitForPendingFinalizers();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void ScanWithComskip()
        {
            string parameters = "";
            
            if(_convOptions.comskipIni != "") // Check for custom Ini file
            {
                if(File.Exists(_convOptions.comskipIni))
                    parameters += "--ini=" + Util.FilePaths.FixSpaces(_convOptions.comskipIni) + " ";
                else
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Custom Comskip INI file does not exist, Skipping custom INI"), Log.LogEntryType.Warning);
            }
            
            parameters += Util.FilePaths.FixSpaces(_videoFileName);

            FFmpegMediaInfo mediaInfo = new FFmpegMediaInfo(_videoFileName, ref _jobStatus, _jobLog);
            mediaInfo.Run();
            if (!mediaInfo.Success  || mediaInfo.ParseError)
                _jobLog.WriteEntry(this, Localise.GetPhrase("Cannot read video duration"), Log.LogEntryType.Error);

            if (mediaInfo.MediaInfo.VideoInfo.Duration == 0)
                _jobLog.WriteEntry(this, Localise.GetPhrase("Video duration 0"), Log.LogEntryType.Warning);

            Comskip comskip;
            
            // Check for custom version of Comskip path
            if (!String.IsNullOrWhiteSpace(_customComskipPath))
                comskip = new Comskip(MCEBuddyConf.GlobalMCEConfig.GeneralOptions.comskipPath, parameters, mediaInfo.MediaInfo.VideoInfo.Duration, ref _jobStatus, _jobLog); // Use custom comskip
            else
                comskip = new Comskip(parameters, mediaInfo.MediaInfo.VideoInfo.Duration, ref _jobStatus, _jobLog, false); // By default use the new version or custom path
            comskip.Run();
            if (!comskip.Success || !(File.Exists(EDLFilePath()) || File.Exists(EDLPFilePath()))) // Check if the EDL/EDLP file exists, % does not always work
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("New Comskip failed, trying old version"), Log.LogEntryType.Warning);
                _jobStatus.CurrentAction = Localise.GetPhrase("Retrying Comskip Advertisement Scan");

                // Try using the old version of Comskip
                comskip = new Comskip(parameters, mediaInfo.MediaInfo.VideoInfo.Duration, ref _jobStatus, _jobLog, true); // Try the old version of Comskip
                comskip.Run();
                if (!comskip.Success || !(File.Exists(EDLFilePath()) || File.Exists(EDLPFilePath()))) // Check if the EDL/EDLP file exists, % does not always work
                    _jobStatus.PercentageComplete = 0;
            }
        }

        public bool Scan()
        {
            _jobStatus.PercentageComplete = 0; //reset 
            _jobStatus.ETA = "";

            // Use EDL file if available
            if (File.Exists(EDLFilePath()))
            {
                _jobLog.WriteEntry(this,Localise.GetPhrase("Found existing EDL file, using it"),Log.LogEntryType.Information);
                _jobStatus.PercentageComplete = 100;
            }
            else if (_useShowAnalyzer) // Use ShowAnalyzer if required
            {
                if (ShowAnalyzerInstalled()) // Check if ShowAnalyzer is installed
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Scanning commercials with ShowAnalyzer"), Log.LogEntryType.Information);
                    ScanWithSA();
                }
                else
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("ShowAnalyzer not installed"), Log.LogEntryType.Error);
                    return false;
                }
            }
            else // else Comskip
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Scanning commercials with Comskip"), Log.LogEntryType.Information); 
                ScanWithComskip();
            }

            _jobLog.WriteEntry(this, Localise.GetPhrase("Commercial Scan: Percentage Complete") + " " + _jobStatus.PercentageComplete.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);

            return (CheckAndUpdateEDL(EDLFilePath())); // EDL file - Comskip new version does not report % anymore for TS files
        }

        public bool CommercialsFound
        { get { return (!String.IsNullOrEmpty(EDLFile)); } }

        private string EDLFilePath()
        {
            return Util.FilePaths.GetFullPathWithoutExtension(_videoFileName) + ".edl";
        }

        private string EDLPFilePath()
        {
            return Util.FilePaths.GetFullPathWithoutExtension(_videoFileName) + ".edlp";
        }


    }
}
