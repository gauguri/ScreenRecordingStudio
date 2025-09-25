// ScreenRecordingStudio.Core/Services/VideoEncoderService.cs
// Simplified version that focuses on working functionality
using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Diagnostics;
using FFMpegCore;
using ScreenRecordingStudio.Core.Models;

namespace ScreenRecordingStudio.Core.Services
{
    public interface IVideoEncoderService
    {
        Task<bool> StartEncodingAsync(string outputPath, RecordingSettings settings);
        Task<bool> StopEncodingAsync();
        Task AddFrameAsync(Bitmap frame);
        bool IsEncoding { get; }
    }

    public class VideoEncoderService : IVideoEncoderService
    {
        private readonly ConcurrentQueue<Bitmap> _frameQueue = new();
        private Task _encodingTask;
        private bool _isEncoding = false;
        private string _outputPath;
        private RecordingSettings _settings;
        private static bool _ffmpegAvailable = true; // Start optimistic

        public bool IsEncoding => _isEncoding;

        public async Task<bool> StartEncodingAsync(string outputPath, RecordingSettings settings)
        {
            try
            {
                if (_isEncoding)
                {
                    return false;
                }

                _outputPath = outputPath;
                _settings = settings;
                _isEncoding = true;

                // Ensure output directory exists
                var directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Configure FFMpeg to look in common locations
                TryConfigureFFMpegPath();

                // Start encoding task
                _encodingTask = Task.Run(async () => await EncodeVideoAsync());

                return true;
            }
            catch (Exception)
            {
                _isEncoding = false;
                return false;
            }
        }

        public async Task<bool> StopEncodingAsync()
        {
            try
            {
                _isEncoding = false;

                if (_encodingTask != null)
                {
                    await _encodingTask;
                    _encodingTask = null;
                }

                // Clear any remaining frames
                while (_frameQueue.TryDequeue(out var frame))
                {
                    frame?.Dispose();
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public async Task AddFrameAsync(Bitmap frame)
        {
            if (!_isEncoding || frame == null)
                return;

            await Task.Run(() =>
            {
                // Clone the bitmap to avoid disposal issues
                var clonedFrame = new Bitmap(frame);
                _frameQueue.Enqueue(clonedFrame);
            });
        }

        private void TryConfigureFFMpegPath()
        {
            var possiblePaths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg", "bin"),
                @"C:\ffmpeg\bin",
                @"C:\ffmpeg"
            };

            foreach (var path in possiblePaths)
            {
                var ffmpegPath = Path.Combine(path, "ffmpeg.exe");
                if (File.Exists(ffmpegPath))
                {
                    try
                    {
                        FFMpegCore.GlobalFFOptions.Configure(options => options.BinaryFolder = path);
                        Debug.WriteLine($"Found FFMpeg at: {path}");
                        return;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to configure FFMpeg path {path}: {ex.Message}");
                    }
                }
            }
        }

        private async Task EncodeVideoAsync()
        {
            var tempImageDir = Path.Combine(Path.GetTempPath(), "ScreenRecording_" + Guid.NewGuid().ToString("N")[..8]);

            try
            {
                Directory.CreateDirectory(tempImageDir);
                var frameCount = 0;

                // Process frames and save as temporary images
                while (_isEncoding || !_frameQueue.IsEmpty)
                {
                    if (_frameQueue.TryDequeue(out var frame))
                    {
                        var frameFile = Path.Combine(tempImageDir, $"frame_{frameCount:D6}.png");

                        using (frame)
                        {
                            frame.Save(frameFile, ImageFormat.Png);
                        }

                        frameCount++;
                    }
                    else
                    {
                        await Task.Delay(16); // Wait for more frames (~60fps check)
                    }
                }

                Debug.WriteLine($"Captured {frameCount} frames");

                // Try to convert to video, fallback to image sequence if FFMpeg fails
                if (frameCount > 0)
                {
                    var success = await TryConvertToVideo(tempImageDir, frameCount);
                    if (!success)
                    {
                        await CreateImageSequenceOutput(tempImageDir, frameCount);
                    }
                }
                else
                {
                    await CreatePlaceholderVideoAsync("No frames were captured");
                }
            }
            catch (Exception ex)
            {
                await CreatePlaceholderVideoAsync($"Encoding failed: {ex.Message}");
                Debug.WriteLine($"Video encoding error: {ex.Message}");
            }
            finally
            {
                // Cleanup temp directory
                try
                {
                    if (Directory.Exists(tempImageDir))
                    {
                        Directory.Delete(tempImageDir, true);
                    }
                }
                catch { /* Ignore cleanup errors */ }
            }
        }

        private async Task<bool> TryConvertToVideo(string tempImageDir, int frameCount)
        {
            try
            {
                var inputPattern = Path.Combine(tempImageDir, "frame_%06d.png");

                await FFMpegArguments
                    .FromFileInput(inputPattern, false, options => options
                        .WithFramerate(_settings.FrameRate))
                    .OutputToFile(_outputPath, true, options => options
                        .WithVideoCodec("libx264")
                        .WithConstantRateFactor(GetCRFFromQuality(_settings.VideoQuality))
                        .WithFastStart()
                        .WithFramerate(_settings.FrameRate)
                        .WithCustomArgument("-pix_fmt yuv420p"))
                    .ProcessAsynchronously();

                // Verify the file was created and has content
                if (File.Exists(_outputPath))
                {
                    var fileInfo = new FileInfo(_outputPath);
                    if (fileInfo.Length > 0)
                    {
                        Debug.WriteLine($"Video created successfully: {_outputPath} ({fileInfo.Length} bytes)");
                        _ffmpegAvailable = true;
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"FFMpeg conversion failed: {ex.Message}");
                _ffmpegAvailable = false;
                return false;
            }
        }

        private async Task CreateImageSequenceOutput(string tempImageDir, int frameCount)
        {
            try
            {
                // Copy frames to output location
                var outputDir = Path.Combine(Path.GetDirectoryName(_outputPath),
                    Path.GetFileNameWithoutExtension(_outputPath) + "_frames");

                Directory.CreateDirectory(outputDir);

                // Copy all frame files
                var frameFiles = Directory.GetFiles(tempImageDir, "frame_*.png");
                foreach (var frameFile in frameFiles)
                {
                    var destFile = Path.Combine(outputDir, Path.GetFileName(frameFile));
                    File.Copy(frameFile, destFile, true);
                }

                // Create instructions file
                await File.WriteAllTextAsync(Path.Combine(outputDir, "README.txt"),
                    $"Screen Recording Frames\n" +
                    $"======================\n" +
                    $"Created: {DateTime.Now}\n" +
                    $"Total Frames: {frameCount}\n" +
                    $"Frame Rate: {_settings.FrameRate} FPS\n" +
                    $"Quality: {_settings.VideoQuality}\n" +
                    $"\n" +
                    $"FFMpeg was not available, so frames were saved as individual images.\n" +
                    $"\n" +
                    $"To create an MP4 video:\n" +
                    $"1. Download FFMpeg from: https://www.gyan.dev/ffmpeg/builds/\n" +
                    $"2. Extract ffmpeg.exe to: C:\\ffmpeg\\bin\\\n" +
                    $"3. Open command prompt in this folder and run:\n" +
                    $"   C:\\ffmpeg\\bin\\ffmpeg.exe -framerate {_settings.FrameRate} -i frame_%06d.png -c:v libx264 -pix_fmt yuv420p video.mp4\n" +
                    $"\n" +
                    $"Or restart the Screen Recording Studio after installing FFMpeg.");

                Debug.WriteLine($"Created image sequence in: {outputDir}");
            }
            catch (Exception ex)
            {
                await CreatePlaceholderVideoAsync($"Failed to create image sequence: {ex.Message}");
            }
        }

        private async Task CreatePlaceholderVideoAsync(string errorMessage)
        {
            try
            {
                var textFile = Path.ChangeExtension(_outputPath, ".txt");
                await File.WriteAllTextAsync(textFile,
                    $"Screen Recording Report\n" +
                    $"======================\n" +
                    $"Created: {DateTime.Now}\n" +
                    $"Intended Output: {_outputPath}\n" +
                    $"Error: {errorMessage}\n" +
                    $"\n" +
                    $"To enable MP4 video recording:\n" +
                    $"1. Download FFMpeg essentials from: https://www.gyan.dev/ffmpeg/builds/\n" +
                    $"2. Extract and place ffmpeg.exe at: C:\\ffmpeg\\bin\\ffmpeg.exe\n" +
                    $"3. Restart the application and try again.\n");

                Debug.WriteLine($"Created error report: {textFile}");
            }
            catch { /* Ignore if this fails too */ }
        }

        private int GetCRFFromQuality(VideoQuality quality)
        {
            return quality switch
            {
                VideoQuality.Low => 28,      // Lower quality, smaller file
                VideoQuality.Medium => 23,   // Balanced
                VideoQuality.High => 18,     // High quality
                VideoQuality.Ultra => 15,    // Very high quality
                _ => 23
            };
        }
    }
}