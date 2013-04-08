using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;

using MCEBuddy.AppWrapper;
using MCEBuddy.Globals;
using MCEBuddy.VideoProperties;
using MCEBuddy.Util;

namespace MCEBuddy.CommercialScan
{
    public class Remover : EDL
    {
        private string _convertedVideo;
        private string _ext = "";
        private float _duration;
        private JobStatus _jobStatus;
        private Log _jobLog;
        private VideoInfo _videoFile;
        private bool _cutMP4Alternative = false;
        private bool _universalCommercialRemover = false;

        public Remover(string profile, string convertedVideo, string edlFile, ref VideoInfo videoFile, ref JobStatus jobStatus, Log jobLog)
            : base(profile, convertedVideo, videoFile.Duration, edlFile, ref jobStatus, jobLog)
        {
            _videoFile = videoFile;
            _convertedVideo = convertedVideo;
            _ext = Util.FilePaths.CleanExt(_convertedVideo);
            _duration = _videoFile.Duration;
            _jobStatus = jobStatus;
            _jobLog = jobLog;

            // Read various profile parameters
            Ini configProfileIni = new Ini(GlobalDefs.ProfileFile);
            _cutMP4Alternative = configProfileIni.ReadBoolean(profile, "CutMP4Alternate", false); // for FFMPEG and Handbrake, we have commerical cutting options, for mEncoder cutting is done during conversion using -edl option
            jobLog.WriteEntry("MP4 Alternative Cutting -> " + _cutMP4Alternative.ToString(CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
            _universalCommercialRemover = configProfileIni.ReadBoolean(profile, "UniversalCommercialRemover", false); // Forcing the use of CutFFMPEG which works on all video types
            jobLog.WriteEntry("Universal Commercial Remover -> " + _universalCommercialRemover.ToString(CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
        }

        /// <summary>
        /// Generic cutting algorithm for cutting any file type based on concat protocol specifications using FFMPEG
        /// </summary>
        /// <param name="filterAudioLanguage">Used to filter out a specific audio language while cutting the file. Primarily for TS file when used in pre-conversion cutting</param>
        private void CutFFMPEG(bool filterAudioLanguage=false)
        {
            List<KeyValuePair<float, float>> cutList = new List<KeyValuePair<float, float>>();

            // Read the EDL file and convert into cut structure
            if (!ParseEDLFile(ref cutList))
                return;

            //Do the cuts and pick them up as we go
            int cutNumber = 0;
            List<string> filesFound = new List<string>();

            foreach (KeyValuePair<float, float> cut in cutList)
            {
                _jobStatus.CurrentAction = Localise.GetPhrase("Cutting commercials from video - segment") + " " + cutNumber.ToString(CultureInfo.InvariantCulture);
                _jobLog.WriteEntry(this, Localise.GetPhrase("CutFFMPEG: Cutting commercials from video - segment ") + cutNumber.ToString(CultureInfo.InvariantCulture), Log.LogEntryType.Information);

                // Create the splits - reset success each time
                string cutFileName = CutFileName(_convertedVideo, cut.Key, cut.Value);
                string parameters = " -y -threads 0";

                // http://ffmpeg.org/trac/ffmpeg/wiki/Seeking%20with%20FFmpeg - We use only FAST seek (since accurate seek cuts on a NON KeyFrame which causes Audio Sync Issues)
                parameters += " -ss " + cut.Key.ToString(CultureInfo.InvariantCulture);

                parameters += " -i " + Util.FilePaths.FixSpaces(_convertedVideo);

                // how much to Cut
                parameters += " -t " + (cut.Value - cut.Key).ToString(CultureInfo.InvariantCulture);

                // If we are requested to filter out the selected audio language, check for it's existance and filter it
                if (filterAudioLanguage)
                {
                    if (_videoFile.AudioStream == -1)
                    {
                        _jobLog.WriteEntry("Cannot get audio stream selection details, copying all audio streams", Log.LogEntryType.Warning);
                        parameters += " -map 0:a -map 0:v";
                    }
                    else
                    {
                        _jobLog.WriteEntry("Selecting Audio Language " + _videoFile.AudioLanguage + " Audio Stream " + _videoFile.AudioStream.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
                        parameters += " -map 0:" + _videoFile.AudioStream.ToString(System.Globalization.CultureInfo.InvariantCulture) + " -map 0:v"; // Select the Audiotrack we had isolated earlier
                    }
                }
                else
                    parameters += " -map 0:a -map 0:v";

                // Stream copy
                parameters += " -vcodec copy -acodec copy " + Util.FilePaths.FixSpaces(cutFileName);
                
                FFmpeg cutFFmpeg = new FFmpeg(parameters, ref _jobStatus, _jobLog);
                cutFFmpeg.Run();
                if (!cutFFmpeg.Success) // Don't check for % here since Comskip has a bug that gives EDL cut segments past the end of the file || _jobStatus.PercentageComplete < GlobObjects.ACCEPTABLE_COMPLETION) // each run resets this number or an error in the process
                {
                    _jobLog.WriteEntry("Cutting video segment failed, retying using GenPts", Log.LogEntryType.Warning);

                    // Create the splits - reset success
                    // genpt is required sometimes when -ss is specified before the inputs file, see ffmpeg ticket #2054
                    string Parameters = "-fflags +genpts " + cutFFmpeg.Parameters; // Save it
                    cutFFmpeg = new FFmpeg(Parameters, ref _jobStatus, _jobLog);
                    cutFFmpeg.Run();

                    if (!cutFFmpeg.Success) //check of file is created, outputhandler reports success (Percentage not requires since Success is more accurate)
                    {
                        CleanupCutFiles(filesFound); //Clean up cut files
                        _jobStatus.PercentageComplete = 0;
                        _jobStatus.ErrorMsg = "CutFFMPEG splitting video segments failed";
                        _jobLog.WriteEntry(this, Localise.GetPhrase("CutFFMPEG splitting video segments failed"), Log.LogEntryType.Error);
                        return;
                    }
                }

                filesFound.Add(cutFileName); // to be deleted later
                cutNumber++;
            }

            if (cutNumber < 1)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("No commercials to remove from ") + EDLFile, Log.LogEntryType.Information);
                _jobStatus.PercentageComplete = 100; //Set to success since sometime mp4box doesn't set to 100 if there no/incomplete pieces to strip
                return;
            }

            // Create a text file with the list of all the files which need to be concatenated
            // http://ffmpeg.org/trac/ffmpeg/wiki/How%20to%20concatenate%20(join,%20merge)%20media%20files
            string concatFileList = "";
            string concatFileName = Util.FilePaths.GetFullPathWithoutExtension(_convertedVideo) + "_ConcatList.txt"; // Write file list to text

            foreach (string file in filesFound)
                concatFileList += "file " + "'" + file.Replace("'", @"'\''") + "'" + "\r\n"; // File paths are enclosed in single quotes (compensate for ' in filenames with '\'')

            System.IO.File.WriteAllText(concatFileName, concatFileList); // write the text file

            cutNumber = 0;
            string mergeParams = "";

            // Merge the files
            string tempFile = Util.FilePaths.GetFullPathWithoutExtension(_convertedVideo) + "_MERGE" + _ext;
            mergeParams += "-y -f concat -i " + Util.FilePaths.FixSpaces(concatFileName) + " -vcodec copy -acodec copy -map a -map v " + Util.FilePaths.FixSpaces(tempFile); // Copy all Audio and Video streams

            _jobStatus.CurrentAction = Localise.GetPhrase("Merging commercial free segments into new video");
            _jobLog.WriteEntry(this, Localise.GetPhrase("CutFFMPEG: Merging commercial free segments into new video"), Log.LogEntryType.Information);

            FFmpeg ffmpegMerge = new FFmpeg(mergeParams, ref _jobStatus, _jobLog);
            ffmpegMerge.Run();
            if (!ffmpegMerge.Success || (Util.FileIO.FileSize(tempFile) <= 0))
            {
                _jobLog.WriteEntry("FFMPEG Merging segments failed, retying using GenPts", Log.LogEntryType.Warning);

                // genpt is required sometimes when -ss is specified before the inputs file, see ffmpeg ticket #2054
                mergeParams = "-fflags +genpts " + mergeParams;
                ffmpegMerge = new FFmpeg(mergeParams, ref _jobStatus, _jobLog);
                ffmpegMerge.Run();

                if (!ffmpegMerge.Success || (Util.FileIO.FileSize(tempFile) <= 0)) //check of file is created, outputhandler reports success (Percentage not requires since Success is more accurate)
                {
                    CleanupCutFiles(filesFound); //Clean up cut files
                    _jobStatus.PercentageComplete = 0;
                    _jobStatus.ErrorMsg = "CutFFMPEG merging video segments failed";
                    _jobLog.WriteEntry(this, Localise.GetPhrase("CutFFMPEG merging video segments failed"), Log.LogEntryType.Error); ;
                    return;
                }
            }

            // Move the files
            _jobLog.WriteEntry(this, Localise.GetPhrase("CutFFMPEG trying to replace file") + " Output : " + _convertedVideo + " Temp : " + tempFile, Log.LogEntryType.Debug);
            Util.FileIO.TryFileReplace(_convertedVideo, tempFile);

            //Clean up cut files
            Util.FileIO.TryFileDelete(concatFileName);
            CleanupCutFiles(filesFound); //Clean up cut files
        }

        /// <summary>
        /// Remove commercials from WMV files using ASFBin
        /// </summary>
        private void CutWMV()
        {
            List<KeyValuePair<float, float>> cutList = new List<KeyValuePair<float, float>>();

            // Read the EDL file and convert into MP4Box cut structure
            if (!ParseEDLFile(ref cutList))
                return;

            // Create params to cut the file
            string tempFile = CutFileName(_convertedVideo, 0, 0); // temp cut file
            int cutNumber = 0;
            string cutParams = " -i " + Util.FilePaths.FixSpaces(_convertedVideo);

            foreach (KeyValuePair<float, float> cut in cutList)
            {
                cutParams += " -start " + cut.Key.ToString(CultureInfo.InvariantCulture) + " -dur " + (cut.Value - cut.Key).ToString(CultureInfo.InvariantCulture);
                cutNumber++;
            }

            cutParams += " -o " + Util.FilePaths.FixSpaces(tempFile) + " -y";

            if (cutNumber < 1)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("No commercials to remove from ") + EDLFile, Log.LogEntryType.Information);
                _jobStatus.PercentageComplete = 100; //Set to success since sometime mp4box doesn't set to 100 if there no/incomplete pieces to strip
                return;
            }

            // Cut the file
            _jobStatus.CurrentAction = Localise.GetPhrase("Merging commercial free segments into new video");
            _jobLog.WriteEntry(this, Localise.GetPhrase("CutWMV: Merging commercial free segments into new video"), Log.LogEntryType.Information);
            
            ASFBin asfBin= new ASFBin(cutParams, ref _jobStatus, _jobLog);
            asfBin.Run();
            if (!asfBin.Success || (Util.FileIO.FileSize(tempFile) <= 0))
            {
                _jobStatus.ErrorMsg = "CutWMV Commercial cutting failed";
                _jobLog.WriteEntry(this, Localise.GetPhrase("CutWMV Commercial cutting failed"), Log.LogEntryType.Error);
                _jobStatus.PercentageComplete = 0;
                return;
            }

            _jobLog.WriteEntry(this, Localise.GetPhrase("CutWMV trying to replace file") + " Output : " + _convertedVideo + " Temp : " + tempFile, Log.LogEntryType.Debug);
            Util.FileIO.TryFileReplace(_convertedVideo, tempFile);
        }

        /// <summary>
        /// Remove commercials using Mencoder for AVI, MPG and TS (not good for TS, last backup)
        /// </summary>
        /// <param name="filterTSAudioLanguage">True is want to filter out the audio language specified by the user while removing commercials from TS files only</param>
        private void CutMencoder(bool filterTSAudioLanguage=false)
        {
            string tempFile = Util.FilePaths.GetFullPathWithoutExtension(_convertedVideo) + "_CUT" + _ext;
            string mencoderParams;
            bool oldVersion = false; // Some cases we want to use old version of Mencoder

            switch (_ext)
            {
                case ".mpg":
                    oldVersion = true; // For MPG and TS we use old version of Mencoder

                    // TODO: Need to figure out how to fix the audio video sync, temp fix is to use a special version of mEncoder build here
                    // Do not use -hr-edl-seek as it doesn't work well with -ovc copy
                    // lavdopts supports a max of 8 threads
                    // avoid using threads, as it may cause a problem - anyways we are only copying here
                    mencoderParams = Util.FilePaths.FixSpaces(_convertedVideo) + " -of mpeg -ovc copy -oac copy";

                    mencoderParams += " -edl " + Util.FilePaths.FixSpaces(EDLFile) + " -o " + Util.FilePaths.FixSpaces(tempFile);

                    break;
                
                case ".ts":
                    oldVersion = true; // For MPG and TS we use old version of Mencoder

                    // TODO: Need to figure out how to fix the audio video sync, temp fix is to use a special version of mEncoder build here
                    // Do not use -hr-edl-seek as it doesn't work well with -ovc copy
                    // lavdopts supports a max of 8 threads
                    // avoid using threads, as it may cause a problem - anyways we are only copying here
                    mencoderParams = Util.FilePaths.FixSpaces(_convertedVideo) + " -of lavf -lavfopts format=mpegts -ovc copy -oac copy";

                    // If we are requested to filter out the selected audio language, check for it's existance and filter it
                    if (filterTSAudioLanguage)
                    {
                        if (_videoFile.AudioPID == -1)
                            _jobLog.WriteEntry("Cannot get audio stream selection details, copying all audio streams", Log.LogEntryType.Warning);
                        else
                        {
                            _jobLog.WriteEntry("Selecting Audio Language " + _videoFile.AudioLanguage + " Audio PID " + _videoFile.AudioPID.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
                            mencoderParams += " -aid " + (_videoFile.AudioPID).ToString(System.Globalization.CultureInfo.InvariantCulture); // Select the Audio track PID we had isolated earlier
                        }
                    }

                    mencoderParams += " -edl " + Util.FilePaths.FixSpaces(EDLFile) + " -o " + Util.FilePaths.FixSpaces(tempFile);

                    break;

                case ".avi":
                    // TODO: bug here for AVI files, using "-mc0 -noskip" helps initially but audio out of sync after cuts - need to figure out. Try the -ni (non interleaved) option for avi files, try -idx to rebuild index for audio video
                    // Do not use -hr-edl-seek as it doesn't work well with -ovc copy
                    // lavdopts supports a max of 8 threads
                    // avoid using thread to increase stability since we are only copying here
                    mencoderParams = Util.FilePaths.FixSpaces(_convertedVideo) + " -oac copy -ovc copy -ni -edl " + Util.FilePaths.FixSpaces(EDLFile) + " -o " + Util.FilePaths.FixSpaces(tempFile);
                    break;

                default:
                    _jobLog.WriteEntry(this, Localise.GetPhrase("CutMencoder unsupported file extension") + " " + _ext, Log.LogEntryType.Error);
                    _jobStatus.ErrorMsg = "CutMencoder unsupported file extension";
                    _jobStatus.PercentageComplete = 0;
                    return;
            }

            _jobStatus.CurrentAction = Localise.GetPhrase("Merging commercial free segments into new video");
            _jobLog.WriteEntry(this, Localise.GetPhrase("CutMencoder: Merging commercial free segments into new video"), Log.LogEntryType.Information);

            Mencoder mencoder = new Mencoder(mencoderParams, ref _jobStatus, _jobLog, oldVersion);
            mencoder.Run();
            if (!mencoder.Success || (Util.FileIO.FileSize(tempFile) <= 0)) // do not check for % success here since sometimes it does not show complete number
            {
                _jobStatus.ErrorMsg = "CutMencoder Commercial cutting failed";
                _jobLog.WriteEntry(this, Localise.GetPhrase("CutMencoder Commercial cutting failed"), Log.LogEntryType.Error);
                _jobStatus.PercentageComplete = 0;
                return;
            }

            _jobLog.WriteEntry(this, Localise.GetPhrase("CutMencoder trying to replace file") + " Output : " + _convertedVideo + " Temp : " + tempFile, Log.LogEntryType.Debug);
            Util.FileIO.TryFileReplace(_convertedVideo, tempFile);
        }

        /// <summary>
        /// Backup remove commercials from MP4 using MEncoder, not the best, limited in many ways
        /// </summary>
        private void CutMP4Alternate()
        {
            string tempFile = CutFileName(_convertedVideo, 0, 0);

            // TODO: Need to add support for non AAC Audio Codecs
            // Refer to: http://forum.videohelp.com/threads/337215-Mencoder-not-copying-aac-to-mp4?p=2140000
            // and http://www.mplayerhq.hu/DOCS/codecs-status.html#ac
            // Do not use -hr-edl-seek as it doesn't work well with -ovc copy
            // lavdopts supports a max of 8 threads
            // avoid using threads, stablity - only copying here
            string mencoderParams = Util.FilePaths.FixSpaces(_convertedVideo) + " -of lavf -ovc copy -oac copy";

            //Set fafmttag based on the type of audio codec of converted file (currently supported aac, ac3, eac3)
            _jobLog.WriteEntry(this, Localise.GetPhrase("Trying to reading converted file Audio information"), Log.LogEntryType.Information);
            FFmpegMediaInfo ffmpegStreamInfo = new FFmpegMediaInfo(_convertedVideo, ref _jobStatus, _jobLog);
            string audioCodec = "";
            ffmpegStreamInfo.Run();
            if (ffmpegStreamInfo.Success && !ffmpegStreamInfo.ParseError)
            {
                // Converted file should contain only 1 audio stream
                audioCodec = ffmpegStreamInfo.MediaInfo.AudioInfo[0].AudioCodec;
                _jobLog.WriteEntry(this, Localise.GetPhrase("Found AudioCodec") + " " + audioCodec, Log.LogEntryType.Information);

                if (String.IsNullOrEmpty(audioCodec))
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Audio codec information is blank, will try to continue without it"), Log.LogEntryType.Warning);
            }
            else
                _jobLog.WriteEntry(this, Localise.GetPhrase("Cannot read Audio codec information, will try to continue without it"), Log.LogEntryType.Warning);

            switch (audioCodec)
            {
                case "aac":
                    _jobLog.WriteEntry(this, Localise.GetPhrase("CutMP4Alternate found AAC Audio Codec"), Log.LogEntryType.Information);
                    mencoderParams += @" -fafmttag 0x706D";
                    break;

                case "ac3":
                case "ac-3":
                    _jobLog.WriteEntry(this, Localise.GetPhrase("CutMP4Alternate found AC3 Audio Codec"), Log.LogEntryType.Information);
                    mencoderParams += @" -fafmttag 0x2000";
                    break;

                case "e-ac-3":
                    _jobLog.WriteEntry(this, Localise.GetPhrase("CutMP4Alternate found E-AC3 Audio Codec"), Log.LogEntryType.Information);
                    mencoderParams += @" -fafmttag 0x33434145";
                    break;

                case "mp3":
                    _jobLog.WriteEntry(this, Localise.GetPhrase("CutMP4Alternate found mp3 Audio Codec"), Log.LogEntryType.Information);
                    mencoderParams += @" -fafmttag 0x55";
                    break;

                case "flac":
                    _jobLog.WriteEntry(this, Localise.GetPhrase("CutMP4Alternate found flac Audio Codec"), Log.LogEntryType.Information);
                    mencoderParams += @" -fafmttag 0xF1AC";
                    break;

                case "mp1":
                case "mp2":
                    _jobLog.WriteEntry(this, Localise.GetPhrase("CutMP4Alternate found mp2 Audio Codec"), Log.LogEntryType.Information);
                    mencoderParams += @" -fafmttag 0x50";
                    break;

                default:
                    _jobLog.WriteEntry(this, Localise.GetPhrase("CutMP4Alternate could not identify Audio Codec, DEFAULTING TO AAC - please check video file and audio encoder type"), Log.LogEntryType.Warning);
                    mencoderParams += @" -fafmttag 0x706D";
                    break;
            }

            mencoderParams += " -edl " + Util.FilePaths.FixSpaces(EDLFile) + " -o " + Util.FilePaths.FixSpaces(tempFile);

            _jobStatus.CurrentAction = Localise.GetPhrase("Merging commercial free segments into new video");
            _jobLog.WriteEntry(this, Localise.GetPhrase("CutMP4Alternate: Merging commercial free segments into new video"), Log.LogEntryType.Information);

            Mencoder mencoder = new Mencoder(mencoderParams, ref _jobStatus, _jobLog, false);
            mencoder.Run();
            if (!mencoder.Success || (Util.FileIO.FileSize(tempFile) <= 0)) // do not check for % success here since sometimes it does not show complete number
            {
                _jobStatus.ErrorMsg = "CutMP4Alternate Commercial cutting failed";
                _jobLog.WriteEntry(this, Localise.GetPhrase("CutMP4Alternate Commercial cutting failed"), Log.LogEntryType.Error);
                _jobStatus.PercentageComplete = 0;
                return;
            }

            _jobLog.WriteEntry(this, Localise.GetPhrase("CutMP4Alternate trying to replace file") + " Output : " + _convertedVideo + " Temp : " + tempFile, Log.LogEntryType.Debug);
            Util.FileIO.TryFileReplace(_convertedVideo, tempFile);
        }

        /// <summary>
        /// Remove commercials from MP4 files using MP4Box
        /// </summary>
        private void CutMP4()
        {
            List<KeyValuePair<float, float>> cutList = new List<KeyValuePair<float, float>>();

            // Read the EDL file and convert into MP4Box cut structure
            if (!ParseEDLFile(ref cutList))
                return;

            // Create the MP4 splits
            //Setup the 'look for splits' variables
            string searchPattern = Path.GetFileNameWithoutExtension(_convertedVideo) + "*" + Path.GetExtension(_convertedVideo);
            string searchPath = Path.GetDirectoryName(_convertedVideo);
            List<string> filesFound = new List<string>();
            filesFound.Add(_convertedVideo);

            //Do the cuts and pick them up as we go
            int cutNumber = 0;
            string mergeParams = "";
            foreach (KeyValuePair<float, float> cut in cutList)
            {
                _jobStatus.CurrentAction = Localise.GetPhrase("Cutting commercials from video - segment") + " " + cutNumber.ToString(CultureInfo.InvariantCulture);
                _jobLog.WriteEntry(this, Localise.GetPhrase("MP4Box: Cutting commercials from video - segment ") + cutNumber.ToString(CultureInfo.InvariantCulture), Log.LogEntryType.Information);
                string cutFileName = CutFileName(_convertedVideo, cut.Key, cut.Value);
                Util.FileIO.TryFileDelete(cutFileName);
                string Parameters = " -keep-all -keep-sys -splitx " + cut.Key.ToString(CultureInfo.InvariantCulture) + ":" + cut.Value.ToString(CultureInfo.InvariantCulture) + " " + Util.FilePaths.FixSpaces(_convertedVideo);
                MP4Box mp4Box = new MP4Box(Parameters, ref _jobStatus, _jobLog);
                mp4Box.Run();
                if (!mp4Box.Success) // Don't check for % here since Comskip has a bug that gives EDL cut segments past the end of the file || _jobStatus.PercentageComplete < GlobObjects.ACCEPTABLE_COMPLETION) // each run resets this number or an error in the process
                {
                    _jobStatus.PercentageComplete = 0;
                    _jobStatus.ErrorMsg = "Mp4Box splitting video segments failed";
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Mp4Box splitting video segments failed"), Log.LogEntryType.Error); ;
                    return;
                }


                //Setup the directory search - do not use Enumerate files since it's lazy in updating and skips files
                IEnumerable<string> newFilesFound = new List<string>();
                try
                {
                    newFilesFound = Directory.GetFiles(searchPath, searchPattern).OrderBy(File.GetCreationTime); //put them back in the order they were split
                }
                catch (Exception e)
                {
                    _jobStatus.ErrorMsg = "MP4 Commercial cut failed trying to get list of file segments";
                    _jobLog.WriteEntry(this, Localise.GetPhrase("MP4 Commercial cut failed trying to get list of file segments") + "\r\nError : " + e.ToString(), Log.LogEntryType.Error);
                    _jobStatus.PercentageComplete = 0;
                    return;
                }

                //Look for the split name as it cannot be specified before mp4box execution
                foreach (string f in newFilesFound)
                {
                    if (!filesFound.Contains(f))
                    {
                        // Build the merge cmd
                        if (cutNumber == 0)
                            mergeParams += " -keep-all -keep-sys";

                        mergeParams += " -cat " + Util.FilePaths.FixSpaces(f);
                        cutNumber++;
                        filesFound.Add(f);
                        _jobLog.WriteEntry(this, Localise.GetPhrase("MP4Box: Found a new segment to merge"), Log.LogEntryType.Information);
                        _jobLog.WriteEntry(this, "No : " + cutNumber.ToString(CultureInfo.InvariantCulture) + " Filename : " + f, Log.LogEntryType.Debug);
                    }
                }
            }

            if (cutNumber < 1)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("No commercials to remove from ") + EDLFile, Log.LogEntryType.Information);
                _jobStatus.PercentageComplete = 100; //Set to success since sometime mp4box doesn't set to 100 if there no/incomplete pieces to strip
                return;
            }

            // Merge the splits
            string tempFile = Util.FilePaths.GetFullPathWithoutExtension(_convertedVideo) + "_MERGE" + _ext;

            mergeParams += " -new " + Util.FilePaths.FixSpaces(tempFile);
            
            _jobStatus.CurrentAction = Localise.GetPhrase("Merging commercial free segments into new video");
            _jobLog.WriteEntry(this, Localise.GetPhrase("MP4Box: Merging commercial free segments into new video"), Log.LogEntryType.Information);

            MP4Box mp4BoxMerge = new MP4Box(mergeParams, ref _jobStatus, _jobLog);
            mp4BoxMerge.Run();
            if (!mp4BoxMerge.Success || _jobStatus.PercentageComplete < GlobalDefs.ACCEPTABLE_COMPLETION || (Util.FileIO.FileSize(tempFile) <= 0)) // each run resets this number or an error in the procecss
            {
                CleanupCutFiles(filesFound); //Clean up cut files
                _jobStatus.PercentageComplete = 0;
                _jobStatus.ErrorMsg = "MP4Box merging video segments failed";
                _jobLog.WriteEntry(this, Localise.GetPhrase("MP4Box merging video segments failed"), Log.LogEntryType.Error); ;
                return;
            }


            // Move the files
            _jobLog.WriteEntry(this, Localise.GetPhrase("MP4Box trying to replace file") + " Output : " + _convertedVideo + " Temp : " + tempFile, Log.LogEntryType.Debug);
            Util.FileIO.TryFileReplace(_convertedVideo, tempFile);

            //Clean up cut files
            CleanupCutFiles(filesFound);
        }

        /// <summary>
        /// Remove commercials from MKV files useing MKVMerge
        /// </summary>
        private void CutMKV()
        {
            List<KeyValuePair<float, float>> cutList = new List<KeyValuePair<float, float>>();
            string tempFile = CutFileName(_convertedVideo, 0, 0);

            // Read the EDL file and convert into MKVMerge cut structure
            if (!ParseEDLFile(ref cutList))
                return;

            // Create the MKV splits
            //Do the cuts and pick them up as we go
            int cutNumber = 0;
            IEnumerable<string> newFilesFound = new List<string>();

            // Build the list of timecodes to split into invidiual files
            string Parameters = " --split parts:";
            foreach (KeyValuePair<float, float> cut in cutList)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("CutMKV: Found commercials from video - segment") + " " + cutNumber.ToString(CultureInfo.InvariantCulture), Log.LogEntryType.Information);

                TimeSpan startCut = TimeSpan.FromSeconds(Convert.ToDouble(cut.Key.ToString(CultureInfo.InvariantCulture)));
                TimeSpan endCut = TimeSpan.FromSeconds(Convert.ToDouble(cut.Value.ToString(CultureInfo.InvariantCulture)));

                if (cutNumber == 0)
                    Parameters += startCut.ToString() + "-" + endCut.ToString();
                else
                    Parameters += ",+" + startCut.ToString() + "-" + endCut.ToString();

                cutNumber++;
            }

            Parameters += " " + Util.FilePaths.FixSpaces(_convertedVideo) + " -o " + Util.FilePaths.FixSpaces(tempFile);

            if (cutNumber < 1)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("No commercials to remove from ") + EDLFile, Log.LogEntryType.Information);
                _jobStatus.PercentageComplete = 100; //Set to success since sometime mp4box doesn't set to 100 if there no/incomplete pieces to strip
                return;
            }

            _jobStatus.CurrentAction = Localise.GetPhrase("Cutting commercials from video");

            MKVMerge mkvMerge = new MKVMerge(Parameters, ref _jobStatus, _jobLog);
            mkvMerge.Run();
            if (!mkvMerge.Success || (Util.FileIO.FileSize(tempFile) <= 0)) // check for +ve success
            {
                _jobStatus.PercentageComplete = 0;
                _jobStatus.ErrorMsg = "CutMKV merging video segments failed";
                _jobLog.WriteEntry(this, Localise.GetPhrase("CutMKV merging video segments failed"), Log.LogEntryType.Error); ;
                return;
            }

            // Move the files
            _jobLog.WriteEntry(this, Localise.GetPhrase("CutMKV trying to replace file") + " Output : " + _convertedVideo + " Temp : " + tempFile, Log.LogEntryType.Debug);
            Util.FileIO.TryFileReplace(_convertedVideo, tempFile);
        }

        /// <summary>
        /// Remove commercials from TS files using FFMPEG, AVIDemux and FFMPEG as backup
        /// </summary>
        /// <param name="filterTSAudioLanguage">True if you want to use the user selected language to filter out while cutting</param>
        private void CutTS(bool filterTSAudioLanguage)
        {
            bool selectedTSAudioSuccess = false; // have we successfully filtered the audio tracks
            List<KeyValuePair<float, float>> cutList = new List<KeyValuePair<float, float>>();
            long totalSegmentSize = 0;

            // Read the EDL file and convert into cut structure
            if (!ParseEDLFile(ref cutList))
                return;

            //Do the cuts and pick them up as we go
            int cutNumber = 0;
            List<string> filesFound = new List<string>();

            foreach (KeyValuePair<float, float> cut in cutList)
            {
                _jobStatus.CurrentAction = Localise.GetPhrase("Cutting commercials from video - segment") + " " + cutNumber.ToString(CultureInfo.InvariantCulture);
                _jobLog.WriteEntry(this, Localise.GetPhrase("CutTS: Cutting commercials from video - segment ") + cutNumber.ToString(CultureInfo.InvariantCulture), Log.LogEntryType.Information);

                // Create the splits - reset success each time
                string cutFileName = CutFileName(_convertedVideo, cut.Key, cut.Value);
                string Parameters = " -y -threads 0";

                // http://ffmpeg.org/trac/ffmpeg/wiki/Seeking%20with%20FFmpeg - We use only FAST seek (since accurate seek cuts on a NON KeyFrame which causes Audio Sync Issues)
                Parameters += " -ss " + cut.Key.ToString(CultureInfo.InvariantCulture);

                Parameters += " -i " + Util.FilePaths.FixSpaces(_convertedVideo);

                // how much to Cut
                Parameters += " -t " + (cut.Value - cut.Key).ToString(CultureInfo.InvariantCulture);

                // If we are requested to filter out the selected audio language, check for it's existance and filter it
                if (filterTSAudioLanguage && (_videoFile.FFMPEGStreamInfo.AudioTracks > 1))
                {
                    if (_videoFile.AudioStream == -1)
                    {
                        _jobLog.WriteEntry("Cannot get audio stream selection details, copying all audio streams", Log.LogEntryType.Warning);
                        Parameters += " -map 0:a -map 0:v";
                    }
                    else
                    {
                        _jobLog.WriteEntry("Selecting Audio Language " + _videoFile.AudioLanguage + " Audio Stream " + _videoFile.AudioStream.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
                        Parameters += " -map 0:" + _videoFile.AudioStream.ToString(System.Globalization.CultureInfo.InvariantCulture) + " -map 0:v"; // Select the Audiotrack we had isolated earlier
                        selectedTSAudioSuccess = true;
                    }
                }
                else
                    Parameters += " -map 0:a -map 0:v";

                // Stream copy
                Parameters += " -vcodec copy -acodec copy " + Util.FilePaths.FixSpaces(cutFileName);

                FFmpeg cutFFmpeg = new FFmpeg(Parameters, ref _jobStatus, _jobLog);
                cutFFmpeg.Run();
                if (!cutFFmpeg.Success) // Don't check for % here since Comskip has a bug that gives EDL cut segments past the end of the file || _jobStatus.PercentageComplete < GlobObjects.ACCEPTABLE_COMPLETION) // each run resets this number or an error in the process
                {
                    _jobLog.WriteEntry("Cutting video segment failed, retying using GenPts", Log.LogEntryType.Warning);

                    // Create the splits - reset success each time
                    // genpt is required sometimes when -ss is specified before the inputs file, see ffmpeg ticket #2054
                    Parameters = "-fflags +genpts " + cutFFmpeg.Parameters; // Save it
                    cutFFmpeg = new FFmpeg(Parameters, ref _jobStatus, _jobLog);
                    cutFFmpeg.Run();

                    if (!cutFFmpeg.Success) //check of file is created, outputhandler reports success (Percentage not requires since Success is more accurate)
                    {
                        CleanupCutFiles(filesFound); //Clean up cut files
                        _jobStatus.PercentageComplete = 0;
                        _jobStatus.ErrorMsg = "CutTS splitting video segments failed";
                        _jobLog.WriteEntry(this, Localise.GetPhrase("CutTS splitting video segments failed"), Log.LogEntryType.Error);
                        return;
                    }
                }

                filesFound.Add(cutFileName); // to be deleted later
                cutNumber++;

                totalSegmentSize += Util.FileIO.FileSize(cutFileName); // Calculate the total size of all segments for validation later
            }

            if (cutNumber < 1)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("No commercials to remove from ") + EDLFile, Log.LogEntryType.Information);
                _jobStatus.PercentageComplete = 100; //Set to success since sometime mp4box doesn't set to 100 if there no/incomplete pieces to strip
                return;
            }

            cutNumber = 0;
            string mergeParams = "";
            foreach(string file in filesFound)
            {
                if (cutNumber++ == 0)
                    mergeParams += Util.FilePaths.FixSpaces(file);
                else
                    mergeParams += " --append " + Util.FilePaths.FixSpaces(file);
            }

            // Save the file as a TS file
            string tempFile = Util.FilePaths.GetFullPathWithoutExtension(_convertedVideo) + "_MERGE" + _ext;
            mergeParams += " --video-codec copy --audio-codec copy --output-format ffts --save " + Util.FilePaths.FixSpaces(tempFile);

            _jobStatus.CurrentAction = Localise.GetPhrase("Merging commercial free segments into new video");
            _jobLog.WriteEntry(this, Localise.GetPhrase("CutTS: Merging commercial free segments into new video"), Log.LogEntryType.Information);

            // Check if the converted file has more than 1 audio track, AVIDemux does not support merging files with > 1 audio track (it drops them randomly)
            bool useFFMPEG = false;
            FFmpegMediaInfo ffmpegStreamInfo = new FFmpegMediaInfo(_convertedVideo, ref _jobStatus, _jobLog);
            ffmpegStreamInfo.Run();
            if (!ffmpegStreamInfo.Success || ffmpegStreamInfo.ParseError)
            {
                CleanupCutFiles(filesFound); //Clean up cut files
                _jobStatus.PercentageComplete = 0;
                _jobStatus.ErrorMsg = "CutTS unable to get video info";
                _jobLog.WriteEntry(this, _jobStatus.ErrorMsg, Log.LogEntryType.Error);
                return;
            }

            AVIDemux aviDemux = new AVIDemux(mergeParams, ref _jobStatus, _jobLog);

            // If we have more than one audio track we should use FFMPEG instead of AVIDemux
            if (ffmpegStreamInfo.AudioTracks <= 1 || selectedTSAudioSuccess) // If the source file has 1 audio track or if we have filtered the audio track while cutting the segements
                aviDemux.Run(); // Run only if we have 1 or less audio tracks
            else
            {
                _jobLog.WriteEntry(this, "More than 1 audio track detected in converted file, using FFMPEG instead of AVIDemux to merge tracks. There may be issues with the video at the merged segments", Log.LogEntryType.Warning);
                useFFMPEG = true;
            }

            if(useFFMPEG || !aviDemux.Success || (Util.FileIO.FileSize(tempFile) < (GlobalDefs.MINIMUM_MERGED_FILE_THRESHOLD * totalSegmentSize)) || (totalSegmentSize <= 0)) // Check final merged file size as proxy, as there is not reliable way to check AVIDemux success
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("CutTS : Merged file size [KB] ") + (Util.FileIO.FileSize(tempFile) / 1024).ToString("N", CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
                _jobLog.WriteEntry(this, Localise.GetPhrase("CutTS : Total Segements size [KB] ") + (totalSegmentSize / 1024).ToString("N", CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
                _jobLog.WriteEntry(this, Localise.GetPhrase("CutTS: AviDemux merging video segments failed, retrying with FFMPEG"), Log.LogEntryType.Warning);

                // Create a text file with the list of all the files which need to be concatenated
                // http://ffmpeg.org/trac/ffmpeg/wiki/How%20to%20concatenate%20(join,%20merge)%20media%20files
                string concatFileList = "";
                string concatFileName = Util.FilePaths.GetFullPathWithoutExtension(_convertedVideo) + "_ConcatList.txt";

                foreach (string file in filesFound)
                    concatFileList += "file " + "'" + file.Replace("'", @"'\''") + "'" + "\r\n"; // File paths are enclosed in single quotes, compensate for ' with '\'' in the filename

                System.IO.File.WriteAllText(concatFileName, concatFileList); // write the text file

                cutNumber = 0;

                // Merge the files using FFMPEG as a backup
                mergeParams = "-y -f concat -i " + Util.FilePaths.FixSpaces(concatFileName) + " -vcodec copy -acodec copy -map a -map v " + Util.FilePaths.FixSpaces(tempFile); // Copy all Audio and Video streams

                _jobStatus.CurrentAction = Localise.GetPhrase("Merging commercial free segments into new video");
                _jobLog.WriteEntry(this, Localise.GetPhrase("CutTS: Merging commercial free segments into new video"), Log.LogEntryType.Information);

                FFmpeg ffmpegMerge = new FFmpeg(mergeParams, ref _jobStatus, _jobLog);
                ffmpegMerge.Run();
                if (!ffmpegMerge.Success || (Util.FileIO.FileSize(tempFile) <= 0))
                {
                    _jobLog.WriteEntry("CutTS Merging segments failed, retying using GenPts", Log.LogEntryType.Warning);
                    _jobStatus.PercentageComplete = 0; // reset it

                    // genpt is required sometimes when -ss is specified before the inputs file, see ffmpeg ticket #2054
                    mergeParams = "-fflags +genpts " + mergeParams;
                    ffmpegMerge = new FFmpeg(mergeParams, ref _jobStatus, _jobLog);
                    ffmpegMerge.Run();

                    if (!ffmpegMerge.Success || (Util.FileIO.FileSize(tempFile) <= 0)) //check of file is created, outputhandler reports success (Percentage not requires since Success is more accurate)
                    {
                        CleanupCutFiles(filesFound); //Clean up cut files
                        _jobStatus.PercentageComplete = 0;
                        _jobStatus.ErrorMsg = "CutTS merging video segments failed";
                        _jobLog.WriteEntry(this, Localise.GetPhrase("CutTS merging video segments failed"), Log.LogEntryType.Error);
                        return;
                    }
                }
            }

            // Move the files
            _jobLog.WriteEntry(this, Localise.GetPhrase("CutTS trying to replace file") + " Output : " + _convertedVideo + " Temp : " + tempFile, Log.LogEntryType.Debug);
            Util.FileIO.TryFileReplace(_convertedVideo, tempFile);

            //Clean up cut files
            CleanupCutFiles(filesFound);
        }

        /// <summary>
        /// Clean up temporary file cut during the commercial removal process
        /// </summary>
        /// <param name="filesFound">List of files to delete</param>
        private void CleanupCutFiles(List<string> filesFound)
        {
                        //Clean up cut files
            foreach (string f in filesFound)
            {
                if (f != _convertedVideo)
                {
                    Util.FileIO.TryFileDelete(f);
                    Util.FileIO.TryFileDelete(f + ".idx2"); // Delete the IDX2 files also created by AVIDemux
                }
            }
        }

        /// <summary>
        /// Returns the Filename to be used for extracting a cut
        /// </summary>
        /// <param name="fileName">Base Filename</param>
        /// <param name="startSec">Starting second for cut</param>
        /// <param name="endSec">Ending second for cut</param>
        /// <returns>Temp Cut Filename</returns>
        private string CutFileName(string fileName, float startSec, float endSec)
        {
            return Util.FilePaths.GetFullPathWithoutExtension(_convertedVideo) + "_" + ((int)startSec).ToString(CultureInfo.InvariantCulture) + "_" + ((int)endSec).ToString(CultureInfo.InvariantCulture) + _ext;
        }

        /// <summary>
        /// Check if removing commercials for the specified extension is supported
        /// </summary>
        /// <param name="extension">Extension to check</param>
        /// <param name="profile">Profile for which supported extensions for commercial removal is to be checked. If this is blank or null, then only natively supported extensions are checked and not those covered by generic commercial removal functions.</param>
        /// <returns>True if commercial stripping is support for the extension</returns>
        public static bool IsSupportedExtension(string extension, string profile="")
        {
            switch (extension)
            {
                case ".ts":
                case ".wmv":
                case ".mpg":
                case ".avi":
                case ".m4v":
                case ".mp4":
                case ".mkv":
                    return true;

                default:
                    if (string.IsNullOrWhiteSpace(profile)) // unknown profile
                        return false; // Just the native supported extensions

                    // If we are using the universal commercial remover, than yes ALL extensions are supported else, we use only native extensions
                    Ini configProfileIni = new Ini(GlobalDefs.ProfileFile);
                    bool universalCommercialRemover = configProfileIni.ReadBoolean(profile, "UniversalCommercialRemover", false); // Forcing the use of CutFFMPEG which works on all video types
                    if (universalCommercialRemover)
                        return true;
                    else
                        return false;
            }
        }

        /// <summary>
        /// Removes commercials from the converted file using the EDL file and updates the RemoveAds flag if successful
        /// </summary>
        /// <param name="filterAudioLanguage">True if you want to keep only the identified audio language while cutting TS files. This is useful when commercial remove is done before video conversion since audio language information is lost.</param>
        public void StripCommercials(bool filterTSAudioLanguage = false)
        {
            _jobStatus.PercentageComplete = 100; //all good to start with 
            _jobStatus.ETA = "";

            if (!File.Exists(_convertedVideo))
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("File not found") + " " + _convertedVideo, Log.LogEntryType.Error);
                _jobStatus.PercentageComplete = 0;
                _jobStatus.ErrorMsg = "Strip Commercials file not found";
                return;
            }
            if (!File.Exists(EDLFile))
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("EDL file does not exist - no commercials found") + " " + EDLFile, Log.LogEntryType.Information);
                _jobStatus.ErrorMsg = "Strip commercial EDL file not found";
                _jobStatus.PercentageComplete = 0;
                return;
            }

            _jobLog.WriteEntry(this, Localise.GetPhrase("Commercial Remove: Before removal file size [KB] ") + (Util.FileIO.FileSize(_convertedVideo)/1024).ToString("N", CultureInfo.InvariantCulture), Log.LogEntryType.Debug); ;

            if (_universalCommercialRemover)
            {
                _jobLog.WriteEntry(this, Localise.GetPhrase("Forcing use of Universal Commercial Remover for") + " " + _ext, Log.LogEntryType.Warning);
                CutFFMPEG(filterTSAudioLanguage);
            }
            else
            {
                switch (_ext)
                {
                    case ".ts":
                        _jobLog.WriteEntry(this, Localise.GetPhrase("Removing commercials using CutTS") + " ext -> " + _ext, Log.LogEntryType.Information);
                        CutTS(filterTSAudioLanguage);
                        if (0 == _jobStatus.PercentageComplete) // Try backup
                        {
                            _jobLog.WriteEntry(this, Localise.GetPhrase("CutTS failed, trying to remove commercials using CutMencoder") + " ext -> " + _ext, Log.LogEntryType.Information);
                            _jobStatus.PercentageComplete = 100; //Reset
                            _jobStatus.ErrorMsg = ""; //reset error message since we are trying again
                            CutMencoder(filterTSAudioLanguage);
                        }
                        break;

                    case ".wmv":
                        _jobLog.WriteEntry(this, Localise.GetPhrase("Removing commercials using CutWMV") + " ext -> " + _ext, Log.LogEntryType.Information);
                        CutWMV();
                        break;

                    case ".mpg":
                    case ".avi":
                        _jobLog.WriteEntry(this, Localise.GetPhrase("Removing commercials using CutMencoder") + " ext -> " + _ext, Log.LogEntryType.Information);
                        CutMencoder();
                        break;

                    case ".m4v":
                    case ".mp4":
                        if (_cutMP4Alternative)
                        {
                            _jobLog.WriteEntry(this, Localise.GetPhrase("Removing commercials using CutMP4Alternate") + " ext -> " + _ext, Log.LogEntryType.Information);
                            CutMP4Alternate();
                        }
                        else
                        {
                            _jobLog.WriteEntry(this, Localise.GetPhrase("Removing commercials using CutMP4") + " ext -> " + _ext, Log.LogEntryType.Information);
                            CutMP4();
                            if (0 == _jobStatus.PercentageComplete) // CutMP4 can fail for some .TS files that can't be read by MediaInfo, do a backup try
                            {
                                _jobStatus.ErrorMsg = ""; //reset error message since we are trying again
                                _jobLog.WriteEntry(this, Localise.GetPhrase("CutMP4 failed, trying backup CutMP4Alternate to remove commercials"), Log.LogEntryType.Warning);
                                _jobStatus.PercentageComplete = 100; //Reset
                                CutMP4Alternate();
                            }
                        }
                        break;

                    case ".mkv":
                        {
                            _jobLog.WriteEntry(this, Localise.GetPhrase("Removing commercials using CutMKV"), Log.LogEntryType.Information);
                            CutMKV();
                            break;
                        }

                    default:
                        {
                            _jobLog.WriteEntry(this, Localise.GetPhrase("Unsupported extension for removing commercials ") + " " + _ext + ", trying default FFMPEG cutter", Log.LogEntryType.Warning);
                            CutFFMPEG(filterTSAudioLanguage);
                            break;
                        }
                }

                // If all fails, try one last chance with CutFFMPEG
                if (0 == _jobStatus.PercentageComplete) // Try backup
                {
                    _jobLog.WriteEntry(this, Localise.GetPhrase("Cutting failed, trying to remove commercials using CutFFMPEG") + " ext -> " + _ext, Log.LogEntryType.Information);
                    _jobStatus.PercentageComplete = 100; //Reset
                    _jobStatus.ErrorMsg = ""; //reset error message since we are trying again
                    CutFFMPEG(filterTSAudioLanguage);
                }
            }

            if (_jobStatus.PercentageComplete != 0)
                _videoFile.AdsRemoved = true; // We have removed the Ad's

            _jobLog.WriteEntry(this, Localise.GetPhrase("Commercial Remove: After removal  file size [KB] ") + (Util.FileIO.FileSize(_convertedVideo) / 1024).ToString("N", CultureInfo.InvariantCulture), Log.LogEntryType.Debug); ;
            _jobLog.WriteEntry(this, Localise.GetPhrase("Commercial Remove: Percentage Complete") + " " + _jobStatus.PercentageComplete.ToString(CultureInfo.InvariantCulture), Log.LogEntryType.Debug);
        }
    }
}
