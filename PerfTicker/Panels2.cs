using System.Drawing.Drawing2D;

namespace PerfTicker;

/// <summary>右侧「主力动向」：按当前品种维度排出前六大进程，带深度条。</summary>
internal sealed class BookPanel : Panel
{
    private const float HeaderH = 30f;
    private const float RowH = 26f;
    private int RowCount => Math.Clamp((int)((LogicalH - HeaderH - 26) / RowH), 4, 10);
    private int _hover = -1;
    private readonly List<ProcSample> _rank = new();

    private string Title
    {
        get
        {
            var m = St.Selected;
            if (m.Group == "图形") return "GPU 持仓";
            if (m.Name.Contains("内存") || m.Name.Contains("提交")) return "内存持仓";
            if (m.Name.Contains("磁盘")) return "磁盘持仓";
            return "CPU 持仓";
        }
    }

    private void BuildRank()
    {
        _rank.Clear();
        var m = St.Selected;
        var procs = St.Procs;
        if (m.Group == "图形")
            _rank.AddRange(procs.Where(p => p.GpuPct > 0.05).OrderByDescending(p => p.GpuPct).Take(RowCount));
        else if (m.Name.Contains("内存") || m.Name.Contains("提交"))
            _rank.AddRange(procs.OrderByDescending(p => p.MemMB).Take(RowCount));
        else if (m.Name.Contains("磁盘"))
            _rank.AddRange(procs.OrderByDescending(p => p.CpuPct).Take(RowCount));
        else
            _rank.AddRange(procs.OrderByDescending(p => p.CpuPct).Take(RowCount));
    }

    private double ValOf(ProcSample p)
    {
        var m = St.Selected;
        if (m.Group == "图形") return p.GpuPct;
        if (m.Name.Contains("内存") || m.Name.Contains("提交")) return p.MemMB;
        return p.CpuPct;
    }

    private string FmtVal(ProcSample p)
    {
        var m = St.Selected;
        if (m.Group == "图形") return p.GpuPct.ToString("0.0") + "%";
        if (m.Name.Contains("内存") || m.Name.Contains("提交")) return p.MemMB >= 1024 ? (p.MemMB / 1024).ToString("0.00") + "G" : p.MemMB.ToString("0") + "M";
        return p.CpuPct.ToString("0.0") + "%";
    }

    public override void Paint(Graphics g, Rectangle clip)
    {
        Begin(g);
        float w = LogicalW, h = LogicalH;
        BuildRank();
        Theme.Fill(g, new Rectangle(0, 0, Bounds.Width, Bounds.Height), Theme.Panel);

        var head = Rc(0, 0, w, HeaderH);
        Theme.VGrad(g, head, Theme.HeadTop, Theme.HeadBot);
        using (var pen = new Pen(Theme.Line2)) g.DrawLine(pen, 0, head.Bottom - 1, Bounds.Width, head.Bottom - 1);
        Theme.Fill(g, Rc(0, 0, 2.5f, HeaderH), Theme.Gold);
        Theme.Text(g, "主力动向", Theme.UiL, Theme.TextHi, new Rectangle(P(12), 0, P(90), P(HeaderH)), Theme.AL);
        Theme.Text(g, Title, Theme.UiXS, Theme.GoldDim, new Rectangle(P(78), 0, P(120), P(HeaderH)), Theme.AL);
        Theme.Text(g, "TOP " + RowCount, Theme.BrandSub, Theme.TextFade, new Rectangle(Bounds.Width - P(60), 0, P(48), P(HeaderH)), Theme.AR);

        float y = HeaderH;
        double max = 1e-9;
        foreach (var p in _rank) max = Math.Max(max, ValOf(p));

        for (int i = 0; i < RowCount; i++)
        {
            var rr = Rc(0, y, w, RowH);
            if (i < _rank.Count)
            {
                var p = _rank[i];
                double v = ValOf(p);
                double frac = Math.Clamp(v / max, 0, 1);

                // 热度底：按占比铺满整行
                if (frac > 0.01)
                {
                    int bw2 = Math.Max(2, (int)((rr.Width - P(20)) * frac));
                    int hx = rr.Right - P(10) - bw2;
                    using var heat = new LinearGradientBrush(new Rectangle(hx, rr.Y, bw2, rr.Height),
                        Color.FromArgb(2, Theme.Up), Color.FromArgb(34, Theme.Up), 0f);
                    g.FillRectangle(heat, new Rectangle(hx, rr.Y + P(2), bw2, rr.Height - P(4)));
                }
                if (i == _hover) Theme.Fill(g, rr, Theme.RowHover);

                // 名次徽标
                var badge = Rc(10, y + (RowH - 15) / 2, 15, 15);
                bool top = i == 0;
                Theme.RoundedRect(g, badge, 2.5f,
                    top ? Color.FromArgb(0xE8, 0xF2, 0xC1, 0x4E) : Theme.PanelSoft,
                    top ? Theme.Gold : Theme.Line2, 1f);
                Theme.Text(g, (i + 1).ToString(), Theme.NumXXS,
                    top ? Theme.DarkInk : Theme.TextDim, badge, Theme.AC);

                Theme.TextEllipsis(g, p.Name, Theme.UiS, Theme.TxtHi2,
                    new Rectangle(rr.X + P(32), rr.Y, rr.Width - P(32) - P(74), rr.Height), Theme.AL);

                Theme.Text(g, FmtVal(p), Theme.NumXS, Theme.Up, new Rectangle(rr.Right - P(72), rr.Y, P(62), rr.Height), Theme.AR);
            }
            else
            {
                Theme.Text(g, "—", Theme.UiXS, Theme.TextFade, new Rectangle(rr.Right - P(72), rr.Y, P(62), rr.Height), Theme.AR);
            }
            if (i < RowCount - 1) Theme.HLine(g, P(10), Bounds.Width - P(10), rr.Bottom, Theme.Line);
            y += RowH;
        }

        // 汇总
        var sumR = Rc(0, y, w, RowH + 4);
        Theme.Fill(g, sumR, Theme.Stripe);
        using (var pen = new Pen(Theme.Line)) g.DrawLine(pen, 0, sumR.Y, Bounds.Width, sumR.Y);
        double sum = 0;
        foreach (var p in _rank) sum += ValOf(p);
        string dim = St.Selected.Group == "图形" ? "" : "%";
        string total = St.Selected.Group == "图形" || St.Selected.Name.Contains("内存") ? "" : "%";
        Theme.Text(g, "合计", Theme.UiXS, Theme.TextDim, new Rectangle(P(12), sumR.Y, P(40), sumR.Height), Theme.AL);
        Theme.Text(g, sum.ToString("0.0") + total, Theme.NumXS, Theme.Gold, new Rectangle(P(46), sumR.Y, P(120), sumR.Height), Theme.AL);
        Theme.Text(g, $"{St.Procs.Count} 只", Theme.UiXS, Theme.TextFade, new Rectangle(Bounds.Width - P(70), sumR.Y, P(58), sumR.Height), Theme.AR);
        End(g);
    }

    public override void MouseMove(Point p)
    {
        var l = Local(p);
        int hh = -1;
        float y = HeaderH;
        for (int i = 0; i < RowCount; i++)
        {
            if (Rc(0, y, LogicalW, RowH).Contains(l)) hh = i;
            y += RowH;
        }
        if (hh != _hover) { _hover = hh; F.Invalidate(Bounds); }
    }

    public override void MouseLeave()
    {
        if (_hover != -1) { _hover = -1; F.Invalidate(Bounds); }
    }
}

/// <summary>右侧「逐笔成交」：把进程启停、频率跳变、告警做成交易所逐笔流水。</summary>
internal sealed class TapePanel : Panel
{
    private const float HeaderH = 30f;
    private const float RowH = 18.5f;

    public override void Paint(Graphics g, Rectangle clip)
    {
        Begin(g);
        float w = LogicalW, h = LogicalH;
        Theme.Fill(g, new Rectangle(0, 0, Bounds.Width, Bounds.Height), Theme.Panel);

        var head = Rc(0, 0, w, HeaderH);
        Theme.VGrad(g, head, Theme.HeadTop, Theme.HeadBot);
        using (var pen = new Pen(Theme.Line2)) g.DrawLine(pen, 0, head.Bottom - 1, Bounds.Width, head.Bottom - 1);
        Theme.Fill(g, Rc(0, 0, 2.5f, HeaderH), Color.FromArgb(0x4D, 0x8D, 0xFF));
        Theme.Text(g, "逐笔成交", Theme.UiL, Theme.TextHi, new Rectangle(P(12), 0, P(90), P(HeaderH)), Theme.AL);
        Theme.Text(g, "LIVE TAPE", Theme.BrandSub, Theme.TextFade, new Rectangle(P(78), 0, P(90), P(HeaderH)), Theme.AL);
        Theme.Text(g, St.Tape.Count.ToString(), Theme.NumXXS, Theme.TextDim, new Rectangle(Bounds.Width - P(50), 0, P(38), P(HeaderH)), Theme.AR);

        float y = HeaderH + P(4);
        int maxRows = (int)((LogicalH - HeaderH - 8) / RowH);
        int n = Math.Min(maxRows, St.Tape.Count);
        for (int i = 0; i < n; i++)
        {
            var e = St.Tape[St.Tape.Count - 1 - i];
            var rr = Rc(0, y, w, RowH);
            Theme.Text(g, e.Time, Theme.MockMono, Theme.TextFade, new Rectangle(P(12), rr.Y, P(52), rr.Height), Theme.AL);
            using (var b = new SolidBrush(e.Color)) g.FillEllipse(b, P(66), rr.Y + P(7), P(4), P(4));
            Theme.TextEllipsis(g, e.Text, Theme.UiXS, Theme.TxtHi2,
                new Rectangle(P(76), rr.Y, rr.Width - P(88), rr.Height), Theme.AL);
            y += P(RowH);
        }
        if (St.Tape.Count == 0)
        {
            Theme.Text(g, "等待行情接入…", Theme.UiS, Theme.TextFade, Rc(0, HeaderH, w, 40), Theme.AC);
        }
        End(g);
    }
}

/// <summary>底部状态条 + 行情跑马灯。</summary>
internal sealed class FooterPanel : Panel
{
    private readonly List<(string text, Color color, int width)> _segs = new();
    private long _segsTick = -1;
    public Rectangle AboutRect;
    private bool _aboutHover;

    public override void Paint(Graphics g, Rectangle clip)
    {
        Begin(g);
        int Wp = Bounds.Width, Hp = Bounds.Height;
        var bgTop = Theme.HeadTop;
        var bgBot = Theme.ChartBg2;
        Theme.VGrad(g, new Rectangle(0, 0, Wp, Hp), bgTop, bgBot);

        // 先测量左侧状态块宽度（不绘制），据此确定跑马灯可用区间
        int x = P(12);
        int statEnd;
        {
            void M(string label, string val)
            {
                x += Theme.Measure(g, label, Theme.UiXS) + P(5) + Theme.Measure(g, val, Theme.NumXS) + P(20);
            }
            var s0 = St.Snap;
            long up0 = (long)(DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime).TotalSeconds;
            M("采样", St.Paused ? "已暂停" : (St.IntervalMs / 1000.0).ToString("0.0") + "s");
            M("运行", $"{up0 / 3600:00}:{up0 % 3600 / 60:00}:{up0 % 60:00}");
            M("进程", s0.ProcCount.ToString());
            M("线程", s0.Threads.ToString());
            M("句柄", s0.Handles >= 100000 ? (s0.Handles / 1024).ToString() + "K" : s0.Handles.ToString());
            statEnd = x + P(12);
        }

        int mqLeft = statEnd + P(12);
        int marqueeRight = Wp - P(268);

        if (_segsTick != St.Snap.Tick)
        {
            BuildSegments(g);
            _segsTick = St.Snap.Tick;
        }

        // 跑马灯：先画（允许溢出），随后用底色盖住两侧，避免 GDI 文本不受裁剪影响
        int total = 0;
        foreach (var seg in _segs) total += seg.width;
        if (!St.Marquee)
        {
            Theme.Text(g, "滚动信息已关闭 · 设置中可重新开启", Theme.UiXS, Theme.TextFade,
                new Rectangle(mqLeft, 0, Math.Max(0, marqueeRight - mqLeft), Hp), Theme.AL);
            total = 0;
        }
        if (total > 8 && marqueeRight - mqLeft > P(60))
        {
            float offset = (float)((St.Frame * 52 * Theme.S) % total);
            float sx = mqLeft - offset;
            for (int pass = 0; pass < 2 && sx < marqueeRight; pass++)
            {
                foreach (var seg in _segs)
                {
                    if (sx >= mqLeft - P(2) && sx < marqueeRight)
                        Theme.Text(g, seg.text, Theme.UiXS, seg.color,
                            new Rectangle((int)sx, 0, seg.width + P(6), Hp), Theme.AL);
                    sx += seg.width;
                }
            }
        }

        // 盖住左侧状态区与右侧提示区（GDI 文本不参与 GDI+ 裁剪）
        Theme.VGrad(g, new Rectangle(0, 0, Math.Max(0, mqLeft - P(1)), Hp), bgTop, bgBot);
        Theme.VGrad(g, new Rectangle(Math.Min(Wp, marqueeRight), 0, Math.Max(0, Wp - marqueeRight), Hp), bgTop, bgBot);

        // 左侧状态
        x = P(12);
        void Stat(string label, string val, Color c)
        {
            int lw = Theme.Measure(g, label, Theme.UiXS);
            Theme.Text(g, label, Theme.UiXS, Theme.TextFade, new Rectangle(x, 0, lw + P(4), Hp), Theme.AL);
            x += lw + P(5);
            int vw = Theme.Measure(g, val, Theme.NumXS);
            Theme.Text(g, val, Theme.NumXS, c, new Rectangle(x, 0, vw + P(4), Hp), Theme.AL);
            x += vw + P(20);
        }

        var s = St.Snap;
        Stat("采样", St.Paused ? "已暂停" : (St.IntervalMs / 1000.0).ToString("0.0") + "s", St.Paused ? Theme.Warn : Theme.Down);
        long up = (long)(DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime).TotalSeconds;
        Stat("运行", $"{up / 3600:00}:{up % 3600 / 60:00}:{up % 60:00}", Theme.Txt);
        Stat("进程", s.ProcCount.ToString(), Theme.Txt);
        Stat("线程", s.Threads.ToString(), Theme.Txt);
        Stat("句柄", s.Handles >= 100000 ? (s.Handles / 1024).ToString() + "K" : s.Handles.ToString(), Theme.Txt);

        Theme.VLine(g, statEnd, P(8), Hp - P(8), Theme.Line2);

        // 跑马灯两侧渐隐
        if (total > 8 && marqueeRight - mqLeft > P(60))
        {
            using (var lg = new LinearGradientBrush(new RectangleF(mqLeft, 0, P(22), Hp),
                       Color.FromArgb(255, 0x0E, 0x12, 0x19), Color.FromArgb(0, 0x0E, 0x12, 0x19), 0f))
                g.FillRectangle(lg, new RectangleF(mqLeft, 0, P(22), Hp));
            using (var lg = new LinearGradientBrush(new RectangleF(marqueeRight - P(22), 0, P(22), Hp),
                       Color.FromArgb(0, 0x0E, 0x12, 0x19), Color.FromArgb(255, 0x0E, 0x12, 0x19), 0f))
                g.FillRectangle(lg, new RectangleF(marqueeRight - P(22), 0, P(22), Hp));
        }

        // 关于入口
        int aboutW = P(58);
        AboutRect = new Rectangle(Wp - aboutW - P(12), P(3), aboutW, Hp - P(6));
        Theme.RoundedRect(g, AboutRect, 3f, _aboutHover ? Theme.PanelSoft : Theme.ChipBg, Theme.Line2, 1f);
        Theme.Text(g, "关于", Theme.UiXS, _aboutHover ? Theme.Gold : Theme.TextDim, AboutRect, Theme.AC);

        using (var pen = new Pen(Theme.Line2)) g.DrawLine(pen, 0, 0, Wp, 0);
        End(g);
    }

    public override void MouseMove(Point p)
    {
        bool h = AboutRect.Contains(Local(p));
        if (h != _aboutHover) { _aboutHover = h; F.Invalidate(Bounds); }
    }

    public override void MouseLeave()
    {
        if (_aboutHover) { _aboutHover = false; F.Invalidate(Bounds); }
    }

    public override Cursor CursorAt(Point p) => AboutRect.Contains(Local(p)) ? Cursors.Hand : Cursors.Default;

    public override bool Click(Point p, MouseButtons b)
    {
        if (!AboutRect.Contains(Local(p))) return false;
        F.ShowAbout();
        return true;
    }
    private void BuildSegments(Graphics g)
    {
        _segs.Clear();
        void Add(string t, Color c) => _segs.Add((t, c, Theme.Measure(g, t, Theme.UiXS)));
        foreach (var m in St.Metrics)
        {
            if (m.Group == "处理器") continue;
            Add(m.Name + " ", Theme.TextDim);
            var col = Theme.ChgColor(m.Chg);
            Add(m.Display, col);
            Add(" " + m.Unit + "  ", Theme.TextFade);
            Add((m.ChgPct >= 0 ? "▲" : "▼") + Math.Abs(m.ChgPct).ToString("0.00") + "%", col);
            Add("   ｜   ", Color.FromArgb(0x3A, 0x44, 0x55));
        }
    }
}
