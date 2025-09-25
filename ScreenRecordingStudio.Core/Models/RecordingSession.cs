// ScreenRecordingStudio.Core/Models/RecordingSession.cs
using System;

namespace ScreenRecordingStudio.Core.Models
{
    public class RecordingSession
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string SessionName { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public RecordingStatus Status { get; set; } = RecordingStatus.Ready;
        public string OutputFilePath { get; set; }
        public RecordingSettings Settings { get; set; }
        public long FileSizeBytes { get; set; }
        public int FrameCount { get; set; }
        public TimeSpan PausedDuration { get; set; }

        // Calculated Properties
        public TimeSpan Duration => EndTime.HasValue ? EndTime.Value - StartTime : DateTime.Now - StartTime;
        public TimeSpan ActualRecordingTime => Duration - PausedDuration;
        public bool IsActive => Status == RecordingStatus.Recording || Status == RecordingStatus.Paused;

        public RecordingSession()
        {
            SessionName = $"Recording_{DateTime.Now:yyyyMMdd_HHmmss}";
        }

        public RecordingSession(RecordingSettings settings) : this()
        {
            Settings = settings;
            OutputFilePath = settings.GetFullOutputPath();
        }
    }

    public enum RecordingStatus
    {
        Ready,
        Recording,
        Paused,
        Stopping,
        Completed,
        Error,
        Cancelled
    }
}