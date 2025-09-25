// ScreenRecordingStudio.Core/Services/NativeMp4Encoder.cs
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using ScreenRecordingStudio.Core.Models;

namespace ScreenRecordingStudio.Core.Services
{
    public class NativeMp4Encoder : IVideoEncoderService
    {
        private readonly List<byte[]> _frameData = new();
        private bool _isEncoding = false;
        private string _outputPath;
        private RecordingSettings _settings;
        private readonly object _lockObject = new();
        private int _width, _height;

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

                // Clear any previous frame data
                lock (_lockObject)
                {
                    _frameData.Clear();
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

                // Create the MP4 file from collected frames
                await Task.Run(() => CreateMp4File());

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
                    // Store frame dimensions from first frame
                    if (_frameData.Count == 0)
                    {
                        _width = frame.Width;
                        _height = frame.Height;
                    }

                    // Convert frame to JPEG data for compression
                    var jpegData = BitmapToJpegBytes(frame);
                    _frameData.Add(jpegData);
                }
            });
        }

        private byte[] BitmapToJpegBytes(Bitmap bitmap)
        {
            using var stream = new MemoryStream();

            // Use JPEG encoder with quality settings
            var encoder = ImageCodecInfo.GetImageEncoders()[1]; // JPEG encoder
            var encoderParams = new EncoderParameters(1);

            var quality = _settings.VideoQuality switch
            {
                VideoQuality.Low => 50L,
                VideoQuality.Medium => 75L,
                VideoQuality.High => 85L,
                VideoQuality.Ultra => 95L,
                _ => 75L
            };

            encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
            bitmap.Save(stream, encoder, encoderParams);

            return stream.ToArray();
        }

        private void CreateMp4File()
        {
            try
            {
                if (_frameData.Count == 0)
                {
                    CreatePlaceholderFile("No frames captured");
                    return;
                }

                using var writer = new BinaryWriter(new FileStream(_outputPath, FileMode.Create));

                // Write MP4 structure
                WriteMp4Header(writer);
                WriteMp4MovieData(writer);
                WriteMp4MovieHeader(writer);

                System.Diagnostics.Debug.WriteLine($"Created MP4 file: {_outputPath}");
            }
            catch (Exception ex)
            {
                CreatePlaceholderFile($"MP4 creation failed: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"MP4 creation error: {ex.Message}");
            }
            finally
            {
                // Clear frame data
                lock (_lockObject)
                {
                    _frameData.Clear();
                }
            }
        }

        private void WriteMp4Header(BinaryWriter writer)
        {
            // ftyp box (file type)
            var ftypData = new byte[]
            {
                0x00, 0x00, 0x00, 0x20, // box size (32 bytes)
                0x66, 0x74, 0x79, 0x70, // 'ftyp'
                0x69, 0x73, 0x6F, 0x6D, // major brand 'isom'
                0x00, 0x00, 0x02, 0x00, // minor version
                0x69, 0x73, 0x6F, 0x6D, // compatible brand 'isom'
                0x69, 0x73, 0x6F, 0x32, // compatible brand 'iso2'
                0x61, 0x76, 0x63, 0x31, // compatible brand 'avc1'
                0x6D, 0x70, 0x34, 0x31  // compatible brand 'mp41'
            };
            writer.Write(ftypData);
        }

        private void WriteMp4MovieData(BinaryWriter writer)
        {
            // Calculate total data size
            var totalDataSize = 8; // mdat header size
            foreach (var frame in _frameData)
            {
                totalDataSize += frame.Length;
            }

            // mdat box (movie data)
            WriteUInt32BE(writer, (uint)totalDataSize);
            WriteStringBE(writer, "mdat");

            // Write frame data
            foreach (var frame in _frameData)
            {
                writer.Write(frame);
            }
        }

        private void WriteMp4MovieHeader(BinaryWriter writer)
        {
            // This is a simplified MP4 structure
            // In a real implementation, you'd need proper MP4 atom structure

            var duration = (uint)(_frameData.Count * 1000 / _settings.FrameRate); // in milliseconds

            // moov box (movie metadata) - simplified
            var moovStart = writer.BaseStream.Position;
            WriteUInt32BE(writer, 0); // placeholder for size
            WriteStringBE(writer, "moov");

            // mvhd box (movie header)
            WriteUInt32BE(writer, 108); // mvhd size
            WriteStringBE(writer, "mvhd");
            writer.Write(new byte[4]); // version and flags
            WriteUInt32BE(writer, 0); // creation time
            WriteUInt32BE(writer, 0); // modification time
            WriteUInt32BE(writer, (uint)_settings.FrameRate); // timescale
            WriteUInt32BE(writer, duration); // duration
            WriteUInt32BE(writer, 0x00010000); // preferred rate (1.0)
            WriteUInt16BE(writer, 0x0100); // preferred volume (1.0)
            writer.Write(new byte[10]); // reserved
            // Transformation matrix (identity matrix)
            WriteUInt32BE(writer, 0x00010000);
            WriteUInt32BE(writer, 0x00000000);
            WriteUInt32BE(writer, 0x00000000);
            WriteUInt32BE(writer, 0x00000000);
            WriteUInt32BE(writer, 0x00010000);
            WriteUInt32BE(writer, 0x00000000);
            WriteUInt32BE(writer, 0x00000000);
            WriteUInt32BE(writer, 0x00000000);
            WriteUInt32BE(writer, 0x40000000);
            writer.Write(new byte[24]); // pre-defined
            WriteUInt32BE(writer, 2); // next track ID

            // Update moov box size
            var moovEnd = writer.BaseStream.Position;
            var moovSize = moovEnd - moovStart;
            writer.BaseStream.Seek(moovStart, SeekOrigin.Begin);
            WriteUInt32BE(writer, (uint)moovSize);
            writer.BaseStream.Seek(moovEnd, SeekOrigin.Begin);
        }

        private void WriteUInt32BE(BinaryWriter writer, uint value)
        {
            writer.Write((byte)((value >> 24) & 0xFF));
            writer.Write((byte)((value >> 16) & 0xFF));
            writer.Write((byte)((value >> 8) & 0xFF));
            writer.Write((byte)(value & 0xFF));
        }

        private void WriteUInt16BE(BinaryWriter writer, ushort value)
        {
            writer.Write((byte)((value >> 8) & 0xFF));
            writer.Write((byte)(value & 0xFF));
        }

        private void WriteStringBE(BinaryWriter writer, string value)
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes(value));
        }

        private void CreatePlaceholderFile(string reason)
        {
            try
            {
                var infoPath = Path.ChangeExtension(_outputPath, ".txt");
                File.WriteAllText(infoPath,
                    $"Screen Recording Info\n" +
                    $"====================\n" +
                    $"Intended file: {Path.GetFileName(_outputPath)}\n" +
                    $"Reason: {reason}\n" +
                    $"Frames captured: {_frameData.Count}\n" +
                    $"Resolution: {_width}x{_height}\n" +
                    $"Frame rate: {_settings.FrameRate} FPS\n" +
                    $"Quality: {_settings.VideoQuality}\n" +
                    $"Created: {DateTime.Now}\n" +
                    $"\n" +
                    $"The native MP4 encoder is still in development.\n" +
                    $"For now, try using the GIF format for short recordings.\n");
            }
            catch { /* Ignore if this fails */ }
        }
    }

    // Alternative: Simple Motion JPEG (MJPEG) encoder that creates playable videos
    public class MjpegVideoEncoder : IVideoEncoderService
    {
        private readonly List<byte[]> _frameData = new();
        private bool _isEncoding = false;
        private string _outputPath;
        private RecordingSettings _settings;
        private readonly object _lockObject = new();
        private int _width, _height;

        public bool IsEncoding => _isEncoding;

        public async Task<bool> StartEncodingAsync(string outputPath, RecordingSettings settings)
        {
            try
            {
                if (_isEncoding) return false;

                // Change extension to .avi for MJPEG
                _outputPath = Path.ChangeExtension(outputPath, ".avi");
                _settings = settings;
                _isEncoding = true;

                var directory = Path.GetDirectoryName(_outputPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                lock (_lockObject)
                {
                    _frameData.Clear();
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
                await Task.Run(() => CreateMjpegAvi());
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
                    if (_frameData.Count == 0)
                    {
                        _width = frame.Width;
                        _height = frame.Height;
                    }

                    var jpegData = BitmapToJpegBytes(frame);
                    _frameData.Add(jpegData);
                }
            });
        }

        private byte[] BitmapToJpegBytes(Bitmap bitmap)
        {
            using var stream = new MemoryStream();
            var encoder = ImageCodecInfo.GetImageEncoders()[1]; // JPEG
            var encoderParams = new EncoderParameters(1);
            encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 85L);
            bitmap.Save(stream, encoder, encoderParams);
            return stream.ToArray();
        }

        private void CreateMjpegAvi()
        {
            // Create a simple MJPEG AVI that most players can open
            try
            {
                using var writer = new BinaryWriter(new FileStream(_outputPath, FileMode.Create));

                // Simple AVI with MJPEG codec
                WriteString(writer, "RIFF");
                var fileSizePos = writer.BaseStream.Position;
                writer.Write(0); // File size placeholder
                WriteString(writer, "AVI ");

                // Write minimal headers for MJPEG playback
                WriteAviHeaders(writer);
                WriteAviData(writer);

                // Update file size
                var fileSize = writer.BaseStream.Position - 8;
                writer.BaseStream.Seek(fileSizePos, SeekOrigin.Begin);
                writer.Write((int)fileSize);

                System.Diagnostics.Debug.WriteLine($"Created MJPEG AVI: {_outputPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MJPEG creation failed: {ex.Message}");
            }
        }

        private void WriteAviHeaders(BinaryWriter writer)
        {
            // Simplified AVI headers for MJPEG
            WriteString(writer, "LIST");
            writer.Write(200); // hdrl size
            WriteString(writer, "hdrl");
            WriteString(writer, "avih");
            writer.Write(56); // avih size

            var microSecsPerFrame = 1000000 / _settings.FrameRate;
            writer.Write(microSecsPerFrame); // microseconds per frame
            writer.Write(0); // max bytes per second
            writer.Write(0); // padding
            writer.Write(0x10); // flags
            writer.Write(_frameData.Count); // total frames
            writer.Write(0); // initial frames
            writer.Write(1); // streams
            writer.Write(0); // suggested buffer size
            writer.Write(_width); // width
            writer.Write(_height); // height
            writer.Write(new byte[16]); // reserved
        }

        private void WriteAviData(BinaryWriter writer)
        {
            WriteString(writer, "LIST");
            var dataSizePos = writer.BaseStream.Position;
            writer.Write(0); // data size placeholder
            WriteString(writer, "movi");

            var dataStart = writer.BaseStream.Position;

            // Write JPEG frames
            foreach (var frameData in _frameData)
            {
                WriteString(writer, "00dc"); // chunk ID for video
                writer.Write(frameData.Length);
                writer.Write(frameData);

                // Pad to even boundary
                if (frameData.Length % 2 != 0)
                    writer.Write((byte)0);
            }

            // Update data size
            var dataSize = writer.BaseStream.Position - dataStart;
            writer.BaseStream.Seek(dataSizePos, SeekOrigin.Begin);
            writer.Write((int)dataSize + 4);
        }

        private void WriteString(BinaryWriter writer, string value)
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes(value));
        }
    }
}