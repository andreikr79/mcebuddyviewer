using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.InteropServices;

using DirectShowLib;
using MCEBuddy.Globals;
using MCEBuddy.Util;

namespace MCEBuddy.RemuxMediaCenter
{
    public class ExtractWithGraph : IDisposable
    {
        private Guid CLSID_StreamBufferSource = new Guid("{C9F5FE02-F851-4EB5-99EE-AD602AF1E619}");
        private Guid CLSID_DVRMSDecryptTag = new Guid("{C4C4C4F2-0049-4E2B-98FB-9537F6CE516D}");
        private Guid CLSID_WTVDecryptTag = new Guid("{09144FD6-BB29-11DB-96F1-005056C00008}");
        //private Guid CLSID_DumpFilter = new Guid("{36A5F770-FE4C-11CE-A8ED-00AA002FEAB5}");
        private Guid CLSID_DumpFilter = new Guid("{60DF815A-784A-4725-8493-C42B166F9D92}");

        private Guid MEDIATYPE_Video = new Guid(0x73646976, 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xaa, 0x00, 0x38, 0x9b, 0x71);
        private Guid MEDIATYPE_Audio = new Guid("{73647561-0000-0010-8000-00AA00389B71} ");

        private string _SourceFile = "";
        private Guid _CLSI_Decryptor;
        private string _VideoPart = "";
        private string _workPath = "";
        private List<string> _AudioParts = new List<string>();
        private bool _SuccessfulExtraction = false;

        private FilterGraph _fg;
        private IGraphBuilder _gb;
        IBaseFilter _SourceF;
        List<IBaseFilter> _DumpFilters = new List<IBaseFilter>();
        List<IBaseFilter> _DecryptFilters = new List<IBaseFilter>();
        AMMediaType[] _mtypes = new AMMediaType[1];

        private bool _Cancel = false; // set to true to cancel graph operation

        public ExtractWithGraph(string SourceFile, string workPath)
        {
            string Ext = Path.GetExtension(SourceFile).ToLower().Trim().Replace(".", "");
            if (!((Ext == "dvr-ms") || (Ext == "wtv")))
            {
                throw new ArgumentException(SourceFile + " " + Localise.GetPhrase("must be a wtv or dvr-ms file"));
            }

            //Set the decryptor type depending on the file type DVR-MS or WTV
            if (Ext == "dvr-ms")
            {
                _CLSI_Decryptor = CLSID_DVRMSDecryptTag;
            }
            else
            {
                _CLSI_Decryptor = CLSID_WTVDecryptTag;
            }

            // Set up base graph
            _fg = new FilterGraph();
            _gb = (IGraphBuilder)_fg;

            _SourceFile = SourceFile;
            _workPath = workPath;
        }

        public bool SuccessfulExtraction
        {
            get
            {
                return _SuccessfulExtraction;
            }
        }

        public string VideoPart
        {
            get
            {
                return _VideoPart;
            }
        }

        public List<string> AudioParts
        {
            get
            {
                return _AudioParts;
            }
        }

        private void checkHR(int hr)
        {
            if (hr < 0)
            {
                DsError.ThrowExceptionForHR(hr);
            }
        }

        private string GetFullPathWithoutExtension(string path)
        {
            return Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path));
        }

        private void ConnectDecryptedDump(string PinID, string DumpFileName)
        {
            int hr;
            Type comtype;
            IBaseFilter DecryptF, DumpF;
            IPin PinOut, PinIn;

            //Create the decrypt filter
            comtype = Type.GetTypeFromCLSID(_CLSI_Decryptor);
            DecryptF = (IBaseFilter)Activator.CreateInstance(comtype);
            hr = _gb.AddFilter((IBaseFilter)DecryptF, "Decrypt" + _DecryptFilters.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            checkHR(hr);
            _DecryptFilters.Add(DecryptF);

            //Connect the decrypt filter
            DecryptF.FindPin("In(Enc/Tag)", out PinIn);
            _SourceF.FindPin(PinID, out PinOut);

            try
            {
                // Try to connect the decrypt filter if it is needed
                hr = _gb.ConnectDirect(PinOut, PinIn, null);
                checkHR(hr);
                DecryptF.FindPin("Out", out PinOut);
            }
            catch
            {
                // Otherwise go direct
                _SourceF.FindPin(PinID, out PinOut);
            }

            // Create the dump filter
            DumpF = (IBaseFilter)Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_DumpFilter));
            hr = _gb.AddFilter(DumpF, "Dump" + _DecryptFilters.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            checkHR(hr);

            //set destination filename

            IFileSinkFilter pDump_sink = DumpF as IFileSinkFilter;
            if (pDump_sink == null) checkHR(unchecked((int)0x80004002));
            hr = pDump_sink.SetFileName(DumpFileName, null);
            checkHR(hr);

            //Connect the dump filter
            DumpF.FindPin("Input", out PinIn);
            hr = _gb.ConnectDirect(PinOut, PinIn, null);
            checkHR(hr);
        }

        public void BuildGraph()
        {
            int hr;
            string DumpFileName = "";

            IntPtr fetched = IntPtr.Zero;
            IntPtr fetched2 = IntPtr.Zero;
            IEnumPins FilterPins;
            IPin[] pins = new IPin[1];
            string PinID;

            // Create the source filter for dvrms or wtv
            hr = _gb.AddSourceFilter(_SourceFile, "Source Filter", out _SourceF);

            // Cycle through pins, connecting as appropriate
            hr = _SourceF.EnumPins(out FilterPins);
            while (FilterPins.Next(pins.Length, pins, fetched) == 0)
            {
                AMMediaType[] mtypes = new AMMediaType[1];
                try
                {
                    IEnumMediaTypes enummtypes;
                    pins[0].EnumMediaTypes(out enummtypes);
                    while (enummtypes.Next(1, mtypes, fetched2) == 0)
                    {
                        if ((mtypes[0].majorType == MEDIATYPE_Video) || (mtypes[0].majorType == MEDIATYPE_Audio))
                        {
                            if ((mtypes[0].majorType == MEDIATYPE_Video))
                            {
                                DumpFileName = Path.Combine(_workPath,
                                                            Path.GetFileNameWithoutExtension(_SourceFile) + "_VIDEO");
                                _VideoPart = DumpFileName;
                            }
                            else
                            {
                                DumpFileName = Path.Combine(_workPath,
                                                            Path.GetFileNameWithoutExtension(_SourceFile) + "_AUDIO" + AudioParts.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
                                _AudioParts.Add(DumpFileName);
                            }
                            hr = pins[0].QueryId(out PinID);
                            ConnectDecryptedDump(PinID, DumpFileName);
                        }
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(pins[0]);
                }
            }
        }

        public void RunGraph()
        {
            bool AbortError = false;
            int hr = 0;

            IMediaControl mediaControl = (IMediaControl)_fg;
            IMediaEvent mediaEvent = (IMediaEvent)_fg;
            hr = mediaControl.Run();
            checkHR(hr);

            _Cancel = false;
            bool stop = false;
            while (!stop)
            {
                System.Threading.Thread.Sleep(100);
                if (_Cancel)
                {
                    // Received a shutdown command external to the filter extraction
                    stop = true;
                    AbortError = true;
                    break;
                }

                EventCode ev;
                IntPtr p1, p2;
                if (mediaEvent.GetEvent(out ev, out p1, out p2, 0) == 0)
                {
                    if (ev == EventCode.Complete || ev == EventCode.UserAbort)
                    {
                        mediaControl.Stop();
                        stop = true;
                    }
                    else if (ev == EventCode.ErrorAbort)
                    {
                        mediaControl.Stop();
                        stop = true;
                        AbortError = true;
                    }
                    mediaEvent.FreeEventParams(ev, p1, p2);
                }
            }

            if (!AbortError) _SuccessfulExtraction = true;

        }

        public void DeleteParts()
        {
            Util.FileIO.TryFileDelete(_VideoPart);
            foreach ( string audioPart in _AudioParts)
            {
                Util.FileIO.TryFileDelete(audioPart);
            }
        }

        public void Dispose()
        {
            // Be goot - release the COM objects

            foreach (IBaseFilter sf in _DecryptFilters) { Marshal.ReleaseComObject(sf); }
            foreach (IBaseFilter sf in _DumpFilters) { Marshal.ReleaseComObject(sf); }
            Marshal.ReleaseComObject(_fg);
            Marshal.ReleaseComObject(_gb);
            Marshal.ReleaseComObject(_SourceF); //TODO: Check initialization of _SourceD: last one to release, since it may not be always initiazed and might lead to exception
        }

    }
}
