// ScreenRecordingStudio.Core/Models/RecordingSettings.cs
using System;
using System.IO;

namespace ScreenRecordingStudio.Core.Models
{
    public class RecordingSettings
    {
        public string OutputDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "ScreenRecordings");
        public string OutputFileName { get; set; } = $"Recording_{DateTime.Now:yyyyMMdd_HHmmss}";
        public VideoFormat VideoFormat { get; set; } = VideoFormat.MP4;
        public VideoQuality VideoQuality { get; set; } = VideoQuality.High;
        public int FrameRate { get; set; } = 30;
        public bool RecordAudio { get; set; } = true;
        public bool RecordMicrophone { get; set; } = true;
        public bool RecordSystemAudio { get; set; } = true;
        public AudioQuality AudioQuality { get; set; } = AudioQuality.High;
        public CaptureMode CaptureMode { get; set; } = CaptureMode.FullScreen;
        public RecordingRegion CustomRegion { get; set; } = new();
        public int MonitorIndex { get; set; } = 0;
        public bool ShowCursor { get; set; } = true;
        public bool EnableWebcam { get; set; } = false;
        public WebcamPosition WebcamPosition { get; set; } = WebcamPosition.BottomRight;
        public bool AutoSave { get; set; } = true;
        public TimeSpan MaxDuration { get; set; } = TimeSpan.FromHours(2);

        public string GetFullOutputPath()
        {
            var extension = VideoFormat switch
            {
                VideoFormat.MP4 => ".mp4",
                VideoFormat.AVI => ".avi",
                VideoFormat.MOV => ".mov",
                VideoFormat.WMV => ".wmv",
                _ => ".mp4"
            };

            return Path.Combine(OutputDirectory, OutputFileName + extension);
        }
    }

    public class RecordingRegion
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }

        public bool IsEmpty => Width <= 0 || Height <= 0;
    }

    public enum VideoFormat
    {
        MP4,
        AVI,
        MOV,
        WMV
    }

    public enum VideoQuality
    {
        Low,      // 480p
        Medium,   // 720p
        High,     // 1080p
        Ultra     // 1440p/4K
    }

    public enum AudioQuality
    {
        Low,      // 64 kbps
        Medium,   // 128 kbps
        High,     // 192 kbps
        Lossless  // 320 kbps
    }

    public enum CaptureMode
    {
        FullScreen,
        ActiveWindow,
        CustomRegion
    }

    public enum WebcamPosition
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
        Center
    }
}