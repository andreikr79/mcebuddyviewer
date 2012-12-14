using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MCEBuddy.Globals
{
    /// <summary>
    /// Class that represents Media information (default values are "" or -1)
    /// </summary>
    [Serializable]
    public class MediaInfo
    {
        /// <summary>
        /// Video information, only 1 video track per file
        /// </summary>
        public Video VideoInfo; // only 1 video track per file
        /// <summary>
        /// Array of Audio information, 1 per audio track
        /// </summary>
        public Audio[] AudioInfo; // can have multiple audio tracks per file
        /// <summary>
        /// Array of Subtitle information, 1 per subtitle track
        /// </summary>
        public Subtitle[] SubtitleInfo; // can have multiple subtitles per file

        /// <summary>
        /// Video information
        /// </summary>
        [Serializable]
        public class Video
        {
            /// <summary>
            /// Video stream number
            /// </summary>
            public int Stream = -1;
            /// <summary>
            /// Video codec
            /// </summary>
            public string VideoCodec = "";
            /// <summary>
            /// Duration of video in seconds
            /// </summary>
            public float Duration = -1;
            /// <summary>
            /// Color space format
            /// </summary>
            public string Format = "";
            /// <summary>
            /// Height in pixels
            /// </summary>
            public int Height = -1;
            /// <summary>
            /// Width in pixels
            /// </summary>
            public int Width = -1;
            /// <summary>
            /// Storage aspect ratio
            /// </summary>
            public string SAR = ""; // Storage Aspect Ratio
            /// <summary>
            /// Display aspect ratio
            /// </summary>
            public string DAR = ""; // Display Aspect Ratio
            /// <summary>
            /// Video Bitrate in kb/s
            /// </summary>
            public int BitRate = -1; // Video BitRate
            /// <summary>
            /// Video FPS
            /// </summary>
            public float FPS = -1; // Frames per second
            /// <summary>
            /// PID for the stream
            /// </summary>
            public int PID = -1; // PID for the stream
        }

        /// <summary>
        /// Audio information
        /// </summary>
        [Serializable]
        public class Audio
        {
            /// <summary>
            /// Audio stream number
            /// </summary>
            public int Stream = -1; // Audio Stream number in the file
            /// <summary>
            /// Audio language
            /// </summary>
            public string Language = ""; // Audio language
            /// <summary>
            /// Audio codec
            /// </summary>
            public string AudioCodec = "";
            /// <summary>
            /// Sampling frequency
            /// </summary>
            public int Rate = -1; // Sampling frequency
            /// <summary>
            /// Number of audio channels
            /// </summary>
            public int Channels = -1; // Number of channels in this audio stream
            /// <summary>
            /// Number of bits per sample
            /// </summary>
            public int SamplingBits = -1; // number of bits per sample
            /// <summary>
            /// Audio bitrate
            /// </summary>
            public int BitRate = -1; // Audio bitrate
            /// <summary>
            /// Audio PID for stream
            /// </summary>
            public int PID = -1; // PID for the stream
        }

        /// <summary>
        /// Subtitle information
        /// </summary>
        [Serializable]
        public class Subtitle
        {
            /// <summary>
            /// Subtitle stream number
            /// </summary>
            public int Stream = -1; // Subtitle stream number
            /// <summary>
            /// Subtitle language
            /// </summary>
            public string Language = ""; // Subtitle language
            /// <summary>
            /// Subtitle name
            /// </summary>
            public string Name = ""; // Subtitle name
            /// <summary>
            /// Subtitle PID for stream
            /// </summary>
            public int PID = -1; // PID for the stream
        }
    }
}
