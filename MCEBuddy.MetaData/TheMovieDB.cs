using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.XPath;

using MCEBuddy.Globals;
using MCEBuddy.Util;

namespace MCEBuddy.MetaData
{
    public static class TheMovieDB
    {
        private static string GetValue(string Tag, string Source)
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


        static public bool DownloadMovieDetails(ref VideoTags videoTags)
        {
            const string APIKey = "5e48613cb827da54f11ab3c8e952bb54";
            XPathDocument Xp;
            string bannerURL = "";
            string movieId = "";

            try
            {
                if (string.IsNullOrEmpty(videoTags.movieId)) // If dont' have a specific movieId specified, look up the movie details
                {
                    if (videoTags.OriginalBroadcastDateTime > GlobalDefs.NO_BROADCAST_TIME)
                    {
                        // The information is stored on the server using the network timezone
                        // So we assume that the show being converted was recorded locally and is converted locally so the timezones match
                        DateTime dt = videoTags.OriginalBroadcastDateTime.ToLocalTime();
                        Xp = new XPathDocument("http://api.themoviedb.org/2.1/Movie.search/" + Localise.TwoLetterISO() + "/xml/" + APIKey + "/" + videoTags.Title + "+" + dt.Year.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        Xp = new XPathDocument("http://api.themoviedb.org/2.1/Movie.search/" + Localise.TwoLetterISO() + "/xml/" + APIKey + "/" + videoTags.Title);
                    }
                }
                else
                {
                    Xp = new XPathDocument("http://api.themoviedb.org/2.1/Movie.imdbLookup/" + Localise.TwoLetterISO() + "/xml/" + APIKey + "/" + videoTags.movieId);
                }

                XPathNavigator Nav = Xp.CreateNavigator();
                XPathExpression Exp = Nav.Compile("//OpenSearchDescription/movies/movie/images/image");
                XPathNodeIterator Itr = Nav.Select(Exp);

                movieId = GetValue("imdb_id", Itr.Current.InnerXml);
                videoTags.movieId = movieId;
                videoTags.IsMovie = true;

                while (Itr.MoveNext())
                {
                    bannerURL = Itr.Current.GetAttribute("url", "");
                    if (bannerURL != "") break;
                }

                if (Util.Internet.WGet(bannerURL, videoTags.BannerFile))
                {
                    if (File.Exists(videoTags.BannerFile))
                    {
                        videoTags.BannerURL = bannerURL;
                        return true;
                    }
                    else
                    {
                        videoTags.BannerFile = "";
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
