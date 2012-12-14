using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

//using ShowAnalyzerLib;

using MCEBuddy.Globals;
using MCEBuddy.Util;
using MCEBuddy.AppWrapper;
using MCEBuddy.Configuration;

namespace MCEBuddy.CommercialScan
{
    public class Scanner
    {
        string _videoFileName;

        bool _useShowAnalyzer = false;
        string _EDLFile = "";
        private JobStatus _jobStatus;
        private Log _jobLog;
        private string _profile;
        private bool _forceEDL = false;
        private bool _forceEDLP = false;
        private ConversionJobOptions _convOptions;

        public Scanner(ConversionJobOptions conversionOptions, string videoFileName, bool useShowAnalyzer, ref JobStatus jobStatus, Log jobLog)
        {
            _videoFileName = videoFileName;
            _useShowAnalyzer = useShowAnalyzer;
            _jobStatus = jobStatus;
            _jobLog = jobLog;
            _profile = conversionOptions.profile;
            _convOptions = conversionOptions;

            //check if we need to use the EDL file instead of the EDLP file
            Ini ini = new Ini(GlobalDefs.ProfileFile);
            _forceEDL = ini.ReadBoolean(_profile, "ForceEDL", false);
            _forceEDLP = ini.ReadBoolean(_profile, "ForceEDLP", false);
        }

        

        public static bool ShowAnalyzerInstalled()
        {
            return false;            
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

            AppWrapper.Comskip comskip = new AppWrapper.Comskip(parameters, mediaInfo.MediaInfo.VideoInfo.Duration, ref _jobStatus, _jobLog, false); // By default use the new version
            comskip.Run();
            if (!comskip.Success || !(File.Exists(EDLFilePath()) || File.Exists(EDLPFilePath()))) // Check if the EDL/EDLP file exists, % does not always work
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("New Comskip failed, trying old version"), Log.LogEntryType.Warning);
                _jobStatus.CurrentAction = Localise.GetPhrase("Retrying Comskip Advertisement Scan");

                // Try using the old version of Comskip
                comskip = new AppWrapper.Comskip(parameters, mediaInfo.MediaInfo.VideoInfo.Duration, ref _jobStatus, _jobLog, true); // Try the old version of Comskip
                comskip.Run();
                if (!comskip.Success || !(File.Exists(EDLFilePath()) || File.Exists(EDLPFilePath()))) // Check if the EDL/EDLP file exists, % does not always work
                    _jobStatus.PercentageComplete = 0;
            }
        }

        private string EDLFilePath()
        {
            return Util.FilePaths.GetFullPathWithoutExtension(_videoFileName) + ".edl";
        }

        private string EDLPFilePath()
        {
            return Util.FilePaths.GetFullPathWithoutExtension(_videoFileName) + ".edlp";
        }

        public bool CheckEDL()
        {
            string edlFile = EDLFilePath(); 
            string newEdl = "";

            if (_forceEDL)
                _jobLog.WriteEntry(this, Localise.GetPhrase("ForceEDL Set, using EDL file for commercial removal"), Log.LogEntryType.Information);
            else if (_forceEDLP)
                _jobLog.WriteEntry(this, Localise.GetPhrase("ForceEDLP Set, using EDLP file for commercial removal"), Log.LogEntryType.Information);

            // TS files use EDL others use EDLP
            // If forceEDLP and forceEDL are set, EDL wins
            if (!_forceEDL)
            {
                if ((_forceEDLP && File.Exists(EDLPFilePath())) || ((Path.GetExtension(_videoFileName).ToLower() != ".ts") && File.Exists(EDLPFilePath()))) // use EDLP if forced
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Using EDLP file for commercial removal"), Log.LogEntryType.Information);
                    Util.FileIO.TryFileDelete(EDLFilePath());
                    try
                    {
                        File.Move(EDLPFilePath(), EDLFilePath());
                    }
                    catch (Exception)
                    {
                        _jobLog.WriteEntry(this, Localise.GetPhrase("Unable to copy EDL File"), Log.LogEntryType.Error);
                        _jobStatus.ErrorMsg = "Unable to copy EDL file";
                        return false;
                    }
                }
                else
                {
                    Util.FileIO.TryFileDelete(EDLPFilePath()); // Delete redundant EDLP file
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Using EDL file for commercial removal"), Log.LogEntryType.Information);
                }
            }
            else
                _jobLog.WriteEntry(this, Localise.GetPhrase("Using EDL file for commercial removal"), Log.LogEntryType.Information);

            if (File.Exists(edlFile))
            {
                try
                {
                    System.IO.StreamReader edlS = new System.IO.StreamReader(edlFile);
                    string line;
                    while ((line = edlS.ReadLine()) != null)
                    {
                        string[] cuts = Regex.Split(line, @"\s+");
                        if (cuts.Length == 3)
                        {
                            if (cuts[0] != cuts[1])
                            {
                                newEdl += line + "\n";
                            }
                        }
                    }
                    edlS.Close();
                    edlS.Dispose();
                    newEdl = newEdl.Trim();
                    if (newEdl != "") // if blank, there's no EDL file but scanning was successful, so continue
                    {
                        System.IO.File.WriteAllText(edlFile, newEdl);
                        _EDLFile = edlFile;
                    }
                }
                catch
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Invalid EDL File"), Log.LogEntryType.Error);
                    _jobStatus.ErrorMsg = "Invalid EDL file";
                    return false;
                }

            }
            else
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Cannot find EDL File"), Log.LogEntryType.Error);
                _jobStatus.ErrorMsg = "Cannot find EDL file";
                return false;
            }

            return true;
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
                    //ScanWithSA();
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

            return (CheckEDL()); // EDL file - Comskip new version does not report % anymore for TS files
        }

        public string EDLFile
        {
            get
            {
                return _EDLFile;
            }
        }

        public bool CommercialsFound
        {
            get
            {
                return (!String.IsNullOrEmpty(_EDLFile));
            }
        }
    }
}
