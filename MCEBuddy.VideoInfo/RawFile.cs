using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MCEBuddy.VideoProperties
{
    public static class RawFile
    {
        public static string VideoFormat( string rawFileName)
        {
            MediaInfoDll mi = new MediaInfoDll();

            try
            {
                mi.Open(rawFileName);
                mi.Option("Inform", "General;%Video_Format_WithHint_List%");
                return mi.Inform().ToLower().Trim();
            }
            catch (Exception)
            {
                return "";
            }
        }

        public static string AudioFormat(string rawFileName)
        {
            MediaInfoDll mi = new MediaInfoDll();

            try
            {
                mi.Open(rawFileName);
                mi.Option("Inform", "General;%Audio_Format_WithHint_List%");
                return mi.Inform().ToLower().Trim();
            }
            catch (Exception)
            {
                return "";
            }
        }


        public static float FPS(string rawFileName)
        {
            MediaInfoDll mi = new MediaInfoDll();

            try
            {
                mi.Open(rawFileName);
                mi.Option("Inform", "Video;%FrameRate%");
                float fpsout = -1;
                float.TryParse(mi.Inform(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fpsout);
                return fpsout;
            }
            catch (Exception)
            {
                return -1;
            }
        }

    }
}
