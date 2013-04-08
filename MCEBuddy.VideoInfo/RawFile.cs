using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MCEBuddy.VideoProperties
{
    public static class RawFile
    {
        public static string VideoFormat(string rawFileName)
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

        /// <summary>
        /// Gets the Audio Delay from a video file
        /// </summary>
        /// <param name="rawFileName">Video file</param>
        /// <returns>Audio delay in seconds</returns>
        public static float AudioDelay(string rawFileName)
        {
            MediaInfoDll mi = new MediaInfoDll();

            try
            {
                mi.Open(rawFileName);
                mi.Option("Inform", "Audio; %Video_Delay%");
                float audioDelay;
                float.TryParse(mi.Inform(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out audioDelay);
                audioDelay = (float)audioDelay / 1000; // convert to seconds
                return audioDelay;
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }
}
