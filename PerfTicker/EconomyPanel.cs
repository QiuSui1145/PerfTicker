using System.Drawing.Drawing2D;

namespace PerfTicker;

/// <summary>营收专区：把经济系统账本做成一条行情带（总营收 / 每秒净收 / 分项 / 走势）。</summary>
internal sealed class EconomyPanel : Panel
{
    private Rectangle _resetRect;
    private bool _resetHover;

    public override Cursor CursorAt(Point p) => _resetRect.Contains(Local(p)) ? Cursors.Hand : Cursors.Default;

    public override void MouseMove(Point p)
    {
        bool h = _resetRect.Contains(Local(p));
        if (h != _resetHover) { _resetHover = h; F.Invalidate(Bounds); }
    }

    public override void MouseLeave()
    {
        if (_resetHover) { _resetHover = false; F.Invalidate(Bounds); }
    }

    public override bool Click(Point p, MouseButtons b)
    {
        if (!_resetRect.Contains(Local(p))) return false;
        F.ResetEconomy();
        return true;
    }

    public override void Paint(Graphics g, Rectangle clip)
    {
        Begin(g);
        float w = LogicalW, h = LogicalH;
        var eco = St.Economy;

        Theme.VGrad(g, new Rectangle(0, 0, Bounds.Width, Bounds.Height),
            Theme.PanelHi, Theme.HeadBot);
        using (var pen = new Pen(Theme.Line2))
        {
            g.DrawLine(pen, 0, 0, Bounds.Width, 0);
            g.DrawLine(pen, 0, Bounds.Height - 1, Bounds.Width, Bounds.Height - 1);
        }
        Theme.Fill(g, new Rectangle(0, 0, Bounds.Width, P(2)), Color.FromArgb(0xB4, 0xF2, 0xC1, 0x4E));

        // ── 左侧：总营收 ──
        var cTot = eco.Total >= 0 ? Theme.Up : Theme.Down;
        Theme.Text(g, "营收中心", Theme.UiS, Theme.TextHi, Rc(14, 8, 76, 16), Theme.AL);
        Theme.Text(g, "REVENUE", Theme.BrandSub, Theme.TextFade, Rc(62, 9, 66, 14), Theme.AL);

        string sign = eco.Total >= 0 ? "+" : "-";
        Theme.Text(g, "¥", Theme.NumS, Theme.Gold, Rc(14, 30, 16, 20), Theme.AL);
        Theme.TextEllipsis(g, sign + Math.Abs(eco.Total).ToString("N2"), Theme.NumXL, cTot, Rc(26, 26, 186, 26), Theme.AL);

        var cRate = eco.Rate >= 0 ? Theme.Up : Theme.Down;
        Theme.Text(g, $"{(eco.Rate >= 0 ? "+" : "")}{eco.Rate:0.000}", Theme.NumS, cRate, Rc(14, 54, 92, 18), Theme.AL);
        Theme.Text(g, "元/s", Theme.UiXS, Theme.TextFade, Rc(88, 54, 34, 18), Theme.AL);
        Theme.Text(g, $"峰值 {(eco.PeakRate >= 0 ? "+" : "")}{eco.PeakRate:0.00}", Theme.NumXXS,
            Theme.TextDim, Rc(14, 74, 106, 14), Theme.AL);
        Theme.Text(g, $"{(int)(eco.Seconds / 60):00}:{(int)(eco.Seconds % 60):00}", Theme.NumXXS,
            Theme.TextFade, Rc(120, 74, 60, 14), Theme.AL);

        // 归零按钮
        _resetRect = Rc(166, 9, 40, 16);
        Theme.RoundedRect(g, _resetRect, 3f, _resetHover ? Color.FromArgb(46, Theme.Up) : Theme.ChipBg, _resetHover ? Theme.Up : Theme.Line2, 1f);
        Theme.Text(g, "归零", Theme.UiXS, _resetHover ? Theme.Up : Theme.TextDim, _resetRect, Theme.AC);

        Theme.VLine(g, P(212), P(10), P(h - 10), Theme.Line2);

        // ── 右侧：分项 + 走势（自适应宽度）──
        float avail = w - 226f;
        bool wide = avail >= 5 * 112f + 128f;
        float sparkW = wide ? 120f : 0f;
        float cellArea = avail - (wide ? sparkW + 16f : 0f);
        int parts = eco.Parts.Length;
        if (!wide)
        {
            parts = Math.Clamp((int)(cellArea / 98f), 2, eco.Parts.Length);
        }
        float cellW = Math.Min(170f, (cellArea - 6f * parts) / Math.Max(1, parts));
        float cx = 226;

        for (int i = 0; i < parts; i++)
        {
            var p = eco.Parts[i];
            float x0 = cx + i * cellW;
            if (x0 + cellW > w - 2) break;

            Theme.TextEllipsis(g, p.Name, Theme.UiXS, Theme.TextDim, Rc(x0, 10, cellW - 56, 14), Theme.AL);
            Theme.Text(g, p.LoadShown.ToString("0.0") + "%", Theme.NumXXS,
                p.LoadShown >= p.BreakEven ? Theme.Txt : Theme.TextFade,
                Rc(x0 + cellW - 60, 10, 48, 14), Theme.AR);

            var barBg = Rc(x0, 28, cellW - 12, 4);
            Theme.RoundedFill(g, barBg, 2f, Theme.Stripe2);
            double frac = Math.Clamp(p.LoadShown / 100.0, 0, 1);
            int bw = (int)(barBg.Width * frac);
            if (bw > 1)
            {
                using var lg = new LinearGradientBrush(new Rectangle(barBg.X, barBg.Y, Math.Max(4, barBg.Width), barBg.Height),
                    Color.FromArgb(0xE0, Theme.Gold), Color.FromArgb(0x66, Theme.Gold), 0f);
                g.FillRectangle(lg, new Rectangle(barBg.X, barBg.Y, bw, barBg.Height));
            }
            int beX = barBg.X + (int)(barBg.Width * (p.BreakEven / 100.0));
            Theme.Fill(g, new Rectangle(beX, barBg.Y - P(2), Math.Max(1, P(1)), barBg.Height + P(4)), Color.FromArgb(0x70, 0x81, 0x94));

            var c = p.Net >= 0 ? Theme.Up : Theme.Down;
            Theme.Text(g, $"{(p.Net >= 0 ? "+" : "")}{p.Net:0.000}", Theme.NumS, c, Rc(x0, 40, cellW - 34, 18), Theme.AL);
            Theme.Text(g, "元/s", Theme.UiXS, Theme.TextFade, Rc(x0 + cellW - 34, 40, 32, 18), Theme.AL);
            Theme.Text(g, p.LoadShown >= p.BreakEven ? "盈利" : "亏损", Theme.UiXS,
                p.LoadShown >= p.BreakEven ? Color.FromArgb(0xB4, Theme.Up) : Color.FromArgb(0xA0, Theme.Down),
                Rc(x0, 64, 40, 14), Theme.AL);
        }

        // ── 净收走势 ──
        if (wide && sparkW > 0)
        {
            float sparkX = w - sparkW - 14;
            Theme.Text(g, "净收走势", Theme.UiXS, Theme.TextFade, Rc(sparkX, 10, sparkW, 14), Theme.AL);
            var data = eco.RateHist;
            if (data.Count > 2)
                Theme.MiniCandles(g, Rc(sparkX, 26, sparkW, h - 44), data.ToArray(), 10);
            else
                Theme.Text(g, "采样中…", Theme.UiXS, Theme.TextFade, Rc(sparkX, 34, sparkW, 18), Theme.AL);
        }

        if (eco.TempPenalty < 0.999)
            Theme.Text(g, $"过热降效 {eco.TempPenalty:P0}", Theme.UiXS, Theme.Warn, Rc(120, 26, 90, 14), Theme.AR);

        End(g);
    }
}
