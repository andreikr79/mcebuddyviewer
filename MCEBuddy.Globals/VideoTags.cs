using System;
using System.Collections.Generic;
using System.Text;
using MCEBuddy.Globals;

namespace MCEBuddy.Globals 
{
    public class VideoTags
    {
        public string Title = "";
        public string SubTitle = "";
        public string Description = "";
        public string Network = "";
        public string[] Genres = null;
        public int Season = 0;
        public int Episode = 0;
        public string BannerFile = "";
        public string BannerURL = ""; 
        public string imdbMovieId = ""; // IMDB ID
        public string movieDBMovieId = ""; // MovieDB ID
        public string tvdbSeriesId = ""; // TVDB ID
        public bool IsMovie = false;
        public DateTime OriginalBroadcastDateTime = GlobalDefs.NO_BROADCAST_TIME;
        public DateTime RecordedDateTime = GlobalDefs.NO_BROADCAST_TIME;
        public bool CopyProtected = false;

        public override string ToString()
        {
            string AllTags = "";
            AllTags += "Title: " + Title + "\n";
            AllTags += "SubTitle: " + SubTitle + "\n";
            AllTags += "Description: " + Description + "\n";
            AllTags += "Network: " + Network + "\n";
            AllTags += "Genres: "; 
            if (Genres != null)
            {
                foreach (string Genre in Genres)
                {
                     AllTags += Genre + ",";
                }
                AllTags = AllTags.Substring(0, AllTags.Length - 1);
                AllTags += "\n";
            }
            AllTags += "Season: " + Season.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n";
            AllTags += "Episode: " + Episode.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n";
            AllTags += "Banner: " + BannerFile + "\n";
            AllTags += "Banner URL: " + BannerURL + "\n";
            AllTags += "IMDB MovieId: " + imdbMovieId + "\n";
            AllTags += "MovieDB MovieId: " + movieDBMovieId + "\n";
            AllTags += "TVDB SeriesId: " + tvdbSeriesId + "\n";
            AllTags += "Is Show Movie: " + IsMovie.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n"; 
            AllTags += "OriginalBroadcastDateTime: " + OriginalBroadcastDateTime.ToLocalTime().ToString("s", System.Globalization.CultureInfo.InvariantCulture) + "\n";
            AllTags += "RecordedDateTime: " + RecordedDateTime.ToLocalTime().ToString("s", System.Globalization.CultureInfo.InvariantCulture) + "\n";
            AllTags += "CopyProtected: " + CopyProtected.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n";
            
            if (AllTags == "") return "No tags found";
            else return AllTags;
        }
    }
}
