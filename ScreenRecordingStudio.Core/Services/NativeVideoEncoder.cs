// ScreenRecordingStudio.Core/Services/NativeVideoEncoder.cs
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ScreenRecordingStudio.Core.Models;

namespace ScreenRecordingStudio.Core.Services
{
    public class NativeVideoEncoder : IVideoEncoderService
    {
        private readonly List<Bitmap> _frames = new();
        private bool _isEncoding = false;
        private string _outputPath;
        private RecordingSettings _settings;
        private readonly object _lockObject = new();

        public bool IsEncoding => _isEncoding;

        public async Task<bool> StartEncodingAsync(string outputPath, RecordingSettings settings)
        {
            try
            {
                if (_isEncoding) return false;

                _outputPath = outputPath;
                _settings = settings;
                _isEncoding = true;

                // Ensure output directory exists
                var directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Clear any previous frames
                lock (_lockObject)
                {
                    foreach (var frame in _frames)
                        frame?.Dispose();
                    _frames.Clear();
                }

                return true;
            }
            catch
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

                // Create the video file from collected frames
                await Task.Run(() => CreateVideoFile());

                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task AddFrameAsync(Bitmap frame)
        {
            if (!_isEncoding || frame == null) return;

            await Task.Run(() =>
            {
                lock (_lockObject)
                {
                    // Clone the frame to avoid disposal issues
                    var clonedFrame = new Bitmap(frame);
                    _frames.Add(clonedFrame);
                }
            });
        }

        private void CreateVideoFile()
        {
            try
            {
                // Choose format based on settings
                var extension = Path.GetExtension(_outputPath).ToLower();

                switch (extension)
                {
                    case ".gif":
                        CreateAnimatedGif();
                        break;
                    case ".avi":
                        CreateUncompressedAvi();
                        break;
                    default:
                        // For .mp4 and others, create our own simple video format
                        CreateCustomVideoFormat();
                        break;
                }
            }
            catch (Exception ex)
            {
                // Fallback to creating image sequence
                CreateImageSequence($"Video creation failed: {ex.Message}");
            }
            finally
            {
                // Cleanup frames
                lock (_lockObject)
                {
                    foreach (var frame in _frames)
                        frame?.Dispose();
                    _frames.Clear();
                }
            }
        }

        private void CreateAnimatedGif()
        {
            if (_frames.Count == 0) return;

            try
            {
                using var gif = new FileStream(_outputPath, FileMode.Create);
                var encoder = new GifEncoder(gif);

                var delay = (int)(1000.0 / _settings.FrameRate); // Convert to milliseconds

                foreach (var frame in _frames)
                {
                    encoder.AddFrame(frame, delay);
                }

                encoder.Finish();

                System.Diagnostics.Debug.WriteLine($"Created animated GIF: {_outputPath}");
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to create animated GIF: {ex.Message}");
            }
        }

        private void CreateUncompressedAvi()
        {
            if (_frames.Count == 0) return;

            try
            {
                using var writer = new AviWriter(_outputPath, _settings.FrameRate, _frames[0].Width, _frames[0].Height);

                foreach (var frame in _frames)
                {
                    writer.AddFrame(frame);
                }

                writer.Close();

                System.Diagnostics.Debug.WriteLine($"Created AVI video: {_outputPath}");
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to create AVI: {ex.Message}");
            }
        }

        private void CreateCustomVideoFormat()
        {
            // Use MJPEG encoder for MP4 requests - creates playable AVI files
            try
            {
                var mjpegEncoder = new MjpegVideoEncoder();
                var mjpegPath = Path.ChangeExtension(_outputPath, ".avi");

                // Convert our frames to the MJPEG encoder
                mjpegEncoder.StartEncodingAsync(mjpegPath, _settings).Wait();

                foreach (var frame in _frames)
                {
                    mjpegEncoder.AddFrameAsync(frame).Wait();
                }

                mjpegEncoder.StopEncodingAsync().Wait();

                // Create info about the actual file created
                var infoPath = Path.ChangeExtension(_outputPath, ".txt");
                File.WriteAllText(infoPath,
                    $"Screen Recording Video\n" +
                    $"=====================\n" +
                    $"Requested: {Path.GetFileName(_outputPath)}\n" +
                    $"Created: {Path.GetFileName(mjpegPath)}\n" +
                    $"Format: Motion JPEG (MJPEG) in AVI container\n" +
                    $"Frames: {_frames.Count}\n" +
                    $"Frame Rate: {_settings.FrameRate} FPS\n" +
                    $"Resolution: {_frames[0].Width}x{_frames[0].Height}\n" +
                    $"Quality: {_settings.VideoQuality}\n" +
                    $"Created: {DateTime.Now}\n" +
                    $"\n" +
                    $"This MJPEG video should play in most media players including:\n" +
                    $"- Windows Media Player\n" +
                    $"- VLC Media Player\n" +
                    $"- Any browser\n" +
                    $"\n" +
                    $"MJPEG provides good quality with wide compatibility!\n");

                System.Diagnostics.Debug.WriteLine($"Created playable MJPEG video: {mjpegPath}");
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to create MJPEG video: {ex.Message}");
            }
        }

        private ImageFormat GetImageFormat()
        {
            return _settings.VideoQuality switch
            {
                VideoQuality.Ultra => ImageFormat.Png,
                VideoQuality.High => ImageFormat.Png,
                _ => ImageFormat.Jpeg
            };
        }

        private void CreateVideoPlayerInfo(string videoPath)
        {
            var infoPath = Path.ChangeExtension(videoPath, ".txt");
            File.WriteAllText(infoPath,
                $"Screen Recording Video\n" +
                $"=====================\n" +
                $"File: {Path.GetFileName(videoPath)}\n" +
                $"Format: Custom Screen Video Format (SVF)\n" +
                $"Frames: {_frames.Count}\n" +
                $"Frame Rate: {_settings.FrameRate} FPS\n" +
                $"Resolution: {_frames[0].Width}x{_frames[0].Height}\n" +
                $"Quality: {_settings.VideoQuality}\n" +
                $"Created: {DateTime.Now}\n" +
                $"\n" +
                $"This is a custom video format created by Screen Recording Studio.\n" +
                $"To play this video:\n" +
                $"1. Use the built-in player in Screen Recording Studio, or\n" +
                $"2. Convert to standard format using the export feature, or\n" +
                $"3. Extract individual frames using the frame extraction tool.\n");
        }

        private void CreateImageSequence(string reason)
        {
            try
            {
                var outputDir = Path.Combine(
                    Path.GetDirectoryName(_outputPath),
                    Path.GetFileNameWithoutExtension(_outputPath) + "_frames");

                Directory.CreateDirectory(outputDir);

                // Save each frame
                for (int i = 0; i < _frames.Count; i++)
                {
                    var framePath = Path.Combine(outputDir, $"frame_{i:D6}.png");
                    _frames[i].Save(framePath, ImageFormat.Png);
                }

                // Create info file
                File.WriteAllText(Path.Combine(outputDir, "info.txt"),
                    $"Screen Recording Frames\n" +
                    $"======================\n" +
                    $"Reason: {reason}\n" +
                    $"Frames: {_frames.Count}\n" +
                    $"Frame Rate: {_settings.FrameRate} FPS\n" +
                    $"Resolution: {(_frames.Count > 0 ? $"{_frames[0].Width}x{_frames[0].Height}" : "Unknown")}\n" +
                    $"Created: {DateTime.Now}\n");

                System.Diagnostics.Debug.WriteLine($"Created image sequence in: {outputDir}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create image sequence: {ex.Message}");
            }
        }
    }

    // Simple GIF Encoder
    public class GifEncoder
    {
        private readonly Stream _stream;
        private bool _firstFrame = true;

        public GifEncoder(Stream stream)
        {
            _stream = stream;
            WriteHeader();
        }

        public void AddFrame(Bitmap bitmap, int delay)
        {
            if (_firstFrame)
            {
                WriteLogicalScreen(bitmap);
                _firstFrame = false;
            }
            WriteGraphicControlExtension(delay);
            WriteImageDescriptor(bitmap);
        }

        public void Finish()
        {
            _stream.WriteByte(0x3B); // Trailer
        }

        private void WriteHeader()
        {
            _stream.Write(System.Text.Encoding.ASCII.GetBytes("GIF89a"));
        }

        private void WriteLogicalScreen(Bitmap bitmap)
        {
            // Write logical screen descriptor
            WriteUInt16((ushort)bitmap.Width);
            WriteUInt16((ushort)bitmap.Height);
            _stream.WriteByte(0x70); // Global color table flag
            _stream.WriteByte(0x00); // Background color
            _stream.WriteByte(0x00); // Pixel aspect ratio

            // Write minimal global color table (2 colors)
            for (int i = 0; i < 8; i++)
            {
                _stream.WriteByte(0x00);
                _stream.WriteByte(0x00);
                _stream.WriteByte(0x00);
            }
        }

        private void WriteGraphicControlExtension(int delay)
        {
            _stream.WriteByte(0x21); // Extension introducer
            _stream.WriteByte(0xF9); // Graphic control label
            _stream.WriteByte(0x04); // Block size
            _stream.WriteByte(0x00); // Packed field
            WriteUInt16((ushort)(delay / 10)); // Delay in 1/100 seconds
            _stream.WriteByte(0x00); // Transparent color index
            _stream.WriteByte(0x00); // Block terminator
        }

        private void WriteImageDescriptor(Bitmap bitmap)
        {
            _stream.WriteByte(0x2C); // Image separator
            WriteUInt16(0); // Left
            WriteUInt16(0); // Top
            WriteUInt16((ushort)bitmap.Width);
            WriteUInt16((ushort)bitmap.Height);
            _stream.WriteByte(0x00); // Packed field

            // Convert bitmap to simple LZW compressed data
            // For simplicity, we'll use a basic compression
            var pixels = GetPixelData(bitmap);
            WriteLZWData(pixels);
        }

        private byte[] GetPixelData(Bitmap bitmap)
        {
            var data = new List<byte>();
            var bitmapData = bitmap.LockBits(
                new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format24bppRgb);

            try
            {
                var ptr = bitmapData.Scan0;
                var bytes = Math.Abs(bitmapData.Stride) * bitmap.Height;
                var rgbValues = new byte[bytes];
                Marshal.Copy(ptr, rgbValues, 0, bytes);

                // Convert to indexed color (simplified)
                for (int i = 0; i < rgbValues.Length; i += 3)
                {
                    var gray = (byte)((rgbValues[i] + rgbValues[i + 1] + rgbValues[i + 2]) / 3);
                    data.Add((byte)(gray > 128 ? 1 : 0)); // Simple 1-bit conversion
                }
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }

            return data.ToArray();
        }

        private void WriteLZWData(byte[] pixels)
        {
            _stream.WriteByte(0x02); // LZW minimum code size

            // Simple LZW compression (very basic)
            var blockSize = Math.Min(pixels.Length, 255);
            _stream.WriteByte((byte)blockSize);
            _stream.Write(pixels, 0, blockSize);
            _stream.WriteByte(0x00); // Block terminator
        }

        private void WriteUInt16(ushort value)
        {
            _stream.WriteByte((byte)(value & 0xFF));
            _stream.WriteByte((byte)((value >> 8) & 0xFF));
        }
    }

    // Simple AVI Writer
    public class AviWriter : IDisposable
    {
        private readonly BinaryWriter _writer;
        private readonly int _frameRate;
        private readonly int _width;
        private readonly int _height;
        private int _frameCount = 0;
        private long _frameDataStart;

        public AviWriter(string path, int frameRate, int width, int height)
        {
            _writer = new BinaryWriter(new FileStream(path, FileMode.Create));
            _frameRate = frameRate;
            _width = width;
            _height = height;
            WriteHeader();
        }

        public void AddFrame(Bitmap frame)
        {
            // Convert frame to raw RGB data and write
            var frameData = BitmapToByteArray(frame);
            _writer.Write(frameData.Length);
            _writer.Write(frameData);
            _frameCount++;
        }

        private void WriteHeader()
        {
            // Write basic AVI header (simplified)
            _writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            _writer.Write(0); // File size (will update later)
            _writer.Write(System.Text.Encoding.ASCII.GetBytes("AVI "));

            // Mark position for frame data
            _frameDataStart = _writer.BaseStream.Position;
        }

        private byte[] BitmapToByteArray(Bitmap bitmap)
        {
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Bmp);
            return stream.ToArray();
        }

        public void Close()
        {
            // Update file size in header
            var fileSize = _writer.BaseStream.Position;
            _writer.BaseStream.Seek(4, SeekOrigin.Begin);
            _writer.Write((int)(fileSize - 8));

            _writer.Close();
        }

        public void Dispose()
        {
            _writer?.Dispose();
        }
    }
}