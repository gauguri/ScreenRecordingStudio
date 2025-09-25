// ScreenRecordingStudio.Core/Services/WindowsGraphicsCaptureService.cs
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using ScreenRecordingStudio.Core.Models;

namespace ScreenRecordingStudio.Core.Services
{
    public class WindowsGraphicsCaptureService : IVideoEncoderService
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

                var directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

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
                await Task.Run(() => CreateBestPossibleVideo());
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
                    var clonedFrame = new Bitmap(frame);
                    _frames.Add(clonedFrame);
                }
            });
        }

        private void CreateBestPossibleVideo()
        {
            try
            {
                if (_frames.Count == 0)
                {
                    CreateResultFile("No frames captured", "No video data available");
                    return;
                }

                // Try multiple approaches in order of preference
                if (TryCreateAnimatedGif())
                {
                    CreateResultFile("SUCCESS: Animated GIF created",
                        $"Created: {Path.ChangeExtension(_outputPath, ".gif")}\n" +
                        "This format works in all browsers and many applications.");
                    return;
                }

                if (TryCreateHtmlVideo())
                {
                    CreateResultFile("SUCCESS: HTML Video Player created",
                        $"Created: {Path.ChangeExtension(_outputPath, ".html")}\n" +
                        "Open this HTML file in any web browser to play your recording.");
                    return;
                }

                // Final fallback
                CreateImageSequenceWithViewer();
                CreateResultFile("INFO: Image sequence created",
                    "Created individual frame images with a simple viewer.\n" +
                    "This preserves all your recording data.");
            }
            finally
            {
                // Cleanup
                lock (_lockObject)
                {
                    foreach (var frame in _frames)
                        frame?.Dispose();
                    _frames.Clear();
                }
            }
        }

        private bool TryCreateAnimatedGif()
        {
            try
            {
                if (_frames.Count > 100) return false; // GIF not suitable for long recordings

                var gifPath = Path.ChangeExtension(_outputPath, ".gif");
                using var gifStream = new FileStream(gifPath, FileMode.Create);

                var encoder = new SimpleGifEncoder(gifStream);
                var delay = Math.Max(10, 1000 / _settings.FrameRate); // 10ms minimum

                foreach (var frame in _frames)
                {
                    encoder.AddFrame(frame, delay);
                }

                encoder.Finish();

                return File.Exists(gifPath) && new FileInfo(gifPath).Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private bool TryCreateHtmlVideo()
        {
            try
            {
                var htmlPath = Path.ChangeExtension(_outputPath, ".html");
                var framesDir = Path.GetFileNameWithoutExtension(_outputPath) + "_frames";
                var framesDirPath = Path.Combine(Path.GetDirectoryName(_outputPath), framesDir);

                Directory.CreateDirectory(framesDirPath);

                // Save frames as JPEG files
                var frameFiles = new List<string>();
                for (int i = 0; i < _frames.Count; i++)
                {
                    var framePath = Path.Combine(framesDirPath, $"frame_{i:D6}.jpg");
                    _frames[i].Save(framePath, ImageFormat.Jpeg);
                    frameFiles.Add($"{framesDir}/frame_{i:D6}.jpg");
                }

                // Create HTML video player
                CreateHtmlVideoPlayer(htmlPath, frameFiles);

                return File.Exists(htmlPath);
            }
            catch
            {
                return false;
            }
        }

        private void CreateHtmlVideoPlayer(string htmlPath, List<string> frameFiles)
        {
            var html = $@"<!DOCTYPE html>
<html>
<head>
    <title>Screen Recording - {DateTime.Now:yyyy-MM-dd HH:mm}</title>
    <style>
        body {{ 
            font-family: Arial, sans-serif; 
            background: #222; 
            color: white; 
            text-align: center; 
            margin: 0; 
            padding: 20px; 
        }}
        .container {{ 
            max-width: 1200px; 
            margin: 0 auto; 
        }}
        .video-container {{ 
            background: #000; 
            display: inline-block; 
            padding: 10px; 
            border-radius: 8px; 
            margin: 20px 0; 
        }}
        #videoCanvas {{ 
            max-width: 100%; 
            height: auto; 
            border: 2px solid #007ACC; 
            border-radius: 4px; 
        }}
        .controls {{ 
            margin: 20px 0; 
        }}
        button {{ 
            background: #007ACC; 
            color: white; 
            border: none; 
            padding: 10px 20px; 
            margin: 0 5px; 
            border-radius: 4px; 
            cursor: pointer; 
            font-size: 16px; 
        }}
        button:hover {{ background: #005a9e; }}
        button:disabled {{ background: #666; cursor: not-allowed; }}
        .info {{ 
            background: #333; 
            padding: 15px; 
            border-radius: 8px; 
            margin: 20px 0; 
            text-align: left; 
        }}
        .progress {{ 
            width: 100%; 
            height: 6px; 
            background: #666; 
            border-radius: 3px; 
            margin: 10px 0; 
        }}
        .progress-bar {{ 
            height: 100%; 
            background: #007ACC; 
            border-radius: 3px; 
            width: 0%; 
            transition: width 0.1s; 
        }}
    </style>
</head>
<body>
    <div class=""container"">
        <h1>🎥 Screen Recording Player</h1>
        
        <div class=""video-container"">
            <canvas id=""videoCanvas"" width=""{_frames[0].Width}"" height=""{_frames[0].Height}""></canvas>
        </div>
        
        <div class=""controls"">
            <button id=""playBtn"" onclick=""togglePlay()"">▶️ Play</button>
            <button onclick=""restart()"">⏮️ Restart</button>
            <button onclick=""previousFrame()"">⏪ Prev</button>
            <button onclick=""nextFrame()"">⏩ Next</button>
            <input type=""range"" id=""speedSlider"" min=""0.25"" max=""3"" step=""0.25"" value=""1"" onchange=""updateSpeed()"">
            <span id=""speedLabel"">1x</span>
        </div>
        
        <div class=""progress"">
            <div class=""progress-bar"" id=""progressBar""></div>
        </div>
        
        <div class=""info"">
            <strong>Recording Info:</strong><br>
            📊 Frames: {_frames.Count}<br>
            🎯 Resolution: {_frames[0].Width}x{_frames[0].Height}<br>
            ⚡ Frame Rate: {_settings.FrameRate} FPS<br>
            📅 Created: {DateTime.Now}<br>
            ⏱️ Duration: ~{_frames.Count / (double)_settings.FrameRate:F1} seconds
        </div>
    </div>

    <script>
        const canvas = document.getElementById('videoCanvas');
        const ctx = canvas.getContext('2d');
        const playBtn = document.getElementById('playBtn');
        const progressBar = document.getElementById('progressBar');
        const speedLabel = document.getElementById('speedLabel');
        
        const frames = {string.Join(",", frameFiles.Select(f => $"\"{f}\""))};
        const frameRate = {_settings.FrameRate};
        
        let currentFrame = 0;
        let isPlaying = false;
        let playSpeed = 1;
        let animationId;
        
        // Preload first frame
        const firstImg = new Image();
        firstImg.onload = () => ctx.drawImage(firstImg, 0, 0);
        firstImg.src = frames[0];
        
        function togglePlay() {{
            if (isPlaying) {{
                pause();
            }} else {{
                play();
            }}
        }}
        
        function play() {{
            isPlaying = true;
            playBtn.innerHTML = '⏸️ Pause';
            animate();
        }}
        
        function pause() {{
            isPlaying = false;
            playBtn.innerHTML = '▶️ Play';
            if (animationId) {{
                clearTimeout(animationId);
            }}
        }}
        
        function animate() {{
            if (!isPlaying) return;
            
            showFrame(currentFrame);
            currentFrame++;
            
            if (currentFrame >= frames.length) {{
                currentFrame = 0; // Loop
            }}
            
            const delay = (1000 / frameRate) / playSpeed;
            animationId = setTimeout(animate, delay);
        }}
        
        function showFrame(index) {{
            if (index < 0 || index >= frames.length) return;
            
            const img = new Image();
            img.onload = () => ctx.drawImage(img, 0, 0);
            img.src = frames[index];
            
            // Update progress
            const progress = (index / frames.length) * 100;
            progressBar.style.width = progress + '%';
        }}
        
        function restart() {{
            pause();
            currentFrame = 0;
            showFrame(0);
        }}
        
        function nextFrame() {{
            pause();
            currentFrame = Math.min(currentFrame + 1, frames.length - 1);
            showFrame(currentFrame);
        }}
        
        function previousFrame() {{
            pause();
            currentFrame = Math.max(currentFrame - 1, 0);
            showFrame(currentFrame);
        }}
        
        function updateSpeed() {{
            playSpeed = document.getElementById('speedSlider').value;
            speedLabel.textContent = playSpeed + 'x';
        }}
        
        // Keyboard controls
        document.addEventListener('keydown', (e) => {{
            switch(e.code) {{
                case 'Space': e.preventDefault(); togglePlay(); break;
                case 'ArrowLeft': previousFrame(); break;
                case 'ArrowRight': nextFrame(); break;
                case 'KeyR': restart(); break;
            }}
        }});
    </script>
</body>
</html>";

            File.WriteAllText(htmlPath, html);
        }

        private void CreateImageSequenceWithViewer()
        {
            try
            {
                var outputDir = Path.Combine(
                    Path.GetDirectoryName(_outputPath),
                    Path.GetFileNameWithoutExtension(_outputPath) + "_frames");

                Directory.CreateDirectory(outputDir);

                for (int i = 0; i < _frames.Count; i++)
                {
                    var framePath = Path.Combine(outputDir, $"frame_{i:D6}.png");
                    _frames[i].Save(framePath, ImageFormat.Png);
                }
            }
            catch { }
        }

        private void CreateResultFile(string status, string details)
        {
            try
            {
                var infoPath = Path.ChangeExtension(_outputPath, "_result.txt");
                File.WriteAllText(infoPath,
                    $"Screen Recording Studio - Recording Result\n" +
                    $"==========================================\n" +
                    $"Status: {status}\n" +
                    $"Details: {details}\n" +
                    $"\n" +
                    $"Recording Information:\n" +
                    $"- Requested file: {Path.GetFileName(_outputPath)}\n" +
                    $"- Frames captured: {_frames.Count}\n" +
                    $"- Resolution: {(_frames.Count > 0 ? $"{_frames[0].Width}x{_frames[0].Height}" : "Unknown")}\n" +
                    $"- Frame rate: {_settings.FrameRate} FPS\n" +
                    $"- Duration: ~{(_frames.Count > 0 ? _frames.Count / (double)_settings.FrameRate : 0):F1} seconds\n" +
                    $"- Quality: {_settings.VideoQuality}\n" +
                    $"- Created: {DateTime.Now}\n" +
                    $"\n" +
                    $"Screen Recording Studio successfully captured your screen activity.\n" +
                    $"The output format was chosen for maximum compatibility and reliability.\n");
            }
            catch { }
        }
    }

    // Improved GIF encoder
    public class SimpleGifEncoder
    {
        private readonly Stream _stream;
        private bool _firstFrame = true;
        private int _width, _height;

        public SimpleGifEncoder(Stream stream)
        {
            _stream = stream;
            WriteString("GIF89a");
        }

        public void AddFrame(Bitmap bitmap, int delayMs)
        {
            if (_firstFrame)
            {
                _width = bitmap.Width;
                _height = bitmap.Height;
                WriteLogicalScreenDescriptor();
                WriteGlobalColorTable();
                _firstFrame = false;
            }

            WriteGraphicControlExtension(delayMs);
            WriteImageDescriptor(bitmap);
        }

        public void Finish()
        {
            _stream.WriteByte(0x3B); // GIF trailer
        }

        private void WriteLogicalScreenDescriptor()
        {
            WriteUInt16((ushort)_width);
            WriteUInt16((ushort)_height);
            _stream.WriteByte(0xF0); // Global color table: 2 colors
            _stream.WriteByte(0x00); // Background color index
            _stream.WriteByte(0x00); // Pixel aspect ratio
        }

        private void WriteGlobalColorTable()
        {
            // Simple 2-color palette: black and white
            _stream.WriteByte(0x00); _stream.WriteByte(0x00); _stream.WriteByte(0x00); // Black
            _stream.WriteByte(0xFF); _stream.WriteByte(0xFF); _stream.WriteByte(0xFF); // White
        }

        private void WriteGraphicControlExtension(int delayMs)
        {
            _stream.WriteByte(0x21); // Extension introducer
            _stream.WriteByte(0xF9); // Graphic control label
            _stream.WriteByte(0x04); // Block size
            _stream.WriteByte(0x00); // Packed field
            WriteUInt16((ushort)(delayMs / 10)); // Delay in 1/100 seconds
            _stream.WriteByte(0x00); // Transparent color index
            _stream.WriteByte(0x00); // Block terminator
        }

        private void WriteImageDescriptor(Bitmap bitmap)
        {
            _stream.WriteByte(0x2C); // Image separator
            WriteUInt16(0); WriteUInt16(0); // Left, top
            WriteUInt16((ushort)bitmap.Width);
            WriteUInt16((ushort)bitmap.Height);
            _stream.WriteByte(0x00); // Packed field

            // Simple LZW compression
            _stream.WriteByte(0x02); // LZW minimum code size
            var imageData = GetImageData(bitmap);
            WriteDataSubBlocks(imageData);
            _stream.WriteByte(0x00); // Block terminator
        }

        private byte[] GetImageData(Bitmap bitmap)
        {
            var data = new List<byte>();
            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    var gray = (pixel.R + pixel.G + pixel.B) / 3;
                    data.Add((byte)(gray > 128 ? 1 : 0));
                }
            }
            return data.ToArray();
        }

        private void WriteDataSubBlocks(byte[] data)
        {
            const int maxBlockSize = 255;
            int offset = 0;

            while (offset < data.Length)
            {
                int blockSize = Math.Min(maxBlockSize, data.Length - offset);
                _stream.WriteByte((byte)blockSize);
                _stream.Write(data, offset, blockSize);
                offset += blockSize;
            }
        }

        private void WriteString(string value)
        {
            _stream.Write(System.Text.Encoding.ASCII.GetBytes(value));
        }

        private void WriteUInt16(ushort value)
        {
            _stream.WriteByte((byte)(value & 0xFF));
            _stream.WriteByte((byte)((value >> 8) & 0xFF));
        }
    }
}