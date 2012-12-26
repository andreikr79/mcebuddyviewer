using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.XPath;
using System.Drawing;

using MCEBuddy.AppWrapper;
using MCEBuddy.Globals;
using MCEBuddy.VideoProperties;
using MCEBuddy.Util;

namespace MCEBuddy.MetaData
{
    public class VideoMetaData
    {
        private string _videoFileName;
        private VideoTags _videoTags = new VideoTags();
        private bool _downloadSeriesDetails = false;

        protected JobStatus _jobStatus;
        protected Log _jobLog;

        public VideoMetaData(string videoFileName, bool downloadSeriesDetails, ref JobStatus jobStatus, Log jobLog)
        {
            _videoFileName = videoFileName;
            _downloadSeriesDetails = downloadSeriesDetails;
            _jobStatus = jobStatus;
            _jobLog = jobLog;
        }
        
        public VideoMetaData(string videoFileName, bool downloadSeriesDetails, string imdbId, string tvdbId, ref JobStatus jobStatus, Log jobLog)
        {
            _videoFileName = videoFileName;
            _downloadSeriesDetails = downloadSeriesDetails;
            _videoTags.seriesId = tvdbId;
            _videoTags.movieId = imdbId;
            _jobStatus = jobStatus;
            _jobLog = jobLog;
        }

        public void Extract()
        {
            string ext = Path.GetExtension(_videoFileName).ToLower();
            string xmlMetaData = Util.FilePaths.GetFullPathWithoutExtension(_videoFileName) + ".xml";
            if ((ext == ".wtv") || (ext == ".dvr-ms"))
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Extracting MCE Tags"), Log.LogEntryType.Information);
                ExtractMCETags();
            }
            else if (File.Exists(xmlMetaData))
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Extracting Media Portal Tags"), Log.LogEntryType.Information);
                ExtractMPTags(xmlMetaData);
            }
            else
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Extracting Generic Tags"), Log.LogEntryType.Information);
                ExtractGenericTags();
            }

            if (String.IsNullOrEmpty(_videoTags.Title))
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("No recording meta data can be extracted, using Title extracted from file name. If you are running MCEBuddy on a Windows Server, this is normal as the Media Center filters are not available on that platfom."), Log.LogEntryType.Warning);
                string baseFileName = Path.GetFileNameWithoutExtension(_videoFileName);
                int pos = baseFileName.IndexOf("_");
                if ( pos > 0)
                {
                    _videoTags.Title = baseFileName.Substring(0, pos);
                }
                else
                {
                    _videoTags.Title = baseFileName;
                }
            }

            DownloadSeriesDetails();

            _jobLog.WriteEntry(this, Localise.GetPhrase("Video Tags extracted") + " -> " + _videoTags.ToString(), Log.LogEntryType.Debug);

            if (_videoTags.CopyProtected)
                _jobLog.WriteEntry(this, Localise.GetPhrase("ERROR: VIDEO IS COPYPROTECTED. CONVERSION WILL FAIL"), Log.LogEntryType.Error);
        }

        private void DownloadSeriesDetails()
        {
            Util.FilePaths.CreateDir(GlobalDefs.CachePath);

            if ((_downloadSeriesDetails) && (!String.IsNullOrEmpty(_videoTags.Title)))
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Downloading Series details"), Log.LogEntryType.Information);
                
                _videoTags.BannerFile = Path.Combine(GlobalDefs.CachePath, Util.FilePaths.RemoveIllegalFilePathChars(_videoTags.Title) + ".jpg");

                if (TheTVDB.DownloadSeriesDetails(ref _videoTags) == false)
                {
                    TheMovieDB.DownloadMovieDetails(ref _videoTags);
                }
            }
            else
                _jobLog.WriteEntry(this, Localise.GetPhrase("Skipping downloading Series details"), Log.LogEntryType.Information);

            if (FileIO.FileSize(_videoTags.BannerFile) <= 0) // Need to avoid an exception while writing tags
                _videoTags.BannerFile = "";
        }

        /// <summary>
        /// Create an XML file containing the XBMC compatible information file.
        /// Refer to http://wiki.xbmc.org/index.php?title=Import_-_Export_Library for more details
        /// </summary>
        /// <param name="sourceVideo">Full path to source video</param>
        /// <param name="convertedFile">Full path to converted file</param>
        /// <param name="videoFile">VideoFile metadata structure</param>
        /// <returns></returns>
        public bool MCECreateXMLTags(string sourceVideo, string convertedFile, VideoInfo videoFile)
        {
            string xmlFileName = Path.Combine(Path.GetDirectoryName(convertedFile), Path.GetFileNameWithoutExtension(convertedFile) + ".nfo"); // Path\FileName.nfo

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
                            writer.WriteElementString("plot", _videoTags.SubTitleDescription);
                            writer.WriteElementString("thumb", _videoTags.BannerURL);
                            writer.WriteElementString("id", _videoTags.seriesId);
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
                            writer.WriteElementString("outline", _videoTags.SubTitleDescription);
                            writer.WriteElementString("plot", _videoTags.SubTitleDescription);
                            writer.WriteElementString("runtime", ((int)(videoFile.Duration/60)).ToString(System.Globalization.CultureInfo.InvariantCulture));
                            writer.WriteElementString("thumb", _videoTags.BannerURL);
                            writer.WriteElementString("id", _videoTags.movieId);
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
            catch
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Unable to extract meta data using filters.  Filters may not be present [eg. Windows Server]."), Log.LogEntryType.Warning);
                return false;
            }
        }

        private string XmlCharacterWhitelist(string in_string)
        {
            if (String.IsNullOrEmpty(in_string)) return null;

            StringBuilder sbOutput = new StringBuilder();
            char ch;

            for (int i = 0; i < in_string.Length; i++)
            {
                ch = in_string[i];
                if ((ch >= 0x0020 && ch <= 0xD7FF) ||
                        (ch >= 0xE000 && ch <= 0xFFFD) ||
                        ch == 0x0009 ||
                        ch == 0x000A ||
                        ch == 0x000D)
                {
                    sbOutput.Append(ch);
                }
            }
            return System.Security.SecurityElement.Escape(sbOutput.ToString());
        } 

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
                    _videoTags.SubTitleDescription = GetMetaTagStringValue(attrs, "WM/SubTitleDescription");
                    _videoTags.Genres = GetMetaTagStringValue(attrs, "WM/Genre").Split(';');
                    _videoTags.Network = GetMetaTagStringValue(attrs, "WM/MediaStationName");
                    _videoTags.CopyProtected = GetMetaTagBoolValue(attrs, "WM/WMRVContentProtected");
                    if (GetMetaTagStringValue(attrs, "WM/MediaOriginalBroadcastDateTime") != "")
                    {
                        // DateTime is reported along with timezone info (typically Z i.e. UTC hence assume Universal)
                        DateTime.TryParse(GetMetaTagStringValue(attrs, "WM/MediaOriginalBroadcastDateTime"), null, System.Globalization.DateTimeStyles.AssumeUniversal, out _videoTags.OriginalBroadcastDateTime);
                    }
                    if (GetMetaTagInt64Value(attrs, "WM/WMRVEncodeTime") != 0)
                    {
                        // Stored in UTC ticks hence assume Universal
                        DateTime.TryParse((new DateTime(GetMetaTagInt64Value(attrs, "WM/WMRVEncodeTime"))).ToString(System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out _videoTags.RecordedDateTime);
                    }
                    _videoTags.IsMovie = GetMetaTagBoolValue(attrs, "WM/MediaIsMovie");
                }
            }
            catch
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Unable to extract meta data using filters.  Filters may not be present [eg. Windows Server]."), Log.LogEntryType.Warning);
                if (Path.GetExtension(_videoFileName).ToLower() == ".dvr-ms")
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Attempting raw container DVR-MS read"), Log.LogEntryType.Information);
                    ExtractGenericTags();
                }
            }
        }

        /// <summary>
        /// Sets the Tags Metadata for a WTV file. If the source is a WTV file it copies all the metadata, else it copies individual components
        /// </summary>
        /// <param name="convertedFile">Path to WTV file</param>
        private void WriteWTVTags(string convertedFile)
        {
            _jobLog.WriteEntry(this, Localise.GetPhrase("Setting WTV metadata"), Log.LogEntryType.Debug);

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
                    if (!GlobalDefs.IsNullOrWhiteSpace(_videoTags.Title)) sourceAttrs.Add("Title", new MetadataItem("Title", _videoTags.Title, DirectShowLib.SBE.StreamBufferAttrDataType.String));
                    if (!GlobalDefs.IsNullOrWhiteSpace(_videoTags.SubTitle)) sourceAttrs.Add("WM/SubTitle", new MetadataItem("WM/SubTitle", _videoTags.SubTitle, DirectShowLib.SBE.StreamBufferAttrDataType.String));
                    if (!GlobalDefs.IsNullOrWhiteSpace(_videoTags.SubTitleDescription)) sourceAttrs.Add("WM/SubTitleDescription", new MetadataItem("WM/SubTitleDescription", _videoTags.SubTitleDescription, DirectShowLib.SBE.StreamBufferAttrDataType.String));
                    if (_videoTags.Genres != null) if (_videoTags.Genres.Length > 0) sourceAttrs.Add("WM/Genre", new MetadataItem("WM/Genre", _videoTags.Genres[0], DirectShowLib.SBE.StreamBufferAttrDataType.String));
                    if (!GlobalDefs.IsNullOrWhiteSpace(_videoTags.Network)) sourceAttrs.Add("WM/MediaStationName", new MetadataItem("WM/MediaStationName", _videoTags.Network, DirectShowLib.SBE.StreamBufferAttrDataType.String));
                    if (_videoTags.OriginalBroadcastDateTime > GlobalDefs.NO_BROADCAST_TIME) sourceAttrs.Add("WM/MediaOriginalBroadcastDateTime", new MetadataItem("WM/MediaOriginalBroadcastDateTime", _videoTags.OriginalBroadcastDateTime.ToString("s") + "Z", DirectShowLib.SBE.StreamBufferAttrDataType.String)); // It is stored a UTC value, we just need to add "Z" at the end to indicate it
                    if (_videoTags.RecordedDateTime > GlobalDefs.NO_BROADCAST_TIME) sourceAttrs.Add("WM/WMRVEncodeTime", new MetadataItem("WM/WMRVEncodeTime", _videoTags.RecordedDateTime.Ticks, DirectShowLib.SBE.StreamBufferAttrDataType.QWord));
                    sourceAttrs.Add("WM/MediaIsMovie", new MetadataItem("WM/MediaIsMovie", _videoTags.IsMovie, DirectShowLib.SBE.StreamBufferAttrDataType.Bool));
                }

                using (MetadataEditor editor = (MetadataEditor)new MCRecMetadataEditor(convertedFile)) // Write All Attributes from the Converted file
                {
                    editor.SetAttributes(sourceAttrs); // Get All Attributes from the Source file
                }
            }
            catch
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Unable to set WTV meta data using filters.  Filters may not be present [eg. Windows Server]."), Log.LogEntryType.Warning);
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

        public VideoTags MetaData
        {
            get
            {
                return _videoTags;
            }
        }

        private static string GetXMLTagValue(string Tag, string Source)
        {
            int StartPos = Source.IndexOf("<" + Tag + ">") + Tag.Length + 2;
            int EndPos = Source.IndexOf("</" + Tag + ">");
            if ((StartPos != -1) && (EndPos != -1) && (EndPos > StartPos))
            {
                return Source.Substring(StartPos, EndPos - StartPos);
            }
            else
            {
                return "";
            }
        }

        private void ExtractMPTags(string XMLFile)
        {
            _jobLog.WriteEntry(this, Localise.GetPhrase("Extracting MediaPortal meta data"), Log.LogEntryType.Debug);
            XPathDocument Xp;
            if (File.Exists(XMLFile))
            {
                try
                {
                    Xp = new XPathDocument(XMLFile);
                }
                catch
                {
                    return;
                }

                string CurrName, CurrValue;
                XPathNavigator Nav = Xp.CreateNavigator();
                XPathExpression Exp = Nav.Compile("//tags/tag/SimpleTag");
                XPathNodeIterator Itr = Nav.Select(Exp);
                while (Itr.MoveNext())
                {
                    CurrName = GetXMLTagValue("name", Itr.Current.InnerXml);
                    CurrValue = GetXMLTagValue("value", Itr.Current.InnerXml);
                    if ((CurrName != "") & (CurrValue != "") & (CurrValue != "-"))
                    {
                        if ((CurrName.ToLower() == "title") && (String.IsNullOrEmpty(_videoTags.Title))) _videoTags.Title = CurrValue;
                        else if ((CurrName.ToLower() == "seriesname") && (String.IsNullOrEmpty(_videoTags.Title))) _videoTags.Title = CurrValue; // sometimes it is stores as series name
                        else if ((CurrName.ToLower() == "origname") && (String.IsNullOrEmpty(_videoTags.Title))) _videoTags.Title = CurrValue; // If this isn't a series, check if it's a movie (use original name is possible)
                        else if ((CurrName.ToLower() == "moviename") && (String.IsNullOrEmpty(_videoTags.Title))) _videoTags.Title = CurrValue; // Last chance use localized name
                        
                        if ((CurrName.ToLower() == "episodename") && (String.IsNullOrEmpty(_videoTags.SubTitle))) _videoTags.SubTitle = CurrValue;
                        else if ((CurrName.ToLower() == "tagline") && (String.IsNullOrEmpty(_videoTags.SubTitle))) _videoTags.SubTitle = CurrValue; // movies info is stores in tagline

                        if ((CurrName.ToLower() == "comment") && (String.IsNullOrEmpty(_videoTags.SubTitleDescription))) _videoTags.SubTitleDescription = CurrValue;
                        else if ((CurrName.ToLower() == "tagline") && (String.IsNullOrEmpty(_videoTags.SubTitleDescription))) _videoTags.SubTitleDescription = CurrValue; // movies info is stores in tagline
                        else if ((CurrName.ToLower() == "storyplot") && (String.IsNullOrEmpty(_videoTags.SubTitleDescription))) _videoTags.SubTitleDescription = CurrValue; // sometimes info is stores in story plot

                        if (CurrName.ToLower() == "channel_name")
                            _videoTags.Network = CurrValue;

                        if (CurrName.ToLower() == "genre")
                        {
                            if (_videoTags.Genres == null)
                                _videoTags.Genres = CurrValue.Split(';');
                            else if (_videoTags.Genres.Length == 0) // sometimes we get a null array, so check the length
                                _videoTags.Genres = CurrValue.Split(';');
                        }
                    }
                }
            }
        }

        private void ExtractGenericTags()
        {
            try
            {
                TagLib.File TagFile = TagLib.File.Create(_videoFileName);
                _videoTags.Title = TagFile.Tag.Title;
                _videoTags.Genres = TagFile.Tag.Genres;
                _videoTags.SubTitle = TagFile.Tag.Album; 
                _videoTags.SubTitleDescription = TagFile.Tag.Comment;
                _videoTags.Season = (int) TagFile.Tag.Disc;
                _videoTags.Episode = (int) TagFile.Tag.Track;

                try
                {
                    // Read the artwork
                    TagLib.File file = TagLib.File.Create(_videoFileName);
                    TagLib.IPicture pic = file.Tag.Pictures[0];  //pic contains data for image.
                    if (pic != null && !String.IsNullOrEmpty(_videoTags.Title))
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
                        if (!string.IsNullOrEmpty(MP4Tag.GetText(new TagLib.ReadOnlyByteVector("tvsh"))[0])) _videoTags.Title = MP4Tag.GetText(new TagLib.ReadOnlyByteVector("tvsh"))[0]; // Showname
                        if (!string.IsNullOrEmpty(MP4Tag.GetText(new TagLib.ReadOnlyByteVector("tven"))[0])) _videoTags.SubTitle = MP4Tag.GetText(new TagLib.ReadOnlyByteVector("tven"))[0]; // Episode
                        if (!string.IsNullOrEmpty(MP4Tag.GetText(new TagLib.ReadOnlyByteVector("tvnn"))[0])) _videoTags.Network = MP4Tag.GetText(new TagLib.ReadOnlyByteVector("tvnn"))[0]; // Network
                        if (!string.IsNullOrEmpty(MP4Tag.GetText(new TagLib.ReadOnlyByteVector("desc"))[0])) _videoTags.SubTitleDescription = MP4Tag.GetText(new TagLib.ReadOnlyByteVector("desc"))[0]; // Description
                        int.TryParse(MP4Tag.GetText(new TagLib.ReadOnlyByteVector("tvsn"))[0], out _videoTags.Season); // Season
                        int.TryParse(MP4Tag.GetText(new TagLib.ReadOnlyByteVector("tves"))[0], out _videoTags.Season); // Episode
                        break;

                    case ".avi": // AVI Extended INFO Tags: http://abcavi.kibi.ru/infotags.htm
                        _jobLog.WriteEntry(this, "Read Tags: AVI file detected using RiffTag", Log.LogEntryType.Information);
                        TagLib.Riff.InfoTag RiffTag = TagFile.GetTag(TagLib.TagTypes.RiffInfo) as TagLib.Riff.InfoTag;
                        if (!string.IsNullOrEmpty(RiffTag.GetValuesAsStrings("ISBJ")[0])) _videoTags.SubTitle = RiffTag.GetValuesAsStrings("ISBJ")[0]; // Subject
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
            }

        }

        private void WriteMP4Tags(string outputFile)
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
                    if (!String.IsNullOrEmpty(_videoTags.SubTitle)) cmdLine += " --TVEpisode " + Util.FilePaths.FixSpaces(_videoTags.SubTitle);
                    if (_videoTags.Season > 0) cmdLine += " --TVSeasonNum " + Util.FilePaths.FixSpaces(_videoTags.Season.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    if (_videoTags.Episode > 0) cmdLine += " --TVEpisodeNum " + Util.FilePaths.FixSpaces(_videoTags.Episode.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            else
                cmdLine += " --stik \"Movie\"";

            if (!String.IsNullOrEmpty(_videoTags.Network))
                cmdLine += " --TVNetwork " + Util.FilePaths.FixSpaces(_videoTags.Network);

            if (_videoTags.Genres != null)
                if (_videoTags.Genres.Length > 0) // Check if number of elements is > 0, check for null does not work snice sometimes there is a null array present
                    cmdLine += " --genre " + Util.FilePaths.FixSpaces(_videoTags.Genres[0]);

            if (!String.IsNullOrEmpty(_videoTags.SubTitleDescription))
            {
                cmdLine += " --description " + Util.FilePaths.FixSpaces(_videoTags.SubTitleDescription);
                cmdLine += " --longdesc " + Util.FilePaths.FixSpaces(_videoTags.SubTitleDescription);                
                cmdLine += " --comment " + Util.FilePaths.FixSpaces(_videoTags.SubTitleDescription);
            }

            if (File.Exists(_videoTags.BannerFile))
                cmdLine += " --artwork " + Util.FilePaths.FixSpaces(_videoTags.BannerFile);

            cmdLine += " --encodingTool \"MCEBuddy\""; //final touch
            _jobLog.WriteEntry(this, Localise.GetPhrase("About to write MP4 Tags :") + " " + cmdLine, Log.LogEntryType.Debug);

            AppWrapper.AtomicParsley ap = new AtomicParsley(cmdLine, ref _jobStatus, _jobLog );
            ap.Run();
            if (!ap.Success)
                _jobStatus.PercentageComplete = 0; //Atomic PArsley didn't find the necessary output or the process failed
        }

        private void WriteGenericTags(string outputFile)
        {
            TagLib.File NewTagFile;

            try
            {
                NewTagFile = TagLib.File.Create(outputFile);

                switch (Path.GetExtension(outputFile.ToLower().Trim()))
                {
                    case ".avi": // AVI Extended INFO Tags: http://abcavi.kibi.ru/infotags.htm
                        _jobLog.WriteEntry(this, "Write Tags: AVI file detected using RiffTag", Log.LogEntryType.Information);
                        TagLib.Riff.InfoTag RiffTag = NewTagFile.GetTag(TagLib.TagTypes.RiffInfo) as TagLib.Riff.InfoTag;
                        RiffTag.Title = _videoTags.Title;
                        RiffTag.SetValue("ISBJ", _videoTags.SubTitle); // Subject
                        RiffTag.Comment = _videoTags.SubTitleDescription;
                        RiffTag.Genres = _videoTags.Genres;
                        RiffTag.Disc = (uint)_videoTags.Season;
                        RiffTag.Track = (uint)_videoTags.Episode;
                        RiffTag.SetValue("ISFT", "MCEBuddy2x"); // Software
                        if (!String.IsNullOrEmpty(_videoTags.BannerFile))
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
                        AsfTag.SetDescriptorString(_videoTags.SubTitleDescription, "WM/SubTitleDescription");
                        AsfTag.Comment = _videoTags.SubTitleDescription;
                        AsfTag.Genres = _videoTags.Genres;
                        AsfTag.Disc = (uint)_videoTags.Season;
                        AsfTag.Track = (uint)_videoTags.Episode;
                        AsfTag.SetDescriptorString(_videoTags.Network, "WM/MediaStationName");
                        AsfTag.SetDescriptors("WM/MediaIsMovie", new TagLib.Asf.ContentDescriptor("WM/MediaIsMovie", _videoTags.IsMovie));
                        AsfTag.SetDescriptorString(_videoTags.OriginalBroadcastDateTime.ToString("s") + "Z", "WM/MediaOriginalBroadcastDateTime");
                        AsfTag.SetDescriptors("WM/WMRVEncodeTime", new TagLib.Asf.ContentDescriptor("WM/WMRVEncodeTime", (ulong)_videoTags.RecordedDateTime.Ticks));

                        if (!String.IsNullOrEmpty(_videoTags.BannerFile))
                        {
                            TagLib.Picture VideoPicture = new TagLib.Picture(_videoTags.BannerFile);
                            AsfTag.Pictures = new TagLib.Picture[] { VideoPicture };
                        }
                        break;

                    case ".mp3":
                        _jobLog.WriteEntry(this, "Write Tags: MP3 file detected using ID3v2", Log.LogEntryType.Information);
                        TagLib.Id3v2.Tag MP3Tag = NewTagFile.GetTag(TagLib.TagTypes.Id3v2) as TagLib.Id3v2.Tag;
                        if (!String.IsNullOrEmpty(_videoTags.SubTitle))
                            MP3Tag.Title = _videoTags.SubTitle;
                        else
                            MP3Tag.Title = _videoTags.Title;
                        MP3Tag.Album = _videoTags.Title;
                        MP3Tag.Comment = _videoTags.SubTitleDescription;
                        MP3Tag.Genres = _videoTags.Genres;
                        MP3Tag.Disc = (uint)_videoTags.Season;
                        MP3Tag.Track = (uint)_videoTags.Episode;
                        if (!String.IsNullOrEmpty(_videoTags.BannerFile))
                        {
                            TagLib.Picture VideoPicture = new TagLib.Picture(_videoTags.BannerFile);
                            MP3Tag.Pictures = new TagLib.Picture[] { VideoPicture };
                        }
                        break;

                    default:
                        _jobLog.WriteEntry(this, "Write Tags: Unknown file detected -> " + Path.GetExtension(outputFile.ToLower().Trim()) + ", writing generic tags", Log.LogEntryType.Warning);
                        NewTagFile.Tag.Title = _videoTags.Title; // Generic Tags
                        NewTagFile.Tag.Album = _videoTags.SubTitle;
                        NewTagFile.Tag.Comment = _videoTags.SubTitleDescription;
                        NewTagFile.Tag.Genres = _videoTags.Genres;
                        NewTagFile.Tag.Genres = _videoTags.Genres;
                        NewTagFile.Tag.Disc = (uint)_videoTags.Season;
                        NewTagFile.Tag.Track = (uint)_videoTags.Episode;
                        if (!String.IsNullOrEmpty(_videoTags.BannerFile))
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
                return;
            }

            try
            {
                _jobLog.WriteEntry(this, "About to write Tags", Log.LogEntryType.Debug);
                NewTagFile.Save();
            }
            catch (Exception Ex)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Unable to write meta data to file") + " " + outputFile + ". " + Ex.Message, Log.LogEntryType.Warning);
            }
        }

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
                WriteWTVTags(outputFile);
            }
            else
            {
                WriteGenericTags(outputFile);
            }
        }
    }
}
