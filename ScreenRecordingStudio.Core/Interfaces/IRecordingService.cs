// ScreenRecordingStudio.Core/Interfaces/IRecordingService.cs
using System;
using System.Threading.Tasks;
using ScreenRecordingStudio.Core.Models;

namespace ScreenRecordingStudio.Core.Interfaces
{
    public interface IRecordingService
    {
        // Events
        event EventHandler<RecordingStartedEventArgs> RecordingStarted;
        event EventHandler<RecordingStoppedEventArgs> RecordingStopped;
        event EventHandler<RecordingErrorEventArgs> RecordingError;
        event EventHandler<RecordingProgressEventArgs> RecordingProgress;

        // Properties
        bool IsRecording { get; }
        bool IsPaused { get; }
        RecordingSession CurrentSession { get; }
        RecordingSettings Settings { get; set; }

        // Core Methods
        Task<bool> StartRecordingAsync();
        Task<bool> StopRecordingAsync();
        Task<bool> PauseRecordingAsync();
        Task<bool> ResumeRecordingAsync();

        // Utility Methods
        Task<bool> InitializeAsync();
        Task CleanupAsync();
        bool ValidateSettings(RecordingSettings settings);
    }

    // Event argument classes
    public class RecordingStartedEventArgs : EventArgs
    {
        public RecordingSession Session { get; set; }
        public DateTime StartTime { get; set; }
    }

    public class RecordingStoppedEventArgs : EventArgs
    {
        public RecordingSession Session { get; set; }
        public DateTime EndTime { get; set; }
        public string OutputFile { get; set; }
        public TimeSpan Duration { get; set; }
    }

    public class RecordingErrorEventArgs : EventArgs
    {
        public string ErrorMessage { get; set; }
        public Exception Exception { get; set; }
    }

    public class RecordingProgressEventArgs : EventArgs
    {
        public TimeSpan ElapsedTime { get; set; }
        public long FileSizeBytes { get; set; }
        public int FrameCount { get; set; }
    }
}