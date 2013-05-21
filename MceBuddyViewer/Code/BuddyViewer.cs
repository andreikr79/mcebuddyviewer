using System;
using System.Diagnostics;
using System.Collections;
using System.Threading;
using Microsoft.MediaCenter.UI;
using System.Collections.Generic;
using System.Globalization;
using MCEBuddy.Engine;
using MCEBuddy.Util;
using MCEBuddy.Configuration;
using MCEBuddy.Globals;
using Microsoft.MediaCenter;
using Microsoft.MediaCenter.Hosting;
using System.IO;
using System.Text;
using System.ServiceModel;
using System.ComponentModel;
using Microsoft.Win32;


namespace MceBuddyViewer
{
    public class BuddyViewer : ModelItem
    {
        private AddInHost host;
        private HistoryOrientedPageSession session;                
        private string processPriority;
        private string _lastProcessPriority;
        private ICore _pipeProxy = null;    
        private volatile bool _engineConnected = false;        
        private bool _exit = false;
        private int _numJobs;        
        //private int _refreshGUIPeriod = GlobalDefs.GUI_REFRESH_PERIOD; // Refresh GUI frequency
        private int _connectPeriod = GlobalDefs.LOCAL_ENGINE_POLL_PERIOD; // Reconnect/get data from remote engine frequency (default is TCP polling period)
        private Object _configLock = new Object(); // Object to lock for configParameters sync
        private volatile RunningStatus _status;
        private EngineStatusEnum _enginestatus = EngineStatusEnum.NotAvailable;
        private int _numworks;
        private string _currentworkname;
        private string _currentworkstatus;
        private Single _procentcomplete;
        private ArrayListDataSet _jobslist;
        private int _jobitemselected = -1;
        private string _estimatedtime;
        private MSTreeView _treeView_videofile = new MSTreeView();
        private EditableText _editableItem = new EditableText();
        private const float NullPercent = 0;
        private volatile bool _versionMismatch = false;
        private Settings _viewersettings = new Settings();
        private Choice _spinnerFontNames;
        private Choice _spinnerLanguages;

        public Choice SpinnerLanguages
        {
            get
            {
                if (_spinnerLanguages == null)
                {
                    _spinnerLanguages = new Choice();
                    _spinnerLanguages.Options = ViewerSettings.Language.languages;
                    _spinnerLanguages.Default = ViewerSettings.Language.CurrentLanguage;
                }
                return _spinnerLanguages;
            }
        }

        public Choice SpinnerFontNames
        {
            get
            {
                if (_spinnerFontNames == null)
                {
                    _spinnerFontNames = new Choice();
                    List<string> stringItems = new List<string>();
                    stringItems.Add("Segoe Media Center");
                    stringItems.Add("Arial");
                    stringItems.Add("Courier");
                    stringItems.Add("Times New Roman");                                        
                    _spinnerFontNames.Options = stringItems;
                }
                return _spinnerFontNames;
            }
            set { _spinnerFontNames = value; }
        }


        public Settings ViewerSettings
        {
            get { return _viewersettings; }
            set { _viewersettings = value; }
        }

        public MSTreeView TreeViewVideoFile
        {
            get { return _treeView_videofile; }
            set { _treeView_videofile = value; }
        }

        public EditableText EditableItem
        {
            get
            {
                if (_editableItem == null)
                {
                    _editableItem = new EditableText();
                }
                return _editableItem;
            }
            set { _editableItem = value; }
        }


        private void BackEstimatedTime(object obj)
        {
            if (!IsDisposed)
            {
                EstimatedTime = (string)obj;
            }
        }

        private void BackCurrentWorkName(object obj)
        {
            if (!IsDisposed)
            {
                CurrentWorkName = (string)obj;
            }
        }

        private void BackCurrentWorkStatus(object obj)
        {
            if (!IsDisposed)
            {
                CurrentWorkStatus = (string)obj;
            }
        }

        private void BackProcentComplete(object obj)
        {
            if (!IsDisposed)
            {
                ProcentComplete = (Single)obj;
            }
        }

        private void BackNumWorks(object obj)
        {
            if (!IsDisposed)
            {
                NumWorks = (int)obj;
            }
        }

        private void BackEngineStatus(object obj)
        {
            if (!IsDisposed)
            {
                EngineStatus = (EngineStatusEnum)obj;
            }
        }

        private void BackChangedJobsList(object obj)
        {
            if (!IsDisposed)
            {                
                try
                {
                    List<ListItem> fileQueue = (List<ListItem>)obj;
                    if (fileQueue.Count != _jobslist.Count)
                    {
                        _jobslist.Clear();
                        foreach (ListItem item in fileQueue)
                        {
                            item.fileQueue[0] = Path.GetFileName(item.fileQueue[0]);
                            ListItemNode nodeitem = new ListItemNode();
                            nodeitem.fileQueue = item.fileQueue;
                            nodeitem.isCurrent = item.isCurrent;
                            nodeitem.Percent = item.Percent;
                            _jobslist.Add(nodeitem);
                        }
                    }
                    else
                    {
                        for (int indx = 0; indx < fileQueue.Count; indx++)
                        {
                            ListItem item = fileQueue[indx];
                            item.fileQueue[0] = Path.GetFileName(item.fileQueue[0]);
                            ListItemNode nodeitem = (ListItemNode)_jobslist[indx];
                            nodeitem.fileQueue = item.fileQueue;
                            nodeitem.isCurrent = item.isCurrent;
                            nodeitem.Percent = item.Percent;
                        }
                    }
                }
                catch
                {
                    _jobslist.Clear();
                }
            }
        }

        public string EstimatedTime
        {
            get
            {
                return _estimatedtime;
            }
            set
            {
                if (_estimatedtime != value)
                {
                    _estimatedtime = value;
                    FirePropertyChanged("EstimatedTime");
                }                
            }
        }

        public int JobItemSelected
        {
            get
            {
                return _jobitemselected;
            }
            set
            {
                _jobitemselected = value;
                //FirePropertyChanged("JobItemSelected");
            }
        }

        public IList JobsList
        {
            get
            {
                return _jobslist;
            }
        }

        public EngineStatusEnum EngineStatus
        {
            get { return _enginestatus; }
            set 
            {
                if (_enginestatus != value)
                {
                    _enginestatus = value;
                    FirePropertyChanged("EngineStatus");
                }
            }
        }

        public int NumWorks
        {
            get { return _numworks; }
            set
            {
                if (_numworks != value)
                {
                    _numworks = value;
                    FirePropertyChanged("NumWorks");
                }
            }
        }

        public string CurrentWorkName
        {
            get { return _currentworkname; }
            set
            {
                if (_currentworkname != value)
                {
                    _currentworkname = value;
                    FirePropertyChanged("CurrentWorkName");
                }
            }
        }

        public string CurrentWorkStatus
        {
            get { return _currentworkstatus; }
            set
            {
                if (_currentworkstatus != value)
                {
                    _currentworkstatus = value;
                    FirePropertyChanged("CurrentWorkStatus");
                }
            }
        }

        public Single ProcentComplete
        {
            get { return _procentcomplete; }
            set
            {
                if (_procentcomplete != value)
                {
                    _procentcomplete = value;
                    FirePropertyChanged("ProcentComplete");
                }
            }
        }


        public BuddyViewer()
            : this(null, null)
        {
        }

        public BuddyViewer(HistoryOrientedPageSession session, AddInHost host)
        {
            this.session = session;
            this.host = host;
            _status = RunningStatus.Stopped;
            _jobslist = new ArrayListDataSet(this);
            MCEBuddyConf.GlobalMCEConfig = new MCEBuddyConf(); // Initialize with default parameters for now, we will get the config file from the server and then re-initialize (don't use null as it keeps accessing win.ini) - this is never written to a file (just a memory object)                        
        }

        public MediaCenterEnvironment MediaCenterEnvironment
        {
            get
            {
                if (host == null) return null;
                return host.MediaCenterEnvironment;
            }
        }

        public void GoToMenu()
        {
            Dictionary<string, object> properties = new Dictionary<string, object>();
            properties["BuddyViewer"] = this;

            if (session != null)
            {
                session.GoToPage("resx://MceBuddyViewer/MceBuddyViewer.Resources/MainForm", properties);
                Start();
            }
            else
            {
                Debug.WriteLine("GoToMenu");
            }
        }

        public void RemoveHistory()
        {
            if (Microsoft.MediaCenter.Hosting.AddInHost.Current.MediaCenterEnvironment.Dialog(ViewerSettings.Language.Translate["ClearHistoryQuestion"], ViewerSettings.Language.Translate["ClearHistoryQuestionTitle"], (DialogButtons)12, 0, true) == DialogResult.Yes)
            {
                try
                {
                    File.Delete(GlobalDefs.HistoryFile);
                }
                catch
                {
                }
            }
        }

        public void ExitApp()
        {
            session.Close();
        }

        //
        // Called to dispose this model item - call Stop
        // to make sure our background search exits.
        //        
        protected override void Dispose(bool fDispose)
        {
            base.Dispose(fDispose);

            if (fDispose)
            {
                Stop();

                if (_pipeProxy != null)
                {
                    _pipeProxy = null;
                }
                if (_jobslist != null)
                {
                    _jobslist.Dispose();
                    _jobslist = null;
                }
                _exit = true;                
            }
        }

        private void SetUserLocale()
        {
            // If there is no locale specified use the local USER locale. 
            // This is to help fix any issues coming from the service using the system locale and the user using the user's locale where they are different

            GeneralOptions go = MCEBuddyConf.GlobalMCEConfig.GeneralOptions;
            string locale = go.locale;

            // Now initialize the locale engine to load the strings
            Localise.Init(locale);
        }

        /// <summary>
        /// Check if the MCEBuddy engine is running
        /// </summary>
        /// <returns>True if the engine is running, False if the engine is not running or unable to connect to the Pipe</returns>
        private bool EngineRunning()
        {
            try
            {
                if (null != _pipeProxy)
                {
                    if (_pipeProxy.EngineRunning())
                        return true;
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.ToString());
            }
            return false;
        }

        /// <summary>
        /// Tries to keep connected to the MCEBuddy Engine over TCP/IP. Once connected it downloads the MCEBuddy Config options from the server.
        /// </summary>
        /// <returns>True if it is connected to the Pipe</returns>
        private void TryConnect(object obj)
        {
            //
            // Drop the priority of our thread so that we don't
            // interfere with other processing
            //           
            ThreadPriority priority = Thread.CurrentThread.Priority;
            Thread.CurrentThread.Priority = ThreadPriority.Lowest;

            while (!_exit) // Keep trying to connect as long as app is running
            {

                if (Status == RunningStatus.PendingStop)
                {
                    Thread.Sleep(_connectPeriod); // Check again in x seconds
                    continue;
                }

                bool configLockTaken = false; // Flag to see state of lock

                try
                {
                    Monitor.Enter(_configLock);
                    configLockTaken = true;
                    MCEBuddyConf.GlobalMCEConfig = new MCEBuddyConf(_pipeProxy.GetConfigParameters()); // Get the parameters from the Engine we are connected to
                    Monitor.Exit(_configLock);
                    configLockTaken = false;
                    _engineConnected = true; // All good, we are connected                    
                    ProgressChanged();
                    Thread.Sleep(_connectPeriod); // Check again in x seconds
                    continue;
                }
                catch (Exception) // Whoops pipe broken!
                {
                    if (configLockTaken)
                    {
                        Monitor.Exit(_configLock); // Release the lock
                        configLockTaken = false;
                    }
                    _engineConnected = false; // We are no longer connected
                    _pipeProxy = null; // We should start over
                }

                try // Try to reconnect
                {
                    ChannelFactory<ICore> pipeFactory;
                    string serverString;

                    Ini tempIni = new Ini(GlobalDefs.TempSettingsFile);
                    string remoteServerName = tempIni.ReadString("Engine", "RemoteServerName", GlobalDefs.MCEBUDDY_SERVER_NAME);
                    int remoteServerPort = tempIni.ReadInteger("Engine", "RemoteServerPort", int.Parse(GlobalDefs.MCEBUDDY_SERVER_PORT));

                    // If it's LOCALHOST, we use NAMED PIPE else TCP PIPE
                    if (remoteServerName == GlobalDefs.MCEBUDDY_SERVER_NAME)
                    {
                        // local NAMED PIPE
                        serverString = GlobalDefs.MCEBUDDY_LOCAL_NAMED_PIPE;
                        NetNamedPipeBinding npb = new NetNamedPipeBinding();
                        npb.MaxReceivedMessageSize = Int32.MaxValue;
                        pipeFactory = new ChannelFactory<ICore>(npb, new EndpointAddress(serverString));
                    }
                    else
                    {
                        // network SOAP WEB SERVICES
                        serverString = GlobalDefs.MCEBUDDY_WEB_SOAP_PIPE;
                        serverString = serverString.Replace(GlobalDefs.MCEBUDDY_SERVER_NAME, remoteServerName); // Update the Server Name with that from the config file
                        serverString = serverString.Replace(GlobalDefs.MCEBUDDY_SERVER_PORT, remoteServerPort.ToString(CultureInfo.InvariantCulture)); // Update the Server Port with that from the config file
                        BasicHttpBinding ntb = new BasicHttpBinding(GlobalDefs.MCEBUDDY_PIPE_SECURITY);
                        TimeSpan timeoutPeriod = new TimeSpan(0, 0, GlobalDefs.PIPE_TIMEOUT);
                        ntb.SendTimeout = ntb.ReceiveTimeout = timeoutPeriod;
                        ntb.MaxReceivedMessageSize = Int32.MaxValue;
                        pipeFactory = new ChannelFactory<ICore>(ntb, new EndpointAddress(serverString));
                    }

                    Monitor.Enter(_configLock);
                    configLockTaken = true;
                    ICore tempPipeProxy = pipeFactory.CreateChannel();
                    MCEBuddyConf.GlobalMCEConfig = new MCEBuddyConf(tempPipeProxy.GetConfigParameters()); // Get the parameters from the Engine we are connected to
                    GlobalDefs.profilesSummary = tempPipeProxy.GetProfilesSummary(); // Get a list of all the profiles and descriptions
                    try
                    {
                        GlobalDefs.showAnalyzerInstalled = tempPipeProxy.ShowAnalyzerInstalled();
                    }
                    catch { }
                    Monitor.Exit(_configLock);

                    SetUserLocale(); // ReInitialize MCEBuddy (we may have been disconnected and parameters changed, connected to a new engine etc)

                    // all good again, update the status
                    _pipeProxy = tempPipeProxy;
                    _engineConnected = true;
                    ProgressChanged();
                }
                catch (Exception e)
                {
                    // Check for version mistmatch
                    if ((e.InnerException is System.Runtime.Serialization.SerializationException) || (e is System.ServiceModel.ActionNotSupportedException))                    
                        _versionMismatch = true;
                    else
                        _versionMismatch = false;

                    //System.Diagnostics.EventLog.WriteEntry("MCEBuddy2x", Localise.GetPhrase("MCEBuddy GUI: Unable to create pipe to MCEBuddy service"), EventLogEntryType.Warning); //not required since this call will fail when MCEBuddy Service has not started and will overflow the eventlog. The message will be shown on the GUI
                    if (configLockTaken)
                    {
                        Monitor.Exit(_configLock);
                        configLockTaken = false;
                    }
                    _engineConnected = false; // Still broken
                    _pipeProxy = null;
                    ProgressChanged();
                    Thread.Sleep(_connectPeriod); // Try to connect every x seconds
                }
            }
            //
            // Reset our thread's priority back to its previous value
            //
            Thread.CurrentThread.Priority = priority;            
        }

        /// <summary>
        /// Change progress
        /// </summary>
        private void ProgressChanged()
        {            
            if (!_engineConnected)
            {
                if (_versionMismatch)
                    Microsoft.MediaCenter.UI.Application.DeferredInvoke(BackEngineStatus, EngineStatusEnum.VersionMismatch);
                else
                    Microsoft.MediaCenter.UI.Application.DeferredInvoke(BackEngineStatus, EngineStatusEnum.NotAvailable);
                Microsoft.MediaCenter.UI.Application.DeferredInvoke(BackNumWorks, 0);
                Microsoft.MediaCenter.UI.Application.DeferredInvoke(BackChangedJobsList, "");
                Microsoft.MediaCenter.UI.Application.DeferredInvoke(BackCurrentWorkName, "");
                Microsoft.MediaCenter.UI.Application.DeferredInvoke(BackCurrentWorkStatus, "");
                Microsoft.MediaCenter.UI.Application.DeferredInvoke(BackProcentComplete, NullPercent);
                Microsoft.MediaCenter.UI.Application.DeferredInvoke(BackEstimatedTime, "");
                //EngineStatus = false;
                return;
            }            

            try
            {
                if (EngineRunning())
                {            
                    //EngineStatus = true;
                    if (_pipeProxy.IsSuspended())
                    {
                        Microsoft.MediaCenter.UI.Application.DeferredInvoke(BackEngineStatus, EngineStatusEnum.Paused);
                    }
                    else
                    {
                        Microsoft.MediaCenter.UI.Application.DeferredInvoke(BackEngineStatus, EngineStatusEnum.Started);
                    }

                    _numJobs = _pipeProxy.NumConversionJobs(); // Get the updated number of jobs in the queue
                    //NumWorks = _numJobs;

                    // Display the file Queue
                    List<string[]> fileQueue = _pipeProxy.FileQueue();
                    List<ListItem> argsQueue = new List<ListItem>();
                    foreach (string[] fn in fileQueue)
                    {
                        ListItem arg = new ListItem();
                        arg.fileQueue = fn;
                        arg.isCurrent = false;
                        arg.Percent = 0;
                        argsQueue.Add(arg);
                    }
                    Microsoft.MediaCenter.UI.Application.DeferredInvoke(BackNumWorks, fileQueue.Count);                    

                    if (fileQueue.Count > 0)
                    {
                        int _curindex = 0;
                        if ((JobItemSelected >= 0) && (JobItemSelected <= _numJobs-1))
                        {
                            _curindex = JobItemSelected;
                        }
                        string[] fn = fileQueue[_curindex];                        
                        fn[0] = Path.GetFileName(fn[0]);
                        JobStatus job2 = _pipeProxy.GetJobStatus(_curindex);
                        //CurrentWorkName = fn[0];
                        Microsoft.MediaCenter.UI.Application.DeferredInvoke(BackCurrentWorkName, fn[0]);
                        
                        if (job2 == null)
                        {
                            //CurrentWorkStatus = "";
                            Microsoft.MediaCenter.UI.Application.DeferredInvoke(BackCurrentWorkStatus, "");
                            //ProcentComplete = 0;
                            Microsoft.MediaCenter.UI.Application.DeferredInvoke(BackProcentComplete, NullPercent);
                            Microsoft.MediaCenter.UI.Application.DeferredInvoke(BackEstimatedTime, "");
                        }
                        else if (job2.Cancelled)
                        {
                            //CurrentWorkStatus = job2.CurrentAction;
                            Microsoft.MediaCenter.UI.Application.DeferredInvoke(BackCurrentWorkStatus, job2.CurrentAction);
                            //ProcentComplete = (int)job2.PercentageComplete;
                            Microsoft.MediaCenter.UI.Application.DeferredInvoke(BackProcentComplete, (Single)job2.PercentageComplete);
                            Microsoft.MediaCenter.UI.Application.DeferredInvoke(BackEstimatedTime, job2.ETA);
                        }
                        else
                        {
                            //CurrentWorkStatus = job2.CurrentAction;
                            Microsoft.MediaCenter.UI.Application.DeferredInvoke(BackCurrentWorkStatus, job2.CurrentAction);
                            //ProcentComplete = (int)job2.PercentageComplete;                          
                            Microsoft.MediaCenter.UI.Application.DeferredInvoke(BackProcentComplete, (Single)job2.PercentageComplete);
                            Microsoft.MediaCenter.UI.Application.DeferredInvoke(BackEstimatedTime, job2.ETA);
                        }                        
                    } else
                    {
                        //CurrentWorkName = "";                        
                        Microsoft.MediaCenter.UI.Application.DeferredInvoke(BackCurrentWorkName, "");
                        //CurrentWorkStatus = "";
                        Microsoft.MediaCenter.UI.Application.DeferredInvoke(BackCurrentWorkStatus, "");
                        Microsoft.MediaCenter.UI.Application.DeferredInvoke(BackProcentComplete, NullPercent);
                        Microsoft.MediaCenter.UI.Application.DeferredInvoke(BackEstimatedTime, "");
                    }

                    //if (fileQueue.Count != JobsList.Count)
                    //{
                    //    Microsoft.MediaCenter.UI.Application.DeferredInvoke(BackChangedJobsList, fileQueue);
                    //}

                    // Set the job status
                    for (int i = 0; i < _numJobs; i++)
                    {
                        JobStatus job = _pipeProxy.GetJobStatus(i);
                        if (job == null)
                        {
                          // for future code
                        }
                        else if (job.Cancelled)
                        {
                            ListItem item = argsQueue[i];
                            item.isCurrent = true;
                            item.Percent = (double)job.PercentageComplete/100;
                            argsQueue[i] = item;
                        }
                        else
                        {
                            ListItem item = argsQueue[i];
                            item.isCurrent = true;
                            item.Percent = (double)job.PercentageComplete/100;
                            argsQueue[i] = item;
                        }
                    }
                    Microsoft.MediaCenter.UI.Application.DeferredInvoke(BackChangedJobsList, argsQueue);
                }
                else
                {                    
                    Microsoft.MediaCenter.UI.Application.DeferredInvoke(BackEngineStatus, EngineStatusEnum.Stopped);
                    Microsoft.MediaCenter.UI.Application.DeferredInvoke(BackNumWorks, 0);
                    Microsoft.MediaCenter.UI.Application.DeferredInvoke(BackChangedJobsList, "");
                    Microsoft.MediaCenter.UI.Application.DeferredInvoke(BackCurrentWorkName, "");
                    Microsoft.MediaCenter.UI.Application.DeferredInvoke(BackCurrentWorkStatus, "");
                    Microsoft.MediaCenter.UI.Application.DeferredInvoke(BackProcentComplete, NullPercent);
                    Microsoft.MediaCenter.UI.Application.DeferredInvoke(BackEstimatedTime, "");
                }

                // Process Priority, if it has changed
                processPriority = MCEBuddyConf.GlobalMCEConfig.GeneralOptions.processPriority;
                _lastProcessPriority = processPriority;
            }
            catch (Exception e1)
            {                
                Debug.WriteLine(e1.ToString());
            }
        }

        //
        // Called to start the search - if we're currently
        // in the stopped state, then change our status to 
        // Started and call DeferredInvokeOnWorkerThread to
        // kick off our search.
        //
        public void Start()
        {
            Ini tempIni = new Ini(GlobalDefs.TempSettingsFile);
            // Check if this is the first time the GUI is start after an installation
            string remoteServerName = tempIni.ReadString("Engine", "RemoteServerName", GlobalDefs.MCEBUDDY_SERVER_NAME);
            if (remoteServerName != GlobalDefs.MCEBUDDY_SERVER_NAME) // check if it's a remote machine
                _connectPeriod = GlobalDefs.REMOTE_ENGINE_POLL_PERIOD; // remote machine needs be pinged slower

            if (Status == RunningStatus.Stopped)
            {
                Status = RunningStatus.Started;

                Microsoft.MediaCenter.UI.Application.DeferredInvokeOnWorkerThread
                                (
                    // Delegate to be invoked on background thread
                                 TryConnect,

                                 // Delegate to be invoked on app thread
                                 ViewFinished,

                                 // Parameter to be passed to both delegates, we don't need it
                                 null
                                 );
            }
            else if (Status == RunningStatus.PendingStop)
            {
                Status = RunningStatus.Started;
            }
        }


        //
        // Called to stop a search in progress.
        // Sets our status to PendingStop, which is
        // a signal to the background search that it 
        // should terminate.
        //
        public void Stop()
        {
            if (Status == RunningStatus.Started)
            {
                Status = RunningStatus.PendingStop;
            }
        }

        public void StartEngine()
        {
            try
            {
                if (EngineStatus == EngineStatusEnum.Stopped)
                {
                    _pipeProxy.Start();
                }
                else if (_pipeProxy.IsSuspended())
                {
                    _pipeProxy.SuspendConversion(false);
                }
                else
                {
                    _pipeProxy.SuspendConversion(true);
                }
            }
            catch
            {
            }
        }

        public void StopEngine()
        {
            try
            {
                _pipeProxy.Stop(true);
            }
            catch
            {
            }
        }

        public void SettingsCmd()
        {
            Dictionary<string, object> properties = new Dictionary<string, object>();
            properties["BuddyViewer"] = this;
            if (session != null)
            {
                session.GoToPage("resx://MceBuddyViewer/MceBuddyViewer.Resources/SettingsForm", properties);
            }
            else
            {
                Debug.WriteLine("GoToMenu");
            }
        }

        public void AddFileCmd()
        {
            EditableItem.Value = ViewerSettings.Language.Translate["FileName"];
            string[] drives = Environment.GetLogicalDrives();            
            TreeViewVideoFile.ChildNodes.Clear();
            TreeViewVideoFile.CheckedNodes.Clear();            
            TreeViewVideoFile.CheckedNode = null;
            string VideoFilesFilter="*.wtv;*.dvr-ms;*.asf;*.avi;*.divx;*.dv;*.flv;*.gxf;*.m1v;*.m2v;*.m2ts;*.m4v;*.mkv;*.mov;*.mp2;*.mp4;*.mpeg;*.mpeg1;*.mpeg2;*.mpeg4;*.mpg;*.mts;*.mxf;*.ogm;*.ts;*.vob;*.wmv;*.tp";

            foreach (string curdrive in drives)
            {
                FileTreeNode nodefile = new FileTreeNode(curdrive, curdrive, VideoFilesFilter, TreeViewVideoFile);
                nodefile.Selectable = false;
                TreeViewVideoFile.ChildNodes.Add(nodefile);
            }
            Dictionary<string, object> properties = new Dictionary<string, object>();
            properties["BuddyViewer"] = this;

            if (session != null)
            {
                session.GoToPage("resx://MceBuddyViewer/MceBuddyViewer.Resources/SelectVideoFile", properties);                
            }
            else
            {
                Debug.WriteLine("GoToMenu");
            }
        }

        public void FileSelected()
        {
            if (TreeViewVideoFile.CheckedNodes.Count > 0)
            {
                foreach (FileTreeNode chnodes in TreeViewVideoFile.CheckedNodes)
                {
                    string videofile = chnodes.FullPath;
                    if (MCEBuddy.Util.Net.IsUNCPath(videofile))
                    {
                        Microsoft.MediaCenter.Hosting.AddInHost.Current.MediaCenterEnvironment.Dialog(Localise.GetPhrase("Warning: Networked files will be accessed using the logon credentials of the MCEBuddy Service, not the currently logged on user. Ensure the MCEBuddy service uses a user account that has local administrative access and access to the remote file [Control Panel -> Administrative Tools -> Services -> MCEBuddy]"), Localise.GetPhrase("Credential Warning"), (DialogButtons)1, 0, true);
                    }
                    addFileToQueue(videofile);
                }
                Rescan();
            }
            if (session != null)
            {
                session.BackPage();
            }
        }

        public void CancelFileCmd()
        {
            BackPage();
        }

        public void CancelSettingsCmd()
        {
            BackPage();
        }

        public void BackPage()
        {
            if (session != null)
            {
                session.BackPage();
            }
        }

        public void SaveSettings()
        {
            string _newlanguage = (string)SpinnerLanguages.Chosen;
            if (ViewerSettings.Language.CurrentLanguage != _newlanguage)
            {
                ViewerSettings.Language.CurrentLanguage = _newlanguage;                
            }
            ViewerSettings.SaveSettings();
            BackPage();            
        }

        private void addFileToQueue(string videoFile)
        {
            //if (MCEBuddy.Util.Net.IsUNCPath(videoFile))
                videoFile = MCEBuddy.Util.Net.GetUNCPath(videoFile);

            try
            {
                _pipeProxy.AddManualJob(videoFile);
                //_pipeProxy.Rescan();
            }
            catch (Exception)
            {
                System.Diagnostics.EventLog.WriteEntry("MCEBuddy2x", Localise.GetPhrase("MCEBuddy GUI: Unable to add job"), EventLogEntryType.Warning);
            }
        }

        //
        // This is the second DeferredHandler that we passed to
        // Application.DeferredInvokeOnWorkerThread; it 
        // is called after our background search has 
        // completed.
        //
        // Notice that since this is a deferred call,
        // (from Application.DeferredInvokeOnWorkerThread),        
        // we have to check IsDisposed before doing any work.
        // 

        private void ViewFinished(object obj)
        {
            if (!IsDisposed)
            {
                Status = RunningStatus.Stopped;
            }
        }

        public void Rescan()
        {
            try
            {
                _pipeProxy.Rescan();
            }
            catch (Exception e1)
            {
                Debug.WriteLine(e1.ToString());
            }
        }

        public void ListItemClicked()
        {
            try
            {                
                if (JobItemSelected >= 0)
                {
                    int[] indxList = new int[1];
                    indxList[0] = JobItemSelected;
                    _pipeProxy.CancelJob(indxList);
                }
            }
            catch
            {                
            }
        }        

        //
        // Our current running state
        // 
        public RunningStatus Status
        {
            get { return _status; }
            set
            {
                if (_status != value)
                {
                    _status = value;
                    FirePropertyChanged("Status");
                }
            }
        }

        public struct ListItem
        {
            public string[] fileQueue;
            public double Percent;
            public bool isCurrent;
        }

        public enum RunningStatus
        {
            Started,
            PendingStop,
            Stopped
        }

        public enum EngineStatusEnum
        {
            Started,
            Stopped,
            Paused,
            NotAvailable,
            VersionMismatch
        }

    }
}