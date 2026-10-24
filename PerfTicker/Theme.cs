using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace PerfTicker;

/// <summary>视觉系统：深色证券终端配色 + 字体阶梯。</summary>
internal static class Theme
{
    public static float S = 1f;
    public static int Px(float v) => (int)MathF.Round(v * S, MidpointRounding.AwayFromZero);

    // ── 主题状态 ──────────────────────────────────────────
    public static bool Dark = true;

    // ── 底色 ─────────────────────────────────────────────
    public static Color BgDeep, Bg, Panel, PanelHi, PanelSoft, RowHover, RowSel;
    public static Color Line, Line2, Grid;

    // ── 结构面（标题栏 / 表头 / 图表 / 弹层）────────────────
    public static Color TitleTop, TitleBot, HeadTop, HeadBot, HeadBot2, StripBg, Stripe, Stripe2;
    public static Color ChartBg, ChartBg2, ChartBg3, SearchBg, SearchBg2, ChipBg, PopBg, Watermark;
    public static Color MarqueeFade, MarqueeFade2, ChartTag, DarkInk;

    // ── 文字 ─────────────────────────────────────────────
    public static Color TextHi, Txt, TxtHi2, TextDim, TextFade, Flat;

    // ── 语义色（A 股：红涨绿跌）────────────────────────────
    public static Color Up, Down, Gold, GoldDim, GoldSoft, Blue, Purple, Cyan, Warn, EcoOk, EcoBad;

    public static void SetTheme(bool dark)
    {
        Dark = dark;
        if (dark)
        {
            BgDeep = C(0x06, 0x08, 0x0C); Bg = C(0x0A, 0x0D, 0x13);
            Panel = C(0x0E, 0x12, 0x1A); PanelHi = C(0x12, 0x17, 0x21); PanelSoft = C(0x15, 0x1B, 0x26);
            RowHover = C(0x18, 0x1F, 0x2C); RowSel = C(0x1C, 0x24, 0x33);
            Line = C(0x18, 0x1F, 0x2B); Line2 = C(0x23, 0x2C, 0x3B); Grid = C(0x14, 0x1A, 0x25);

            TitleTop = C(0x14, 0x19, 0x23); TitleBot = C(0x0B, 0x0E, 0x15);
            HeadTop = C(0x16, 0x1C, 0x27); HeadBot = C(0x11, 0x16, 0x1F); HeadBot2 = C(0x0C, 0x10, 0x18);
            StripBg = C(0x0B, 0x0E, 0x15); Stripe = C(0x0B, 0x0F, 0x16); Stripe2 = C(0x0A, 0x0E, 0x15);
            ChartBg = C(0x07, 0x0A, 0x0F); ChartBg2 = C(0x0A, 0x0D, 0x14); ChartBg3 = C(0x08, 0x0B, 0x11);
            SearchBg = C(0x0B, 0x0F, 0x17); SearchBg2 = C(0x0E, 0x12, 0x1A);
            ChipBg = Color.FromArgb(20, 255, 255, 255);
            PopBg = Color.FromArgb(0xF8, 0x13, 0x18, 0x23);
            Watermark = Color.FromArgb(13, 236, 244, 255);
            MarqueeFade = C(0x0E, 0x12, 0x19); MarqueeFade2 = C(0x0D, 0x11, 0x18);
            ChartTag = Color.FromArgb(0xF4, 0x14, 0x19, 0x23); DarkInk = C(0x12, 0x16, 0x1F);

            TextHi = C(0xE9, 0xEF, 0xF8); Txt = C(0xAD, 0xB9, 0xCB); TxtHi2 = C(0xC2, 0xCC, 0xDA);
            TextDim = C(0x74, 0x81, 0x93); TextFade = C(0x46, 0x50, 0x60); Flat = C(0x9A, 0xA6, 0xB8);

            Up = C(0xFF, 0x4D, 0x4F); Down = C(0x00, 0xC0, 0x8B);
            Gold = C(0xF2, 0xC1, 0x4E); GoldDim = C(0xA8, 0x86, 0x36); GoldSoft = C(0xFF, 0xD9, 0x8A);
            Blue = C(0x4D, 0x8D, 0xFF); Purple = C(0xA0, 0x74, 0xFF); Cyan = C(0x3A, 0xD6, 0xD6);
            Warn = C(0xFF, 0xA9, 0x40);
            EcoOk = Color.FromArgb(0x8E, 0xC9, 0xB0); EcoBad = Color.FromArgb(0xA0, 0x6E, 0x6E);
        }
        else
        {
            BgDeep = C(0xEE, 0xF1, 0xF6); Bg = C(0xF7, 0xF9, 0xFC);
            Panel = C(0xFF, 0xFF, 0xFF); PanelHi = C(0xF8, 0xFA, 0xFD); PanelSoft = C(0xF0, 0xF4, 0xF9);
            RowHover = C(0xEC, 0xF2, 0xFA); RowSel = C(0xDF, 0xEA, 0xF8);
            Line = C(0xE6, 0xEB, 0xF2); Line2 = C(0xD5, 0xDC, 0xE7); Grid = C(0xEC, 0xF0, 0xF6);

            TitleTop = C(0xFF, 0xFF, 0xFF); TitleBot = C(0xF5, 0xF8, 0xFC);
            HeadTop = C(0xFA, 0xFC, 0xFE); HeadBot = C(0xF3, 0xF7, 0xFB); HeadBot2 = C(0xF1, 0xF5, 0xF9);
            StripBg = C(0xFF, 0xFF, 0xFF); Stripe = C(0xFA, 0xFC, 0xFE); Stripe2 = C(0xF8, 0xFA, 0xFD);
            ChartBg = C(0xFF, 0xFF, 0xFF); ChartBg2 = C(0xFD, 0xFE, 0xFF); ChartBg3 = C(0xF9, 0xFB, 0xFE);
            SearchBg = C(0xFF, 0xFF, 0xFF); SearchBg2 = C(0xF7, 0xFA, 0xFD);
            ChipBg = Color.FromArgb(10, 0, 0, 0);
            PopBg = Color.FromArgb(0xFC, 0xFF, 0xFF, 0xFF);
            Watermark = Color.FromArgb(16, 20, 32, 48);
            MarqueeFade = C(0xF6, 0xF9, 0xFC); MarqueeFade2 = C(0xF5, 0xF8, 0xFC);
            ChartTag = Color.FromArgb(0xF6, 0xFF, 0xFF, 0xFF); DarkInk = C(0xFF, 0xFF, 0xFF);

            TextHi = C(0x16, 0x20, 0x2E); Txt = C(0x3B, 0x46, 0x57); TxtHi2 = C(0x22, 0x2E, 0x40);
            TextDim = C(0x6C, 0x77, 0x88); TextFade = C(0x9A, 0xA4, 0xB3); Flat = C(0x84, 0x8F, 0x9E);

            Up = C(0xDD, 0x35, 0x38); Down = C(0x0B, 0x9D, 0x6E);
            Gold = C(0xB4, 0x84, 0x1A); GoldDim = C(0x8C, 0x6C, 0x1E); GoldSoft = C(0x9A, 0x66, 0x00);
            Blue = C(0x1F, 0x6F, 0xEB); Purple = C(0x7A, 0x4F, 0xD0); Cyan = C(0x0E, 0x8C, 0x8C);
            Warn = C(0xB2, 0x6A, 0x00);
            EcoOk = C(0x0B, 0x8F, 0x62); EcoBad = C(0xC0, 0x4A, 0x4A);
        }
    }

    private static Color C(int r, int g, int b) => Color.FromArgb(r, g, b);

    public static Color UpWarm(float a) => Color.FromArgb((int)(255 * a), Up);
    public static Color DownWarm(float a) => Color.FromArgb((int)(255 * a), Down);

    // ── 字体 ─────────────────────────────────────────────
    private static readonly string NumFamily = PickFamily("Bahnschrift SemiCondensed", "Bahnschrift", "Consolas", "Segoe UI");
    private static readonly string UiFamily = PickFamily("Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI");
    private static readonly string MonoFamily = PickFamily("Cascadia Mono", "Consolas", "Segoe UI");

    public static Font BrandFont, BrandSub, NumXL, NumL, NumM, NumS, NumXS, NumXXS,
        UiXL, UiL, UiM, UiS, UiXS, MockMono, IconFont, NumBig, NumHuge;

    private static string PickFamily(params string[] candidates)
    {
        try
        {
            using var col = new InstalledFontCollection();
            var names = col.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var c in candidates)
                if (names.Contains(c)) return c;
        }
        catch { }
        return FontFamily.GenericSansSerif.Name;
    }

    public static void Rebuild(float scale)
    {
        S = scale;
        BrandFont?.Dispose(); BrandSub?.Dispose(); NumXL?.Dispose(); NumL?.Dispose(); NumM?.Dispose();
        NumS?.Dispose(); NumXS?.Dispose(); NumXXS?.Dispose(); UiXL?.Dispose(); UiL?.Dispose();
        UiM?.Dispose(); UiS?.Dispose(); UiXS?.Dispose(); MockMono?.Dispose(); IconFont?.Dispose();
        NumBig?.Dispose(); NumHuge?.Dispose();

        Font N(float px, FontStyle st = FontStyle.Regular) => new(NumFamily, px * scale, st, GraphicsUnit.Pixel);
        Font U(float px, FontStyle st = FontStyle.Regular) => new(UiFamily, px * scale, st, GraphicsUnit.Pixel);

        NumHuge = N(34, FontStyle.Bold);
        NumBig = N(26, FontStyle.Bold);
        NumXL = N(19, FontStyle.Bold);
        NumL = N(15.5f);
        NumM = N(13.5f);
        NumS = N(12f);
        NumXS = N(11f);
        NumXXS = N(10f);

        UiXL = U(15, FontStyle.Bold);
        UiL = U(13);
        UiM = U(12);
        UiS = U(11);
        UiXS = U(10);

        BrandFont = U(15.5f, FontStyle.Bold);
        BrandSub = N(9.5f);
        MockMono = new Font(MonoFamily, 10.5f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        IconFont = U(11);
    }

    // ── 绘图工具 ─────────────────────────────────────────
    public const TextFormatFlags AL = TextFormatFlags.Left | TextFormatFlags.VerticalCenter;
    public const TextFormatFlags AC = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;
    public const TextFormatFlags AR = TextFormatFlags.Right | TextFormatFlags.VerticalCenter;
    public const TextFormatFlags ATL = TextFormatFlags.Left | TextFormatFlags.Top;
    public const TextFormatFlags ATR = TextFormatFlags.Right | TextFormatFlags.Top;

    /// <summary>GDI 文本不受 GDI+ 变换影响，这里用全局偏移补偿面板平移。</summary>
    public static int TX, TY;

    public static void Text(Graphics g, string s, Font f, Color c, Rectangle r, TextFormatFlags a)
        => Text(g, s, f, c, r, a, TextFormatFlags.NoPadding);

    public static void Text(Graphics g, string s, Font f, Color c, Rectangle r, TextFormatFlags a, TextFormatFlags extra)
    {
        if (string.IsNullOrEmpty(s)) return;
        var flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | a | extra;
        TextRenderer.DrawText(g, s, f, new Rectangle(r.X + TX, r.Y + TY, r.Width, r.Height), c, flags);
    }

    public static void TextEllipsis(Graphics g, string s, Font f, Color c, Rectangle r, TextFormatFlags a)
    {
        if (string.IsNullOrEmpty(s)) return;
        var flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | a | TextFormatFlags.NoPadding
                    | TextFormatFlags.EndEllipsis | TextFormatFlags.PathEllipsis;
        TextRenderer.DrawText(g, s, f, new Rectangle(r.X + TX, r.Y + TY, r.Width, r.Height), c, flags);
    }

    public static int Measure(Graphics g, string s, Font f)
        => TextRenderer.MeasureText(g, s ?? "", f, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;

    public static void Fill(Graphics g, Rectangle r, Color c)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        using var b = new SolidBrush(c);
        g.FillRectangle(b, r);
    }

    public static void VGrad(Graphics g, Rectangle r, Color top, Color bottom)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        using var b = new LinearGradientBrush(new Rectangle(r.X, r.Y, r.Width, Math.Max(2, r.Height)), top, bottom, 90f);
        g.FillRectangle(b, r);
    }

    public static void HGrad(Graphics g, Rectangle r, Color left, Color right)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        using var b = new LinearGradientBrush(new Rectangle(r.X, r.Y, Math.Max(2, r.Width), r.Height), left, right, 0f);
        g.FillRectangle(b, r);
    }

    public static GraphicsPath RoundRect(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
        if (d <= 1) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void RoundedFill(Graphics g, Rectangle r, float rad, Color fill)
    {
        if (r.Width <= 1 || r.Height <= 1) return;
        using var p = RoundRect(new RectangleF(r.X, r.Y, r.Width, r.Height), rad);
        using var b = new SolidBrush(fill);
        g.FillPath(b, p);
    }

    public static void RoundedRect(Graphics g, Rectangle r, float rad, Color fill, Color border, float borderW = 1f)
    {
        if (r.Width <= 1 || r.Height <= 1) return;
        using var p = RoundRect(new RectangleF(r.X, r.Y, r.Width, r.Height), rad);
        using var b = new SolidBrush(fill);
        g.FillPath(b, p);
        if (borderW > 0)
        {
            using var pen = new Pen(border, borderW * S);
            g.DrawPath(pen, p);
        }
    }

    public static void HLine(Graphics g, int x1, int x2, int y, Color c)
    {
        using var pen = new Pen(c, 1f);
        g.DrawLine(pen, x1, y, x2, y);
    }

    public static void VLine(Graphics g, int x, int y1, int y2, Color c)
    {
        using var pen = new Pen(c, 1f);
        g.DrawLine(pen, x, y1, x, y2);
    }

    public static void DashHLine(Graphics g, int x1, int x2, int y, Color c)
    {
        using var pen = new Pen(c, 1f) { DashStyle = DashStyle.Dash, DashPattern = new[] { 3f, 3f } };
        g.DrawLine(pen, x1, y, x2, y);
    }

    public static void DashVLine(Graphics g, int x, int y1, int y2, Color c)
    {
        using var pen = new Pen(c, 1f) { DashStyle = DashStyle.Dash, DashPattern = new[] { 3f, 3f } };
        g.DrawLine(pen, x, y1, x, y2);
    }

    public static Color ChgColor(double v)
        => v > 1e-9 ? Up : v < -1e-9 ? Down : Flat;

    /// <summary>分时线 + 渐变面积。</summary>
    public static void Sparkline(Graphics g, Rectangle r, double[] data, Color line, bool area = true, float lineW = 1.2f)
    {
        if (data == null || data.Length < 2 || r.Width < 4 || r.Height < 4) return;
        double min = data.Min(), max = data.Max();
        if (max - min < 1e-9) { max += 0.5; min -= 0.5; }
        double span = max - min;
        var pts = new PointF[data.Length];
        float stepX = (float)r.Width / (data.Length - 1);
        for (int i = 0; i < data.Length; i++)
        {
            float y = r.Bottom - (float)((data[i] - min) / span) * r.Height;
            pts[i] = new PointF(r.X + i * stepX, Math.Clamp(y, r.Y, r.Bottom));
        }
        if (area)
        {
            var poly = new PointF[pts.Length + 2];
            Array.Copy(pts, poly, pts.Length);
            poly[^2] = new PointF(r.Right, r.Bottom);
            poly[^1] = new PointF(r.X, r.Bottom);
            using var grad = new LinearGradientBrush(new Rectangle(r.X, r.Y, r.Width, Math.Max(2, r.Height)),
                Color.FromArgb(90, line), Color.FromArgb(0, line), 90f);
            g.FillPolygon(grad, poly);
        }
        using var pen = new Pen(line, lineW * S) { LineJoin = LineJoin.Round };
        g.DrawLines(pen, pts);
    }

    /// <summary>迷你分时柱：正/负变化用红绿色，中性用灰。</summary>
    public static void MiniBars(Graphics g, Rectangle r, double[] data, Color neutral)
    {
        if (data == null || data.Length < 2 || r.Width < 4) return;
        double max = 1e-9;
        foreach (var d in data) if (d > max) max = d;
        int n = Math.Min(data.Length, Math.Max(2, r.Width / Math.Max(2, Px(2))));
        float slot = (float)r.Width / n;
        float bw = Math.Max(1f, slot - 1f);
        for (int i = 0; i < n; i++)
        {
            double v = data[data.Length - n + i];
            float h = (float)(v / max) * r.Height;
            if (h < 1) h = 1;
            using var b = new SolidBrush(neutral);
            g.FillRectangle(b, r.X + i * slot, r.Bottom - h, bw, h);
        }
    }

    /// <summary>迷你 K 线：把数据分桶成 OHLC，用于列表里的走势列。</summary>
    public static void MiniCandles(Graphics g, Rectangle r, double[] data, int bars)
    {
        if (data == null || data.Length < 2 || r.Width < 6 || r.Height < 6) return;
        bars = Math.Clamp(bars, 3, Math.Max(3, (int)(r.Width / (3.2f * S))));
        int n = data.Length;
        double min = double.MaxValue, max = double.MinValue;
        foreach (var d in data) { if (d < min) min = d; if (d > max) max = d; }
        if (max - min < 1e-9) { max += 0.01; min -= 0.01; }
        double pad = (max - min) * 0.12;
        min -= pad; max += pad;
        float slot = (float)r.Width / bars;
        float bw = Math.Max(1.1f, slot * 0.58f);
        float Y(double v) => r.Bottom - (float)((v - min) / (max - min)) * r.Height;

        using var penUpM = new Pen(Color.FromArgb(215, Up), 1f);
        using var penDnM = new Pen(Color.FromArgb(215, Down), 1f);
        using var brUpM = new SolidBrush(Up);
        using var brDnM = new SolidBrush(Down);
        for (int i = 0; i < bars; i++)
        {
            int i0 = (int)((long)i * n / bars);
            int i1 = (int)((long)(i + 1) * n / bars);
            if (i1 <= i0) i1 = i0 + 1;
            if (i0 >= n) break;
            double o = data[i0], c = data[Math.Min(i1, n) - 1], h = double.MinValue, l = double.MaxValue;
            for (int k = i0; k < i1 && k < n; k++) { if (data[k] > h) h = data[k]; if (data[k] < l) l = data[k]; }
            bool up = c >= o;
            float cx = r.X + (i + 0.5f) * slot;
            g.DrawLine(up ? penUpM : penDnM, cx, Y(h), cx, Y(l));
            float top = Y(Math.Max(o, c));
            float hh = Math.Max(1f, Math.Abs(Y(o) - Y(c)));
            g.FillRectangle(up ? brUpM : brDnM, cx - bw / 2f, top, Math.Max(1f, bw), hh);
        }
    }
}
