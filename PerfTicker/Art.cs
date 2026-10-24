using System.Drawing.Drawing2D;

namespace PerfTicker;

internal static class Art
{
    public static Icon MakeAppIcon()
    {
        using var bmp = new Bitmap(64, 64);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var path = Theme.RoundRect(new RectangleF(3, 3, 58, 58), 14);
            using var lg = new LinearGradientBrush(new Rectangle(0, 0, 64, 64),
                Color.FromArgb(0xFF, 0xDF, 0x9A), Color.FromArgb(0xC2, 0x88, 0x14), 55f);
            g.FillPath(lg, path);
            using (var b = new SolidBrush(Color.FromArgb(0x11, 0x15, 0x1E)))
            {
                g.FillRectangle(b, 17, 30, 7, 22);
                g.FillRectangle(b, 29, 15, 7, 37);
                g.FillRectangle(b, 41, 24, 7, 28);
            }
        }
        IntPtr h = bmp.GetHicon();
        return Icon.FromHandle(h);
    }
}
