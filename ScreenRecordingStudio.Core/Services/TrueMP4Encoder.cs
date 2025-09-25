// ScreenRecordingStudio.Core/Services/TrueMp4Encoder.cs
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using ScreenRecordingStudio.Core.Models;

namespace ScreenRecordingStudio.Core.Services
{
    public class TrueMp4Encoder : IVideoEncoderService
    {
        private readonly List<byte[]> _frameData = new();
        private readonly List<uint> _chunkOffsets = new();
        private bool _isEncoding = false;
        private string _outputPath;
        private RecordingSettings _settings;
        private readonly object _lockObject = new();
        private int _width, _height;
        private DateTime _startTime;

        public bool IsEncoding => _isEncoding;

        public async Task<bool> StartEncodingAsync(string outputPath, RecordingSettings settings)
        {
            try
            {
                if (_isEncoding) return false;

                _outputPath = outputPath;
                _settings = settings;
                _isEncoding = true;
                _startTime = DateTime.Now;

                var directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                lock (_lockObject)
                {
                    _frameData.Clear();
                    _chunkOffsets.Clear();
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
                await Task.Run(() => CreateTrueMp4File());
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
            var encoder = GetJpegEncoder();
            var encoderParams = new EncoderParameters(1);

            var quality = _settings.VideoQuality switch
            {
                VideoQuality.Low => 60L,
                VideoQuality.Medium => 75L,
                VideoQuality.High => 85L,
                VideoQuality.Ultra => 95L,
                _ => 75L
            };

            encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
            bitmap.Save(stream, encoder, encoderParams);
            return stream.ToArray();
        }

        private ImageCodecInfo GetJpegEncoder()
        {
            var codecs = ImageCodecInfo.GetImageEncoders();
            foreach (var codec in codecs)
            {
                if (codec.FormatID == ImageFormat.Jpeg.Guid)
                    return codec;
            }
            return null;
        }

        private void CreateTrueMp4File()
        {
            try
            {
                if (_frameData.Count == 0)
                {
                    CreatePlaceholderFile("No frames captured");
                    return;
                }

                _chunkOffsets.Clear();

                using var writer = new BinaryWriter(new FileStream(_outputPath, FileMode.Create));

                // Create a proper MP4 file structure
                var duration = CalculateDuration();

                // Write MP4 atoms in correct order
                WriteFtypAtom(writer);
                var mdatPosition = WriteMdatAtomHeader(writer);
                WriteMoovAtom(writer, duration);
                UpdateMdatSize(writer, mdatPosition);

                System.Diagnostics.Debug.WriteLine($"Created true MP4 file: {_outputPath}");
            }
            catch (Exception ex)
            {
                CreatePlaceholderFile($"MP4 creation failed: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"MP4 creation error: {ex.Message}");
            }
        }

        private uint CalculateDuration()
        {
            return (uint)(_frameData.Count * 1000 / _settings.FrameRate); // Duration in timescale units
        }

        private void WriteFtypAtom(BinaryWriter writer)
        {
            // File Type Box
            WriteUInt32BE(writer, 32); // Size
            WriteStringBE(writer, "ftyp"); // Type
            WriteStringBE(writer, "mp42"); // Major brand
            WriteUInt32BE(writer, 0); // Minor version
            WriteStringBE(writer, "mp42"); // Compatible brand 1
            WriteStringBE(writer, "mp41"); // Compatible brand 2
            WriteStringBE(writer, "isom"); // Compatible brand 3
        }

        private long WriteMdatAtomHeader(BinaryWriter writer)
        {
            var mdatStart = writer.BaseStream.Position;
            WriteUInt32BE(writer, 0); // Size placeholder
            WriteStringBE(writer, "mdat"); // Type

            // Write all frame data and track chunk offsets per frame
            foreach (var frameData in _frameData)
            {
                var frameOffset = writer.BaseStream.Position;
                if (frameOffset > uint.MaxValue)
                {
                    throw new InvalidOperationException("Recording exceeded 32-bit chunk offset capacity");
                }
                _chunkOffsets.Add((uint)frameOffset);
                writer.Write(frameData);
            }

            return mdatStart;
        }

        private void WriteMoovAtom(BinaryWriter writer, uint duration)
        {
            var moovStart = writer.BaseStream.Position;
            WriteUInt32BE(writer, 0); // Size placeholder
            WriteStringBE(writer, "moov"); // Type

            WriteMvhdAtom(writer, duration);
            WriteTrakAtom(writer, duration);

            // Update moov size
            var moovEnd = writer.BaseStream.Position;
            var moovSize = (uint)(moovEnd - moovStart);
            writer.BaseStream.Seek(moovStart, SeekOrigin.Begin);
            WriteUInt32BE(writer, moovSize);
            writer.BaseStream.Seek(moovEnd, SeekOrigin.Begin);
        }

        private void WriteMvhdAtom(BinaryWriter writer, uint duration)
        {
            WriteUInt32BE(writer, 108); // Size
            WriteStringBE(writer, "mvhd"); // Type
            WriteUInt32BE(writer, 0); // Version and flags
            WriteUInt32BE(writer, ToMp4Time(_startTime)); // Creation time
            WriteUInt32BE(writer, ToMp4Time(DateTime.Now)); // Modification time
            WriteUInt32BE(writer, 1000); // Timescale (1000 units per second)
            WriteUInt32BE(writer, duration); // Duration
            WriteUInt32BE(writer, 0x00010000); // Preferred rate (1.0)
            WriteUInt16BE(writer, 0x0100); // Preferred volume (1.0)
            WriteUInt16BE(writer, 0); // Reserved
            WriteUInt32BE(writer, 0); // Reserved
            WriteUInt32BE(writer, 0); // Reserved

            // Transformation matrix (identity)
            WriteUInt32BE(writer, 0x00010000); WriteUInt32BE(writer, 0); WriteUInt32BE(writer, 0);
            WriteUInt32BE(writer, 0); WriteUInt32BE(writer, 0x00010000); WriteUInt32BE(writer, 0);
            WriteUInt32BE(writer, 0); WriteUInt32BE(writer, 0); WriteUInt32BE(writer, 0x40000000);

            // Pre-defined
            for (int i = 0; i < 6; i++) WriteUInt32BE(writer, 0);

            WriteUInt32BE(writer, 2); // Next track ID
        }

        private void WriteTrakAtom(BinaryWriter writer, uint duration)
        {
            var trakStart = writer.BaseStream.Position;
            WriteUInt32BE(writer, 0); // Size placeholder
            WriteStringBE(writer, "trak"); // Type

            WriteTkhdAtom(writer, duration);
            WriteMediaAtom(writer, duration);

            // Update trak size
            var trakEnd = writer.BaseStream.Position;
            var trakSize = (uint)(trakEnd - trakStart);
            writer.BaseStream.Seek(trakStart, SeekOrigin.Begin);
            WriteUInt32BE(writer, trakSize);
            writer.BaseStream.Seek(trakEnd, SeekOrigin.Begin);
        }

        private void WriteTkhdAtom(BinaryWriter writer, uint duration)
        {
            WriteUInt32BE(writer, 92); // Size
            WriteStringBE(writer, "tkhd"); // Type
            WriteUInt32BE(writer, 7); // Version and flags (track enabled, in movie, in preview)
            WriteUInt32BE(writer, ToMp4Time(_startTime)); // Creation time
            WriteUInt32BE(writer, ToMp4Time(DateTime.Now)); // Modification time
            WriteUInt32BE(writer, 1); // Track ID
            WriteUInt32BE(writer, 0); // Reserved
            WriteUInt32BE(writer, duration); // Duration
            WriteUInt32BE(writer, 0); WriteUInt32BE(writer, 0); // Reserved
            WriteUInt16BE(writer, 0); // Layer
            WriteUInt16BE(writer, 0); // Alternate group
            WriteUInt16BE(writer, 0); // Volume
            WriteUInt16BE(writer, 0); // Reserved

            // Transformation matrix (identity)
            WriteUInt32BE(writer, 0x00010000); WriteUInt32BE(writer, 0); WriteUInt32BE(writer, 0);
            WriteUInt32BE(writer, 0); WriteUInt32BE(writer, 0x00010000); WriteUInt32BE(writer, 0);
            WriteUInt32BE(writer, 0); WriteUInt32BE(writer, 0); WriteUInt32BE(writer, 0x40000000);

            WriteUInt32BE(writer, (uint)(_width << 16)); // Width
            WriteUInt32BE(writer, (uint)(_height << 16)); // Height
        }

        private void WriteMediaAtom(BinaryWriter writer, uint duration)
        {
            var mdiaStart = writer.BaseStream.Position;
            WriteUInt32BE(writer, 0); // Size placeholder
            WriteStringBE(writer, "mdia"); // Type

            WriteMdhdAtom(writer, duration);
            WriteHdlrAtom(writer);
            WriteMinfAtom(writer);

            // Update mdia size
            var mdiaEnd = writer.BaseStream.Position;
            var mdiaSize = (uint)(mdiaEnd - mdiaStart);
            writer.BaseStream.Seek(mdiaStart, SeekOrigin.Begin);
            WriteUInt32BE(writer, mdiaSize);
            writer.BaseStream.Seek(mdiaEnd, SeekOrigin.Begin);
        }

        private void WriteMdhdAtom(BinaryWriter writer, uint duration)
        {
            WriteUInt32BE(writer, 32); // Size
            WriteStringBE(writer, "mdhd"); // Type
            WriteUInt32BE(writer, 0); // Version and flags
            WriteUInt32BE(writer, ToMp4Time(_startTime)); // Creation time
            WriteUInt32BE(writer, ToMp4Time(DateTime.Now)); // Modification time
            WriteUInt32BE(writer, 1000); // Timescale
            WriteUInt32BE(writer, duration); // Duration
            WriteUInt16BE(writer, 0x55C4); // Language (undetermined)
            WriteUInt16BE(writer, 0); // Pre-defined
        }

        private void WriteHdlrAtom(BinaryWriter writer)
        {
            WriteUInt32BE(writer, 45); // Size
            WriteStringBE(writer, "hdlr"); // Type
            WriteUInt32BE(writer, 0); // Version and flags
            WriteUInt32BE(writer, 0); // Pre-defined
            WriteStringBE(writer, "vide"); // Handler type (video)
            WriteUInt32BE(writer, 0); WriteUInt32BE(writer, 0); WriteUInt32BE(writer, 0); // Reserved
            WriteStringBE(writer, "Screen Recording\0"); // Name
        }

        private void WriteMinfAtom(BinaryWriter writer)
        {
            var minfStart = writer.BaseStream.Position;
            WriteUInt32BE(writer, 0); // Size placeholder
            WriteStringBE(writer, "minf"); // Type

            WriteVmhdAtom(writer);
            WriteDinfAtom(writer);
            WriteStblAtom(writer);

            // Update minf size
            var minfEnd = writer.BaseStream.Position;
            var minfSize = (uint)(minfEnd - minfStart);
            writer.BaseStream.Seek(minfStart, SeekOrigin.Begin);
            WriteUInt32BE(writer, minfSize);
            writer.BaseStream.Seek(minfEnd, SeekOrigin.Begin);
        }

        private void WriteVmhdAtom(BinaryWriter writer)
        {
            WriteUInt32BE(writer, 20); // Size
            WriteStringBE(writer, "vmhd"); // Type
            WriteUInt32BE(writer, 1); // Version and flags
            WriteUInt16BE(writer, 0); // Graphics mode
            WriteUInt16BE(writer, 0); WriteUInt16BE(writer, 0); WriteUInt16BE(writer, 0); // Opcolor
        }

        private void WriteDinfAtom(BinaryWriter writer)
        {
            WriteUInt32BE(writer, 36); // Size
            WriteStringBE(writer, "dinf"); // Type
            WriteUInt32BE(writer, 28); // dref size
            WriteStringBE(writer, "dref"); // Type
            WriteUInt32BE(writer, 0); // Version and flags
            WriteUInt32BE(writer, 1); // Entry count
            WriteUInt32BE(writer, 12); // url size
            WriteStringBE(writer, "url "); // Type
            WriteUInt32BE(writer, 1); // Version and flags (self-contained)
        }

        private void WriteStblAtom(BinaryWriter writer)
        {
            var stblStart = writer.BaseStream.Position;
            WriteUInt32BE(writer, 0); // Size placeholder
            WriteStringBE(writer, "stbl"); // Type

            WriteStsdAtom(writer);
            WriteSttsAtom(writer);
            WriteStscAtom(writer);
            WriteStszAtom(writer);
            WriteStcoAtom(writer);

            // Update stbl size
            var stblEnd = writer.BaseStream.Position;
            var stblSize = (uint)(stblEnd - stblStart);
            writer.BaseStream.Seek(stblStart, SeekOrigin.Begin);
            WriteUInt32BE(writer, stblSize);
            writer.BaseStream.Seek(stblEnd, SeekOrigin.Begin);
        }

        private void WriteStsdAtom(BinaryWriter writer)
        {
            WriteUInt32BE(writer, 86); // Size
            WriteStringBE(writer, "stsd"); // Type
            WriteUInt32BE(writer, 0); // Version and flags
            WriteUInt32BE(writer, 1); // Entry count

            // JPEG sample description
            WriteUInt32BE(writer, 70); // Size
            WriteStringBE(writer, "jpeg"); // Type (Motion JPEG)
            WriteUInt32BE(writer, 0); WriteUInt16BE(writer, 0); // Reserved
            WriteUInt16BE(writer, 1); // Data reference index
            WriteUInt16BE(writer, 0); WriteUInt16BE(writer, 0); // Pre-defined, reserved
            WriteUInt32BE(writer, 0); WriteUInt32BE(writer, 0); WriteUInt32BE(writer, 0); // Pre-defined
            WriteUInt16BE(writer, (ushort)_width); // Width
            WriteUInt16BE(writer, (ushort)_height); // Height
            WriteUInt32BE(writer, 0x00480000); // Horizontal resolution (72 dpi)
            WriteUInt32BE(writer, 0x00480000); // Vertical resolution (72 dpi)
            WriteUInt32BE(writer, 0); // Data size
            WriteUInt16BE(writer, 1); // Frame count
            for (int i = 0; i < 32; i++) writer.Write((byte)0); // Compressor name
            WriteUInt16BE(writer, 24); // Depth
            WriteUInt16BE(writer, 0xFFFF); // Color table ID
        }

        private void WriteSttsAtom(BinaryWriter writer)
        {
            WriteUInt32BE(writer, 24); // Size
            WriteStringBE(writer, "stts"); // Type
            WriteUInt32BE(writer, 0); // Version and flags
            WriteUInt32BE(writer, 1); // Entry count
            WriteUInt32BE(writer, (uint)_frameData.Count); // Sample count
            WriteUInt32BE(writer, (uint)(1000 / _settings.FrameRate)); // Sample duration
        }

        private void WriteStscAtom(BinaryWriter writer)
        {
            WriteUInt32BE(writer, 28); // Size
            WriteStringBE(writer, "stsc"); // Type
            WriteUInt32BE(writer, 0); // Version and flags
            WriteUInt32BE(writer, 1); // Entry count
            WriteUInt32BE(writer, 1); // First chunk
            WriteUInt32BE(writer, 1); // Samples per chunk
            WriteUInt32BE(writer, 1); // Sample description index
        }

        private void WriteStszAtom(BinaryWriter writer)
        {
            WriteUInt32BE(writer, (uint)(20 + _frameData.Count * 4)); // Size
            WriteStringBE(writer, "stsz"); // Type
            WriteUInt32BE(writer, 0); // Version and flags
            WriteUInt32BE(writer, 0); // Sample size (variable)
            WriteUInt32BE(writer, (uint)_frameData.Count); // Sample count

            foreach (var frame in _frameData)
            {
                WriteUInt32BE(writer, (uint)frame.Length); // Sample size
            }
        }

        private void WriteStcoAtom(BinaryWriter writer)
        {
            WriteUInt32BE(writer, (uint)(16 + _chunkOffsets.Count * 4)); // Size
            WriteStringBE(writer, "stco"); // Type
            WriteUInt32BE(writer, 0); // Version and flags
            WriteUInt32BE(writer, (uint)_chunkOffsets.Count); // Entry count

            foreach (var offset in _chunkOffsets)
            {
                WriteUInt32BE(writer, offset); // Chunk offset for each frame
            }
        }

        private void UpdateMdatSize(BinaryWriter writer, long mdatPosition)
        {
            var currentPosition = writer.BaseStream.Position;
            var mdatSize = (uint)(currentPosition - mdatPosition);

            writer.BaseStream.Seek(mdatPosition, SeekOrigin.Begin);
            WriteUInt32BE(writer, mdatSize);
            writer.BaseStream.Seek(currentPosition, SeekOrigin.Begin);
        }

        private uint ToMp4Time(DateTime dateTime)
        {
            var epoch = new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return (uint)(dateTime.ToUniversalTime() - epoch).TotalSeconds;
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
                    $"Screen Recording Report\n" +
                    $"======================\n" +
                    $"Error: {reason}\n" +
                    $"Frames captured: {_frameData.Count}\n" +
                    $"Resolution: {_width}x{_height}\n" +
                    $"Frame rate: {_settings.FrameRate} FPS\n" +
                    $"Created: {DateTime.Now}\n");
            }
            catch { }
        }
    }
}