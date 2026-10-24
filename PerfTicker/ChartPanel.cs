using System.Drawing.Drawing2D;

namespace PerfTicker;

/// <summary>主图：K 线（含均线）+ 成交量 + 十字光标，带插入 / 缩放过渡动画。</summary>
internal sealed class ChartPanel : Panel
{
    private Point _mouse;
    private bool _inPlot;
    private Rectangle _plot, _vol;
    private float _slot, _x0;   // 固定槽宽 + 右对齐起点（逻辑单位）

    // 动画状态
    private double _drawMin, _drawMax;
    private bool _hasScale;
    private Metric _scaleFor;
    private float _slide;
    private long _lastBucket = -1;
    private double _lcO, _lcH, _lcL, _lcC;
    public bool Animating { get; private set; }

    private const float PadL = 8f, PadR = 62f, PadT = 24f, PadB = 18f;

    private int MaxBars() => Math.Max(8, (int)((_plot.Width / Theme.S) / Math.Clamp((_plot.Width / Theme.S) / 60f, 6f, 20f)));

    private static void ScaleOf(double lo, double hi, out double yMin, out double yMax)
    {
        if (hi - lo < 1e-9) { hi += Math.Abs(hi) * 0.01 + 0.5; lo -= Math.Abs(lo) * 0.01 + 0.5; }
        double pad = (hi - lo) * 0.10;
        yMin = lo - pad;
        yMax = hi + pad;
        if (yMax - yMin < 1e-9) yMax = yMin + 1;
    }

    /// <summary>逐帧推进动画：坐标轴缓动、最新一根 K 线生长、新 K 线滑入。</summary>
    public override bool Advance()
    {
        var m = St.Selected;
        var candles = m.Candles;
        if (candles.Count == 0 || _plot.Width <= 0) { Animating = false; return false; }

        bool dirty = false;
        int maxBars = MaxBars();
        int bars = Math.Min(candles.Count, maxBars);
        int first = candles.Count - bars;

        double lo = double.MaxValue, hi = double.MinValue;
        for (int i = first; i < candles.Count; i++)
        {
            lo = Math.Min(lo, candles[i].L);
            hi = Math.Max(hi, candles[i].H);
        }
        ScaleOf(lo, hi, out double tMin, out double tMax);

        if (!_hasScale || !ReferenceEquals(_scaleFor, m))
        {
            _drawMin = tMin; _drawMax = tMax; _hasScale = true; _scaleFor = m;
            _lastBucket = -1;
            dirty = true;
        }
        else
        {
            double dMin = tMin - _drawMin, dMax = tMax - _drawMax;
            double span = Math.Max(1e-9, tMax - tMin);
            if (Math.Abs(dMin) > span * 0.004 || Math.Abs(dMax) > span * 0.004)
            {
                _drawMin += dMin * 0.38;
                _drawMax += dMax * 0.38;
                dirty = true;
            }
            else if (Math.Abs(dMin) > 1e-9 || Math.Abs(dMax) > 1e-9)
            {
                _drawMin = tMin; _drawMax = tMax;   // 收敛
                dirty = true;
            }
        }

        var last = candles[^1];
        if (_lastBucket != last.Bucket)
        {
            _lastBucket = last.Bucket;
            _lcO = last.O; _lcH = last.H; _lcL = last.L; _lcC = last.C;
            _slide = 1f;
            dirty = true;
        }
        else
        {
            double dh = last.H - _lcH, dl = last.L - _lcL, dc = last.C - _lcC;
            double eps = Math.Max(1e-9, (_drawMax - _drawMin) * 0.0015);
            if (Math.Abs(dh) > eps || Math.Abs(dl) > eps || Math.Abs(dc) > eps)
            {
                _lcH += dh * 0.45; _lcL += dl * 0.45; _lcC += dc * 0.45;
                dirty = true;
            }
            else { _lcH = last.H; _lcL = last.L; _lcC = last.C; }
        }

        if (_slide > 0.006f)
        {
            _slide *= 0.62f;
            dirty = true;
        }
        else _slide = 0;

        Animating = dirty;
        return dirty;
    }

    public override void Paint(Graphics g, Rectangle clip)
    {
        Begin(g);
        var m = St.Selected;
        float w = LogicalW, h = LogicalH;

        Theme.Fill(g, new Rectangle(0, 0, Bounds.Width, Bounds.Height), Theme.ChartBg);

        float availH = h - PadT - PadB;
        float volH = Math.Max(28, availH * 0.19f);
        float gap = 12;
        float priceH = availH - volH - gap;
        _plot = Rc(PadL, PadT, w - PadL - PadR, priceH);
        _vol = Rc(PadL, PadT + priceH + gap, w - PadL - PadR, volH);

        Theme.VGrad(g, _plot, Theme.ChartBg2, Theme.ChartBg3);

        var candles = m.Candles;
        if (candles.Count == 0)
        {
            Watermark(g, m, w, h);
            Theme.Text(g, "正在接收行情数据…", Theme.UiM, Theme.TextFade, new Rectangle(0, 0, Bounds.Width, Bounds.Height), Theme.AC);
            End(g);
            return;
        }

        int maxBars = MaxBars();
        int bars = Math.Min(candles.Count, maxBars);
        int first = candles.Count - bars;
        _slot = (_plot.Width / Theme.S) / maxBars;
        _x0 = (_plot.Width / Theme.S) - bars * _slot + _slide * _slot;
        float slot = _slot;

        double lo = double.MaxValue, hi = double.MinValue;
        var vols = new List<double>(bars);
        for (int i = first; i < candles.Count; i++)
        {
            var c = candles[i];
            lo = Math.Min(lo, c.L);
            hi = Math.Max(hi, c.H);
            vols.Add(c.Vol);
        }
        vols.Sort();
        double maxVol = Math.Max(1, vols[(int)(vols.Count * 0.92)]);

        ScaleOf(lo, hi, out double rawMin, out double rawMax);
        if (!_hasScale || !ReferenceEquals(_scaleFor, m))
        {
            _drawMin = rawMin; _drawMax = rawMax; _hasScale = true; _scaleFor = m;
        }
        double yMin = _drawMin, yMax = _drawMax;
        if (yMax - yMin < 1e-9) { yMin = rawMin; yMax = rawMax; }

        float Py(double v) => _plot.Bottom - (float)((v - yMin) / (yMax - yMin)) * _plot.Height;

        // ── 网格 + 右侧坐标轴 ──
        int rows = 4;
        for (int i = 0; i <= rows; i++)
        {
            double v = yMin + (yMax - yMin) * i / rows;
            int y = (int)Py(v);
            using (var pen = new Pen(i == 0 || i == rows ? Theme.Line2 : Theme.Grid)) g.DrawLine(pen, _plot.X, y, _plot.Right, y);
            Theme.Text(g, v.ToString("F" + m.Decimals), Theme.NumXXS, Theme.TextFade,
                new Rectangle(_plot.Right + P(7), y - P(7), P(52), P(14)), Theme.AL);
        }
        Theme.VLine(g, _plot.Right + P(1), _plot.Y, _vol.Bottom, Theme.Line);

        // ── 时间轴 ──
        long periodMs = Math.Max(250, St.PeriodMs);
        int labelEvery = Math.Max(1, (int)Math.Ceiling(34f / Math.Max(1, slot)));
        for (int i = 0; i < bars; i += labelEvery)
        {
            var c = candles[first + i];
            int x = (int)(_plot.X + (_x0 + (i + 0.5f) * slot) * Theme.S);
            if (i > 0) Theme.VLine(g, x, _plot.Y, _vol.Bottom, Theme.Grid);
            string t = TimeText(c.Bucket * periodMs);
            Theme.Text(g, t, Theme.NumXXS, Theme.TextFade, new Rectangle(x - P(26), _vol.Bottom + P(2), P(52), P(14)), Theme.AC);
        }

        Watermark(g, m, w, h);

        DrawCandles(g, m, candles, first, bars, slot, Py);
        DrawVolume(g, candles, first, bars, maxVol);
        DrawLegend(g, m, candles);

        // ── 最新价参考线 ──
        double last = _lcC;
        float ly = Py(last);
        if (ly >= _plot.Y - 1 && ly <= _plot.Bottom + 1)
        {
            var lc = Theme.ChgColor(m.ShownChg);
            Theme.DashHLine(g, _plot.X, _plot.Right, (int)ly, Color.FromArgb(80, lc));
            var tag = new Rectangle(_plot.Right + P(3), (int)ly - P(7), P(56), P(14));
            Theme.RoundedFill(g, tag, 2f, lc);
            Theme.Text(g, m.Fmt(last), Theme.NumXXS, Color.FromArgb(0x08, 0x0A, 0x0E), tag, Theme.AC);
        }

        if (_inPlot && _mouse.X >= _plot.X && _mouse.X <= _plot.Right && _mouse.Y >= _plot.Y && _mouse.Y <= _plot.Bottom)
            DrawCrosshair(g, m, candles, first, bars, slot, Py, yMin, yMax, periodMs);

        using (var pen = new Pen(Theme.Line)) g.DrawLine(pen, 0, Bounds.Height - 1, Bounds.Width, Bounds.Height - 1);
        End(g);
    }

    private void Watermark(Graphics g, Metric m, float w, float h)
    {
        var f = Theme.NumBig;
        string t = "PERF · TERMINAL";
        int tw = Theme.Measure(g, t, f);
        using (var b = new SolidBrush(Theme.Watermark))
            g.DrawString(t, f, b, (w * Theme.S - tw) / 2f, h * Theme.S / 2f - P(26));
    }

    private void DrawLegend(Graphics g, Metric m, List<Candle> candles)
    {
        float lx = _plot.X + P(8);
        float ly = _plot.Y + P(4);
        void Item(string label, string value, Color c)
        {
            Theme.Text(g, label, Theme.NumXXS, c, new Rectangle((int)lx, (int)ly, P(46), P(13)), Theme.AL);
            lx += P(Theme.Measure(g, label, Theme.NumXXS) / Theme.S + 6);
            Theme.Text(g, value, Theme.NumXXS, Theme.TextDim,
                new Rectangle((int)lx, (int)ly, P(64), P(13)), Theme.AL);
            lx += P(58);
        }

        var mas = new[] { 5, 10, 20 };
        var cols = new[] { Theme.Gold, Theme.Purple, Theme.Cyan };
        for (int k = 0; k < 3; k++)
        {
            int n = mas[k];
            string v = "—";
            if (candles.Count >= n)
            {
                double s = 0;
                for (int i = 0; i < n; i++) s += candles[candles.Count - 1 - i].C;
                v = (s / n).ToString("F" + m.Decimals);
            }
            Item("MA" + n, v, cols[k]);
        }
    }

    private void DrawCandles(Graphics g, Metric m, List<Candle> candles, int first, int bars, float slot, Func<double, float> Py)
    {
        float bodyW = Math.Min(12f, Math.Max(1.5f, slot * 0.62f));
        using var penUp = new Pen(Theme.Up, Math.Max(1f, Theme.S));
        using var penDown = new Pen(Theme.Down, Math.Max(1f, Theme.S));
        using var brushUp = new SolidBrush(Theme.Up);
        using var brushDown = new SolidBrush(Theme.Down);
        using var brushBg = new SolidBrush(Theme.ChartBg);

        for (int i = 0; i < bars; i++)
        {
            var c = candles[first + i];
            bool isLast = first + i == candles.Count - 1;
            double o = isLast ? _lcO : c.O;
            double h2 = isLast ? _lcH : c.H;
            double l2 = isLast ? _lcL : c.L;
            double cc = isLast ? _lcC : c.C;

            float cx = _plot.X + (_x0 + (i + 0.5f) * slot) * Theme.S;
            bool up = cc >= o;
            float yh = Py(h2), yl = Py(l2), yo = Py(o), yc = Py(cc);

            g.DrawLine(up ? penUp : penDown, cx, yh, cx, yl);

            float top = Math.Min(yo, yc);
            float bh = Math.Max(1f, Math.Abs(yc - yo));
            var body = new RectangleF(cx - bodyW * Theme.S / 2, top, bodyW * Theme.S, bh);

            if (bodyW < 3.6f)
            {
                g.FillRectangle(up ? brushUp : brushDown, body);
            }
            else if (up)
            {
                g.FillRectangle(brushBg, body);
                g.DrawRectangle(penUp, body.X, body.Y, Math.Max(1f, body.Width), bh);
            }
            else
            {
                g.FillRectangle(brushDown, body);
            }
        }

        // 均线
        var conf = new[] { (5, Theme.Gold), (10, Theme.Purple), (20, Theme.Cyan) };
        foreach (var (n, col) in conf)
        {
            var pts = new List<PointF>(bars);
            for (int i = 0; i < bars; i++)
            {
                int gi = first + i;
                if (gi < n - 1) continue;
                double sum = 0;
                for (int k = 0; k < n; k++) sum += candles[gi - k].C;
                pts.Add(new PointF(_plot.X + (_x0 + (i + 0.5f) * slot) * Theme.S, Py(sum / n)));
            }
            if (pts.Count > 1)
            {
                using var pen = new Pen(Color.FromArgb(210, col), Math.Max(1f, 1.1f * Theme.S)) { LineJoin = LineJoin.Round };
                g.DrawLines(pen, pts.ToArray());
            }
        }

        // 今开参考线
        float oy = Py(m.Open);
        if (oy > _plot.Y && oy < _plot.Bottom)
        {
            using var pen = new Pen(Theme.Line2, 1f) { DashStyle = DashStyle.Dash, DashPattern = new[] { 4f, 4f } };
            g.DrawLine(pen, _plot.X, oy, _plot.Right, oy);
            Theme.Text(g, "今开 " + m.Fmt(m.Open), Theme.NumXXS, Theme.TextFade,
                new Rectangle(_plot.X + P(6), (int)oy - P(15), P(130), P(13)), Theme.AL);
        }
    }

    private void DrawVolume(Graphics g, List<Candle> candles, int first, int bars, double maxVol)
    {
        float slot = _slot;
        Theme.Text(g, "成交量", Theme.UiXS, Theme.TextFade, new Rectangle(_vol.X, _vol.Y - P(13), P(50), P(12)), Theme.AL);
        double cur = candles[^1].Vol;
        Theme.Text(g, cur >= 1000 ? (cur / 1000).ToString("0.0") + "K" : cur.ToString("0"),
            Theme.NumXXS, Theme.TextFade, new Rectangle(_vol.X + P(44), _vol.Y - P(13), P(60), P(12)), Theme.AL);
        using (var pen = new Pen(Theme.Line)) g.DrawLine(pen, _vol.X, _vol.Bottom, _vol.Right, _vol.Bottom);

        float bw = Math.Min(12f, Math.Max(1f, slot * 0.62f));
        using var volUp = new SolidBrush(Color.FromArgb(140, Theme.Up));
        using var volDown = new SolidBrush(Color.FromArgb(140, Theme.Down));
        for (int i = 0; i < bars; i++)
        {
            var c = candles[first + i];
            float cx = _plot.X + (_x0 + (i + 0.5f) * slot) * Theme.S;
            float hh = (float)(c.Vol / maxVol) * _vol.Height;
            if (hh < 1) hh = 1;
            g.FillRectangle(c.C >= c.O ? volUp : volDown, cx - bw * Theme.S / 2, _vol.Bottom - hh, bw * Theme.S, hh);
        }
    }

    private void DrawCrosshair(Graphics g, Metric m, List<Candle> candles, int first, int bars, float slot, Func<double, float> Py, double yMin, double yMax, long periodMs)
    {
        int idx = Math.Clamp((int)(((_mouse.X - _plot.X) / Theme.S - _x0) / slot), 0, bars - 1);
        var c = candles[first + idx];
        float cx = _plot.X + (_x0 + (idx + 0.5f) * slot) * Theme.S;
        float my = Math.Clamp(_mouse.Y, _plot.Y, _plot.Bottom);

        Theme.DashVLine(g, (int)cx, _plot.Y, _plot.Bottom, Color.FromArgb(0x66, 0x77, 0x88));
        Theme.DashHLine(g, _plot.X, _plot.Right, (int)my, Color.FromArgb(0x66, 0x77, 0x88));

        double vAt = yMin + (yMax - yMin) * (_plot.Bottom - my) / _plot.Height;
        var pt = new Rectangle(_plot.Right + P(3), (int)my - P(7), P(56), P(14));
        Theme.RoundedFill(g, pt, 2f, Theme.PanelSoft);
        Theme.Text(g, vAt.ToString("F" + m.Decimals), Theme.NumXXS, Theme.TextHi, pt, Theme.AC);

        string tt = TimeText(c.Bucket * periodMs);
        int tw = Theme.Measure(g, tt, Theme.NumXXS);
        var tb = new Rectangle((int)cx - (tw + P(10)) / 2, _vol.Bottom + P(1), tw + P(10), P(14));
        Theme.RoundedFill(g, tb, 2f, Theme.PanelSoft);
        Theme.Text(g, tt, Theme.NumXXS, Theme.TextHi, tb, Theme.AC);

        double chg = c.C - m.Open;
        double chgPct = m.Open != 0 ? chg / Math.Abs(m.Open) * 100 : 0;
        var col = Theme.ChgColor(chg);
        string[] lines =
        {
            "开盘  " + c.O.ToString("F" + m.Decimals),
            "最高  " + c.H.ToString("F" + m.Decimals),
            "最低  " + c.L.ToString("F" + m.Decimals),
            "收盘  " + c.C.ToString("F" + m.Decimals),
            "涨跌  " + (chg >= 0 ? "+" : "") + chg.ToString("F" + m.Decimals) +
                "  (" + (chgPct >= 0 ? "+" : "") + chgPct.ToString("0.00") + "%)",
        };
        int boxW = P(158), boxH = P(97);
        int bx = (int)cx + P(16);
        if (bx + boxW > _plot.Right - P(4)) bx = (int)cx - boxW - P(16);
        int by = (int)my + P(10);
        if (by + boxH > _plot.Bottom) by = (int)my - boxH - P(10);
        var box = new Rectangle(bx, by, boxW, boxH);
        using (var sh = new SolidBrush(Color.FromArgb(80, 0, 0, 0)))
            g.FillRectangle(sh, box.X + P(2), box.Y + P(2), box.Width, box.Height);
        Theme.RoundedFill(g, box, 3f, Theme.ChartTag);
        using (var pen = new Pen(Theme.Line2, 1f)) g.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);
        Theme.Text(g, tt + " · " + m.Name, Theme.UiXS, Theme.TextDim,
            new Rectangle(box.X + P(9), box.Y + P(5), box.Width - P(14), P(15)), Theme.AL);
        for (int i = 0; i < lines.Length; i++)
        {
            var lc = i == 4 ? col : Theme.Txt;
            Theme.Text(g, lines[i], Theme.NumXXS, lc,
                new Rectangle(box.X + P(9), box.Y + P(22) + P(14) * i, box.Width - P(14), P(13)), Theme.AL);
        }
    }

    private string TimeText(long tickMs)
    {
        long now = Environment.TickCount64;
        var t = DateTime.Now.AddMilliseconds(tickMs - now);
        return St.IntervalMs >= 1000 ? t.ToString("HH:mm:ss") : t.ToString("HH:mm:ss.f");
    }

    public override void MouseMove(Point p)
    {
        var l = Local(p);
        bool inside = _plot.Contains(l) || _vol.Contains(l);
        if (inside)
        {
            _mouse = l;
            _inPlot = true;
            F.Invalidate(Bounds);
        }
        else if (_inPlot)
        {
            _inPlot = false;
            F.Invalidate(Bounds);
        }
    }

    public override void MouseLeave()
    {
        if (_inPlot) { _inPlot = false; F.Invalidate(Bounds); }
    }

    public override Cursor CursorAt(Point p) => Cursors.Cross;
}
