// ScreenRecordingStudio.Core/Interfaces/IScreenCaptureService.cs
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using ScreenRecordingStudio.Core.Models;

namespace ScreenRecordingStudio.Core.Interfaces
{
    public interface IScreenCaptureService
    {
        // Events
        event EventHandler<FrameCapturedEventArgs> FrameCaptured;
        event EventHandler<CaptureErrorEventArgs> CaptureError;

        // Properties
        bool IsCapturing { get; }
        int FrameRate { get; set; }
        Rectangle CaptureRegion { get; set; }

        // Methods
        Task<bool> StartCaptureAsync(RecordingSettings settings);
        Task<bool> StopCaptureAsync();
        Task<List<DisplayMonitor>> GetAvailableMonitorsAsync();
        Task<Rectangle> GetMonitorBoundsAsync(int monitorIndex);
        Task<Rectangle> GetActiveWindowBoundsAsync();
        Task<Bitmap> CaptureScreenshotAsync(Rectangle region);

        // Configuration
        bool ValidateCaptureRegion(Rectangle region);
    }

    // Event argument classes
    public class FrameCapturedEventArgs : EventArgs
    {
        public Bitmap Frame { get; set; }
        public DateTime Timestamp { get; set; }
        public int FrameNumber { get; set; }
    }

    public class CaptureErrorEventArgs : EventArgs
    {
        public string ErrorMessage { get; set; }
        public Exception Exception { get; set; }
    }
}