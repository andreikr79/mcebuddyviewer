using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Net;

namespace MCEBuddy.Util
{
    public static class Internet
    {
        public static bool WGet(string URL, string destFileName)
        {
            DateTime EndTime = DateTime.Now.AddSeconds(30);
            try
            {
                WebClient Client = new WebClient();
                Client.DownloadFileAsync(new Uri(URL), destFileName);
                while ((Client.IsBusy) && (DateTime.Now < EndTime))
                {
                    System.Threading.Thread.Sleep(200);
                }
                if (Client.IsBusy) Client.CancelAsync();
                Client.Dispose();
            }
            catch
            {
                if (File.Exists(destFileName)) File.Delete(destFileName);
                return false;
            }
            return true;
        }
    }
}
