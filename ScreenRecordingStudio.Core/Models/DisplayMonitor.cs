// ScreenRecordingStudio.Core/Models/DisplayMonitor.cs
using System.Drawing;

namespace ScreenRecordingStudio.Core.Models
{
    public class DisplayMonitor
    {
        public int Index { get; set; }
        public string Name { get; set; }
        public string DeviceName { get; set; }
        public Rectangle Bounds { get; set; }
        public Rectangle WorkingArea { get; set; }
        public bool IsPrimary { get; set; }
        public int BitsPerPixel { get; set; }

        // Calculated Properties
        public int Width => Bounds.Width;
        public int Height => Bounds.Height;
        public string Resolution => $"{Width}x{Height}";
        public string DisplayName => IsPrimary ? $"{Name} (Primary)" : Name;

        public override string ToString()
        {
            return $"{DisplayName} - {Resolution}";
        }
    }
}