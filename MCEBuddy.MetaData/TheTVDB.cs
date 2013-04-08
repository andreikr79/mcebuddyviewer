using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.XPath;

using MCEBuddy.Globals;
using MCEBuddy.Util;

namespace MCEBuddy.MetaData
{
    public static class TheTVDB
    {
        private const string MCEBUDDY_THETVDB_API_KEY = "24BC47A0DF94324E";
        private static string[] THETVDB_SUPPORTED_LANGUAGES = { "en", "da", "fi", "nl", "de", "it","es", "fr","pl","hu","el","tr","ru","he","ja","pt","zh","cs","sl","hr","ko","sv","no"};

        public static bool DownloadSeriesDetails(ref VideoTags videoTags)
        {
            XPathDocument Xp;
            XPathNavigator Nav;
            XPathExpression Exp;
            XPathNodeIterator Itr;

            // There are no TheTVDB mirrors, so skip that step

            // ******************
            // Get the series ID
            // ******************
            try
            {
                if (!String.IsNullOrWhiteSpace(videoTags.imdbMovieId)) // If we have a specific IMDB movieId specified, look up the movie details on TVDB
                {
                    Xp = new XPathDocument("http://www.thetvdb.com/api/GetSeriesByRemoteID.php?imdbid=" + videoTags.imdbMovieId);
                }
                else if (!String.IsNullOrWhiteSpace(videoTags.tvdbSeriesId)) // If we have a specific TVDB seriesId specified, look up the series details
                {
                    // First try to match by Episode name, many time broadcasters mess up the Original Broadcast date or TVDB has the wrong date (user submitted)
                    if (MatchByEpisodeName(ref videoTags, videoTags.tvdbSeriesId) == true)
                        return true; // If it's false, we keep looping through all the returned series

                    // Since we cannot match by Episode name, (assuming an error from the broadcaster), let us try to match by original broadcast date
                    if (MatchByBroadcastTime(ref videoTags, videoTags.tvdbSeriesId) == true)
                        return true; // If it's false, we keep looping through all the returned series

                    return false;
                }
                else // Generic search by name
                {
                    Xp = new XPathDocument("http://www.thetvdb.com/api/GetSeries.php?seriesname=" + videoTags.Title);
                }

                Nav = Xp.CreateNavigator();
                Exp = Nav.Compile("//Data/Series");
                Itr = Nav.Select(Exp);
            }
            catch
            {
                return false;
            }

            while (Itr.MoveNext()) // loop through all series returned trying to find a match
            {
                string seriesID = XML.GetXMLTagValue("seriesid", Itr.Current.OuterXml);
                string seriesTitle = XML.GetXMLTagValue("SeriesName", Itr.Current.OuterXml);

                // Compare the series title with the title of the recording
                if (String.Compare(seriesTitle.ToLower().Trim(), videoTags.Title.ToLower().Trim(), CultureInfo.InvariantCulture, CompareOptions.IgnoreSymbols) != 0)
                    continue; // Name mismatch

                if (String.IsNullOrWhiteSpace(seriesID)) continue; // can't do anything without seriesID

                // First try to match by Episode name, many time broadcasters mess up the Original Broadcast date or TVDB has the wrong date (user submitted)
                if (MatchByEpisodeName(ref videoTags, seriesID) == true)
                    return true; // If it's false, we keep looping through all the returned series

                // Since we cannot match by Episode name, (assuming an error from the broadcaster), let us try to match by original broadcast date
                if (MatchByBroadcastTime(ref videoTags, seriesID) == true)
                    return true; // If it's false, we keep looping through all the returned series
            }

            return false; // no match found
        }

        
        /// <summary>
        /// Match the series information by Original Broadcast date
        /// </summary>
        private static bool MatchByBroadcastTime(ref VideoTags videoTags, string seriesID)
        {
            // If we have no original broadcasttime 
            if (videoTags.OriginalBroadcastDateTime <= Globals.GlobalDefs.NO_BROADCAST_TIME)
            {
                return false;
            }

            // **************************************
            // Get the series and episode information
            // **************************************
            string lang = Localise.TwoLetterISO();

            if (!((IList<string>)THETVDB_SUPPORTED_LANGUAGES).Contains(lang))
            {
                lang = "en";
            }

            string queryUrl = "http://www.thetvdb.com/api/" + MCEBUDDY_THETVDB_API_KEY + "/series/" + seriesID + "/all/" + lang + ".xml";
            XPathDocument XpS;
            XPathNavigator NavS;
            XPathExpression ExpS;
            XPathNodeIterator ItrS;
            string overview = "";
            string bannerUrl = "";
            string imdbID = "";
            string firstAiredStr = "";
            DateTime firstAired = GlobalDefs.NO_BROADCAST_TIME;
            int seasonNo = 0;
            int episodeNo = 0;
            string episodeName = "";
            string episodeOverview = "";
            List<string> genres = new List<string>();

            try
            {
                // Get the Series information
                XpS = new XPathDocument(queryUrl);
                NavS = XpS.CreateNavigator();
                ExpS = NavS.Compile("//Data/Series"); // Series information
                ItrS = NavS.Select(ExpS);
                ItrS.MoveNext();
                overview = XML.GetXMLTagValue("Overview", ItrS.Current.OuterXml);
                bannerUrl = XML.GetXMLTagValue("banner", ItrS.Current.OuterXml);
                imdbID = XML.GetXMLTagValue("IMDB_ID", ItrS.Current.OuterXml);
                string genreValue = XML.GetXMLTagValue("Genre", ItrS.Current.OuterXml);
                if (!String.IsNullOrWhiteSpace(genreValue))
                    foreach (string genre in genreValue.Split('|'))
                        if (!String.IsNullOrWhiteSpace(genre)) genres.Add(genre);

                // Get the Episode information
                XpS = new XPathDocument(queryUrl);
                NavS = XpS.CreateNavigator();
                ExpS = NavS.Compile("//Data/Episode");
                ItrS = NavS.Select(ExpS);
            }
            catch
            {
                return false;
            }

            while (ItrS.MoveNext())
            {
                firstAiredStr = XML.GetXMLTagValue("FirstAired", ItrS.Current.OuterXml);
                if (DateTime.TryParse(firstAiredStr, null, DateTimeStyles.AssumeLocal, out firstAired))
                {
                    // The information is stored on the server using the network timezone
                    // So we assume that the show being converted was recorded locally and is converted locally so the timezones match
                    DateTime dt = videoTags.OriginalBroadcastDateTime.ToLocalTime();
                    if (firstAired.Date == dt.Date) // TVDB only reports the date not the time
                    {
                        episodeName = XML.GetXMLTagValue("EpisodeName", ItrS.Current.OuterXml);
                        if (String.IsNullOrWhiteSpace(episodeName))
                            return false; // WRONG series, if there is no name we're in the incorrect series (probably wrong country)

                        int.TryParse(XML.GetXMLTagValue("SeasonNumber", ItrS.Current.OuterXml), out seasonNo);
                        int.TryParse(XML.GetXMLTagValue("EpisodeNumber", ItrS.Current.OuterXml), out episodeNo);
                        episodeOverview = XML.GetXMLTagValue("Overview", ItrS.Current.OuterXml);
                            
                        // ********************
                        // Get the banner file
                        // ********************
                        VideoMetaData.DownloadBannerFile(ref videoTags, "http://www.thetvdb.com/banners/" + bannerUrl); // Get bannerfile

                        if ((episodeNo != 0) && (videoTags.Episode == 0)) videoTags.Episode = episodeNo;
                        if ((seasonNo != 0) && (videoTags.Season == 0)) videoTags.Season = seasonNo;
                        if (!String.IsNullOrWhiteSpace(episodeName) && String.IsNullOrWhiteSpace(videoTags.SubTitle)) videoTags.SubTitle = episodeName;
                        if (!String.IsNullOrWhiteSpace(episodeOverview) && String.IsNullOrWhiteSpace(videoTags.Description)) videoTags.Description = episodeOverview;
                            else if (!String.IsNullOrWhiteSpace(overview) && (String.IsNullOrWhiteSpace(videoTags.Description))) videoTags.Description = overview;
                        if (!String.IsNullOrWhiteSpace(seriesID) && String.IsNullOrWhiteSpace(videoTags.tvdbSeriesId)) videoTags.tvdbSeriesId = seriesID;
                        if (!String.IsNullOrWhiteSpace(imdbID) && String.IsNullOrWhiteSpace(videoTags.imdbMovieId)) videoTags.imdbMovieId = imdbID;
                        if (genres.Count > 0)
                        {
                            if (videoTags.Genres != null)
                            {
                                if (videoTags.Genres.Length == 0)
                                    videoTags.Genres = genres.ToArray();
                            }
                            else
                                videoTags.Genres = genres.ToArray();
                        }

                        return true; // Found a match got all the data, we're done here
                    }
                }
            }
            
            return false;
        }

        /// <summary>
        /// Match the series information by Episode name
        /// </summary>
        private static bool MatchByEpisodeName(ref VideoTags videoTags, string seriesID)
        {

            if (String.IsNullOrWhiteSpace(videoTags.SubTitle))
                return false; //Nothing to match here

            // **************************************
            // Get the series and episode information
            // **************************************
            foreach (string lang in THETVDB_SUPPORTED_LANGUAGES) // Cycle through all languages looking for a match since people in different countries/locales could be viewing shows recorded in different languages
            {
                string queryUrl = "http://www.thetvdb.com/api/" + MCEBUDDY_THETVDB_API_KEY + "/series/" + seriesID + "/all/" + lang + ".xml";
                XPathDocument XpS;
                XPathNavigator NavS;
                XPathExpression ExpS;
                XPathNodeIterator ItrS;
                string overview = "";
                string bannerUrl = "";
                string imdbID = "";
                List<String> genres = new List<string>();;
                int seasonNo = 0;
                int episodeNo = 0;
                string episodeName = "";
                string episodeOverview = "";
                string firstAiredStr = "";
                DateTime firstAired = GlobalDefs.NO_BROADCAST_TIME;

                try
                {
                    // Get the Series information
                    XpS = new XPathDocument(queryUrl);
                    NavS = XpS.CreateNavigator();
                    ExpS = NavS.Compile("//Data/Series"); // Series information
                    ItrS = NavS.Select(ExpS);
                    ItrS.MoveNext();
                    overview = XML.GetXMLTagValue("Overview", ItrS.Current.OuterXml);
                    bannerUrl = XML.GetXMLTagValue("banner", ItrS.Current.OuterXml);
                    imdbID = XML.GetXMLTagValue("IMDB_ID", ItrS.Current.OuterXml);
                    string genreValue = XML.GetXMLTagValue("Genre", ItrS.Current.OuterXml);
                    if (!String.IsNullOrWhiteSpace(genreValue))
                        foreach (string genre in genreValue.Split('|'))
                            if (!String.IsNullOrWhiteSpace(genre)) genres.Add(genre);

                    // Get the episode information
                    XpS = new XPathDocument(queryUrl);
                    NavS = XpS.CreateNavigator();
                    ExpS = NavS.Compile("//Data/Episode"); // Episode information
                    ItrS = NavS.Select(ExpS);
                }
                catch
                {
                    return false;
                }


                while (ItrS.MoveNext())
                {
                    episodeName = XML.GetXMLTagValue("EpisodeName", ItrS.Current.OuterXml);
                    if (!String.IsNullOrWhiteSpace(episodeName))
                    {

                        if (String.Compare(videoTags.SubTitle.Trim().ToLower(), episodeName.Trim().ToLower(), CultureInfo.InvariantCulture, CompareOptions.IgnoreSymbols) == 0) // Compare the episode names (case / special characters / whitespace can change very often)
                        {
                            int.TryParse(XML.GetXMLTagValue("SeasonNumber", ItrS.Current.OuterXml), out seasonNo);
                            int.TryParse(XML.GetXMLTagValue("EpisodeNumber", ItrS.Current.OuterXml), out episodeNo);
                            episodeOverview = XML.GetXMLTagValue("Overview", ItrS.Current.OuterXml);

                            // ********************
                            // Get the banner file
                            // ********************
                            VideoMetaData.DownloadBannerFile(ref videoTags, "http://www.thetvdb.com/banners/" + bannerUrl); // Get bannerfile

                            if ((episodeNo != 0) && (videoTags.Episode == 0)) videoTags.Episode = episodeNo;
                            if ((seasonNo != 0) && (videoTags.Season == 0)) videoTags.Season = seasonNo;
                            if (!String.IsNullOrWhiteSpace(episodeOverview) && String.IsNullOrWhiteSpace(videoTags.Description)) videoTags.Description = episodeOverview;
                            else if (!String.IsNullOrWhiteSpace(overview) && (String.IsNullOrWhiteSpace(videoTags.Description))) videoTags.Description = overview;
                            if (!String.IsNullOrWhiteSpace(seriesID) && String.IsNullOrWhiteSpace(videoTags.tvdbSeriesId)) videoTags.tvdbSeriesId = seriesID;
                            if (!String.IsNullOrWhiteSpace(imdbID) && String.IsNullOrWhiteSpace(videoTags.imdbMovieId)) videoTags.imdbMovieId = imdbID;
                            if (genres.Count > 0)
                            {
                                if (videoTags.Genres != null)
                                {
                                    if (videoTags.Genres.Length == 0)
                                        videoTags.Genres = genres.ToArray();
                                }
                                else
                                    videoTags.Genres = genres.ToArray();
                            }

                            firstAiredStr = XML.GetXMLTagValue("FirstAired", ItrS.Current.OuterXml);
                            if (DateTime.TryParse(firstAiredStr, out firstAired))
                            {
                                if ((firstAired > GlobalDefs.NO_BROADCAST_TIME) && (videoTags.OriginalBroadcastDateTime <= GlobalDefs.NO_BROADCAST_TIME)) // Only update if there isn't one already
                                    videoTags.OriginalBroadcastDateTime = firstAired; // TVDB stores time in network (local) timezone
                            }

                            return true; // Found a match got all the data, we're done here
                        }
                    }
                }
            }

            return false;
        }
    }
}
