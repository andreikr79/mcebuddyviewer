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

        private static string GetValue( string Tag, string Source )
        {
            // It's a hack, but it works

            int StartPos = Source.IndexOf("<" + Tag + ">") + Tag.Length + 2;
            int EndPos = Source.IndexOf("</" + Tag + ">");
            if ((StartPos != -1 ) && (EndPos != -1 ) && (EndPos > StartPos))
            {
                return Source.Substring(StartPos, EndPos - StartPos);
            }
            else
            {
                return "";
            }
        }

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
                if (!string.IsNullOrEmpty(videoTags.movieId)) // If we have a specific IMDB movieId specified, look up the movie details
                {
                    Xp = new XPathDocument("http://www.thetvdb.com/api/GetSeriesByRemoteID.php?imdbid=" + videoTags.movieId);
                }
                else if (!string.IsNullOrEmpty(videoTags.seriesId)) // If we have a specific TVDB seriesId specified, look up the movie details
                {
                    // First try to match by Episode name, many time broadcasters mess up the Original Broadcast date or TVDB has the wrong date (user submitted)
                    if (MatchByEpisodeName(ref videoTags, videoTags.seriesId) == true)
                        return true; // If it's false, we keep looping through all the returned series

                    // Since we cannot match by Episode name, (assuming an error from the broadcaster), let us try to match by original broadcast date
                    if (MatchByBroadcastTime(ref videoTags, videoTags.seriesId) == true)
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
                string seriesID = GetValue("seriesid", Itr.Current.InnerXml);

                if (seriesID == "") continue; // can't do anything without seriesID

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

                try
                {
                    // Get the Series information
                    XpS = new XPathDocument(queryUrl);
                    NavS = XpS.CreateNavigator();
                    ExpS = NavS.Compile("//Data/Series"); // Series information
                    ItrS = NavS.Select(ExpS);
                    ItrS.MoveNext();
                    overview = GetValue("Overview", ItrS.Current.InnerXml);
                    bannerUrl = GetValue("banner", ItrS.Current.InnerXml);

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


                string firstAiredStr = "";
                DateTime firstAired = GlobalDefs.NO_BROADCAST_TIME;
                string seasonNumber = "";
                string episodeNumber = "";
                string episodeName = "";
                string episodeOverview = "";
                while (ItrS.MoveNext())
                {
                    firstAiredStr = GetValue("FirstAired", ItrS.Current.InnerXml);
                    if (DateTime.TryParse(firstAiredStr, out firstAired))
                    {
                        // The information is stored on the server using the network timezone
                        // So we assume that the show being converted was recorded locally and is converted locally so the timezones match
                        DateTime dt = videoTags.OriginalBroadcastDateTime.ToLocalTime();
                        if (firstAired.Date == dt.Date) // TVDB only reports the date not the time
                        {
                            seasonNumber = GetValue("SeasonNumber", ItrS.Current.InnerXml);
                            episodeNumber = GetValue("EpisodeNumber", ItrS.Current.InnerXml);
                            episodeName = GetValue("EpisodeName", ItrS.Current.InnerXml);
                            episodeOverview = GetValue("Overview", ItrS.Current.InnerXml);
                            
                            // ********************
                            // Get the banner file
                            // ********************
                            if ((!File.Exists(videoTags.BannerFile)) && (!String.IsNullOrEmpty(bannerUrl)))
                            {
                                Util.Internet.WGet("http://www.thetvdb.com/banners/" + bannerUrl, videoTags.BannerFile);
                                if (!File.Exists(videoTags.BannerFile))
                                    videoTags.BannerFile = "";
                                else
                                    videoTags.BannerURL = "http://www.thetvdb.com/banners/" + bannerUrl;
                            }
                            else
                                videoTags.BannerURL = "http://www.thetvdb.com/banners/" + bannerUrl;

                            int.TryParse(seasonNumber, out videoTags.Season);
                            int.TryParse(episodeNumber, out videoTags.Episode);
                            videoTags.SubTitle = episodeName;
                            if (episodeOverview != "") videoTags.SubTitleDescription = episodeOverview;
                            else if ((overview != "") && (String.IsNullOrEmpty(videoTags.SubTitleDescription))) videoTags.SubTitleDescription = overview;
                            videoTags.seriesId = seriesID;

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

            if (String.IsNullOrEmpty(videoTags.SubTitle))
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

                try
                {
                    // Get the Series information
                    XpS = new XPathDocument(queryUrl);
                    NavS = XpS.CreateNavigator();
                    ExpS = NavS.Compile("//Data/Series"); // Series information
                    ItrS = NavS.Select(ExpS);
                    ItrS.MoveNext();
                    overview = GetValue("Overview", ItrS.Current.InnerXml);
                    bannerUrl = GetValue("banner", ItrS.Current.InnerXml);

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


                string seasonNumber = "";
                string episodeNumber = "";
                string episodeName = "";
                string episodeOverview = "";
                string firstAiredStr = "";
                DateTime firstAired = GlobalDefs.NO_BROADCAST_TIME;

                while (ItrS.MoveNext())
                {
                    episodeName = GetValue("EpisodeName", ItrS.Current.InnerXml);
                    if (episodeName != "")
                    {

                        if (videoTags.SubTitle.ToLower() == episodeName.ToLower()) // Compare the episode names (case can change very often)
                        {
                            seasonNumber = GetValue("SeasonNumber", ItrS.Current.InnerXml);
                            episodeNumber = GetValue("EpisodeNumber", ItrS.Current.InnerXml);
                            episodeOverview = GetValue("Overview", ItrS.Current.InnerXml);

                            // ********************
                            // Get the banner file
                            // ********************
                            if ((!File.Exists(videoTags.BannerFile)) && (!String.IsNullOrEmpty(bannerUrl)))
                            {
                                Util.Internet.WGet("http://www.thetvdb.com/banners/" + bannerUrl, videoTags.BannerFile);
                                if (!File.Exists(videoTags.BannerFile))
                                {
                                    videoTags.BannerFile = "";
                                }
                                else
                                    videoTags.BannerURL = "http://www.thetvdb.com/banners/" + bannerUrl;
                            }
                            else
                                videoTags.BannerURL = "http://www.thetvdb.com/banners/" + bannerUrl;

                            int.TryParse(seasonNumber, out videoTags.Season);
                            int.TryParse(episodeNumber, out videoTags.Episode);
                            videoTags.SubTitle = episodeName;
                            if (episodeOverview != "") videoTags.SubTitleDescription = episodeOverview;
                            else if ((overview != "") && (String.IsNullOrEmpty(videoTags.SubTitleDescription))) videoTags.SubTitleDescription = overview;
                            videoTags.seriesId = seriesID;

                            firstAiredStr = GetValue("FirstAired", ItrS.Current.InnerXml);
                            if (DateTime.TryParse(firstAiredStr, out firstAired))
                            {
                                if (videoTags.OriginalBroadcastDateTime == GlobalDefs.NO_BROADCAST_TIME) // Only update if there isn't one already
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
