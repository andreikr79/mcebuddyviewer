using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.XPath;
using System.Drawing;

using MCEBuddy.AppWrapper;
using MCEBuddy.Globals;
using MCEBuddy.VideoProperties;
using MCEBuddy.Util;
using MCEBuddy.Configuration;

namespace MCEBuddy.MetaData
{
    public class VideoMetaData
    {
        private string _videoFileName;
        private VideoTags _videoTags = new VideoTags();
        private bool _downloadSeriesDetails = true;
        private bool _downloadBannerFile = true;
        private string _tivoMAKKey = "";

        protected JobStatus _jobStatus;
        protected Log _jobLog;

        public VideoTags MetaData
        { get { return _videoTags; } }

        /// <summary>
        /// Extract the metadata from the video file (WTV/DVRMS/MP4/MKV/AVI/TS XML) and supplement with information downloaded from TVDB and MovieDB.
        /// Does not extract XML files from TiVO files.
        /// </summary>
        /// <param name="videoFileName">Full path to the video from which to extract the Metadata</param>
        /// <param name="downloadSeriesDetails">Supplement metadata with additional information downloaded from TVDB and MovieDB</param>
        public VideoMetaData(string videoFileName, bool downloadSeriesDetails, ref JobStatus jobStatus, Log jobLog)
        {
            _videoFileName = videoFileName;
            _downloadSeriesDetails = downloadSeriesDetails;
            _jobStatus = jobStatus;
            _jobLog = jobLog;
        }
        
        /// <summary>
        /// Extract the metadata from the video file (WTV/DVRMS/MP4/MKV/AVI/TS XML) and supplement with information downloaded from TVDB and MovieDB
        /// </summary>
        /// <param name="cjo">Conversion job options</param>
        /// <param name="disableDownloadSeriesDetails">(Optional) True if you want to override adn disable the DownloadSeriesDetails option from TVDB/MovieDB</param>
        public VideoMetaData(ConversionJobOptions cjo, ref JobStatus jobStatus, Log jobLog, bool disableDownloadSeriesDetails = false)
        {
            _videoFileName = cjo.sourceVideo;
            _downloadSeriesDetails = (cjo.downloadSeriesDetails && !disableDownloadSeriesDetails); // special case, if want to override it
            _downloadBannerFile = (cjo.downloadBanner && !disableDownloadSeriesDetails);
            _videoTags.tvdbSeriesId = cjo.tvdbSeriesId;
            _videoTags.imdbMovieId = cjo.imdbSeriesId;
            _jobStatus = jobStatus;
            _jobLog = jobLog;
            _tivoMAKKey = cjo.tivoMAKKey;
        }

        /// <summary>
        /// Extract metadata from the file and downloading supplemental from the internet if required
        /// </summary>
        public void Extract()
        {
            string ext = Path.GetExtension(_videoFileName).ToLower();
            if ((ext == ".wtv") || (ext == ".dvr-ms"))
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Extracting MCE Tags"), Log.LogEntryType.Information);
                ExtractMCETags();
            }
            else
            {
                if (!ExtractXMLMetadata())
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Extracting Generic Tags"), Log.LogEntryType.Information);
                    ExtractGenericTags();
                }
            }

            if (String.IsNullOrWhiteSpace(_videoTags.Title))
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("No recording meta data can be extracted, using Title extracted from file name. If you are running MCEBuddy on a Windows Server, this is normal as the Media Center filters are not available on that platfom."), Log.LogEntryType.Warning);
                
                // NextPVR and some software use the following format -> SHOWNAME_AIRDATE_AIRTIME.<ext> - AIRDATE - YYYYMMDD, AIRTIME - HHMMHHMM (Start and End)
                string baseFileName = Path.GetFileNameWithoutExtension(_videoFileName);
                int pos = baseFileName.IndexOf("_");
                if ( pos > 0)
                {
                    _videoTags.Title = baseFileName.Substring(0, pos); // SHOWNAME
                    int pos1 = baseFileName.IndexOf("_", pos + 1);
                    if (pos1 == -1) // sometime the AIRTIME may be missing
                        pos1 = baseFileName.Length; // Try to parse to end
                    if ((pos1 > 0) && (pos1 > pos))
                    {
                        string date = baseFileName.Substring(pos + 1, pos1 - pos - 1);
                        if (DateTime.TryParseExact(date, "yyyyMMdd", null, System.Globalization.DateTimeStyles.AssumeLocal, out _videoTags.OriginalBroadcastDateTime)) // AIRDATE - Assume it's reported in local date/time to avoid messing up the comparison
                            _jobLog.WriteEntry(this, "Extracted Original Broadcast Date from Title -> " + _videoTags.OriginalBroadcastDateTime.ToString("YYYY-MM-DD"), Log.LogEntryType.Debug);
                    }

                }
                else
                {
                    _videoTags.Title = baseFileName;
                }

                _jobLog.WriteEntry(this, "Extracted Showname from Title -> " + _videoTags.Title, Log.LogEntryType.Debug);
            }

            DownloadSeriesDetails();

            _jobLog.WriteEntry(this, Localise.GetPhrase("Video Tags extracted") + " -> " + _videoTags.ToString(), Log.LogEntryType.Debug);

            if (_videoTags.CopyProtected)
                _jobLog.WriteEntry(this, Localise.GetPhrase("ERROR: VIDEO IS COPYPROTECTED. CONVERSION WILL FAIL"), Log.LogEntryType.Warning);
        }

        /// <summary>
        /// Download metadata from TVDB and MovieDB including banners
        /// </summary>
        private void DownloadSeriesDetails()
        {
            if ((_downloadSeriesDetails) && (!String.IsNullOrWhiteSpace(_videoTags.Title)))
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Downloading Series details"), Log.LogEntryType.Information);

                if (_downloadBannerFile)
                    _videoTags.BannerFile = Path.Combine(GlobalDefs.CachePath, Util.FilePaths.RemoveIllegalFilePathChars(_videoTags.Title) + ".jpg");
                else
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Skipping downloading Banner file"), Log.LogEntryType.Information);
                    _videoTags.BannerFile = "";
                }

                bool downloadSuccess = false;
                // Check them all in succession, each add more information to the previous
                if (_videoTags.IsMovie) // For movies we have different matching logic
                {
                    _jobLog.WriteEntry(this, "Recording Type Movie", Log.LogEntryType.Information);

                    _jobLog.WriteEntry(this, "Checking IMDB", Log.LogEntryType.Information);
                    if (!_jobStatus.Cancelled) // These are log jobs, incase the user presses cancelled, it should not fall through
                        if ((downloadSuccess |= IMDB.DownloadMovieDetails(ref _videoTags)) == false)
                            _jobLog.WriteEntry(this, Localise.GetPhrase("IMDB failed"), Log.LogEntryType.Warning);

                    _jobLog.WriteEntry(this, "Checking TheMovieDB", Log.LogEntryType.Information);
                    if (!_jobStatus.Cancelled)
                        if ((downloadSuccess |= TheMovieDB.DownloadMovieDetails(ref _videoTags)) == false)
                            _jobLog.WriteEntry(this, Localise.GetPhrase("TheMovieDB failed"), Log.LogEntryType.Warning);
                }
                else
                {
                    _jobLog.WriteEntry(this, "Recording Type Show", Log.LogEntryType.Information);

                    _jobLog.WriteEntry(this, Localise.GetPhrase("Checking TheTVDB"), Log.LogEntryType.Information);
                    if (!_jobStatus.Cancelled)
                        if ((downloadSuccess |= TheTVDB.DownloadSeriesDetails(ref _videoTags)) == false)
                            _jobLog.WriteEntry(this, Localise.GetPhrase("TheTVDB failed"), Log.LogEntryType.Warning);

                    _jobLog.WriteEntry(this, "Checking TheMovieDB", Log.LogEntryType.Information);
                    if (!_jobStatus.Cancelled)
                        if ((downloadSuccess |= IMDB.DownloadSeriesDetails(ref _videoTags)) == false)
                            _jobLog.WriteEntry(this, Localise.GetPhrase("IMDB failed"), Log.LogEntryType.Warning);
                    
                    _jobLog.WriteEntry(this, "Checking TV.com", Log.LogEntryType.Information);
                    if (!_jobStatus.Cancelled)
                        if ((downloadSuccess |= TV.DownloadSeriesDetails(ref _videoTags)) == false) // Last one to check since it contains the least amount of information
                            _jobLog.WriteEntry(this, "TV.com failed", Log.LogEntryType.Warning);
                }

                if (!downloadSuccess)
                {   
                    // Send an eMail if required
                    if (MCEBuddyConf.GlobalMCEConfig.GeneralOptions.sendEmail && MCEBuddyConf.GlobalMCEConfig.GeneralOptions.eMailSettings.downloadFailedEvent)
                    {
                        string subject = Localise.GetPhrase("MCEBuddy unable to download series information");
                        string message = Localise.GetPhrase("Source Video") + " -> " + _videoFileName + "\r\n";
                        message += Localise.GetPhrase("Failed At") + " -> " + DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture);
                        message += "\r\n" + _videoTags.ToString();

                        new Thread(() => eMail.SendEMail(MCEBuddyConf.GlobalMCEConfig.GeneralOptions.eMailSettings, subject, message, ref Log.AppLog, this)).Start(); // Send the eMail through a thead
                    }
                }
            }
            else
                _jobLog.WriteEntry(this, Localise.GetPhrase("Skipping downloading Series details"), Log.LogEntryType.Information);

            if (!File.Exists(_videoTags.BannerFile) || (FileIO.FileSize(_videoTags.BannerFile) <= 0)) // Need to avoid an exception while writing tags
                _videoTags.BannerFile = "";
        }

        /// <summary>
        /// Extract metadata from WTV or DVRMS file
        /// </summary>
        private void ExtractMCETags()
        {
            _jobLog.WriteEntry(this, Localise.GetPhrase("Extracting Windows Media Center meta data"),Log.LogEntryType.Debug);
            try
            {
                using (MetadataEditor editor = (MetadataEditor)new MCRecMetadataEditor(_videoFileName))
                {
                    IDictionary attrs = editor.GetAttributes();
                    _videoTags.Title = GetMetaTagStringValue(attrs, "Title");
                    _videoTags.SubTitle = GetMetaTagStringValue(attrs, "WM/SubTitle");
                    _videoTags.Description = GetMetaTagStringValue(attrs, "WM/SubTitleDescription");
                    _videoTags.Genres = GetMetaTagStringValue(attrs, "WM/Genre").Split(';');
                    _videoTags.Network = GetMetaTagStringValue(attrs, "WM/MediaStationName");
                    _videoTags.CopyProtected = GetMetaTagBoolValue(attrs, "WM/WMRVContentProtected");
                    if (GetMetaTagStringValue(attrs, "WM/MediaOriginalBroadcastDateTime") != "")
                    {
                        // DateTime is reported along with timezone info (typically Z i.e. UTC hence assume None)
                        DateTime.TryParse(GetMetaTagStringValue(attrs, "WM/MediaOriginalBroadcastDateTime"), null, System.Globalization.DateTimeStyles.None, out _videoTags.OriginalBroadcastDateTime);
                    }
                    if (GetMetaTagInt64Value(attrs, "WM/WMRVEncodeTime") != 0)
                    {
                        // Stored in UTC ticks hence assume Universal
                        DateTime.TryParse((new DateTime(GetMetaTagInt64Value(attrs, "WM/WMRVEncodeTime"))).ToString(System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out _videoTags.RecordedDateTime);
                    }
                    _videoTags.IsMovie = GetMetaTagBoolValue(attrs, "WM/MediaIsMovie");
                }
            }
            catch (Exception e)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Unable to extract meta data using filters.  Filters may not be present [eg. Windows Server].\nError : " + e.ToString()), Log.LogEntryType.Warning);
                if (Path.GetExtension(_videoFileName).ToLower() == ".dvr-ms")
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Attempting raw container DVR-MS read"), Log.LogEntryType.Information);
                    ExtractGenericTags();
                }
            }
        }

        /// <summary>
        /// Write the Tags / Metadata for a WTV/DVR-MS file. If the source is a WTV/DVR-MS file it copies all the metadata, else it copies individual components
        /// </summary>
        /// <param name="convertedFile">Path to WTV/DVR-MS file</param>
        private void WriteMCETags(string convertedFile)
        {
            _jobLog.WriteEntry(this, Localise.GetPhrase("Setting WTV/DVR-MS metadata"), Log.LogEntryType.Debug);

            try
            {
                IDictionary sourceAttrs;

                if ((Util.FilePaths.CleanExt(_videoFileName) == ".wtv") || (Util.FilePaths.CleanExt(_videoFileName) == ".dvr-ms")) // Copy all WTV/DVRMS Metadata
                {
                    using (MetadataEditor editor = (MetadataEditor)new MCRecMetadataEditor(_videoFileName)) // Get All Attributes from the Source file
                    {
                        sourceAttrs = editor.GetAttributes(true); // Get All Attributes from the Source file
                    }
                }
                else // Try to copy as many attributes as possible if the source is not WTV or DVRMS
                {
                    sourceAttrs = new Hashtable();
                    if (!String.IsNullOrWhiteSpace(_videoTags.Title)) sourceAttrs.Add("Title", new MetadataItem("Title", _videoTags.Title, DirectShowLib.SBE.StreamBufferAttrDataType.String));
                    if (!String.IsNullOrWhiteSpace(_videoTags.SubTitle)) sourceAttrs.Add("WM/SubTitle", new MetadataItem("WM/SubTitle", _videoTags.SubTitle, DirectShowLib.SBE.StreamBufferAttrDataType.String));
                    if (!String.IsNullOrWhiteSpace(_videoTags.Description)) sourceAttrs.Add("WM/SubTitleDescription", new MetadataItem("WM/SubTitleDescription", _videoTags.Description, DirectShowLib.SBE.StreamBufferAttrDataType.String));
                    if (_videoTags.Genres != null) if (_videoTags.Genres.Length > 0) sourceAttrs.Add("WM/Genre", new MetadataItem("WM/Genre", _videoTags.Genres[0], DirectShowLib.SBE.StreamBufferAttrDataType.String));
                    if (!String.IsNullOrWhiteSpace(_videoTags.Network)) sourceAttrs.Add("WM/MediaStationName", new MetadataItem("WM/MediaStationName", _videoTags.Network, DirectShowLib.SBE.StreamBufferAttrDataType.String));
                    if (_videoTags.OriginalBroadcastDateTime > GlobalDefs.NO_BROADCAST_TIME) sourceAttrs.Add("WM/MediaOriginalBroadcastDateTime", new MetadataItem("WM/MediaOriginalBroadcastDateTime", _videoTags.OriginalBroadcastDateTime.ToString("s") + "Z", DirectShowLib.SBE.StreamBufferAttrDataType.String)); // It is stored a UTC value, we just need to add "Z" at the end to indicate it
                    if (_videoTags.RecordedDateTime > GlobalDefs.NO_BROADCAST_TIME) sourceAttrs.Add("WM/WMRVEncodeTime", new MetadataItem("WM/WMRVEncodeTime", _videoTags.RecordedDateTime.Ticks, DirectShowLib.SBE.StreamBufferAttrDataType.QWord));
                    sourceAttrs.Add("WM/MediaIsMovie", new MetadataItem("WM/MediaIsMovie", _videoTags.IsMovie, DirectShowLib.SBE.StreamBufferAttrDataType.Bool));
                }

                using (MetadataEditor editor = (MetadataEditor)new MCRecMetadataEditor(convertedFile)) // Write All Attributes to the Converted file
                {
                    editor.SetAttributes(sourceAttrs); // Set All Attributes to the converted file
                }
            }
            catch (Exception e)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Unable to set WTV meta data using filters.  Filters may not be present [eg. Windows Server].\nError : " + e.ToString()), Log.LogEntryType.Warning);
            }
        }

        private Int64 GetMetaTagInt64Value(IDictionary attrs, string Key)
        {
            if (attrs.Contains(Key))
            {
                MetadataItem item = (MetadataItem)attrs[Key];
                return (Int64) item.Value;
            }
            else return 0;
        }

        private string GetMetaTagStringValue(IDictionary attrs, string Key)
        {
            if (attrs.Contains(Key))
            {
                MetadataItem item = (MetadataItem)attrs[Key];
                return item.Value.ToString();
            }
            else return "";
        }

        private bool GetMetaTagBoolValue(IDictionary attrs, string Key)
        {
            if (attrs.Contains(Key))
            {
                MetadataItem item = (MetadataItem)attrs[Key];
                bool ret;
                bool.TryParse(item.Value.ToString(), out ret);
                return ret;
            }
            else return false;
        }

        /// <summary>
        /// Create an XML file containing the XBMC compatible information file.
        /// Refer to http://wiki.xbmc.org/index.php?title=Import_-_Export_Library for more details
        /// </summary>
        /// <param name="sourceVideo">Full path to source video</param>
        /// <param name="workingPath">Full path to working temp directory</param>
        /// <param name="videoFile">VideoFile metadata structure</param>
        /// <returns></returns>
        public bool WriteXBMCXMLTags(string sourceVideo, string workingPath, VideoInfo videoFile)
        {
            string xmlFileName = Path.Combine(workingPath, Path.GetFileNameWithoutExtension(sourceVideo) + ".nfo"); // Path\FileName.nfo

            _jobLog.WriteEntry(this, Localise.GetPhrase("Extracting info from source video into NFO file (XML)"), Log.LogEntryType.Information);

            // Check if the NFO file exists with the source video and then copy it else extract it
            string sourceXMLFile = Path.Combine(Path.GetDirectoryName(sourceVideo), Path.GetFileNameWithoutExtension(sourceVideo) + ".nfo");
            if (File.Exists(sourceXMLFile) && (sourceXMLFile.ToLower() != xmlFileName.ToLower())) // check incase the source and destination are the same
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Found source NFO file, copying to output folder"), Log.LogEntryType.Information);
                Util.FileIO.TryFileDelete(xmlFileName); // just in case target exists, delete it
                File.Copy(sourceXMLFile, xmlFileName);
                return true;
            }

            try
            {
                Util.FileIO.TryFileDelete(xmlFileName); // just incase it exists
                using (MetadataEditor editor = (MetadataEditor)new MCRecMetadataEditor(_videoFileName))
                {
                    // Get the WTV/DVRMS attributes
                    IDictionary attrs = editor.GetAttributes();

                    // Create the XMLFile
                    XmlWriterSettings set = new XmlWriterSettings();
                    set.Indent = true;
                    set.IndentChars = "\t";
                    set.Encoding = Encoding.UTF8;
                    using (XmlWriter writer = XmlWriter.Create(xmlFileName, set))
                    {
                        // If this is a TV Espisode
                        if (_videoTags.IsMovie == false)
                        {
                            writer.WriteStartDocument();
                            writer.WriteStartElement("episodedetails");

                            writer.WriteElementString("title", _videoTags.SubTitle);
                            writer.WriteElementString("season", (_videoTags.Season == 0 ? "" : _videoTags.Season.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                            writer.WriteElementString("episode", (_videoTags.Season == 0 ? "" : _videoTags.Episode.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                            writer.WriteElementString("plot", _videoTags.Description);
                            writer.WriteElementString("thumb", _videoTags.BannerURL);
                            writer.WriteElementString("id", _videoTags.tvdbSeriesId);
                            writer.WriteElementString("credits", GetMetaTagStringValue(attrs, "WM/MediaCredits"));
                            writer.WriteElementString("aired", (_videoTags.RecordedDateTime > GlobalDefs.NO_BROADCAST_TIME ? _videoTags.RecordedDateTime.ToLocalTime().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : ""));
                            writer.WriteElementString("premiered", (_videoTags.OriginalBroadcastDateTime > GlobalDefs.NO_BROADCAST_TIME ? _videoTags.OriginalBroadcastDateTime.ToLocalTime().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : ""));
                            writer.WriteElementString("studio", _videoTags.Network);

                            writer.WriteStartElement("fileinfo");
                            writer.WriteStartElement("streamdetails");

                            writer.WriteStartElement("audio");
                            writer.WriteElementString("channels", videoFile.AudioChannels.ToString(System.Globalization.CultureInfo.InvariantCulture));
                            writer.WriteElementString("codec", videoFile.AudioCodec);
                            writer.WriteEndElement();

                            writer.WriteStartElement("video");
                            writer.WriteElementString("codec", videoFile.VideoCodec);
                            writer.WriteElementString("durationinseconds", ((int)videoFile.Duration).ToString(System.Globalization.CultureInfo.InvariantCulture));
                            writer.WriteElementString("height", videoFile.Height.ToString(System.Globalization.CultureInfo.InvariantCulture));
                            writer.WriteElementString("language", videoFile.AudioLanguage); // TODO: How can we use WM/Language to get original (e.g. "en-us")
                            writer.WriteElementString("longlanguage", ISO639_3.GetLanguageName(videoFile.AudioLanguage));
                            writer.WriteElementString("width", videoFile.Width.ToString(System.Globalization.CultureInfo.InvariantCulture));
                            writer.WriteEndElement();

                            writer.WriteEndElement();
                            writer.WriteEndElement();

                            writer.WriteEndElement();
                            writer.WriteEndDocument();
                        }
                        else // This for a movie
                        {
                            writer.WriteStartDocument();
                            writer.WriteStartElement("movie");

                            writer.WriteElementString("title", _videoTags.Title);
                            writer.WriteElementString("year", GetMetaTagStringValue(attrs, "WM/OriginalReleaseTime"));
                            writer.WriteElementString("outline", _videoTags.Description);
                            writer.WriteElementString("plot", _videoTags.Description);
                            writer.WriteElementString("runtime", ((int)(videoFile.Duration / 60)).ToString(System.Globalization.CultureInfo.InvariantCulture));
                            writer.WriteElementString("thumb", _videoTags.BannerURL);
                            writer.WriteElementString("id", _videoTags.imdbMovieId);
                            writer.WriteElementString("genre", (_videoTags.Genres == null ? "" : _videoTags.Genres[0]));
                            writer.WriteElementString("credits", GetMetaTagStringValue(attrs, "WM/MediaCredits"));

                            writer.WriteStartElement("fileinfo");
                            writer.WriteStartElement("streamdetails");

                            writer.WriteStartElement("video");
                            writer.WriteElementString("codec", videoFile.VideoCodec);
                            writer.WriteElementString("width", videoFile.Width.ToString(System.Globalization.CultureInfo.InvariantCulture));
                            writer.WriteElementString("height", videoFile.Height.ToString(System.Globalization.CultureInfo.InvariantCulture));
                            writer.WriteEndElement();

                            writer.WriteStartElement("audio");
                            writer.WriteElementString("codec", videoFile.AudioCodec);
                            writer.WriteElementString("language", videoFile.AudioLanguage); // TODO: How can we use WM/Language to get original (e.g. "en-us")
                            writer.WriteElementString("channels", videoFile.AudioChannels.ToString(System.Globalization.CultureInfo.InvariantCulture));
                            writer.WriteEndElement();

                            writer.WriteEndElement();
                            writer.WriteEndElement();

                            writer.WriteEndElement();
                            writer.WriteEndDocument();
                        }
                    }
                }

                return true;
            }
            catch (Exception e)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Unable to extract meta data using filters.  Filters may not be present [eg. Windows Server].\nError : " + e.ToString()), Log.LogEntryType.Warning);
                return false;
            }
        }

        /// <summary>
        /// Extracts XML Metadata from XML/NFO file checking for different formats such as Media Portal, nPVR etc
        /// </summary>
        private bool ExtractXMLMetadata()
        {
            bool retVal = false;
            bool tivoXMLGen = false;

            string xmlFile = Util.FilePaths.GetFullPathWithoutExtension(_videoFileName) + ".xml";
            string argFile = Util.FilePaths.GetFullPathWithoutExtension(_videoFileName) + ".arg";

            if (Util.FilePaths.CleanExt(_videoFileName) == ".tivo") // First extract the metadata
                if (!File.Exists(xmlFile))
                    if (ExtractTiVOMetadata(xmlFile))
                        tivoXMLGen = true; // We generated a TiVO XML file, clean up later

            if (File.Exists(xmlFile))
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Extracting XML Tags"), Log.LogEntryType.Information);
                
                retVal |= ExtractMPTags(xmlFile); // Media Portal
                retVal |= ExtractNPVRTags(xmlFile); // nPVR
                retVal |= ExtractTiVOTags(xmlFile); // TiVO

                if (tivoXMLGen) // Delete the XML file generated, leave no artifacts behind
                    Util.FileIO.TryFileDelete(xmlFile);

                return retVal; // Processed
            }
            else if (File.Exists(argFile))
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Extracting ARG Tags"), Log.LogEntryType.Information);
                retVal |= ExtractArgusTVTags(argFile); // ArgusTV

                return retVal; // Processed
            }

            return false; // No XML files here to process, try something else
        }

        /// <summary>
        /// Extract the metadata from the TiVO file into a XML file
        /// </summary>
        /// <param name="xmlFile">XML File to create</param>
        private bool ExtractTiVOMetadata(string xmlFile)
        {
            _jobLog.WriteEntry(this, "Extracting TiVO metadata using TDCat", Log.LogEntryType.Information);

            string tivoMetaParams = "";

            if (String.IsNullOrWhiteSpace(_tivoMAKKey))
            {
                _jobLog.WriteEntry(this, "No TiVO MAK key found, cannot extract TiVO Metadata", Log.LogEntryType.Error);
                return false;
            }

            tivoMetaParams += "-m " + _tivoMAKKey + " -1 -o " + Util.FilePaths.FixSpaces(xmlFile) + " " + Util.FilePaths.FixSpaces(_videoFileName);
            TiVOMetaExtract tivoExtract = new TiVOMetaExtract(tivoMetaParams, ref _jobStatus, _jobLog);
            tivoExtract.Run();

            if ((Util.FileIO.FileSize(xmlFile) <= 0) || !tivoExtract.Success)
            {
                Util.FileIO.TryFileDelete(xmlFile);
                _jobLog.WriteEntry(this, "TiVODecode failed to extact XML Metadata", Log.LogEntryType.Error);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Extracts Metadata from the XML file written in TiVO format
        /// </summary>
        /// <param name="XMLFile">Path to XML file</param>
        private bool ExtractTiVOTags(string XMLFile)
        {
            bool retVal = false; // no success as yet

            _jobLog.WriteEntry(this, Localise.GetPhrase("Extracting TiVO meta data"), Log.LogEntryType.Debug);

            try
            {
                XPathDocument Xp;
                if (File.Exists(XMLFile))
                {
                    try
                    {
                        Xp = new XPathDocument(XMLFile);
                    }
                    catch
                    {
                        _jobLog.WriteEntry(this, "Invalid XML File", Log.LogEntryType.Error);
                        return false;
                    }

                    XPathNavigator Nav = Xp.CreateNavigator();
                    XmlNamespaceManager manager = new XmlNamespaceManager(Nav.NameTable);
                    manager.AddNamespace("TvBusMarshalledStruct", "http://tivo.com/developer/xml/idl/TvBusMarshalledStruct");
                    XPathNodeIterator Itr = Nav.Select("//TvBusMarshalledStruct:TvBusEnvelope/vActualShowing/element", manager);
                    while (Itr.MoveNext())
                    {
                        if (_videoTags.RecordedDateTime <= GlobalDefs.NO_BROADCAST_TIME)
                        {
                            string recordedDateTime = XML.GetXMLTagValue("time", Itr.Current.OuterXml);
                            DateTime.TryParse(recordedDateTime, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeLocal, out _videoTags.RecordedDateTime);
                        }
                    }

                    Itr = Nav.Select("//TvBusMarshalledStruct:TvBusEnvelope/vActualShowing/element/program", manager);
                    while (Itr.MoveNext())
                    {
                        string showType = XML.GetXMLTagValue("showType", Itr.Current.OuterXml);
                        if (!String.IsNullOrWhiteSpace(showType))
                            if (showType.ToLower().Contains("movie"))
                                _videoTags.IsMovie = true;
                        string movieYear = XML.GetXMLTagValue("movieYear", Itr.Current.OuterXml).ToLower().Trim();
                        if (!String.IsNullOrWhiteSpace(movieYear)) // Use the movie year to set the original broadcast date as YYYY-05-05 (mid year to avoid timezone issues)
                            DateTime.TryParse(movieYear + "-05-05", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeLocal, out _videoTags.OriginalBroadcastDateTime);
                        if (String.IsNullOrWhiteSpace(_videoTags.Title)) _videoTags.Title = XML.GetXMLTagValue("title", Itr.Current.OuterXml);
                        if (String.IsNullOrWhiteSpace(_videoTags.SubTitle)) _videoTags.SubTitle = XML.GetXMLTagValue("episodeTitle", Itr.Current.OuterXml);
                        if (String.IsNullOrWhiteSpace(_videoTags.Description)) _videoTags.Description = XML.GetXMLTagValue("description", Itr.Current.OuterXml);
                        if (_videoTags.Genres == null) _videoTags.Genres = XML.GetXMLSubTagValues("vProgramGenre", "element", Itr.Current.OuterXml);
                        if (_videoTags.OriginalBroadcastDateTime <= GlobalDefs.NO_BROADCAST_TIME)
                        {
                            string airDateTime = XML.GetXMLTagValue("originalAirDate", Itr.Current.OuterXml);
                            DateTime.TryParse(airDateTime, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeLocal, out _videoTags.OriginalBroadcastDateTime);
                        }

                        retVal = true; // We got what we wanted here
                    }
                }
            }
            catch (Exception e)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Error extracting TiVO meta data : " + e.ToString()), Log.LogEntryType.Warning);
                return false;
            }

            return retVal;
        }

        /// <summary>
        /// Extracts Metadata from the ARG (XML) file written in ArgusTV format
        /// </summary>
        /// <param name="XMLFile">Path to XML file</param>
        private bool ExtractArgusTVTags(string XMLFile)
        {
            bool retVal = false; // no success as yet

            _jobLog.WriteEntry(this, Localise.GetPhrase("Extracting ArgusTV meta data"), Log.LogEntryType.Debug);

            try
            {
                XPathDocument Xp;
                if (File.Exists(XMLFile))
                {
                    try
                    {
                        Xp = new XPathDocument(XMLFile);
                    }
                    catch
                    {
                        _jobLog.WriteEntry(this, "Invalid XML File", Log.LogEntryType.Error);
                        return false;
                    }

                    XPathNavigator Nav = Xp.CreateNavigator();
                    XPathExpression Exp = Nav.Compile("//Recording");
                    XPathNodeIterator Itr = Nav.Select(Exp);
                    while (Itr.MoveNext())
                    {
                        if (String.IsNullOrWhiteSpace(_videoTags.Title)) _videoTags.Title = XML.GetXMLTagValue("Title", Itr.Current.OuterXml);
                        if (String.IsNullOrWhiteSpace(_videoTags.SubTitle)) _videoTags.SubTitle = XML.GetXMLTagValue("SubTitle", Itr.Current.OuterXml);
                        if (String.IsNullOrWhiteSpace(_videoTags.Description)) _videoTags.Description = XML.GetXMLTagValue("Description", Itr.Current.OuterXml);
                        if (_videoTags.Episode == 0) int.TryParse(XML.GetXMLTagValue("EpisodeNumber", Itr.Current.OuterXml), out _videoTags.Episode);
                        if (_videoTags.Season == 0) int.TryParse(XML.GetXMLTagValue("SeriesNumber", Itr.Current.OuterXml), out _videoTags.Season);
                        if (String.IsNullOrWhiteSpace(_videoTags.Network)) _videoTags.Network = XML.GetXMLTagValue("ChannelDisplayName", Itr.Current.OuterXml);
                        if (_videoTags.RecordedDateTime <= GlobalDefs.NO_BROADCAST_TIME)
                        {
                            string recordedDateTime = XML.GetXMLTagValue("RecordingStartTime", Itr.Current.OuterXml);
                            DateTime.TryParse(recordedDateTime, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeLocal, out _videoTags.RecordedDateTime);
                        }

                        retVal = true; // We got what we wanted here
                    }
                }
            }
            catch (Exception e)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Error extracting ArgusTV meta data : " + e.ToString()), Log.LogEntryType.Warning);
                return false;
            }

            return retVal;
        }

        /// <summary>
        /// Extracts Metadata from the XML file written in nPVR format
        /// </summary>
        /// <param name="XMLFile">Path to XML file</param>
        private bool ExtractNPVRTags(string XMLFile)
        {
            bool retVal = false; // no success as yet

            _jobLog.WriteEntry(this, Localise.GetPhrase("Extracting nPVR meta data"), Log.LogEntryType.Debug);

            try
            {
                XPathDocument Xp;
                if (File.Exists(XMLFile))
                {
                    try
                    {
                        Xp = new XPathDocument(XMLFile);
                    }
                    catch
                    {
                        _jobLog.WriteEntry(this, "Invalid XML File", Log.LogEntryType.Error);
                        return false;
                    }

                    XPathNavigator Nav = Xp.CreateNavigator();
                    XPathExpression Exp = Nav.Compile("//recording");
                    XPathNodeIterator Itr = Nav.Select(Exp);
                    while (Itr.MoveNext())
                    {
                        if (String.IsNullOrWhiteSpace(_videoTags.Title)) _videoTags.Title = XML.GetXMLTagValue("title", Itr.Current.OuterXml);
                        if (String.IsNullOrWhiteSpace(_videoTags.SubTitle)) _videoTags.SubTitle = XML.GetXMLTagValue("subtitle", Itr.Current.OuterXml);
                        if (String.IsNullOrWhiteSpace(_videoTags.Description)) _videoTags.Description = XML.GetXMLTagValue("description", Itr.Current.OuterXml);
                        if (String.IsNullOrWhiteSpace(_videoTags.Network)) _videoTags.Network = XML.GetXMLTagValue("channel", Itr.Current.OuterXml);
                        if (_videoTags.Episode == 0) int.TryParse(XML.GetXMLTagValue("episode", Itr.Current.OuterXml), out _videoTags.Episode);
                        if (_videoTags.Season == 0) int.TryParse(XML.GetXMLTagValue("season", Itr.Current.OuterXml), out _videoTags.Season);
                        if (_videoTags.Genres == null) _videoTags.Genres = XML.GetXMLSubTagValues("genres", "genre", Itr.Current.OuterXml);
                        if (_videoTags.RecordedDateTime <= GlobalDefs.NO_BROADCAST_TIME)
                        {
                            string recordedDateTime = XML.GetXMLTagValue("startTime", Itr.Current.OuterXml);
                            DateTime.TryParse(recordedDateTime, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeLocal, out _videoTags.RecordedDateTime);
                        }
                        if (_videoTags.OriginalBroadcastDateTime <= GlobalDefs.NO_BROADCAST_TIME)
                        {
                            string airDateTime = XML.GetXMLTagValue("original_air_date", Itr.Current.OuterXml);
                            DateTime.TryParse(airDateTime, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeLocal, out _videoTags.OriginalBroadcastDateTime);
                        }

                        retVal = true; // We got what we wanted here
                    }
                }
            }
            catch (Exception e)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Error extracting nPVR meta data : " + e.ToString()), Log.LogEntryType.Warning);
                return false;
            }

            return retVal;
        }

        /// <summary>
        /// Extracts tags from Media Portal compliant XML/NFO file
        /// </summary>
        /// <param name="XMLFile">Path to XML/NFO file</param>
        private bool ExtractMPTags(string XMLFile)
        {
            bool retVal = false; // no success as yet

            _jobLog.WriteEntry(this, Localise.GetPhrase("Extracting MediaPortal meta data"), Log.LogEntryType.Debug);

            try
            {
                XPathDocument Xp;
                if (File.Exists(XMLFile))
                {
                    try
                    {
                        Xp = new XPathDocument(XMLFile);
                    }
                    catch
                    {
                        _jobLog.WriteEntry(this, "Invalid XML File", Log.LogEntryType.Error);
                        return false;
                    }

                    string CurrName, CurrValue;
                    XPathNavigator Nav = Xp.CreateNavigator();
                    XPathExpression Exp = Nav.Compile("//tags/tag/SimpleTag");
                    XPathNodeIterator Itr = Nav.Select(Exp);
                    while (Itr.MoveNext())
                    {
                        CurrName = XML.GetXMLTagValue("name", Itr.Current.OuterXml);
                        CurrValue = XML.GetXMLTagValue("value", Itr.Current.OuterXml);
                        if ((CurrName != "") & (CurrValue != "") & (CurrValue != "-"))
                        {
                            if ((CurrName.ToLower() == "title") && (String.IsNullOrWhiteSpace(_videoTags.Title))) _videoTags.Title = CurrValue;
                            else if ((CurrName.ToLower() == "seriesname") && (String.IsNullOrWhiteSpace(_videoTags.Title))) _videoTags.Title = CurrValue; // sometimes it is stores as series name
                            else if ((CurrName.ToLower() == "origname") && (String.IsNullOrWhiteSpace(_videoTags.Title))) _videoTags.Title = CurrValue; // If this isn't a series, check if it's a movie (use original name is possible)
                            else if ((CurrName.ToLower() == "moviename") && (String.IsNullOrWhiteSpace(_videoTags.Title))) _videoTags.Title = CurrValue; // Last chance use localized name

                            if ((CurrName.ToLower() == "episodename") && (String.IsNullOrWhiteSpace(_videoTags.SubTitle))) _videoTags.SubTitle = CurrValue;
                            else if ((CurrName.ToLower() == "tagline") && (String.IsNullOrWhiteSpace(_videoTags.SubTitle))) _videoTags.SubTitle = CurrValue; // movies info is stores in tagline

                            if ((CurrName.ToLower() == "comment") && (String.IsNullOrWhiteSpace(_videoTags.Description))) _videoTags.Description = CurrValue;
                            else if ((CurrName.ToLower() == "tagline") && (String.IsNullOrWhiteSpace(_videoTags.Description))) _videoTags.Description = CurrValue; // movies info is stores in tagline
                            else if ((CurrName.ToLower() == "storyplot") && (String.IsNullOrWhiteSpace(_videoTags.Description))) _videoTags.Description = CurrValue; // sometimes info is stores in story plot

                            if ((CurrName.ToLower() == "channel_name") && (String.IsNullOrWhiteSpace(_videoTags.Network)))
                                _videoTags.Network = CurrValue;

                            if (CurrName.ToLower() == "genre")
                            {
                                if (_videoTags.Genres == null)
                                    _videoTags.Genres = CurrValue.Split(';');
                                else if (_videoTags.Genres.Length == 0) // sometimes we get a null array, so check the length
                                    _videoTags.Genres = CurrValue.Split(';');
                            }

                            retVal = true;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Error extracting Media Portal meta data : " + e.ToString()), Log.LogEntryType.Warning);
                return false;
            }

            return retVal;
        }

        /// <summary>
        /// Extract tags from NON WTV/DVRMS files
        /// </summary>
        private bool ExtractGenericTags()
        {
            try
            {
                TagLib.File TagFile = TagLib.File.Create(_videoFileName);
                _videoTags.Title = TagFile.Tag.Title;
                _videoTags.Genres = TagFile.Tag.Genres;
                _videoTags.SubTitle = TagFile.Tag.Album; 
                _videoTags.Description = TagFile.Tag.Comment;
                _videoTags.Season = (int) TagFile.Tag.Disc;
                _videoTags.Episode = (int) TagFile.Tag.Track;

                try
                {
                    if (_downloadBannerFile)
                    {
                        // Read the artwork
                        TagLib.File file = TagLib.File.Create(_videoFileName);
                        TagLib.IPicture pic = file.Tag.Pictures[0];  //pic contains data for image.
                        if (pic != null && !String.IsNullOrWhiteSpace(_videoTags.Title))
                        {
                            _jobLog.WriteEntry(this, "Trying to extract Artwork from file", Log.LogEntryType.Information);
                            using (MemoryStream stream = new MemoryStream(pic.Data.Data))  //create an in memory stream
                            {
                                // Write to file ...
                                _videoTags.BannerFile = Path.Combine(GlobalDefs.CachePath, Util.FilePaths.RemoveIllegalFilePathChars(_videoTags.Title) + ".jpg");
                                Bitmap artWork = new Bitmap(stream);
                                artWork.Save(_videoTags.BannerFile, System.Drawing.Imaging.ImageFormat.Jpeg); // Save as JPEG
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    _jobLog.WriteEntry(this, "Error reading Artwork -> " + e.ToString(), Log.LogEntryType.Warning);
                    _videoTags.BannerFile = "";
                }

                switch (FilePaths.CleanExt(_videoFileName)) // special case processing
                {
                    case ".mp4":
                    case ".m4v": // Apple MPEG4 Tags ATOMS http://atomicparsley.sourceforge.net/mpeg-4files.html
                        _jobLog.WriteEntry(this, "Read Tags: MPEG4 file detected using AppleTag", Log.LogEntryType.Information);
                        TagLib.Mpeg4.AppleTag MP4Tag = TagFile.GetTag(TagLib.TagTypes.Apple) as TagLib.Mpeg4.AppleTag;
                        if (!String.IsNullOrEmpty(MP4Tag.GetText(new TagLib.ReadOnlyByteVector("tvsh"))[0])) _videoTags.Title = MP4Tag.GetText(new TagLib.ReadOnlyByteVector("tvsh"))[0]; // Showname
                        if (!String.IsNullOrEmpty(MP4Tag.GetText(new TagLib.ReadOnlyByteVector("tven"))[0])) _videoTags.SubTitle = MP4Tag.GetText(new TagLib.ReadOnlyByteVector("tven"))[0]; // Episode
                        if (!String.IsNullOrEmpty(MP4Tag.GetText(new TagLib.ReadOnlyByteVector("tvnn"))[0])) _videoTags.Network = MP4Tag.GetText(new TagLib.ReadOnlyByteVector("tvnn"))[0]; // Network
                        if (!String.IsNullOrEmpty(MP4Tag.GetText(new TagLib.ReadOnlyByteVector("desc"))[0])) _videoTags.Description = MP4Tag.GetText(new TagLib.ReadOnlyByteVector("desc"))[0]; // Description
                        int.TryParse(MP4Tag.GetText(new TagLib.ReadOnlyByteVector("tvsn"))[0], out _videoTags.Season); // Season
                        int.TryParse(MP4Tag.GetText(new TagLib.ReadOnlyByteVector("tves"))[0], out _videoTags.Season); // Episode
                        break;

                    case ".avi": // AVI Extended INFO Tags: http://abcavi.kibi.ru/infotags.htm
                        _jobLog.WriteEntry(this, "Read Tags: AVI file detected using RiffTag", Log.LogEntryType.Information);
                        TagLib.Riff.InfoTag RiffTag = TagFile.GetTag(TagLib.TagTypes.RiffInfo) as TagLib.Riff.InfoTag;
                        if (!String.IsNullOrEmpty(RiffTag.GetValuesAsStrings("ISBJ")[0])) _videoTags.SubTitle = RiffTag.GetValuesAsStrings("ISBJ")[0]; // Subject
                        break;

                    case ".mp3":
                        _jobLog.WriteEntry(this, "Read Tags: MP3 file detected", Log.LogEntryType.Information);
                        if (!String.IsNullOrEmpty(TagFile.Tag.Album)) _videoTags.Title = TagFile.Tag.Album; // Reversed for MP3
                        if (!String.IsNullOrEmpty(TagFile.Tag.Title)) _videoTags.SubTitle = TagFile.Tag.Title;
                        break;

                    default:
                        break;
                }

                _jobLog.WriteEntry(this, Localise.GetPhrase("Extracted Generic Tags:") + " " + _videoTags.ToString(), Log.LogEntryType.Debug);
            }
            catch (Exception Ex)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Unable to extract meta data from file") + " " + _videoFileName + ". " + Ex.Message,  Log.LogEntryType.Warning);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Write tags/metadata to files (generic)
        /// </summary>
        /// <param name="outputFile">File to write into</param>
        private bool WriteGenericTags(string outputFile)
        {
            TagLib.File NewTagFile;

            try
            {
                TagLib.ByteVector.UseBrokenLatin1Behavior = true; // Use Default Encoding for Latin-1 strings instead of code page 1252 (this is how WMP works, technically broken and should ideally be code page 1252 or UTF-8, but WMP takes the default local encoding). Required for non english symbols
                NewTagFile = TagLib.File.Create(outputFile);

                switch (Path.GetExtension(outputFile.ToLower().Trim()))
                {
                    case ".avi": // AVI Extended INFO Tags: http://abcavi.kibi.ru/infotags.htm
                        _jobLog.WriteEntry(this, "Write Tags: AVI file detected using RiffTag", Log.LogEntryType.Information);
                        TagLib.Riff.InfoTag RiffTag = NewTagFile.GetTag(TagLib.TagTypes.RiffInfo) as TagLib.Riff.InfoTag;
                        RiffTag.Title = _videoTags.Title;
                        RiffTag.SetValue("ISBJ", _videoTags.SubTitle); // Subject
                        RiffTag.Comment = _videoTags.Description;
                        RiffTag.Genres = _videoTags.Genres;
                        RiffTag.Disc = (uint)_videoTags.Season;
                        RiffTag.Track = (uint)_videoTags.Episode;
                        RiffTag.SetValue("ISFT", "MCEBuddy2x"); // Software
                        if (!String.IsNullOrWhiteSpace(_videoTags.BannerFile))
                        {
                            TagLib.Picture VideoPicture = new TagLib.Picture(_videoTags.BannerFile);
                            RiffTag.Pictures = new TagLib.Picture[] { VideoPicture };
                        }

                        break;

                    case ".wmv":
                        _jobLog.WriteEntry(this, "Write Tags: WMV file detected using AsfTag", Log.LogEntryType.Information);
                        TagLib.Asf.Tag AsfTag = NewTagFile.GetTag(TagLib.TagTypes.Asf) as TagLib.Asf.Tag;
                        AsfTag.Title = _videoTags.Title;
                        AsfTag.SetDescriptorString(_videoTags.SubTitle, "WM/SubTitle");
                        AsfTag.SetDescriptorString(_videoTags.Description, "WM/SubTitleDescription");
                        AsfTag.Comment = _videoTags.Description;
                        AsfTag.Genres = _videoTags.Genres;
                        AsfTag.Disc = (uint)_videoTags.Season;
                        AsfTag.Track = (uint)_videoTags.Episode;
                        AsfTag.SetDescriptorString(_videoTags.Network, "WM/MediaStationName");
                        AsfTag.SetDescriptors("WM/MediaIsMovie", new TagLib.Asf.ContentDescriptor("WM/MediaIsMovie", _videoTags.IsMovie));
                        AsfTag.SetDescriptorString(_videoTags.OriginalBroadcastDateTime.ToString("s") + "Z", "WM/MediaOriginalBroadcastDateTime");
                        AsfTag.SetDescriptors("WM/WMRVEncodeTime", new TagLib.Asf.ContentDescriptor("WM/WMRVEncodeTime", (ulong)_videoTags.RecordedDateTime.Ticks));

                        if (!String.IsNullOrWhiteSpace(_videoTags.BannerFile))
                        {
                            TagLib.Picture VideoPicture = new TagLib.Picture(_videoTags.BannerFile);
                            AsfTag.Pictures = new TagLib.Picture[] { VideoPicture };
                        }
                        break;

                    case ".mp3":
                        _jobLog.WriteEntry(this, "Write Tags: MP3 file detected using ID3v2", Log.LogEntryType.Information);
                        TagLib.Id3v2.Tag MP3Tag = NewTagFile.GetTag(TagLib.TagTypes.Id3v2) as TagLib.Id3v2.Tag;
                        if (!String.IsNullOrWhiteSpace(_videoTags.SubTitle))
                            MP3Tag.Title = _videoTags.SubTitle;
                        else
                            MP3Tag.Title = _videoTags.Title;
                        MP3Tag.Album = _videoTags.Title;
                        MP3Tag.Comment = _videoTags.Description;
                        MP3Tag.Genres = _videoTags.Genres;
                        MP3Tag.Disc = (uint)_videoTags.Season;
                        MP3Tag.Track = (uint)_videoTags.Episode;
                        if (!String.IsNullOrWhiteSpace(_videoTags.BannerFile))
                        {
                            TagLib.Picture VideoPicture = new TagLib.Picture(_videoTags.BannerFile);
                            MP3Tag.Pictures = new TagLib.Picture[] { VideoPicture };
                        }
                        break;

                    default:
                        _jobLog.WriteEntry(this, "Write Tags: Unknown file detected -> " + Path.GetExtension(outputFile.ToLower().Trim()) + ", writing generic tags", Log.LogEntryType.Warning);
                        NewTagFile.Tag.Title = _videoTags.Title; // Generic Tags
                        NewTagFile.Tag.Album = _videoTags.SubTitle;
                        NewTagFile.Tag.Comment = _videoTags.Description;
                        NewTagFile.Tag.Genres = _videoTags.Genres;
                        NewTagFile.Tag.Genres = _videoTags.Genres;
                        NewTagFile.Tag.Disc = (uint)_videoTags.Season;
                        NewTagFile.Tag.Track = (uint)_videoTags.Episode;
                        if (!String.IsNullOrWhiteSpace(_videoTags.BannerFile))
                        {
                            TagLib.Picture VideoPicture = new TagLib.Picture(_videoTags.BannerFile);
                            NewTagFile.Tag.Pictures = new TagLib.Picture[] { VideoPicture };
                        }
                        break;
                }
            }
            catch (Exception Ex)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Unable to write meta data to file") + " " + outputFile + ". " + Ex.Message, Log.LogEntryType.Warning);
                return false;
            }

            try
            {
                _jobLog.WriteEntry(this, "About to write Tags", Log.LogEntryType.Debug);
                NewTagFile.Save();
            }
            catch (Exception Ex)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Unable to write meta data to file") + " " + outputFile + ". " + Ex.Message, Log.LogEntryType.Warning);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Write tags to MP4 files using Atomic Parsley
        /// </summary>
        /// <param name="outputFile">MP4 file to write the tags into</param>
        private bool WriteMP4Tags(string outputFile)
        {
            // TODO Replace with Taglib once we can debug the MP4 box write.  Use atomic parlsey in the meantime
            // --overwrite HAS to be first option since it directs AtomicParsley NOT to rename the file (which it does by default)
            // Windows limits the arguments + full filename path to 255 bytes, if the description/comments are too long it windows will clip the arguments
            // hence --overWrite has to be the first parameter else it might be cut off and the filename will change leading to a conversion failure

            string cmdLine = Util.FilePaths.FixSpaces(outputFile) + " --overWrite --title " + Util.FilePaths.FixSpaces(_videoTags.Title);
                
            
            // Prioritze information, 255 character limit
            if (_videoTags.IsMovie == false || _videoTags.Season != 0) // TV show
            {
                    cmdLine += " --TVShowName " + Util.FilePaths.FixSpaces(_videoTags.Title) + " --stik \"TV Show\"";
                    if (!String.IsNullOrWhiteSpace(_videoTags.SubTitle)) cmdLine += " --TVEpisode " + Util.FilePaths.FixSpaces(_videoTags.SubTitle);
                    if (_videoTags.Season > 0) cmdLine += " --TVSeasonNum " + Util.FilePaths.FixSpaces(_videoTags.Season.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    if (_videoTags.Episode > 0) cmdLine += " --TVEpisodeNum " + Util.FilePaths.FixSpaces(_videoTags.Episode.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            else
                cmdLine += " --stik \"Movie\"";

            if (!String.IsNullOrWhiteSpace(_videoTags.Network))
                cmdLine += " --TVNetwork " + Util.FilePaths.FixSpaces(_videoTags.Network);

            if (_videoTags.Genres != null)
                if (_videoTags.Genres.Length > 0) // Check if number of elements is > 0, check for null does not work snice sometimes there is a null array present
                    cmdLine += " --genre " + Util.FilePaths.FixSpaces(_videoTags.Genres[0]);

            if (!String.IsNullOrWhiteSpace(_videoTags.Description))
            {
                cmdLine += " --description " + Util.FilePaths.FixSpaces(_videoTags.Description);
                cmdLine += " --longdesc " + Util.FilePaths.FixSpaces(_videoTags.Description);                
                cmdLine += " --comment " + Util.FilePaths.FixSpaces(_videoTags.Description);
            }

            if (File.Exists(_videoTags.BannerFile))
                cmdLine += " --artwork " + Util.FilePaths.FixSpaces(_videoTags.BannerFile);

            cmdLine += " --encodingTool \"MCEBuddy\""; //final touch
            _jobLog.WriteEntry(this, Localise.GetPhrase("About to write MP4 Tags :") + " " + cmdLine, Log.LogEntryType.Debug);

            AppWrapper.AtomicParsley ap = new AtomicParsley(cmdLine, ref _jobStatus, _jobLog );
            ap.Run();
            if (!ap.Success)
            {
                _jobStatus.PercentageComplete = 0; //Atomic PArsley didn't find the necessary output or the process failed
                return false;
            }

            return true;
        }

        /// <summary>
        /// Write tags and metadata to file
        /// </summary>
        /// <param name="outputFile">File to write tags/metadata info</param>
        public void WriteTags(string outputFile)
        {
            if (!File.Exists(outputFile)) return;

            string ext = Path.GetExtension(outputFile.ToLower().Trim());
            if (!((ext == ".wtv") || (ext == ".mp4") || (ext == ".m4v") || (ext == ".avi") || (ext == ".wmv") || (ext == ".mp3"))) return;

            if (ext == ".mp4" || ext == ".m4v")
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("About to write MP4 Tags"), Log.LogEntryType.Information);
                WriteMP4Tags(outputFile);
            }
            else if (ext == ".wtv")
            {
                WriteMCETags(outputFile);
            }
            else
            {
                WriteGenericTags(outputFile);
            }
        }

        /// <summary>
        /// Adds the subtitle file and chapters to the target file
        /// </summary>
        /// <param name="srtFile">Path to subtitle file</param>
        /// <param name="neroChapterFile">Path to Nero chapter file</param>
        /// <param name="xmlChapterFile">Path to xml (iTunes) chapter file</param>
        /// <param name="targetFile">Path to target file</param>
        /// <returns>False in case of an error</returns>
        public bool AddSubtitlesAndChaptersToFile(string srtFile, string neroChapterFile, string xmlChapterFile, string targetFile)
        {
            _jobLog.WriteEntry("Subtitle File : " + srtFile + "\nChapter File : " + neroChapterFile + "\nTarget File : " + targetFile, Log.LogEntryType.Debug);

            if (!File.Exists(targetFile))
                return false;

            if (String.IsNullOrWhiteSpace(xmlChapterFile) && String.IsNullOrWhiteSpace(neroChapterFile) && String.IsNullOrWhiteSpace(srtFile)) // Atleast one file should be there to proceeed
                return true; // nothing to do

            if ((FileIO.FileSize(xmlChapterFile) <= 0) && (FileIO.FileSize(neroChapterFile) <= 0) && (FileIO.FileSize(srtFile) < 0)) // Atleast one file should be valid to proceeed
                return true; // nothing to do

            string parameters = "";
            switch (FilePaths.CleanExt(targetFile))
            {
                case ".mp4":
                case ".m4v":
                    if (File.Exists(srtFile)) // Add the subtitles
                        parameters += " -add " + Util.FilePaths.FixSpaces(srtFile) + ":hdlr=sbtl";

                    if (File.Exists(neroChapterFile)) // Add the nero chapters
                        parameters += " -chap " + Util.FilePaths.FixSpaces(neroChapterFile);

                    if (File.Exists(xmlChapterFile)) // Add the iTunes chapters
                        parameters += " -add " + Util.FilePaths.FixSpaces(xmlChapterFile) + ":chap";

                    parameters += " " + Util.FilePaths.FixSpaces(targetFile); // output file

                    MP4Box mp4Box = new MP4Box(parameters, ref _jobStatus, _jobLog);
                    mp4Box.Run();
                    if (!mp4Box.Success || (FileIO.FileSize(targetFile) <= 0))
                    {
                        _jobStatus.PercentageComplete = 0;
                        _jobStatus.ErrorMsg = "Mp4Box adding subtitles failed";
                        _jobLog.WriteEntry(Localise.GetPhrase("Mp4Box adding subtitles failed"), Log.LogEntryType.Error); ;
                        return false;
                    }

                    return true; // all done

                case ".mkv":
                    string outputFile = FilePaths.GetFullPathWithoutExtension(targetFile) + "-temp.mkv";
                    parameters += "-o " + Util.FilePaths.FixSpaces(outputFile) + " " + Util.FilePaths.FixSpaces(targetFile); // output file

                    if (File.Exists(srtFile)) // Add the subtitles
                        parameters += " " + Util.FilePaths.FixSpaces(srtFile);

                    if (File.Exists(neroChapterFile)) // Add the chapters
                        parameters += " --chapters " + Util.FilePaths.FixSpaces(neroChapterFile);

                    MKVMerge mkvMerge = new MKVMerge(parameters, ref _jobStatus, _jobLog);
                    mkvMerge.Run();
                    if (!mkvMerge.Success || (Util.FileIO.FileSize(outputFile) <= 0)) // check for +ve success
                    {
                        _jobStatus.PercentageComplete = 0;
                        _jobStatus.ErrorMsg = "MKVMerge adding subtitles failed";
                        _jobLog.WriteEntry(Localise.GetPhrase("MKVMerge adding subtitles failed"), Log.LogEntryType.Error); ;
                        return false;
                    }

                    // Replace the temp file
                    Util.FileIO.TryFileReplace(targetFile, outputFile);

                    if (Util.FileIO.FileSize(targetFile) <= 0)
                    {
                        _jobLog.WriteEntry(Localise.GetPhrase("MKVMerge: Error moving files"), Log.LogEntryType.Error); ;
                        return false; // Something went wrong
                    }

                    return true; // done

                default:
                    return true; // not a valid type, so ignore it
            }
        }

        /// <summary>
        /// Downloads the banner file if needed and available
        /// </summary>
        /// <param name="videoTags">VideoTags structure</param>
        /// <param name="bannerUrl">URL to Banner File</param>
        public static void DownloadBannerFile(ref VideoTags videoTags, string bannerUrl)
        {
            try
            {
                if (!String.IsNullOrWhiteSpace(videoTags.BannerFile))
                {
                    if ((!File.Exists(videoTags.BannerFile)) && (!String.IsNullOrWhiteSpace(bannerUrl)))
                    {
                        Util.Internet.WGet(bannerUrl, videoTags.BannerFile);
                        if (File.Exists(videoTags.BannerFile))
                            videoTags.BannerURL = bannerUrl;
                    }
                    else if (!String.IsNullOrWhiteSpace(bannerUrl) && String.IsNullOrWhiteSpace(videoTags.BannerURL))
                        videoTags.BannerURL = bannerUrl;
                }
            }
            catch (Exception)
            { }
        }
    }
}
