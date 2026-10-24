using System.Drawing.Drawing2D;

namespace PerfTicker;

internal abstract class Panel
{
    public Rectangle Bounds;
    public AppForm F;
    public AppState St => F.St;

    public abstract void Paint(Graphics g, Rectangle clip);

    public virtual void MouseMove(Point p) { }
    public virtual void MouseLeave() { }
    public virtual bool Click(Point p, MouseButtons b) => false;
    public virtual void RightClick(Point p) { }
    public virtual bool Advance() => false;
    public virtual void Wheel(Point p, int delta) { }
    public virtual void DoubleClick(Point p) { }
    public virtual Cursor CursorAt(Point p) => Cursors.Default;

    private GraphicsState _gs;

    protected void Begin(Graphics g)
    {
        _gs = g.Save();
        g.TranslateTransform(Bounds.X, Bounds.Y);
        g.SetClip(new Rectangle(0, 0, Bounds.Width, Bounds.Height));
        Theme.TX = Bounds.X;
        Theme.TY = Bounds.Y;
    }

    protected void End(Graphics g)
    {
        Theme.TX = 0;
        Theme.TY = 0;
        g.Restore(_gs);
    }

    protected static int P(float v) => Theme.Px(v);

    protected static Rectangle Rc(float x, float y, float w, float h)
        => new(P(x), P(y), P(w), P(h));

    protected Point Local(Point p) => new(p.X - Bounds.X, p.Y - Bounds.Y);

    protected float LogicalW => Bounds.Width / Theme.S;
    protected float LogicalH => Bounds.Height / Theme.S;

    protected static bool Hit(Rectangle r, Point local) => r.Contains(local);

    protected void Chip(Graphics g, Rectangle r, string text, bool active, bool hover, Color accent, Font font = null)
    {
        var fill = active ? Color.FromArgb(38, accent) : hover ? Theme.PanelSoft : Theme.ChipBg;
        var border = active ? Color.FromArgb(150, accent) : hover ? Theme.Line2 : Theme.Line2;
        Theme.RoundedRect(g, r, 3.5f, fill, border, 1f);
        var c = active ? accent : hover ? Theme.TextHi : Theme.TextDim;
        Theme.Text(g, text, font ?? Theme.UiS, c, r, Theme.AC);
    }
}

/// <summary>顶部标题栏：品牌、状态、采样设置、时钟与窗口按钮。</summary>
internal sealed class TitleBarPanel : Panel
{
    public Rectangle BtnMin, BtnMax, BtnClose, Clock, ChipPause, ChipTop, ChipColor, ChipEco, Pill, Gear;
    public Rectangle[] Segs = new Rectangle[5];
    public string[] SegLabels = { "0.5s", "1s", "2s", "5s", "10s" };
    public int[] SegValues = { 500, 1000, 2000, 5000, 10000 };
    private int _hover = -1;
    private int _hoverSeg = -1;

    public void Layout()
    {
        float w = LogicalW, h = LogicalH;
        float bw = 46;
        BtnClose = Rc(w - bw, 0, bw, h);
        BtnMax = Rc(w - 2 * bw, 0, bw, h);
        BtnMin = Rc(w - 3 * bw, 0, bw, h);
        Clock = Rc(w - 3 * bw - 104, 0, 100, h);

        float x = w - 3 * bw - 116;
        ChipColor = Rc(x - 58, 10, 58, 23); x -= 66;
        ChipTop = Rc(x - 50, 10, 50, 23); x -= 58;
        ChipPause = Rc(x - 62, 10, 62, 23); x -= 70;
        ChipEco = Rc(x - 52, 10, 52, 23); x -= 60;
        for (int i = Segs.Length - 1; i >= 0; i--)
        {
            Segs[i] = Rc(x - 44 * (Segs.Length - i), 10, 42, 23);
        }
        x -= 44 * Segs.Length + 8;
        Pill = Rc(236, 11, 132, 21);
        Gear = Rc(150, 8, 26, 26);
    }

    public override void Paint(Graphics g, Rectangle clip)
    {
        Layout();
        Begin(g);
        float w = LogicalW, h = LogicalH;

        Theme.VGrad(g, new Rectangle(0, 0, Bounds.Width, P(h)), Theme.TitleTop, Theme.TitleBot);
        using (var pen = new Pen(Theme.Line2)) g.DrawLine(pen, 0, P(h) - 1, Bounds.Width, P(h) - 1);
        // 顶部金色高光
        using (var grad = new LinearGradientBrush(new Rectangle(0, 0, Bounds.Width, 2),
                   Color.FromArgb(0, 0xF2, 0xC1, 0x4E), Color.FromArgb(0, 0xF2, 0xC1, 0x4E), 0f))
        {
            var blend = new ColorBlend(3)
            {
                Colors = new[] { Color.FromArgb(0, 0xF2, 0xC1, 0x4E), Color.FromArgb(120, 0xF2, 0xC1, 0x4E), Color.FromArgb(0, 0xF2, 0xC1, 0x4E) },
                Positions = new[] { 0f, 0.45f, 1f }
            };
            grad.InterpolationColors = blend;
            g.FillRectangle(grad, 0, 0, Bounds.Width, P(1));
        }

        // ── 品牌 ──
        var logo = Rc(14, 9, 24, 24);
        using (var gp = Theme.RoundRect(new RectangleF(logo.X, logo.Y, logo.Width, logo.Height), P(6)))
        using (var lg = new LinearGradientBrush(logo, Color.FromArgb(0xFF, 0xD9, 0x8A), Color.FromArgb(0xD9, 0x9C, 0x1E), 55f))
        {
            g.FillPath(lg, gp);
        }
        // logo 内的 K 线柱
        var barCol = Color.FromArgb(0x14, 0x18, 0x22);
        using (var b = new SolidBrush(barCol))
        {
            g.FillRectangle(b, logo.X + P(6), logo.Y + P(11), P(3), P(9));
            g.FillRectangle(b, logo.X + P(11), logo.Y + P(6), P(3), P(14));
            g.FillRectangle(b, logo.X + P(16), logo.Y + P(9), P(3), P(8));
        }

        Theme.Text(g, "性能行情终端", Theme.BrandFont, Theme.TextHi, Rc(46, 5, 200, 19), Theme.AL);
        Theme.Text(g, "PERF · TERMINAL", Theme.BrandSub, Theme.GoldDim, Rc(47, 22, 200, 14), Theme.AL);
        // 设置齿轮
        bool setOn = F.SettingsOpen;
        if (setOn || _hover == 7) Theme.RoundedFill(g, Gear, 4f, setOn ? Color.FromArgb(46, Theme.Gold) : Theme.RowHover);
        {
            float gx = Gear.X + Gear.Width / 2f, gy = Gear.Y + Gear.Height / 2f;
            float rOut = P(8f), rIn = P(4.6f);
            using var pen = new Pen(setOn ? Theme.Gold : _hover == 7 ? Theme.TextHi : Theme.TextDim, Math.Max(1.1f, 1.3f * Theme.S));
            for (int i = 0; i < 6; i++)
            {
                double a = i * Math.PI / 3 + 0.26;
                g.DrawLine(pen, gx + (float)Math.Cos(a) * rIn, gy + (float)Math.Sin(a) * rIn,
                    gx + (float)Math.Cos(a) * rOut, gy + (float)Math.Sin(a) * rOut);
            }
            g.DrawEllipse(pen, gx - rIn, gy - rIn, rIn * 2, rIn * 2);
        }

        Theme.VLine(g, P(190), P(11), P(h - 11), Theme.Line2);

        // ── 状态胶囊 ──
        bool live = !St.Paused;
        var dotC = live ? Theme.Down : Theme.Warn;
        Theme.RoundedRect(g, Pill, (Pill.Height) / 2f / Theme.S, Color.FromArgb(24, dotC), Color.FromArgb(80, dotC), 1f);
        double pulse = (Math.Sin(St.Frame * 3.2) + 1) / 2;
        int dotR = P(5);
        int dotX = Pill.X + P(10);
        int dotY = Pill.Y + (Pill.Height - dotR) / 2;
        if (live)
        {
            using var halo = new SolidBrush(Color.FromArgb((int)(40 + 60 * (1 - pulse)), dotC));
            g.FillEllipse(halo, dotX - P(3), dotY - P(3), dotR + P(6), dotR + P(6));
        }
        using (var b = new SolidBrush(dotC)) g.FillEllipse(b, dotX, dotY, dotR, dotR);
        string pillTxt = live ? $"实时采样 {St.IntervalMs / 1000.0:0.0}s" : "行情已暂停";
        Theme.Text(g, pillTxt, Theme.UiXS, live ? Color.FromArgb(0x7E, 0xE0, 0xC0) : Theme.Warn,
            new Rectangle(dotX + P(10), Pill.Y, Pill.Width - P(20), Pill.Height), Theme.AL);

        // ── 采样周期分段器 ──
        Theme.Text(g, "周期", Theme.UiXS, Theme.TextFade,
            new Rectangle(Segs[0].X - P(42), Segs[0].Y, P(36), Segs[0].Height), Theme.AR);
        var segBox = new Rectangle(Segs[0].X - P(2), Segs[0].Y, (Segs[^1].Right - Segs[0].X) + P(4), Segs[0].Height);
        Theme.RoundedRect(g, segBox, 3.5f, Theme.ChipBg, Theme.Line2, 1f);
        for (int i = 0; i < Segs.Length; i++)
        {
            bool act = St.IntervalMs == SegValues[i];
            if (act) Theme.RoundedFill(g, Segs[i], 3f, Color.FromArgb(30, 0xF2, 0xC1, 0x4E));
            var c = act ? Theme.Gold : _hoverSeg == i ? Theme.TextHi : Theme.TextDim;
            Theme.Text(g, SegLabels[i], Theme.NumXXS, c, Segs[i], Theme.AC);
        }

        // ── 功能 chips ──
        Chip(g, ChipPause, St.Paused ? "继续" : "暂停", St.Paused, _hover == 3, Theme.Gold);
        Chip(g, ChipTop, "置顶", St.TopMost, _hover == 4, Theme.Blue);
        Chip(g, ChipColor, St.RedUp ? "红涨" : "绿涨", true, _hover == 5, St.RedUp ? Theme.Up : Theme.Down);
        Chip(g, ChipEco, "营收", St.Economy.Enabled, _hover == 6, Theme.Gold);

        // ── 时钟 ──
        var now = DateTime.Now;
        Theme.Text(g, now.ToString("HH:mm:ss"), Theme.NumXL, Theme.GoldSoft, new Rectangle(Clock.X, Clock.Y + P(2), Clock.Width, P(20)), Theme.AR);
        Theme.Text(g, now.ToString("yyyy-MM-dd ddd"), Theme.UiXS, Theme.TextFade, new Rectangle(Clock.X, Clock.Y + P(21), Clock.Width, P(14)), Theme.AR);

        // ── 窗口按钮 ──
        DrawWinBtn(g, BtnMin, 0, _hover == 0);
        DrawWinBtn(g, BtnMax, 1, _hover == 1);
        DrawWinBtn(g, BtnClose, 2, _hover == 2);

        End(g);
    }

    private void DrawWinBtn(Graphics g, Rectangle r, int kind, bool hover)
    {
        if (hover)
        {
            var bg = kind == 2 ? Color.FromArgb(0xC4, 0x2B, 0x1C) : Theme.RowHover;
            Theme.Fill(g, r, bg);
        }
        var c = hover && kind == 2 ? Color.White : hover ? Theme.TextHi : Theme.TextDim;
        int cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
        int s = P(4);
        using var pen = new Pen(c, Math.Max(1f, 1.1f * Theme.S));
        switch (kind)
        {
            case 0:
                g.DrawLine(pen, cx - s, cy, cx + s, cy);
                break;
            case 1:
                if (F.WindowState == FormWindowState.Maximized)
                {
                    g.DrawRectangle(pen, cx - s, cy - s + P(1), P(7), P(7));
                    g.DrawRectangle(pen, cx - s + P(2), cy - s - P(1), P(7), P(7));
                }
                else
                {
                    g.DrawRectangle(pen, cx - s, cy - s, s * 2, s * 2);
                }
                break;
            default:
                g.DrawLine(pen, cx - s, cy - s, cx + s, cy + s);
                g.DrawLine(pen, cx + s, cy - s, cx - s, cy + s);
                break;
        }
    }

    public override void MouseMove(Point p)
    {
        var l = Local(p);
        int h = -1, seg = -1;
        if (BtnMin.Contains(l)) h = 0;
        else if (BtnMax.Contains(l)) h = 1;
        else if (BtnClose.Contains(l)) h = 2;
        else if (ChipPause.Contains(l)) h = 3;
        else if (ChipTop.Contains(l)) h = 4;
        else if (ChipColor.Contains(l)) h = 5;
        else if (ChipEco.Contains(l)) h = 6;
        else if (Gear.Contains(l)) h = 7;
        else
        {
            for (int i = 0; i < Segs.Length; i++) if (Segs[i].Contains(l)) seg = i;
        }
        if (h != _hover || seg != _hoverSeg)
        {
            _hover = h;
            _hoverSeg = seg;
            F.Invalidate(Bounds);
        }
    }

    public override void MouseLeave()
    {
        if (_hover != -1 || _hoverSeg != -1)
        {
            _hover = -1;
            _hoverSeg = -1;
            F.Invalidate(Bounds);
        }
    }

    public override Cursor CursorAt(Point p)
        => _hover >= 0 || _hoverSeg >= 0 ? Cursors.Hand : Cursors.Default;

    /// <summary>该点是否落在可交互控件上（用于决定标题栏能否作为拖拽区域）。</summary>
    public bool IsInteractive(Point clientPoint)
    {
        var l = Local(clientPoint);
        var lg = new Point((int)(l.X / Theme.S), (int)(l.Y / Theme.S));
        bool Hit(Rectangle r) => r.Contains(l) || r.Contains(lg);
        if (Hit(BtnMin) || Hit(BtnMax) || Hit(BtnClose)) return true;
        if (Hit(ChipPause) || Hit(ChipTop) || Hit(ChipColor) || Hit(ChipEco) || Hit(Gear)) return true;
        for (int i = 0; i < Segs.Length; i++) if (Hit(Segs[i])) return true;
        return false;
    }

    public override bool Click(Point p, MouseButtons b)
    {
        var l = Local(p);
        if (BtnClose.Contains(l)) { F.Close(); return true; }
        if (BtnMin.Contains(l)) { F.WindowState = FormWindowState.Minimized; return true; }
        if (BtnMax.Contains(l))
        {
            F.WindowState = F.WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
            return true;
        }
        if (ChipPause.Contains(l)) { F.TogglePause(); return true; }
        if (ChipTop.Contains(l)) { F.ToggleTopMost(); return true; }
        if (ChipColor.Contains(l)) { F.ToggleColor(); return true; }
        if (ChipEco.Contains(l)) { F.ToggleEconomy(); return true; }
        if (Gear.Contains(l)) { F.ToggleSettings(); return true; }
        for (int i = 0; i < Segs.Length; i++)
            if (Segs[i].Contains(l)) { F.SetInterval(SegValues[i]); return true; }
        return false;
    }
}

/// <summary>指数条：六个核心指标的行情摘要。</summary>
internal sealed class IndexStripPanel : Panel
{
    private static readonly string[] Codes = { "000001", "000002", "600519", "600521", "000003", "600523", "600036", "000301" };
    private int _hover = -1;

    private Rectangle CellRect(int i)
    {
        float cw = LogicalW / Codes.Length;
        return Rc(cw * i, 0, cw, LogicalH);
    }

    public override void Paint(Graphics g, Rectangle clip)
    {
        Begin(g);
        Theme.Fill(g, new Rectangle(0, 0, Bounds.Width, Bounds.Height), Theme.TitleBot);
        using (var pen = new Pen(Theme.Line))
        {
            g.DrawLine(pen, 0, Bounds.Height - 1, Bounds.Width, Bounds.Height - 1);
        }

        for (int i = 0; i < Codes.Length; i++)
        {
            var cell = CellRect(i);
            var m = St.ByCode(Codes[i]);
            if (m == null) continue;
            if (i == _hover) Theme.Fill(g, cell, Theme.RowHover);
            long nowMs = Environment.TickCount64;
            if (nowMs < m.FlashUntil)
            {
                double t = (m.FlashUntil - nowMs) / 700.0;
                using var fb = new SolidBrush(Color.FromArgb((int)(13 * t), m.FlashDir > 0 ? Theme.Up : Theme.Down));
                g.FillRectangle(fb, cell);
            }
            if (i > 0) Theme.VLine(g, cell.X, P(10), P(LogicalH - 10), Theme.Line);

            var col = Theme.ChgColor(m.ShownChg);
            var spark = new Rectangle(cell.Right - P(80), cell.Y + P(14), P(66), P(32));
            Theme.MiniCandles(g, spark, m.Spark(48), 14);

            Theme.TextEllipsis(g, m.Name, Theme.UiXS, Theme.TextDim,
                new Rectangle(cell.X + P(14), cell.Y + P(6), cell.Width - P(28), P(14)), Theme.AL);
            Theme.Text(g, m.ShownText, Theme.NumL, col,
                new Rectangle(cell.X + P(14), cell.Y + P(21), P(120), P(22)), Theme.AL);
            int vw = Theme.Measure(g, m.ShownText, Theme.NumL);
            Theme.Text(g, m.Unit, Theme.UiXS, Theme.TextFade,
                new Rectangle(cell.X + P(16) + vw, cell.Y + P(24), P(44), P(16)), Theme.AL);
            Theme.Text(g, m.ShownChgText, Theme.NumXXS, col,
                new Rectangle(cell.X + P(14), cell.Y + P(43), P(90), P(13)), Theme.AL);
        }
        End(g);
    }

    public override void MouseMove(Point p)
    {
        var l = Local(p);
        int h = -1;
        for (int i = 0; i < Codes.Length; i++) if (CellRect(i).Contains(l)) h = i;
        if (h != _hover) { _hover = h; F.Invalidate(Bounds); }
    }

    public override void MouseLeave()
    {
        if (_hover != -1) { _hover = -1; F.Invalidate(Bounds); }
    }

    public override Cursor CursorAt(Point p) => _hover >= 0 ? Cursors.Hand : Cursors.Default;

    public override bool Click(Point p, MouseButtons b)
    {
        var l = Local(p);
        for (int i = 0; i < Codes.Length; i++)
        {
            if (!CellRect(i).Contains(l)) continue;
            F.SelectMetric(Codes[i]);
            return true;
        }
        return false;
    }
}

/// <summary>左侧自选行情列表：按板块分组的全部监测指标。</summary>
internal sealed class WatchPanel : Panel
{
    private const float HeaderH = 30f;
    private const float ColH = 21f;
    private const float GroupH = 23f;
    private const float RowH = 24f;
    private int _hoverRow = -1;

    public override void Paint(Graphics g, Rectangle clip)
    {
        Begin(g);
        float w = LogicalW, h = LogicalH;
        Theme.Fill(g, new Rectangle(0, 0, Bounds.Width, Bounds.Height), Theme.Panel);
        using (var pen = new Pen(Theme.Line2)) g.DrawLine(pen, Bounds.Width - 1, 0, Bounds.Width - 1, Bounds.Height);

        // 头部
        var head = Rc(0, 0, w, HeaderH);
        Theme.VGrad(g, head, Theme.HeadTop, Theme.HeadBot);
        using (var pen = new Pen(Theme.Line2)) g.DrawLine(pen, 0, head.Bottom - 1, Bounds.Width, head.Bottom - 1);
        Theme.Fill(g, Rc(0, 0, 2.5f, HeaderH), Theme.Gold);
        Theme.Text(g, "自选行情", Theme.UiL, Theme.TextHi, new Rectangle(P(12), 0, P(100), P(HeaderH)), Theme.AL);
        Theme.Text(g, "WATCHLIST", Theme.BrandSub, Theme.TextFade, new Rectangle(P(82), 0, P(90), P(HeaderH)), Theme.AL);
        Theme.Text(g, $"{St.Metrics.Count} 项", Theme.UiXS, Theme.TextDim, new Rectangle(Bounds.Width - P(64), 0, P(52), P(HeaderH)), Theme.AR);

        // 列头
        float y = HeaderH;
        var ch = Rc(0, y, w, ColH);
        Theme.Fill(g, ch, Theme.HeadBot2);
        Theme.Text(g, "代码 / 名称", Theme.UiXS, Theme.TextFade, new Rectangle(P(12), ch.Y, P(150), ch.Height), Theme.AL);
        Theme.Text(g, "走势", Theme.UiXS, Theme.TextFade, new Rectangle(Bounds.Width - P(170), ch.Y, P(38), ch.Height), Theme.AL);
        Theme.Text(g, "现价", Theme.UiXS, Theme.TextFade, new Rectangle(Bounds.Width - P(116), ch.Y, P(42), ch.Height), Theme.AR);
        Theme.Text(g, "涨跌", Theme.UiXS, Theme.TextFade, new Rectangle(Bounds.Width - P(58), ch.Y, P(48), ch.Height), Theme.AR);
        y += ColH;

        // 分组行
        int rowIndex = 0;
        float contentTop = y;
        float scroll = P(St.WatchScroll);
        y -= scroll;

        int firstRowTop = int.MaxValue;
        foreach (var group in Catalog.GroupOrder)
        {
            var items = St.Metrics.Where(m => m.Group == group).ToList();
            if (items.Count == 0) continue;
            var gh = Rc(0, y, w, GroupH);
            if (gh.Bottom > contentTop && gh.Y < Bounds.Height)
            {
                Theme.Fill(g, gh, Theme.Stripe2);
                using (var pen = new Pen(Theme.Line)) g.DrawLine(pen, 0, gh.Bottom - 1, Bounds.Width, gh.Bottom - 1);
                Theme.Fill(g, Rc(8, y + GroupH / 2 - 3, 2, 6), Theme.GoldDim);
                Theme.Text(g, group, Theme.UiXS, Theme.TxtHi2, new Rectangle(P(16), gh.Y, P(100), gh.Height), Theme.AL);
                Theme.Text(g, items.Count.ToString(), Theme.NumXXS, Theme.TextFade, new Rectangle(Bounds.Width - P(30), gh.Y, P(18), gh.Height), Theme.AR);
            }
            y += GroupH;

            foreach (var m in items)
            {
                var rr = Rc(0, y, w, RowH);
                if (rr.Bottom > contentTop && rr.Y < Bounds.Height)
                {
                    int idx = St.Metrics.IndexOf(m);
                    if (idx == St.SelectedIndex)
                    {
                        Theme.Fill(g, rr, Theme.RowSel);
                        Theme.Fill(g, Rc(0, y, 2.5f, RowH), Theme.Gold);
                    }
                    else if (idx == _hoverRow)
                    {
                        Theme.Fill(g, rr, Theme.RowHover);
                    }
                    PaintRow(g, rr, m, idx == St.SelectedIndex);
                }
                if (firstRowTop == int.MaxValue) firstRowTop = rr.Y;
                y += RowH;
                rowIndex++;
            }
        }

        // 滚动条
        float contentH = y + scroll - contentTop;
        float viewH = LogicalH - contentTop;
        if (contentH > viewH)
        {
            float trackH = LogicalH - contentTop;
            float thumbH = Math.Max(24, trackH * viewH / contentH);
            float maxScroll = contentH - viewH;
            float t = maxScroll <= 0 ? 0 : P(St.WatchScroll) / maxScroll;
            float ty = contentTop + t * (trackH - thumbH);
            Theme.RoundedFill(g, new Rectangle(Bounds.Width - P(4), (int)ty, P(3), (int)thumbH), 1.5f, Theme.Line2);
        }
        End(g);
    }

    private void PaintRow(Graphics g, Rectangle rr, Metric m, bool selected)
    {
        float w = LogicalW;
        long nowMs = Environment.TickCount64;
        if (nowMs < m.FlashUntil)
        {
            double t = (m.FlashUntil - nowMs) / 700.0;
            var fc = m.FlashDir > 0 ? Theme.Up : Theme.Down;
            using var fb = new SolidBrush(Color.FromArgb((int)(18 * t), fc));
            g.FillRectangle(fb, rr);
        }
        // 代码
        Theme.Text(g, m.Code, Theme.MockMono, Theme.TextFade,
            new Rectangle(rr.X + P(10), rr.Y, P(48), rr.Height), Theme.AL);
        // 名称
        Theme.TextEllipsis(g, m.Name, selected ? Theme.UiM : Theme.UiS,
            selected ? Theme.TextHi : Theme.TxtHi2,
            new Rectangle(rr.X + P(62), rr.Y, P(96), rr.Height), Theme.AL);
        // 走势
        var spark = new Rectangle(Bounds.Width - P(168), rr.Y + P(5), P(46), P(14));
        Theme.MiniCandles(g, spark, m.Spark(28), 8);
        // 现价
        var col = Theme.ChgColor(m.ShownChg);
        Theme.Text(g, m.ShownText, Theme.NumS, col, new Rectangle(Bounds.Width - P(120), rr.Y, P(46), rr.Height), Theme.AR);
        // 涨跌
        string chg = m.ShownChgText;
        var badge = new Rectangle(Bounds.Width - P(62), rr.Y + P(5), P(54), P(14));
        if (Math.Abs(m.ChgPct) > 0.001)
            Theme.RoundedFill(g, badge, 2f, Color.FromArgb(26, col));
        Theme.Text(g, chg, Theme.NumXXS, col, badge, Theme.AC);
    }

    private List<(int startIndex, int count, float top, float h)> BuildLayout()
    {
        var list = new List<(int, int, float, float)>();
        float y = HeaderH + ColH;
        foreach (var group in Catalog.GroupOrder)
        {
            var items = St.Metrics.Where(m => m.Group == group).ToList();
            if (items.Count == 0) continue;
            list.Add((-1, 0, y, GroupH));
            y += GroupH;
            int start = St.Metrics.IndexOf(items[0]);
            list.Add((start, items.Count, y, items.Count * RowH));
            y += items.Count * RowH;
        }
        return list;
    }

    private int RowAtLocal(Point l, out Rectangle rect)
    {
        rect = Rectangle.Empty;
        float y = HeaderH + ColH - P(St.WatchScroll);
        foreach (var group in Catalog.GroupOrder)
        {
            var items = St.Metrics.Where(m => m.Group == group).ToList();
            if (items.Count == 0) continue;
            y += GroupH;
            foreach (var m in items)
            {
                var rr = Rc(0, y, LogicalW, RowH);
                if (rr.Contains(l)) { rect = rr; return St.Metrics.IndexOf(m); }
                y += RowH;
            }
        }
        return -1;
    }

    public override void MouseMove(Point p)
    {
        int idx = RowAtLocal(Local(p), out _);
        if (idx != _hoverRow) { _hoverRow = idx; F.Invalidate(Bounds); }
    }

    public override void MouseLeave()
    {
        if (_hoverRow != -1) { _hoverRow = -1; F.Invalidate(Bounds); }
    }

    public override Cursor CursorAt(Point p) => _hoverRow >= 0 ? Cursors.Hand : Cursors.Default;

    public override bool Click(Point p, MouseButtons b)
    {
        int idx = RowAtLocal(Local(p), out _);
        if (idx >= 0)
        {
            St.SelectedIndex = idx;
            F.OnMetricChanged();
            return true;
        }
        return false;
    }

    public override void Wheel(Point p, int delta)
    {
        float contentH = 0;
        float y = HeaderH + ColH;
        foreach (var group in Catalog.GroupOrder)
        {
            var items = St.Metrics.Where(m => m.Group == group).ToList();
            if (items.Count == 0) continue;
            y += GroupH + items.Count * RowH;
        }
        contentH = y;
        float viewH = LogicalH - (HeaderH + ColH);
        int max = (int)Math.Max(0, contentH - viewH);
        St.WatchScroll = Math.Clamp(St.WatchScroll - delta / 3, 0, max);
        F.Invalidate(Bounds);
    }
}

/// <summary>图表上方的品种信息条。</summary>
internal sealed class InstrumentPanel : Panel
{
    public Rectangle[] Tabs = new Rectangle[5];
    public string[] TabLabels = { "2秒K", "5秒K", "10秒K", "30秒K", "1分K" };
    public long[] TabPeriods = { 2000, 5000, 10000, 30000, 60000 };
    private int _hover = -1;

    public override void Paint(Graphics g, Rectangle clip)
    {
        Begin(g);
        float w = LogicalW;
        var m = St.Selected;
        Theme.Fill(g, new Rectangle(0, 0, Bounds.Width, Bounds.Height), Theme.HeadBot2);

        // 第一行：名称 + 代码 + 周期切换
        Theme.Text(g, m.Name, Theme.UiXL, Theme.TextHi, Rc(14, 9, 260, 22), Theme.AL);
        int nw = Theme.Measure(g, m.Name, Theme.UiXL);
        Theme.Text(g, m.Code, Theme.MockMono, Theme.GoldDim, new Rectangle(P(20) + nw, P(9), P(70), P(22)), Theme.AL);
        string tag = m.Group == "指数" ? "大盘" : m.Group;
        var tagR = new Rectangle(P(24) + nw + P(44), P(12), P(38), P(16));
        Theme.RoundedRect(g, tagR, 2f, Color.FromArgb(24, 0xF2, 0xC1, 0x4E), Color.FromArgb(70, 0xF2, 0xC1, 0x4E), 1f);
        Theme.Text(g, tag, Theme.UiXS, Theme.Gold, tagR, Theme.AC);

        // 周期切换（右侧）
        float tw = 64, gap = 5;
        float totalW = 5 * tw + 4 * gap;
        float tx = w - totalW - P(14) / Theme.S;
        for (int i = 0; i < 5; i++)
        {
            Tabs[i] = Rc(tx + i * (tw + gap), 10, tw, 23);
            bool enabled = TabPeriods[i] >= St.IntervalMs * 2L;
            bool act = St.PeriodMs == TabPeriods[i];
            var fill = act ? Color.FromArgb(36, 0xF2, 0xC1, 0x4E) : _hover == i ? Theme.PanelSoft : Theme.ChipBg;
            Theme.RoundedRect(g, Tabs[i], 3f, fill, act ? Color.FromArgb(140, 0xF2, 0xC1, 0x4E) : Theme.Line2, 1f);
            Theme.Text(g, TabLabels[i], Theme.UiXS,
                act ? Theme.Gold : !enabled ? Theme.TextFade : _hover == i ? Theme.TextHi : Theme.TextDim, Tabs[i], Theme.AC);
        }

        // 第二行：现价 + 涨跌
        var col = Theme.ChgColor(m.ShownChg);
        Theme.Text(g, m.ShownText, Theme.NumHuge, col, Rc(14, 32, 220, 36), Theme.AL);
        int bw2 = Theme.Measure(g, m.ShownText, Theme.NumHuge);
        Theme.Text(g, m.Unit, Theme.UiS, Theme.TextDim, new Rectangle(P(20) + bw2, P(46), P(60), P(18)), Theme.AL);

        string sign = m.ShownChg >= 0 ? "+" : "-";
        string chgTxt = sign + m.Fmt(Math.Abs(m.ShownChg)) + (m.Unit == "%" ? "pt" : "");
        string pctTxt = sign + Math.Abs(m.ChgPct).ToString("0.00") + "%";
        int cx2 = P(20) + bw2 + P(70);
        Theme.Text(g, chgTxt, Theme.NumS, col, new Rectangle(cx2, P(38), P(120), P(16)), Theme.AL);
        if (m.Unit != "%")
        {
            var pctBox = new Rectangle(cx2 + P(76), P(54), P(76), P(16));
            Theme.RoundedFill(g, pctBox, 2f, Color.FromArgb(38, col));
            Theme.Text(g, pctTxt, Theme.NumXS, col, pctBox, Theme.AC);
        }

        // 第三行：统计
        var stats = new[]
        {
            ("今开", m.Fmt(m.Open)), ("最高", m.Fmt(m.High)), ("最低", m.Fmt(m.Low)),
            ("均价", m.Fmt(m.Count > 0 ? m.Sum / m.Count : 0)),
            ("振幅", (m.Open != 0 ? (m.High - m.Low) / Math.Abs(m.Open) * 100 : 0).ToString("0.00") + "%"),
        };
        float sx = 14;
        foreach (var (label, val) in stats)
        {
            Theme.Text(g, label, Theme.UiXS, Theme.TextFade, Rc(sx, 74, 34, 16), Theme.AL);
            Theme.Text(g, val, Theme.NumXS, Theme.Txt, Rc(sx + 30, 74, 72, 16), Theme.AL);
            sx += 104;
        }

        using (var pen = new Pen(Theme.Line2)) g.DrawLine(pen, 0, Bounds.Height - 1, Bounds.Width, Bounds.Height - 1);
        End(g);
    }

    public override void MouseMove(Point p)
    {
        var l = Local(p);
        int h = -1;
        for (int i = 0; i < 5; i++) if (Tabs[i].Contains(l)) h = i;
        if (h != _hover) { _hover = h; F.Invalidate(Bounds); }
    }

    public override void MouseLeave()
    {
        if (_hover != -1) { _hover = -1; F.Invalidate(Bounds); }
    }

    public override Cursor CursorAt(Point p) => _hover >= 0 ? Cursors.Hand : Cursors.Default;

    public override bool Click(Point p, MouseButtons b)
    {
        var l = Local(p);
        for (int i = 0; i < 5; i++)
        {
            if (!Tabs[i].Contains(l)) continue;
            F.SetChartMode(TabPeriods[i]);
            return true;
        }
        return false;
    }
}
