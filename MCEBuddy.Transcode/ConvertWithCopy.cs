using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using MCEBuddy.Globals;
using MCEBuddy.VideoProperties;
using MCEBuddy.Util;
using MCEBuddy.CommercialScan;

namespace MCEBuddy.Transcode
{
    public class ConvertWithCopy : ConvertBase
    {
        public ConvertWithCopy(ConversionJobOptions conversionOptions, string tool, ref VideoInfo videoFile, ref JobStatus jobStatus, Log jobLog, ref Scanner commercialScan, bool fixCorruptedRemux)
            : base(conversionOptions, tool, ref videoFile, ref jobStatus, jobLog, ref commercialScan)
        {

        }

        protected override void SetTrim()
        {
        }

        protected override void SetPostDRC()
        {
        }

        protected override void SetPreDRC() // ffmpeg needs to setup this parameter before the inputs file because it applies to decoding the input
        {
        }

        protected override void SetVolume()
        {
        }

        protected override void SetQuality()
        {
        }

        protected override void SetResize()
        {
        }

        protected override void SetPostCrop()
        {
        }

        protected override void SetPreCrop()
        {
        }

        protected override void SetAspectRatio()
        {
        }

        protected override bool ConstantQuality
        {
            get
            {
                return false;
            }
        }

        protected override int DefaultVideoWidth
        {
            get
            {
                // Get the profile conversion width
                return DEFAULT_VIDEO_WIDTH;
            }
        }

        protected override void SetInputFileName() // general parameters already setup, now add the input filename details
        {
        }

        protected override void SetOutputFileName() // general + input + video + audio setup, now add the output filename
        {
        }

        protected override void SetAudioLanguage()
        {
        }

        protected override void SetAudioChannels()
        {
        }

        protected override bool ConvertWithTool()
        {
            //nothing here
            _replaceWithOriginalName = ""; // incase someone accidentally populated the copy-ext= field
            _convertedFile = SourceVideo; // same file
            _jobStatus.PercentageComplete = 100;
            return true;
        }
    }
}
