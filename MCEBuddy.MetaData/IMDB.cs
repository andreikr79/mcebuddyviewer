using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.XPath;
using System.Globalization;

using MCEBuddy.Globals;
using MCEBuddy.Util;

namespace MCEBuddy.MetaData
{
    public static class IMDB
    {
        static public bool DownloadMovieDetails(ref VideoTags videoTags)
        {
            XPathDocument Xp;
            string bannerUrl = "";

            try
            {
                if (MCEBuddy.Globals.GlobalDefs.IsNullOrWhiteSpace(videoTags.imdbMovieId)) // If dont' have a specific movieId specified, look up the movie details
                {
                    if (videoTags.OriginalBroadcastDateTime > GlobalDefs.NO_BROADCAST_TIME)
                    {
                        // The information is stored on the server using the network timezone
                        // So we assume that the show being converted was recorded locally and is converted locally so the timezones match
                        DateTime dt = videoTags.OriginalBroadcastDateTime.ToLocalTime();
                        Xp = new XPathDocument("http://imdbapi.org/?title=" + videoTags.Title + "&type=xml&plot=full&episode=1&limit=100&mt=none&lang=en-US&offset=&aka=full&release=simple&business=0&tech=0&yg=1&year=" + dt.Year.ToString(CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        Xp = new XPathDocument("http://imdbapi.org/?title=" + videoTags.Title + "&type=xml&plot=full&episode=1&limit=100&mt=none&lang=en-US&offset=&aka=full&release=simple&business=0&tech=0&yg=0");
                    }
                }
                else
                {
                    Xp = new XPathDocument("http://imdbapi.org/?ids=" + videoTags.imdbMovieId + "&type=xml&plot=full&episode=1&lang=en-US&aka=full&release=simple&business=0&tech=0");
                }

                XPathNavigator Nav = Xp.CreateNavigator();
                XPathExpression Exp = Nav.Compile("//IMDBDocumentList/item");
                XPathNodeIterator Itr = Nav.Select(Exp);

                while (Itr.MoveNext())
                {
                    // Get and match Movie name
                    string movieName = XML.GetXMLTagValue("title", Itr.Current.OuterXml);
                    if (MCEBuddy.Globals.GlobalDefs.IsNullOrWhiteSpace(videoTags.imdbMovieId)) // Match names only if the movie imdb id is not forced, else take what is returned by moviedb
                    {
                        string[] akaValues = XML.GetXMLSubTagValues("also_known_as", "item", "title", Itr.Current.OuterXml); // check AKA names also - Also Known As (for language and localization)
                        string title = videoTags.Title;
                        bool akaMatch = false;
                        if (akaValues != null) // Check if there are any AKA names to match
                            akaMatch = akaValues.Any(s => (String.Compare(s.ToLower().Trim(), title.ToLower().Trim(), CultureInfo.InvariantCulture, CompareOptions.IgnoreSymbols) == 0 ? true : false));
                        
                        if ((String.Compare(movieName.ToLower().Trim(), videoTags.Title.ToLower().Trim(), CultureInfo.InvariantCulture, CompareOptions.IgnoreSymbols) != 0) && (!akaMatch)) // ignore white space and special characters - check for both, Title and AKA names looking for a match
                            continue; // No match in name or AKA

                        videoTags.imdbMovieId = XML.GetXMLTagValue("imdb_id", Itr.Current.OuterXml); // since IMDB movie is not forced, get it here
                    }
                    else if (!MCEBuddy.Globals.GlobalDefs.IsNullOrWhiteSpace(movieName)) // make sure there is actually something returned here otherwise use default title
                        videoTags.Title = movieName; // Take what is forced for the imdb movie id

                    videoTags.IsMovie = true;

                    // Get Overview
                    string overview = XML.GetXMLTagValue("plot", Itr.Current.OuterXml);
                    if (!MCEBuddy.Globals.GlobalDefs.IsNullOrWhiteSpace(overview) && MCEBuddy.Globals.GlobalDefs.IsNullOrWhiteSpace(videoTags.Description))
                        videoTags.Description = overview;

                    // Get original release date
                    DateTime releaseDate = GlobalDefs.NO_BROADCAST_TIME;
                    string released = XML.GetXMLTagValue("release_date", Itr.Current.OuterXml);
                    if (DateTime.TryParseExact(released, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out releaseDate))
                    {
                        if ((videoTags.OriginalBroadcastDateTime <= GlobalDefs.NO_BROADCAST_TIME) && (releaseDate > GlobalDefs.NO_BROADCAST_TIME)) // Only update if there isn't one already
                            videoTags.OriginalBroadcastDateTime = releaseDate; // MovieDB stores time in network (local) timezone
                    }

                    string[] genres = XML.GetXMLSubTagValues("genres", "item", Itr.Current.OuterXml); // Get Genres
                    if (genres != null)
                    {
                        if (genres.Length > 0)
                        {
                            if (videoTags.Genres != null)
                            {
                                if (videoTags.Genres.Length == 0)
                                    videoTags.Genres = genres;
                            }
                            else
                                videoTags.Genres = genres;
                        }
                    }

                    bannerUrl = XML.GetXMLTagValue("poster", Itr.Current.OuterXml); // Get Poster URL

                    // Download the banner file
                    VideoMetaData.DownloadBannerFile(ref videoTags, bannerUrl); // Get bannerfile

                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        
        static public bool DownloadSeriesDetails(ref VideoTags videoTags)
        {
            XPathDocument Xp;
            string bannerUrl = "";

            try
            {
                if (MCEBuddy.Globals.GlobalDefs.IsNullOrWhiteSpace(videoTags.imdbMovieId)) // If dont' have a specific movieId specified, look up the series details
                {
                    Xp = new XPathDocument("http://imdbapi.org/?title=" + videoTags.Title + "&type=xml&plot=full&episode=1&limit=100&mt=none&lang=en-US&offset=&aka=full&release=simple&business=0&tech=0&yg=0");
                }
                else
                {
                    Xp = new XPathDocument("http://imdbapi.org/?ids=" + videoTags.imdbMovieId + "&type=xml&plot=full&episode=1&lang=en-US&aka=full&release=simple&business=0&tech=0");
                }

                XPathNavigator Nav = Xp.CreateNavigator();
                XPathExpression Exp = Nav.Compile("//IMDBDocumentList/item");
                XPathNodeIterator Itr = Nav.Select(Exp);

                while (Itr.MoveNext())
                {
                    // Get and match the Series name
                    string seriesName = XML.GetXMLTagValue("title", Itr.Current.OuterXml);

                    if (MCEBuddy.Globals.GlobalDefs.IsNullOrWhiteSpace(videoTags.imdbMovieId)) // Match names only if the movie imdb id is not forced, else take what is returned by moviedb
                    {
                        string[] akaValues = XML.GetXMLSubTagValues("also_known_as", "item", "title", Itr.Current.OuterXml); // check AKA names also - Also Known As (for language and localization)
                        string title = videoTags.Title;
                        bool akaMatch = false;
                        if (akaValues != null) // Check if there are any AKA names to match
                            akaMatch = akaValues.Any(s => (String.Compare(s.ToLower().Trim(), title.ToLower().Trim(), CultureInfo.InvariantCulture, CompareOptions.IgnoreSymbols) == 0 ? true : false));
                        
                        if ((String.Compare(seriesName.ToLower().Trim(), videoTags.Title.ToLower().Trim(), CultureInfo.InvariantCulture, CompareOptions.IgnoreSymbols) != 0) && (!akaMatch)) // ignore white space and special characters - check for both, Title and AKA names looking for a match
                            continue; // No match in name or AKA
                    }

                    // Look for the right Episode
                    XPathNodeIterator ItrE = Nav.Select(Nav.Compile("//IMDBDocumentList/item/episodes/item"));

                    while (ItrE.MoveNext())
                    {
                        // Get the Episode name and match it
                        string episodeName = XML.GetXMLTagValue("title", ItrE.Current.OuterXml);
                        if (String.Compare(episodeName.ToLower().Trim(), videoTags.SubTitle.ToLower().Trim(), CultureInfo.InvariantCulture, CompareOptions.IgnoreSymbols) == 0) // ignore white space and special characters
                        {
                            // Bingo - home run - set the stuff and get out of here
                            bannerUrl = XML.GetXMLTagValue("poster", Itr.Current.OuterXml); // Get Poster URL

                            // TODO: IMDBAPI.org does not yet support series release dates -> Get original release date

                            // Get Genre's
                            string[] genres = XML.GetXMLSubTagValues("genres", "item", Itr.Current.OuterXml); // Get Genres

                            // Get Overview
                            string episodeOverview = XML.GetXMLTagValue("plot", Itr.Current.OuterXml);

                            // Season
                            int episodeNo = 0;
                            int.TryParse(XML.GetXMLTagValue("episode", ItrE.Current.OuterXml), out episodeNo);

                            // Episode
                            int seasonNo = 0;
                            int.TryParse(XML.GetXMLTagValue("season", ItrE.Current.OuterXml), out seasonNo);

                            // IMDB Movie Id
                            string imdbID = XML.GetXMLTagValue("imdb_id", Itr.Current.OuterXml);

                            if ((episodeNo != 0) && (videoTags.Episode == 0)) videoTags.Episode = episodeNo;
                            if ((seasonNo != 0) && (videoTags.Season == 0)) videoTags.Season = seasonNo;
                            if (!MCEBuddy.Globals.GlobalDefs.IsNullOrWhiteSpace(episodeName) && MCEBuddy.Globals.GlobalDefs.IsNullOrWhiteSpace(videoTags.SubTitle)) videoTags.SubTitle = episodeName;
                            if (!MCEBuddy.Globals.GlobalDefs.IsNullOrWhiteSpace(episodeOverview) && MCEBuddy.Globals.GlobalDefs.IsNullOrWhiteSpace(videoTags.Description)) videoTags.Description = episodeOverview;
                            else if (!MCEBuddy.Globals.GlobalDefs.IsNullOrWhiteSpace(episodeOverview) && (MCEBuddy.Globals.GlobalDefs.IsNullOrWhiteSpace(videoTags.Description))) videoTags.Description = episodeOverview;
                            if (!MCEBuddy.Globals.GlobalDefs.IsNullOrWhiteSpace(imdbID) && MCEBuddy.Globals.GlobalDefs.IsNullOrWhiteSpace(videoTags.imdbMovieId)) videoTags.imdbMovieId = imdbID;

                            if (genres != null)
                            {
                                if (genres.Length > 0)
                                {
                                    if (videoTags.Genres != null)
                                    {
                                        if (videoTags.Genres.Length == 0)
                                            videoTags.Genres = genres;
                                    }
                                    else
                                        videoTags.Genres = genres;
                                }
                            }

                            // Download the banner file
                            VideoMetaData.DownloadBannerFile(ref videoTags, bannerUrl); // Get bannerfile

                            return true; // Golden
                        }
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
