using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.XPath;
using System.Globalization;

//using HtmlAgilityPack;

using MCEBuddy.Globals;
using MCEBuddy.Util;

namespace MCEBuddy.MetaData
{
    public static class TV
    {
        static public bool DownloadSeriesDetails(ref VideoTags videoTags)
        {
            //HtmlDocument htmlDoc;

            //try
            //{
            //    htmlDoc = new HtmlWeb().Load("http://www.tv.com/search?q=" + videoTags.Title);
            //    HtmlNodeCollection nodes = htmlDoc.DocumentNode.SelectNodes("//li[@class='result show']");
            //    foreach (HtmlNode node in nodes)
            //    {
            //        string seriesTitle = "", seriesLink = "", bannerUrl = "";

            //        HtmlDocument subDoc = new HtmlDocument();
            //        subDoc.Load(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(node.InnerHtml)));

            //        // Get the banner URL
            //        HtmlNode urlNode = subDoc.DocumentNode.SelectSingleNode("//div[@class='mask']//img[@src]");
            //        if (urlNode != null)
            //            if (urlNode.HasAttributes)
            //                bannerUrl = urlNode.Attributes["src"].Value.Trim(); // URL
            //            else
            //                bannerUrl = ""; // reset each cycle
            //        else
            //            bannerUrl = ""; // reset each cycle

            //        // Get the series name and link to page
            //        HtmlNode subNode = subDoc.DocumentNode.SelectSingleNode("//div[@class='info']//h4//a[@href]");
            //        if (subNode != null)
            //        {
            //            seriesTitle = subNode.InnerText.Trim(); // Get the title of the series

            //            // Compare the series title with the title of the recording
            //            if (String.Compare(seriesTitle.ToLower().Trim(), videoTags.Title.ToLower().Trim(), CultureInfo.InvariantCulture, CompareOptions.IgnoreSymbols) == 0)
            //            {
            //                HtmlNode subNode1 = subDoc.DocumentNode.SelectSingleNode("//ul[@class='sub_links _inline_navigation']");
            //                if (subNode1 != null)
            //                {
            //                    HtmlDocument subDoc1 = new HtmlDocument();
            //                    subDoc1.Load(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(subNode1.InnerHtml)));
            //                    HtmlNode subNode2 = subDoc1.DocumentNode.SelectSingleNode("//li//a[@href]");
            //                    if (subNode2 != null)
            //                    {
            //                        if (subNode2.HasAttributes)
            //                        {
            //                            seriesLink = subNode2.Attributes["href"].Value; // Get the link for the episodes in the series

            //                            // Now the links to the various season pages
            //                            HtmlDocument sDoc = new HtmlWeb().Load("http://www.tv.com" + seriesLink);
            //                            HtmlNodeCollection sNodes = sDoc.DocumentNode.SelectNodes("//li[@class='filter ']//a[@href]");
            //                            foreach (HtmlNode sNode in sNodes) // go through each season
            //                            {
            //                                string seasonLink;

            //                                // Now extract the link to the season episodes page
            //                                if (sNode.HasAttributes)
            //                                {
            //                                    seasonLink = sNode.Attributes["href"].Value; // the href has the link to the season page

            //                                    // Now the links to the various season pages
            //                                    HtmlDocument eDoc = new HtmlWeb().Load("http://www.tv.com" + seasonLink);

            //                                    HtmlNodeCollection eNodes = eDoc.DocumentNode.SelectNodes("//div[@class='no_toggle_wrapper _clearfix']");
            //                                    foreach (HtmlNode eNode in eNodes) // Now extract the episode names, original air dates and compare
            //                                    {
            //                                        string episodeName = "", episodeDesc = "";
            //                                        DateTime airDate = GlobalDefs.NO_BROADCAST_TIME;
            //                                        int episodeNo = 0, seasonNo = 0;

            //                                        HtmlNode tempNode;
            //                                        HtmlDocument tempDoc = new HtmlDocument();
            //                                        tempDoc.Load(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(eNode.InnerHtml)));

            //                                        // Extract the season number
            //                                        tempNode = eDoc.DocumentNode.SelectSingleNode("//li[@class='filter selected']");
            //                                        if (tempNode != null)
            //                                            if (tempNode.HasAttributes)
            //                                                int.TryParse(tempNode.Attributes["data-season"].Value.Trim(), out seasonNo); // Season number

            //                                        // Extract the episode name
            //                                        tempNode = tempDoc.DocumentNode.SelectSingleNode("//a[@class='title']");
            //                                        if (tempNode != null)
            //                                            episodeName = tempNode.InnerText.Trim(); // Episode Name

            //                                        // Extract the episode number
            //                                        tempNode = tempDoc.DocumentNode.SelectSingleNode("//div[@class='ep_info']");
            //                                        if (tempNode != null)
            //                                            int.TryParse(tempNode.InnerText.Trim().Replace("Episode", ""), out episodeNo); // Episode number

            //                                        // Extract the original broadcast date
            //                                        tempNode = tempDoc.DocumentNode.SelectSingleNode("//div[@class='date']");
            //                                        if (tempNode != null)
            //                                            DateTime.TryParse(tempNode.InnerText.Trim(), null, DateTimeStyles.AssumeLocal, out airDate); // Air Date

            //                                        // Extract the episode description
            //                                        tempNode = tempDoc.DocumentNode.SelectSingleNode("//div[@class='description']");
            //                                        if (tempNode != null)
            //                                            episodeDesc = tempNode.InnerText.Trim(); // Episode description

            //                                        // Now match and store - match either episode name or air date
            //                                        // The information is stored on the server using the network timezone
            //                                        // So we assume that the show being converted was recorded locally and is converted locally so the timezones match
            //                                        DateTime recordingAir = videoTags.OriginalBroadcastDateTime.ToLocalTime();
            //                                        if ((String.Compare(episodeName.ToLower().Trim(), videoTags.SubTitle.ToLower().Trim(), CultureInfo.InvariantCulture, CompareOptions.IgnoreSymbols) == 0) || (recordingAir.Date == airDate.Date))
            //                                        {
            //                                            // Home free - update all the info where required
            //                                            if (!MCEBuddy.Globals.GlobalDefs.IsNullOrWhiteSpace(episodeName) && MCEBuddy.Globals.GlobalDefs.IsNullOrWhiteSpace(videoTags.SubTitle)) videoTags.SubTitle = episodeName;
            //                                            if (!MCEBuddy.Globals.GlobalDefs.IsNullOrWhiteSpace(episodeDesc) && MCEBuddy.Globals.GlobalDefs.IsNullOrWhiteSpace(videoTags.Description)) videoTags.Description = episodeDesc;
            //                                            if ((episodeNo != 0) && (videoTags.Episode == 0)) videoTags.Episode = episodeNo;
            //                                            if ((seasonNo != 0) && (videoTags.Season == 0)) videoTags.Season = seasonNo;
            //                                            if ((airDate > GlobalDefs.NO_BROADCAST_TIME) && (videoTags.OriginalBroadcastDateTime <= GlobalDefs.NO_BROADCAST_TIME)) videoTags.OriginalBroadcastDateTime = airDate;

            //                                            VideoMetaData.DownloadBannerFile(ref videoTags, bannerUrl); // Get bannerfile

            //                                            // All Good now
            //                                            return true;
            //                                        }
            //                                    }
            //                                }
            //                            }
            //                        }
            //                    }
            //                }
            //            }
            //        }
            //    }

                return false;
            //}
            //catch
            //{
            //    return false;
            //}
        }
    }
}
