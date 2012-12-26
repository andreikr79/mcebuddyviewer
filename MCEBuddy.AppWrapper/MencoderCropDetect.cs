using System;
using System.Collections.Generic;
using System.IO;

using MCEBuddy.Globals;
using MCEBuddy.Util;

namespace MCEBuddy.AppWrapper
{
    public class MencoderCropDetect : AppWrapper.Base
    {
        private const string APP_PATH = "mencoder\\mencoder.exe";
        protected SortedList<string, int> _CropResults = new SortedList<string, int>();
        private int _cropHeight = -1;
        private int _cropWidth = -1;
        private string _cropString = "";

        public MencoderCropDetect(string sourceFile, string edlFile, ref JobStatus jobStatus, Log jobLog)
            : base(sourceFile, APP_PATH, ref jobStatus, jobLog)
        {
            // Update the parameters to be passed to mEncoder for cropdetect (ladvopts supports a max of 8 threads)
            _Parameters = Util.FilePaths.FixSpaces(sourceFile) + " -lavdopts threads=" + Math.Min(8, Environment.ProcessorCount).ToString(System.Globalization.CultureInfo.InvariantCulture) + " -nosound -ovc raw -o nul -vf cropdetect"; // setup for multiple processors to speed it up
            
            if (!string.IsNullOrEmpty(edlFile))
                _Parameters += " -edl " + Util.FilePaths.FixSpaces(edlFile); // If there is an EDL file use it to speed things up

            _success = true; //by deafult everything here works unless the process hangs
        }

        protected override void OutputHandler(object sendingProcess, System.Diagnostics.DataReceivedEventArgs ConsoleOutput)
        {
            string StdOut, CropRes;
            int StartPos, EndPos;
            float perc;

            base.OutputHandler(sendingProcess, ConsoleOutput);
            if (ConsoleOutput.Data == null) return;

            if (!String.IsNullOrEmpty(ConsoleOutput.Data))
            {
                StdOut = ConsoleOutput.Data;
                if ((StdOut.Contains("-vf crop=")) && (StdOut.Contains(")")))
                {
                    StartPos = StdOut.IndexOf("-vf crop=") + 9;
                    EndPos = StdOut.IndexOf(")", StartPos);
                    if (EndPos > 0)
                    {
                        CropRes = StdOut.Substring(StartPos, EndPos - StartPos);
                        if (_CropResults.ContainsKey(CropRes))
                        {
                            _CropResults[CropRes]++;
                        }
                        else
                        {
                            _CropResults.Add(CropRes, 1);
                        }
                    }
                }

                //Update % complete
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
                        _jobStatus.PercentageComplete = perc;
                    }
                }
                
                //Update ETA
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
            }
        }

        public override void Run()
        {
            base.Run();
            //Check to see if it completed succesfully
            if (_CropResults.Count > 0 && true == _success && _jobStatus.PercentageComplete >= GlobalDefs.ACCEPTABLE_COMPLETION)
            {
                int Highest = 0;
                string BestCrop = "";

                foreach (KeyValuePair<string, int> Row in _CropResults)
                {
                    if (Row.Value > Highest)
                    {
                        Highest = Row.Value;
                        BestCrop = Row.Key;
                    }
                }
                if (BestCrop != "")
                {
                    _cropString = BestCrop;
                    _cropWidth = int.Parse(BestCrop.Split(':')[0]);
                    _cropHeight = int.Parse(BestCrop.Split(':')[1]);
                }
            }
        }

        public int CropHeight
        {
            get { return _cropHeight; }
        }

        public int CropWidth
        {
            get { return _cropWidth; }
        }

        public string CropString
        {
            get { return _cropString; }
        }

    }
}
