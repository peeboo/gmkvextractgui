﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.IO;

namespace gMKVToolnix
{
    public class gMKVInfo
    {
        // mkvinfo [options] <inname>

        internal class OptionValue
        {
            private MkvInfoOptions _Option;
            private String _Parameter;

            public MkvInfoOptions Option
            {
                get { return _Option; }
                set { _Option = value; }
            }	

            public String Parameter
            {
                get { return _Parameter; }
                set { _Parameter = value; }
            }

            public OptionValue(MkvInfoOptions opt, String par)
            {
                _Option = opt;
                _Parameter = par;
            }
        }

        public enum MkvInfoOptions
        {
            gui, // Start the GUI (and open inname if it was given)
            checksum, // Calculate and display checksums of frame contents
            check_mode, // Calculate and display checksums and use verbosity level 4.
            summary, // Only show summaries of the contents, not each element
            track_info, // Show statistics for each track in verbose mode
            hexdump, // Show the first 16 bytes of each frame as a hex dump
            full_hexdump, // Show all bytes of each frame as a hex dump
            size, // Show the size of each element including its header
            verbose, // Increase verbosity
            quiet, // Suppress status output
            ui_language, // Force the translations for 'code' to be used
            command_line_charset, //  Charset for strings on the command line
            output_charset, // Output messages in this charset
            redirect_output, // Redirects all messages into this file
            help, // Show this help
            version, // Show version information
            check_for_updates // Check online for the latest release
        }

        private String _MKVToolnixPath = String.Empty;
        private String _MKVInfoFilename = String.Empty;
        private List<gMKVSegment> _SegmentList = new List<gMKVSegment>();
        private StringBuilder _MKVInfoOutput = new StringBuilder();
        private StringBuilder _ErrorBuilder = new StringBuilder();

        public gMKVInfo(String mkvToonlixPath)
        {
            _MKVToolnixPath = mkvToonlixPath;
            _MKVInfoFilename = Path.Combine(_MKVToolnixPath, "mkvinfo.exe");            
        }

        public List<gMKVSegment> GetMKVSegments(String argMKVFile)
        {
            // First clear the segment list
            _SegmentList.Clear();
            // Clear the mkvinfo output
            _MKVInfoOutput.Length = 0;
            // Clear the error builder
            _ErrorBuilder.Length = 0;

            using (Process myProcess = new Process())
            {
                List<OptionValue> optionList = new List<OptionValue>();
                //optionList.Add(new OptionValue(MkvInfoOptions.verbose, "1"));
                //optionList.Add(new OptionValue(MkvInfoOptions.summary, String.Empty));
                //optionList.Add(new OptionValue(MkvInfoOptions.command_line_charset, "\"UFT-8\""));
                //optionList.Add(new OptionValue(MkvInfoOptions.output_charset, "\"UFT-8\""));

                ProcessStartInfo myProcessInfo = new ProcessStartInfo();
                myProcessInfo.FileName = _MKVInfoFilename;
                //myProcessInfo.Arguments = " -?";
                //myProcessInfo.Arguments = ConvertOptionValueListToString(optionList) + " " + argMKVFile;                
                myProcessInfo.Arguments = String.Format("{0} \"{1}\"", ConvertOptionValueListToString(optionList), argMKVFile);
                //myProcessInfo.Arguments = String.Format("--parse-mode fast \"{0}\"", argMKVFile);
                myProcessInfo.UseShellExecute = false;
                myProcessInfo.RedirectStandardOutput = true;
                myProcessInfo.StandardOutputEncoding = Encoding.UTF8;
                myProcessInfo.RedirectStandardError = true;
                myProcessInfo.StandardErrorEncoding = Encoding.UTF8;
                myProcessInfo.CreateNoWindow = true;
                myProcessInfo.WindowStyle = ProcessWindowStyle.Hidden;
                myProcess.StartInfo = myProcessInfo;
                myProcess.OutputDataReceived += myProcess_OutputDataReceived;

                Debug.WriteLine(myProcessInfo.Arguments);
                // Start the mkvinfo process
                myProcess.Start();
                // Start reading the output
                myProcess.BeginOutputReadLine();
                // Wait for the process to exit
                myProcess.WaitForExit();
                // unregister the event
                myProcess.OutputDataReceived -= myProcess_OutputDataReceived;

                // Debug write the exit code
                Debug.WriteLine("Exit code: " + myProcess.ExitCode);

                // Check the exit code
                if (myProcess.ExitCode > 0)
                {
                    // something went wrong!
                    throw new Exception(String.Format("Mkvinfo exited with error code {0}!\r\n\r\nErrors reported:\r\n{1}",
                        myProcess.ExitCode, _ErrorBuilder.ToString()));
                }
                // Start the parsing of the output
                ParseMkvInfoOutput();
                return _SegmentList;
            }
        }

        private enum MkvInfoParseState
        {
            Searching,
            InsideSegmentInfo,
            InsideTrackInfo,
            InsideAttachentInfo,
            InsideChapterInfo
        }

        private void ParseMkvInfoOutput()
        {
            // start the loop for each line of the output
            gMKVSegment tmpSegment = null;
            MkvInfoParseState tmpState = MkvInfoParseState.Searching;
            Int32 attachmentID = 1;
            foreach (String outputLine in _MKVInfoOutput.ToString().Split(new String[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                // first determine the parse state we are in
                if (outputLine.Contains("Segment,"))
                {
                    tmpState = MkvInfoParseState.InsideSegmentInfo;
                    continue;
                }
                else if (outputLine.Contains("Segment tracks"))
                {
                    tmpState = MkvInfoParseState.InsideTrackInfo;
                    continue;
                }
                else if (outputLine.Contains("Attachments"))
                {
                    tmpState = MkvInfoParseState.InsideAttachentInfo;
                    continue;
                }
                else if (outputLine.Contains("Chapters"))
                {
                    tmpState = MkvInfoParseState.InsideChapterInfo;
                    continue;
                }

                // now that we have determined the state, we parse the segment
                switch (tmpState)
                {
                    case MkvInfoParseState.Searching:
                        // if we are still searching for the state, just continue with next line
                        continue;
                    case MkvInfoParseState.InsideSegmentInfo:
                        if (outputLine.Contains("Segment information"))
                        {
                            // if previous segment is not null, add it to the list and create a new one
                            if (tmpSegment != null)
                            {
                                _SegmentList.Add(tmpSegment);
                            }
                            tmpSegment = new gMKVSegmentInfo();
                        }
                        else if (outputLine.Contains("Timecode scale:"))
                        {
                            ((gMKVSegmentInfo)tmpSegment).TimecodeScale = outputLine.Substring(outputLine.IndexOf(":") + 1).Trim();
                        }
                        else if (outputLine.Contains("Muxing application:"))
                        {
                            ((gMKVSegmentInfo)tmpSegment).MuxingApplication = outputLine.Substring(outputLine.IndexOf(":") + 1).Trim();
                        }
                        else if (outputLine.Contains("Writing application:"))
                        {
                            ((gMKVSegmentInfo)tmpSegment).WritingApplication = outputLine.Substring(outputLine.IndexOf(":") + 1).Trim();
                        }
                        else if (outputLine.Contains("Duration:"))
                        {
                            ((gMKVSegmentInfo)tmpSegment).Duration = outputLine.Substring(outputLine.IndexOf(":") + 1).Trim();
                        }
                        else if (outputLine.Contains("Date:"))
                        {
                            ((gMKVSegmentInfo)tmpSegment).Date = outputLine.Substring(outputLine.IndexOf(":") + 1).Trim();
                        }
                        //8 + Segment, size 320677930
                        //9 |+ Seek head (subentries will be skipped)
                        //10 |+ EbmlVoid (size: 4013)
                        //11 |+ Segment information
                        //12 | + Timecode scale: 1000000
                        //13 | + Muxing application: libebml v1.3.0 + libmatroska v1.4.1
                        //14 | + Writing application: mkvmerge v6.6.0 ('The Edge Of The In Between') built on Dec  1 2013 17:55:00
                        //15 | + Duration: 1364.905s (00:22:44.905)
                        //16 | + Date: Mon Jan 20 21:40:32 2014 UTC
                        //17 | + Segment UID: 0xa3 0x55 0x8d 0x9c 0x25 0x0f 0xba 0x16 0x94 0x09 0xf0 0xc9 0xb4 0x0f 0xc7 0x4b
                        break;
                    case MkvInfoParseState.InsideTrackInfo:
                        if (outputLine.Contains("+ A track"))
                        {
                            // if previous segment is not null, add it to the list and create a new one
                            if (tmpSegment != null)
                            {
                                _SegmentList.Add(tmpSegment);
                            }
                            tmpSegment = new gMKVTrack();
                        }
                        else if (outputLine.Contains("Track number:"))
                        {
                            ((gMKVTrack)tmpSegment).TrackNumber = Int32.Parse(outputLine.Substring(outputLine.IndexOf(":") + 1, outputLine.IndexOf("(") - outputLine.IndexOf(":") - 1).Trim());
                            ((gMKVTrack)tmpSegment).TrackID = Int32.Parse(outputLine.Substring(outputLine.LastIndexOf(":") + 1, outputLine.IndexOf(")") - outputLine.LastIndexOf(":") - 1).Trim());
                        }
                        else if (outputLine.Contains("Track type:"))
                        {
                            ((gMKVTrack)tmpSegment).TrackType = (MkvTrackType)Enum.Parse(typeof(MkvTrackType), outputLine.Substring(outputLine.IndexOf(":") + 1).Trim());
                        }
                        else if (outputLine.Contains("Codec ID:"))
                        {
                            ((gMKVTrack)tmpSegment).CodecID = outputLine.Substring(outputLine.IndexOf(":") + 1).Trim();
                        }
                        else if (outputLine.Contains("Language:"))
                        {
                            ((gMKVTrack)tmpSegment).Language = outputLine.Substring(outputLine.IndexOf(":") + 1).Trim();
                        }
                        else if (outputLine.Contains("Name:"))
                        {
                            ((gMKVTrack)tmpSegment).TrackName = outputLine.Substring(outputLine.IndexOf(":") + 1).Trim();
                        }
                        else if (outputLine.Contains("Pixel width:"))
                        {
                            ((gMKVTrack)tmpSegment).ExtraInfo = outputLine.Substring(outputLine.IndexOf(":") + 1).Trim();
                        }
                        else if (outputLine.Contains("Pixel height:"))
                        {
                            ((gMKVTrack)tmpSegment).ExtraInfo += "x" + outputLine.Substring(outputLine.IndexOf(":") + 1).Trim();
                        }
                        else if (outputLine.Contains("Sampling frequency:"))
                        {
                            ((gMKVTrack)tmpSegment).ExtraInfo = outputLine.Substring(outputLine.IndexOf(":") + 1).Trim();
                        }
                        else if (outputLine.Contains("Channels:"))
                        {
                            ((gMKVTrack)tmpSegment).ExtraInfo += ", Ch:" + outputLine.Substring(outputLine.IndexOf(":") + 1).Trim();
                        }

                        //18 |+ Segment tracks
                        //19 | + A track
                        //20 |  + Track number: 1 (track ID for mkvmerge & mkvextract: 0)
                        //21 |  + Track UID: 16103463283017343410
                        //22 |  + Track type: video
                        //23 |  + Lacing flag: 0
                        //24 |  + MinCache: 1
                        //25 |  + Codec ID: V_MPEG4/ISO/AVC
                        //26 |  + CodecPrivate, length 41 (h.264 profile: High @L4.1)
                        //27 |  + Default duration: 41.708ms (23.976 frames/fields per second for a video track)
                        //28 |  + Language: jpn
                        //29 |  + Name: Video
                        //30 |  + Video track
                        //31 |   + Pixel width: 1280
                        //32 |   + Pixel height: 720
                        //33 |   + Display width: 1280
                        //34 |   + Display height: 720
                        //35 | + A track
                        //36 |  + Track number: 2 (track ID for mkvmerge & mkvextract: 1)
                        //37 |  + Track UID: 7691413846401821864
                        //38 |  + Track type: audio
                        //39 |  + Codec ID: A_AAC
                        //40 |  + CodecPrivate, length 5
                        //41 |  + Default duration: 21.333ms (46.875 frames/fields per second for a video track)
                        //42 |  + Language: jpn
                        //43 |  + Name: Audio
                        //44 |  + Audio track
                        //45 |   + Sampling frequency: 48000
                        //46 |   + Channels: 2
                        //47 | + A track
                        //48 |  + Track number: 3 (track ID for mkvmerge & mkvextract: 2)
                        //49 |  + Track UID: 12438050378713133751
                        //50 |  + Track type: subtitles
                        //51 |  + Lacing flag: 0
                        //52 |  + Codec ID: S_TEXT/ASS
                        //53 |  + CodecPrivate, length 1530
                        //54 |  + Language: gre
                        //55 |  + Name: Subs
                        break;
                    case MkvInfoParseState.InsideAttachentInfo:
                        if (outputLine.Contains("Attached"))
                        {
                            // if previous segment is not null, add it to the list and create a new one
                            if (tmpSegment != null)
                            {
                                _SegmentList.Add(tmpSegment);
                            }
                            tmpSegment = new gMKVAttachment();
                            ((gMKVAttachment)tmpSegment).ID = attachmentID;
                            attachmentID++;
                        }
                        else if (outputLine.Contains("File name:"))
                        {
                            ((gMKVAttachment)tmpSegment).Filename = outputLine.Substring(outputLine.IndexOf(":") + 1).Trim();
                        }
                        else if (outputLine.Contains("File data, size:"))
                        {
                            ((gMKVAttachment)tmpSegment).FileSize = outputLine.Substring(outputLine.IndexOf(":") + 1).Trim();
                        }
                        else if (outputLine.Contains("Mime type:"))
                        {
                            ((gMKVAttachment)tmpSegment).MimeType = outputLine.Substring(outputLine.IndexOf(":") + 1).Trim();
                        }

                        //57 |+ Attachments
                        //58 | + Attached
                        //59 |  + File name: CENSCBK.TTF
                        //60 |  + Mime type: application/x-truetype-font
                        //61 |  + File data, size: 51716
                        //62 |  + File UID: 2140725150727954330
                        //63 | + Attached
                        //64 |  + File name: segoeui.ttf
                        //65 |  + Mime type: application/x-truetype-font
                        //66 |  + File data, size: 516560
                        //67 |  + File UID: 16764867774861384581
                        //68 | + Attached
                        //69 |  + File name: segoeuib.ttf
                        //70 |  + Mime type: application/x-truetype-font
                        //71 |  + File data, size: 497372
                        //72 |  + File UID: 9565689537892410299
                        //73 | + Attached
                        //74 |  + File name: segoeuii.ttf
                        //75 |  + Mime type: application/x-truetype-font
                        //76 |  + File data, size: 385560
                        //77 |  + File UID: 13103126947871579286
                        //78 | + Attached
                        //79 |  + File name: segoeuil.ttf
                        //80 |  + Mime type: application/x-truetype-font
                        //81 |  + File data, size: 330908
                        //82 |  + File UID: 3235680178630447031
                        //83 | + Attached
                        //84 |  + File name: segoeuiz.ttf
                        //85 |  + Mime type: application/x-truetype-font
                        //86 |  + File data, size: 398148
                        //87 |  + File UID: 17520358819351445890
                        //88 | + Attached
                        //89 |  + File name: seguisb.ttf
                        //90 |  + Mime type: application/x-truetype-font
                        //91 |  + File data, size: 406192
                        //92 |  + File UID: 8550836732450669472

                        break;
                    case MkvInfoParseState.InsideChapterInfo:
                        if (outputLine.Contains("EditionEntry"))
                        {
                            // if previous segment is not null, add it to the list and create a new one
                            if (tmpSegment != null)
                            {
                                _SegmentList.Add(tmpSegment);
                            }
                            tmpSegment = new gMKVChapter();
                        }
                        else if (outputLine.Contains("ChapterAtom"))
                        {
                            ((gMKVChapter)tmpSegment).ChapterCount += 1;
                        }

                         //93 |+ Chapters
                        //94 | + EditionEntry
                        //95 |  + EditionFlagHidden: 0
                        //96 |  + EditionFlagDefault: 0
                        //97 |  + EditionUID: 5248481698181523363
                        //98 |  + ChapterAtom
                        //99 |   + ChapterUID: 13651813039521317265
                        //100 |   + ChapterTimeStart: 00:00:00.000000000
                        //101 |   + ChapterTimeEnd: 00:00:40.874000000
                        //102 |   + ChapterFlagHidden: 0
                        //103 |   + ChapterFlagEnabled: 1
                        //104 |   + ChapterDisplay
                        //105 |    + ChapterString: ��������
                        //106 |    + ChapterLanguage: und
                        //107 |  + ChapterAtom
                        //108 |   + ChapterUID: 9861180919652459706
                        //109 |   + ChapterTimeStart: 00:00:40.999000000
                        //110 |   + ChapterTimeEnd: 00:02:00.829000000
                        //111 |   + ChapterFlagHidden: 0
                        //112 |   + ChapterFlagEnabled: 1
                        //113 |   + ChapterDisplay
                        //114 |    + ChapterString: ������ �����
                        //115 |    + ChapterLanguage: und
                        //116 |  + ChapterAtom
                        //117 |   + ChapterUID: 18185444543032186557
                        //118 |   + ChapterTimeStart: 00:02:00.954000000
                        //119 |   + ChapterTimeEnd: 00:21:24.700000000
                        //120 |   + ChapterFlagHidden: 0
                        //121 |   + ChapterFlagEnabled: 1
                        //122 |   + ChapterDisplay
                        //123 |    + ChapterString: ������ �����
                        //124 |    + ChapterLanguage: und
                        //125 |  + ChapterAtom
                        //126 |   + ChapterUID: 12481834811641996944
                        //127 |   + ChapterTimeStart: 00:21:24.867000000
                        //128 |   + ChapterTimeEnd: 00:22:44.864000000
                        //129 |   + ChapterFlagHidden: 0
                        //130 |   + ChapterFlagEnabled: 1
                        //131 |   + ChapterDisplay
                        //132 |    + ChapterString: ������ ������
                        //133 |    + ChapterLanguage: und

                        break;
                    default:
                        break;
                }
            }
            // if the last segment was not added to the list and it is not null, add it to the list
            if (tmpSegment != null)
            {
                _SegmentList.Add(tmpSegment);
            }
        }

        private void myProcess_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                if (e.Data.Trim().Length > 0)
                {
                    // add the line to the output stringbuilder
                    _MKVInfoOutput.AppendLine(e.Data);
                    // check for errors
                    if (e.Data.Contains("Error:"))
                    {
                        _ErrorBuilder.AppendLine(e.Data.Substring(e.Data.IndexOf(":") + 1).Trim());
                    }
                    // debug write the output line
                    Debug.WriteLine(e.Data);
                    // log the output
                    gMKVLogger.Log(e.Data);
                }
            }
        }

        private String ConvertOptionValueListToString(List<OptionValue> listOptionValue)
        {
            StringBuilder optionString = new StringBuilder();
            foreach (OptionValue optVal in listOptionValue)
            {
                optionString.AppendFormat(" {0} {1}", ConvertEnumOptionToStringOption(optVal.Option), optVal.Parameter);
            }
            return optionString.ToString();
        }

        private String ConvertEnumOptionToStringOption(MkvInfoOptions enumOption)
        {
            return String.Format("--{0}", Enum.GetName(typeof(MkvInfoOptions), enumOption).Replace("_", "-"));
        }
    }
}