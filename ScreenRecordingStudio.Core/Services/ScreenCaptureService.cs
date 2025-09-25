// ScreenRecordingStudio.Core/Services/ScreenCaptureService.cs
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Forms;
using ScreenRecordingStudio.Core.Interfaces;
using ScreenRecordingStudio.Core.Models;

namespace ScreenRecordingStudio.Core.Services
{
    public class ScreenCaptureService : IScreenCaptureService
    {
        private bool _isCapturing;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _captureTask;
        private Rectangle _captureRegion;
        private int _frameRate = 30;

        // Events
        public event EventHandler<FrameCapturedEventArgs> FrameCaptured;
        public event EventHandler<CaptureErrorEventArgs> CaptureError;

        // Properties
        public bool IsCapturing => _isCapturing;
        public int FrameRate
        {
            get => _frameRate;
            set => _frameRate = Math.Max(1, Math.Min(120, value));
        }
        public Rectangle CaptureRegion
        {
            get => _captureRegion;
            set => _captureRegion = value;
        }

        public async Task<bool> StartCaptureAsync(RecordingSettings settings)
        {
            try
            {
                if (_isCapturing)
                {
                    OnCaptureError("Screen capture is already running");
                    return false;
                }

                // Set capture parameters
                FrameRate = settings.FrameRate;

                // Determine capture region
                _captureRegion = await GetCaptureRegionAsync(settings);

                if (!ValidateCaptureRegion(_captureRegion))
                {
                    OnCaptureError("Invalid capture region");
                    return false;
                }

                // Start capture loop
                _cancellationTokenSource = new CancellationTokenSource();
                _isCapturing = true;

                _captureTask = Task.Run(async () => await CaptureLoopAsync(_cancellationTokenSource.Token));

                return true;
            }
            catch (Exception ex)
            {
                OnCaptureError($"Failed to start screen capture: {ex.Message}", ex);
                return false;
            }
        }

        public async Task<bool> StopCaptureAsync()
        {
            try
            {
                if (!_isCapturing)
                {
                    return true; // Already stopped
                }

                _isCapturing = false;
                _cancellationTokenSource?.Cancel();

                // Wait for capture task to complete
                if (_captureTask != null)
                {
                    await _captureTask;
                    _captureTask = null;
                }

                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;

                return true;
            }
            catch (Exception ex)
            {
                OnCaptureError($"Failed to stop screen capture: {ex.Message}", ex);
                return false;
            }
        }

        public async Task<List<DisplayMonitor>> GetAvailableMonitorsAsync()
        {
            return await Task.Run(() =>
            {
                var monitors = new List<DisplayMonitor>();
                var screens = Screen.AllScreens;

                for (int i = 0; i < screens.Length; i++)
                {
                    var screen = screens[i];
                    monitors.Add(new DisplayMonitor
                    {
                        Index = i,
                        Name = $"Display {i + 1}",
                        DeviceName = screen.DeviceName,
                        Bounds = screen.Bounds,
                        WorkingArea = screen.WorkingArea,
                        IsPrimary = screen.Primary,
                        BitsPerPixel = 32 // Default assumption
                    });
                }

                return monitors;
            });
        }

        public async Task<Rectangle> GetMonitorBoundsAsync(int monitorIndex)
        {
            return await Task.Run(() =>
            {
                var screens = Screen.AllScreens;
                if (monitorIndex >= 0 && monitorIndex < screens.Length)
                {
                    return screens[monitorIndex].Bounds;
                }
                return Screen.PrimaryScreen.Bounds;
            });
        }

        public async Task<Rectangle> GetActiveWindowBoundsAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    // This is a simplified implementation
                    // In a real app, you'd use Win32 API to get the active window
                    var activeWindow = Form.ActiveForm;
                    if (activeWindow != null)
                    {
                        return activeWindow.Bounds;
                    }

                    // Fallback to primary screen
                    return Screen.PrimaryScreen.Bounds;
                }
                catch
                {
                    return Screen.PrimaryScreen.Bounds;
                }
            });
        }

        public async Task<Bitmap> CaptureScreenshotAsync(Rectangle region)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var bitmap = new Bitmap(region.Width, region.Height);
                    using (var graphics = Graphics.FromImage(bitmap))
                    {
                        graphics.CopyFromScreen(region.X, region.Y, 0, 0, region.Size);
                    }
                    return bitmap;
                }
                catch (Exception ex)
                {
                    OnCaptureError($"Failed to capture screenshot: {ex.Message}", ex);
                    return null;
                }
            });
        }

        public bool ValidateCaptureRegion(Rectangle region)
        {
            return region.Width > 0 && region.Height > 0 &&
                   region.Width <= 7680 && region.Height <= 4320; // Max 8K resolution
        }

        private async Task<Rectangle> GetCaptureRegionAsync(RecordingSettings settings)
        {
            switch (settings.CaptureMode)
            {
                case CaptureMode.FullScreen:
                    return await GetMonitorBoundsAsync(settings.MonitorIndex);

                case CaptureMode.ActiveWindow:
                    return await GetActiveWindowBoundsAsync();

                case CaptureMode.CustomRegion:
                    return new Rectangle(
                        settings.CustomRegion.X,
                        settings.CustomRegion.Y,
                        settings.CustomRegion.Width,
                        settings.CustomRegion.Height);

                default:
                    return Screen.PrimaryScreen.Bounds;
            }
        }

        private async Task CaptureLoopAsync(CancellationToken cancellationToken)
        {
            var frameInterval = TimeSpan.FromMilliseconds(1000.0 / FrameRate);
            var frameNumber = 0;

            while (!cancellationToken.IsCancellationRequested && _isCapturing)
            {
                try
                {
                    var startTime = DateTime.Now;

                    // Capture frame
                    var screenshot = await CaptureScreenshotAsync(_captureRegion);
                    if (screenshot != null)
                    {
                        frameNumber++;

                        // Raise frame captured event
                        FrameCaptured?.Invoke(this, new FrameCapturedEventArgs
                        {
                            Frame = screenshot,
                            Timestamp = startTime,
                            FrameNumber = frameNumber
                        });
                    }

                    // Calculate delay to maintain frame rate
                    var processingTime = DateTime.Now - startTime;
                    var delay = frameInterval - processingTime;

                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    break; // Expected when cancellation is requested
                }
                catch (Exception ex)
                {
                    OnCaptureError($"Error in capture loop: {ex.Message}", ex);
                    await Task.Delay(100, cancellationToken); // Brief pause before retrying
                }
            }
        }

        private void OnCaptureError(string message, Exception exception = null)
        {
            CaptureError?.Invoke(this, new CaptureErrorEventArgs
            {
                ErrorMessage = message,
                Exception = exception
            });
        }

        public void Dispose()
        {
            StopCaptureAsync().Wait();
        }
    }
}