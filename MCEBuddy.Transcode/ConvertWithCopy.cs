using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

using MCEBuddy.Globals;
using MCEBuddy.VideoProperties;
using MCEBuddy.Util;
using MCEBuddy.CommercialScan;

namespace MCEBuddy.Transcode
{
    public class ConvertWithCopy : ConvertBase
    {
        public ConvertWithCopy(ConversionJobOptions conversionOptions, string tool, ref VideoInfo videoFile, ref JobStatus jobStatus, Log jobLog, ref Scanner commercialScan)
            : base(conversionOptions, tool, ref videoFile, ref jobStatus, jobLog, ref commercialScan)
        {

        }

        protected override bool IsPresetWidth()
        {
            return false;
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

        protected override void SetBitrateAndQuality()
        {
        }

        protected override void SetResize()
        {
        }

        protected override void SetCrop()
        {
        }

        protected override void SetAspectRatio()
        {
        }

        protected override bool ConstantQuality
        {
            get { return false; }
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
            try
            {
                //nothing here
                File.Copy(SourceVideo, _convertedFile); // Same file just copy it over since we need to preserve the original/remuxed file
                _jobStatus.PercentageComplete = 100;
                return true;
            }
            catch (Exception e)
            {
                _jobLog.WriteEntry("Error processing file with Copy Encoder, error -> " + e.ToString(), Log.LogEntryType.Error);
                return false;
            }
        }
    }
}
