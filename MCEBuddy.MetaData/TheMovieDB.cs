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
    public static class TheMovieDB
    {
        static public bool DownloadMovieDetails(ref VideoTags videoTags)
        {
            const string APIKey = "5e48613cb827da54f11ab3c8e952bb54";
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
                        Xp = new XPathDocument("http://api.themoviedb.org/2.1/Movie.search/" + Localise.TwoLetterISO() + "/xml/" + APIKey + "/" + videoTags.Title.Trim().ToLower() + "+" + dt.Year.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        Xp = new XPathDocument("http://api.themoviedb.org/2.1/Movie.search/" + Localise.TwoLetterISO() + "/xml/" + APIKey + "/" + videoTags.Title.Trim().ToLower());
                    }
                }
                else
                {
                    Xp = new XPathDocument("http://api.themoviedb.org/2.1/Movie.imdbLookup/" + Localise.TwoLetterISO() + "/xml/" + APIKey + "/" + videoTags.imdbMovieId);
                }

                XPathNavigator Nav = Xp.CreateNavigator();
                XPathExpression Exp = Nav.Compile("//OpenSearchDescription/movies/movie");
                XPathNodeIterator Itr = Nav.Select(Exp);

                while (Itr.MoveNext())
                {
                    // Get and match Movie name
                    string movieName = XML.GetXMLTagValue("original_name", Itr.Current.OuterXml);
                    if (MCEBuddy.Globals.GlobalDefs.IsNullOrWhiteSpace(videoTags.imdbMovieId)) // Match names only if the movie imdb id is not forced, else take what is returned by moviedb
                    {
                        if (String.Compare(movieName.ToLower().Trim(), videoTags.Title.ToLower().Trim(), CultureInfo.InvariantCulture, CompareOptions.IgnoreSymbols) != 0) // ignore white space and special characters
                            continue; // No match in name

                        videoTags.imdbMovieId = XML.GetXMLTagValue("imdb_id", Itr.Current.OuterXml); // since IMDB movie is not forced, get it here
                    }
                    else if (!MCEBuddy.Globals.GlobalDefs.IsNullOrWhiteSpace(movieName)) // make sure there is actually something returned here otherwise use default title
                        videoTags.Title = movieName; // Take what is forced for the imdb movie id

                    // Get Movie Id
                    videoTags.movieDBMovieId = XML.GetXMLTagValue("id", Itr.Current.OuterXml);

                    videoTags.IsMovie = true;

                    // Get Overview
                    string overview = XML.GetXMLTagValue("overview", Itr.Current.OuterXml);
                    if (!MCEBuddy.Globals.GlobalDefs.IsNullOrWhiteSpace(overview) && MCEBuddy.Globals.GlobalDefs.IsNullOrWhiteSpace(videoTags.Description))
                        videoTags.Description = overview;

                    // Get original release date
                    DateTime releaseDate = GlobalDefs.NO_BROADCAST_TIME;
                    string released = XML.GetXMLTagValue("released", Itr.Current.OuterXml);
                    if (DateTime.TryParse(released, out releaseDate))
                    {
                        if ((videoTags.OriginalBroadcastDateTime <= GlobalDefs.NO_BROADCAST_TIME) && (releaseDate > GlobalDefs.NO_BROADCAST_TIME)) // Only update if there isn't one already
                            videoTags.OriginalBroadcastDateTime = releaseDate; // MovieDB stores time in network (local) timezone
                    }

                    // Genres from attribute
                    XPathExpression ExpG = Itr.Current.Compile("categories/category");
                    XPathNodeIterator ItrG = Itr.Current.Select(ExpG);

                    List<string> genres = new List<string>();
                    while (ItrG.MoveNext())
                    {
                        string genre = ItrG.Current.GetAttribute("name", "");
                        if (!MCEBuddy.Globals.GlobalDefs.IsNullOrWhiteSpace(genre)) genres.Add(genre);
                    }
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

                    // Banner URL from attribute
                    XPathExpression ExpI = Itr.Current.Compile("images/image");
                    XPathNodeIterator ItrI = Itr.Current.Select(ExpI);

                    while (ItrI.MoveNext())
                    {
                        bannerUrl = ItrI.Current.GetAttribute("url", "");
                        if (!MCEBuddy.Globals.GlobalDefs.IsNullOrWhiteSpace(bannerUrl)) break;
                    }

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
    }
}
