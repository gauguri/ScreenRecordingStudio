// ScreenRecordingStudio.Core/Services/RecordingService.cs
// Updated version with video encoding
using System;
using System.IO;
using System.Threading.Tasks;
using ScreenRecordingStudio.Core.Interfaces;
using ScreenRecordingStudio.Core.Models;

namespace ScreenRecordingStudio.Core.Services
{
    public class RecordingService : IRecordingService
    {
        private readonly IScreenCaptureService _screenCaptureService;
        private readonly IVideoEncoderService _videoEncoderService;
        private RecordingSession _currentSession;
        private bool _isInitialized = false;

        // Events
        public event EventHandler<RecordingStartedEventArgs> RecordingStarted;
        public event EventHandler<RecordingStoppedEventArgs> RecordingStopped;
        public event EventHandler<RecordingErrorEventArgs> RecordingError;
        public event EventHandler<RecordingProgressEventArgs> RecordingProgress;

        // Properties
        public bool IsRecording => _currentSession?.Status == RecordingStatus.Recording;
        public bool IsPaused => _currentSession?.Status == RecordingStatus.Paused;
        public RecordingSession CurrentSession => _currentSession;
        public RecordingSettings Settings { get; set; } = new RecordingSettings();

        public RecordingService(IScreenCaptureService screenCaptureService, IVideoEncoderService videoEncoderService)
        {
            _screenCaptureService = screenCaptureService ?? throw new ArgumentNullException(nameof(screenCaptureService));
            _videoEncoderService = videoEncoderService ?? throw new ArgumentNullException(nameof(videoEncoderService));

            // Subscribe to screen capture events
            _screenCaptureService.FrameCaptured += OnFrameCaptured;
            _screenCaptureService.CaptureError += OnCaptureError;
        }

        public async Task<bool> InitializeAsync()
        {
            try
            {
                // Ensure output directory exists
                if (!Directory.Exists(Settings.OutputDirectory))
                {
                    Directory.CreateDirectory(Settings.OutputDirectory);
                }

                // Validate settings
                if (!ValidateSettings(Settings))
                {
                    OnRecordingError("Invalid recording settings");
                    return false;
                }

                _isInitialized = true;
                return true;
            }
            catch (Exception ex)
            {
                OnRecordingError($"Failed to initialize recording service: {ex.Message}", ex);
                return false;
            }
        }

        public async Task<bool> StartRecordingAsync()
        {
            try
            {
                if (!_isInitialized)
                {
                    if (!await InitializeAsync())
                    {
                        return false;
                    }
                }

                if (IsRecording || IsPaused)
                {
                    OnRecordingError("Recording is already in progress");
                    return false;
                }

                // Create new recording session
                _currentSession = new RecordingSession(Settings)
                {
                    StartTime = DateTime.Now,
                    Status = RecordingStatus.Recording
                };

                // Start video encoder
                var encoderStarted = await _videoEncoderService.StartEncodingAsync(_currentSession.OutputFilePath, Settings);
                if (!encoderStarted)
                {
                    OnRecordingError("Failed to start video encoder");
                    _currentSession.Status = RecordingStatus.Error;
                    return false;
                }

                // Start screen capture
                var captureStarted = await _screenCaptureService.StartCaptureAsync(Settings);
                if (!captureStarted)
                {
                    OnRecordingError("Failed to start screen capture");
                    await _videoEncoderService.StopEncodingAsync();
                    _currentSession.Status = RecordingStatus.Error;
                    return false;
                }

                // Raise recording started event
                RecordingStarted?.Invoke(this, new RecordingStartedEventArgs
                {
                    Session = _currentSession,
                    StartTime = _currentSession.StartTime
                });

                return true;
            }
            catch (Exception ex)
            {
                OnRecordingError($"Failed to start recording: {ex.Message}", ex);
                if (_currentSession != null)
                {
                    _currentSession.Status = RecordingStatus.Error;
                }
                return false;
            }
        }

        public async Task<bool> StopRecordingAsync()
        {
            try
            {
                if (!IsRecording && !IsPaused)
                {
                    OnRecordingError("No recording in progress");
                    return false;
                }

                _currentSession.Status = RecordingStatus.Stopping;

                // Stop screen capture first
                var captureStopped = await _screenCaptureService.StopCaptureAsync();
                if (!captureStopped)
                {
                    OnRecordingError("Failed to stop screen capture properly");
                }

                // Stop video encoder
                var encoderStopped = await _videoEncoderService.StopEncodingAsync();
                if (!encoderStopped)
                {
                    OnRecordingError("Failed to stop video encoder properly");
                }

                // Finalize recording
                _currentSession.EndTime = DateTime.Now;
                _currentSession.Status = RecordingStatus.Completed;

                // Update file size
                try
                {
                    if (File.Exists(_currentSession.OutputFilePath))
                    {
                        var fileInfo = new FileInfo(_currentSession.OutputFilePath);
                        _currentSession.FileSizeBytes = fileInfo.Length;
                    }
                }
                catch { /* Ignore file size errors */ }

                // Raise recording stopped event
                RecordingStopped?.Invoke(this, new RecordingStoppedEventArgs
                {
                    Session = _currentSession,
                    EndTime = _currentSession.EndTime.Value,
                    OutputFile = _currentSession.OutputFilePath,
                    Duration = _currentSession.Duration
                });

                return true;
            }
            catch (Exception ex)
            {
                OnRecordingError($"Failed to stop recording: {ex.Message}", ex);
                if (_currentSession != null)
                {
                    _currentSession.Status = RecordingStatus.Error;
                }
                return false;
            }
        }

        public async Task<bool> PauseRecordingAsync()
        {
            try
            {
                if (!IsRecording)
                {
                    OnRecordingError("No recording in progress to pause");
                    return false;
                }

                _currentSession.Status = RecordingStatus.Paused;
                // TODO: Implement actual pause logic for screen capture
                return true;
            }
            catch (Exception ex)
            {
                OnRecordingError($"Failed to pause recording: {ex.Message}", ex);
                return false;
            }
        }

        public async Task<bool> ResumeRecordingAsync()
        {
            try
            {
                if (!IsPaused)
                {
                    OnRecordingError("No paused recording to resume");
                    return false;
                }

                _currentSession.Status = RecordingStatus.Recording;
                // TODO: Implement actual resume logic for screen capture
                return true;
            }
            catch (Exception ex)
            {
                OnRecordingError($"Failed to resume recording: {ex.Message}", ex);
                return false;
            }
        }

        public async Task CleanupAsync()
        {
            try
            {
                if (IsRecording || IsPaused)
                {
                    await StopRecordingAsync();
                }

                _currentSession = null;
                _isInitialized = false;
            }
            catch (Exception ex)
            {
                OnRecordingError($"Error during cleanup: {ex.Message}", ex);
            }
        }

        public bool ValidateSettings(RecordingSettings settings)
        {
            if (settings == null)
                return false;

            if (string.IsNullOrWhiteSpace(settings.OutputDirectory))
                return false;

            if (string.IsNullOrWhiteSpace(settings.OutputFileName))
                return false;

            if (settings.FrameRate <= 0 || settings.FrameRate > 120)
                return false;

            if (settings.CaptureMode == CaptureMode.CustomRegion && settings.CustomRegion.IsEmpty)
                return false;

            return true;
        }

        private async void OnFrameCaptured(object sender, FrameCapturedEventArgs e)
        {
            if (_currentSession != null && IsRecording && e.Frame != null)
            {
                try
                {
                    // Send frame to video encoder
                    await _videoEncoderService.AddFrameAsync(e.Frame);

                    _currentSession.FrameCount++;

                    // Update file size (approximate)
                    try
                    {
                        var fileInfo = new FileInfo(_currentSession.OutputFilePath);
                        if (fileInfo.Exists)
                        {
                            _currentSession.FileSizeBytes = fileInfo.Length;
                        }
                    }
                    catch { /* Ignore file access errors during recording */ }

                    // Raise progress event
                    RecordingProgress?.Invoke(this, new RecordingProgressEventArgs
                    {
                        ElapsedTime = _currentSession.Duration,
                        FileSizeBytes = _currentSession.FileSizeBytes,
                        FrameCount = _currentSession.FrameCount
                    });
                }
                finally
                {
                    e.Frame.Dispose();
                }
            }
        }

        private void OnCaptureError(object sender, CaptureErrorEventArgs e)
        {
            OnRecordingError($"Screen capture error: {e.ErrorMessage}", e.Exception);
        }

        private void OnRecordingError(string message, Exception exception = null)
        {
            RecordingError?.Invoke(this, new RecordingErrorEventArgs
            {
                ErrorMessage = message,
                Exception = exception
            });
        }

        public void Dispose()
        {
            CleanupAsync().Wait();

            if (_screenCaptureService != null)
            {
                _screenCaptureService.FrameCaptured -= OnFrameCaptured;
                _screenCaptureService.CaptureError -= OnCaptureError;
            }
        }
    }
}