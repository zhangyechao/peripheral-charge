using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace PeripheralCharge
{
    internal sealed class DeviceBadge
    {
        public readonly Guid Id;
        public readonly int? Percent;
        public readonly string Tooltip;

        private DeviceBadge(string key, int? percent, string tooltip)
        {
            Id = IdFor(key);
            Percent = percent;
            Tooltip = tooltip;
        }

        public static Guid IdFor(string key)
        {
            // Explorer ties an unsigned icon's GUID to its executable path.
            string identity = Application.ExecutablePath.ToUpperInvariant() + "\0" + key.ToUpperInvariant();
            using (var sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes("PeripheralCharge\0" + identity));
                return new Guid(new ArraySegment<byte>(bytes, 0, 16).ToArray());
            }
        }

        public static IEnumerable<DeviceBadge> For(Snapshot snapshot)
        {
            if (snapshot.Devices.Count == 0)
            {
                yield return new DeviceBadge("application", null, "余电 · " + snapshot.EmptyMessage);
                yield break;
            }
            foreach (DeviceStatus device in snapshot.Devices)
                yield return new DeviceBadge(device.Key, device.Battery.Percent, device.Name + " · " + device.Battery.Display);
        }
    }

    internal static class BadgeIcon
    {
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);

        public static Bitmap Render(int? percent, int size, bool highContrast)
        {
            var bitmap = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Color fill = highContrast ? SystemColors.Highlight :
                    !percent.HasValue ? Color.FromArgb(78, 85, 92) :
                    percent <= BatteryAlert.LowPercent ? Color.FromArgb(159, 50, 37) : Color.FromArgb(22, 108, 72);
                Color ink = highContrast ? SystemColors.HighlightText : Color.White;
                float radius = size * 0.24f;
                using (var tile = new GraphicsPath())
                using (var brush = new SolidBrush(fill))
                {
                    tile.AddArc(0, 0, radius, radius, 180, 90);
                    tile.AddArc(size - radius, 0, radius, radius, 270, 90);
                    tile.AddArc(size - radius, size - radius, radius, radius, 0, 90);
                    tile.AddArc(0, size - radius, radius, radius, 90, 90);
                    tile.CloseFigure();
                    g.FillPath(brush, tile);
                }
                using (var brush = new SolidBrush(ink))
                {
                    if (!percent.HasValue)
                    {
                        int width = size / 2, height = Math.Max(2, size / 8);
                        g.SmoothingMode = SmoothingMode.None;
                        g.FillRectangle(brush, (size - width) / 2, (size - height) / 2, width, height);
                    }
                    else
                    {
                        using (var glyphs = new GraphicsPath())
                        using (var family = new FontFamily("Segoe UI"))
                        {
                            glyphs.AddString(percent.Value.ToString(CultureInfo.InvariantCulture), family,
                                (int)FontStyle.Bold, 64, PointF.Empty, StringFormat.GenericTypographic);
                            RectangleF bounds = glyphs.GetBounds();
                            float sy = (float)Math.Round(size * 0.68) / bounds.Height;
                            float sx = Math.Min(sy, (size - 2f) / bounds.Width);
                            float x = (size - bounds.Width * sx) / 2 - bounds.Left * sx;
                            float y = (size - bounds.Height * sy) / 2 - bounds.Top * sy;
                            using (var position = new Matrix(sx, 0, 0, sy, x, y)) glyphs.Transform(position);
                            g.FillPath(brush, glyphs);
                        }
                    }
                }
            }
            return bitmap;
        }

        public static Icon Draw(int? percent, int size, bool highContrast)
        {
            using (var bitmap = Render(percent, size, highContrast))
            {
                IntPtr native = bitmap.GetHicon();
                try { using (Icon borrowed = Icon.FromHandle(native)) return (Icon)borrowed.Clone(); }
                finally { DestroyIcon(native); }
            }
        }
    }
}
