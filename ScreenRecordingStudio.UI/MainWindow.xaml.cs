// ScreenRecordingStudio.UI/MainWindow.xaml.cs
using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using ScreenRecordingStudio.Core.Interfaces;
using ScreenRecordingStudio.Core.Models;

namespace ScreenRecordingStudio.UI
{
    public partial class MainWindow : Window
    {
        private readonly IRecordingService _recordingService;
        private readonly IScreenCaptureService _screenCaptureService;
        private DispatcherTimer _recordingTimer;
        private DateTime _recordingStartTime;

        public MainWindow(IRecordingService recordingService, IScreenCaptureService screenCaptureService)
        {
            InitializeComponent();

            _recordingService = recordingService ?? throw new ArgumentNullException(nameof(recordingService));
            _screenCaptureService = screenCaptureService ?? throw new ArgumentNullException(nameof(screenCaptureService));

            InitializeUI();
            InitializeTimer();
            InitializeServices();
        }

        private void InitializeUI()
        {
            // Set initial output path
            var defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "ScreenRecordings");
            OutputPathText.Text = defaultPath;

            LoadMonitors();
            UpdateStatus("Ready", Colors.LimeGreen);
        }

        private async void LoadMonitors()
        {
            try
            {
                var monitors = await _screenCaptureService.GetAvailableMonitorsAsync();
                MonitorComboBox.Items.Clear();

                foreach (var monitor in monitors)
                {
                    MonitorComboBox.Items.Add(monitor.ToString());
                }

                if (MonitorComboBox.Items.Count > 0)
                {
                    MonitorComboBox.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load monitors: {ex.Message}", "Error",
                               MessageBoxButton.OK, MessageBoxImage.Warning);

                // Fallback
                MonitorComboBox.Items.Add("Primary Monitor - 1920x1080");
                MonitorComboBox.SelectedIndex = 0;
            }
        }

        private void InitializeTimer()
        {
            _recordingTimer = new DispatcherTimer();
            _recordingTimer.Interval = TimeSpan.FromSeconds(1);
            _recordingTimer.Tick += RecordingTimer_Tick;
        }

        private void InitializeServices()
        {
            // Subscribe to recording events
            _recordingService.RecordingStarted += OnRecordingStarted;
            _recordingService.RecordingStopped += OnRecordingStopped;
            _recordingService.RecordingError += OnRecordingError;
            _recordingService.RecordingProgress += OnRecordingProgress;

            // Set initial settings
            _recordingService.Settings = GetCurrentSettings();
        }

        private RecordingSettings GetCurrentSettings()
        {
            var settings = new RecordingSettings
            {
                OutputDirectory = Path.GetDirectoryName(OutputPathText.Text) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                OutputFileName = $"Recording_{DateTime.Now:yyyyMMdd_HHmmss}",
                RecordAudio = RecordAudioCheckBox.IsChecked == true,
                RecordMicrophone = RecordMicrophoneCheckBox.IsChecked == true,
                RecordSystemAudio = RecordAudioCheckBox.IsChecked == true,
                MonitorIndex = MonitorComboBox.SelectedIndex,
                ShowCursor = true,
                AutoSave = true
            };

            // Set capture mode
            switch (CaptureModeComboBox.SelectedIndex)
            {
                case 0: settings.CaptureMode = CaptureMode.FullScreen; break;
                case 1: settings.CaptureMode = CaptureMode.ActiveWindow; break;
                case 2: settings.CaptureMode = CaptureMode.CustomRegion; break;
                default: settings.CaptureMode = CaptureMode.FullScreen; break;
            }

            // Set frame rate
            switch (FrameRateComboBox.SelectedIndex)
            {
                case 0: settings.FrameRate = 15; break;
                case 1: settings.FrameRate = 30; break;
                case 2: settings.FrameRate = 60; break;
                default: settings.FrameRate = 30; break;
            }

            // Set video quality
            switch (QualityComboBox.SelectedIndex)
            {
                case 0: settings.VideoQuality = VideoQuality.Low; break;
                case 1: settings.VideoQuality = VideoQuality.Medium; break;
                case 2: settings.VideoQuality = VideoQuality.High; break;
                case 3: settings.VideoQuality = VideoQuality.Ultra; break;
                default: settings.VideoQuality = VideoQuality.High; break;
            }

            return settings;
        }

        private void RecordingTimer_Tick(object sender, EventArgs e)
        {
            if (_recordingService.IsRecording)
            {
                var elapsed = DateTime.Now - _recordingStartTime;
                RecordingTimeText.Text = elapsed.ToString(@"hh\:mm\:ss");
            }
        }

        private async void StartRecording_Click(object sender, RoutedEventArgs e)
        {
            if (!_recordingService.IsRecording)
            {
                try
                {
                    // Update settings
                    _recordingService.Settings = GetCurrentSettings();

                    // Start recording
                    var success = await _recordingService.StartRecordingAsync();
                    if (!success)
                    {
                        MessageBox.Show("Failed to start recording. Check the error message.", "Recording Failed",
                                       MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error starting recording: {ex.Message}", "Error",
                                   MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void StopRecording_Click(object sender, RoutedEventArgs e)
        {
            if (_recordingService.IsRecording)
            {
                try
                {
                    var success = await _recordingService.StopRecordingAsync();
                    if (!success)
                    {
                        MessageBox.Show("Failed to stop recording properly.", "Recording Stop Failed",
                                       MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error stopping recording: {ex.Message}", "Error",
                                   MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #region Recording Service Event Handlers

        private void OnRecordingStarted(object sender, RecordingStartedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                _recordingStartTime = e.StartTime;
                StartRecordingButton.IsEnabled = false;
                StopRecordingButton.IsEnabled = true;
                PreviewStatusText.Text = "Recording in progress...";
                RecordingTimeText.Visibility = Visibility.Visible;
                RecordingTimeText.Text = "00:00:00";
                UpdateStatus("Recording", Colors.Red);
                _recordingTimer.Start();
            });
        }

        private void OnRecordingStopped(object sender, RecordingStoppedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                StartRecordingButton.IsEnabled = true;
                StopRecordingButton.IsEnabled = false;
                PreviewStatusText.Text = $"Recording saved: {Path.GetFileName(e.OutputFile)}";
                RecordingTimeText.Visibility = Visibility.Collapsed;
                UpdateStatus("Ready", Colors.LimeGreen);
                _recordingTimer.Stop();

                // Show completion message
                MessageBox.Show($"Recording completed!\n\nDuration: {e.Duration:hh\\:mm\\:ss}\nSaved to: {e.OutputFile}",
                               "Recording Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            });
        }

        private void OnRecordingError(object sender, RecordingErrorEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateStatus("Error", Colors.Red);
                MessageBox.Show($"Recording error: {e.ErrorMessage}", "Recording Error",
                               MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }

        private void OnRecordingProgress(object sender, RecordingProgressEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                // Update progress information (optional)
                // You could show file size, frame count, etc. here
            });
        }

        #endregion

        private void UpdateStatus(string message, Color color)
        {
            StatusText.Text = message;
            StatusIndicator.Fill = new SolidColorBrush(color);
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Settings window will be implemented in the next phase", "Settings",
                           MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BrowseOutput_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select Output Directory",
                InitialDirectory = Path.GetDirectoryName(OutputPathText.Text)
            };

            if (dialog.ShowDialog() == true)
            {
                OutputPathText.Text = dialog.FolderName + "\\";
            }
        }

        protected override async void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_recordingService.IsRecording)
            {
                var result = MessageBox.Show("Recording is in progress. Stop recording before closing?",
                                           "Recording in Progress",
                                           MessageBoxButton.YesNoCancel,
                                           MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    await _recordingService.StopRecordingAsync();
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }
            }

            // Cleanup services
            _recordingTimer?.Stop();
            await _recordingService.CleanupAsync();

            base.OnClosing(e);
        }
    }
}