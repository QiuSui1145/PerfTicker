using System.Drawing.Drawing2D;

namespace PerfTicker;

/// <summary>底部「个股列表」：进程行情表，支持排序、搜索、置顶、右键全抛、平滑滚动。</summary>
internal sealed class ProcPanel : Panel
{
    private const float HeaderH = 34f;
    private const float ColH = 22f;
    private const float RowH = 22f;
    private readonly string[] SortNames = { "CPU", "GPU", "内存", "线程" };
    private readonly Rectangle[] _sortChips = new Rectangle[4];
    private Rectangle _searchBox, _searchClear;
    private int _hoverChip = -1;
    private float _scrollAnim;

    private float NameX => 92f;
    private float SparkW => Math.Clamp(LogicalW * 0.17f, 130f, 260f);

    public override bool Advance()
    {
        float target = St.ProcScroll;
        if (Math.Abs(_scrollAnim - target) < 0.01f)
        {
            if (_scrollAnim != target) { _scrollAnim = target; return true; }
            return false;
        }
        _scrollAnim += (target - _scrollAnim) * 0.38f;
        if (Math.Abs(target - _scrollAnim) < 0.02f) _scrollAnim = target;
        return true;
    }

    private (float cpu, float chg, float gpu, float mem, float thr, float hnd, float spark) Cols()
    {
        float w = LogicalW;
        float spark = w - 12 - SparkW;
        float hnd = spark - 10 - 60;
        float thr = hnd - 48 - 10;
        float mem = thr - 60 - 10;
        float gpu = mem - 78 - 10;
        float chg = gpu - 58 - 8;
        float cpu = chg - 54 - 8;
        return (cpu, chg, gpu, mem, thr, hnd, spark);
    }

    private List<ProcSample> _viewCache;
    private long _viewTick = -1;
    private string _viewFilter;
    private int _viewPins = -1;

    /// <summary>过滤 + 置顶 + 排序后的可见进程（按采样号缓存）。</summary>
    public List<ProcSample> View()
    {
        long tick = St.Snap.Tick;
        if (_viewCache != null && _viewTick == tick && _viewFilter == St.ProcFilter && _viewPins == St.Pinned.Count)
            return _viewCache;

        string f = St.ProcFilter;
        var list = new List<ProcSample>(St.Procs.Count);
        foreach (var p in St.Procs)
        {
            if (f.Length > 0)
            {
                bool hit = p.Name.Contains(f, StringComparison.OrdinalIgnoreCase)
                           || p.Pid.ToString().Contains(f, StringComparison.Ordinal);
                if (!hit) continue;
            }
            list.Add(p);
        }
        if (St.Pinned.Count > 0)
        {
            list.Sort((a, b) =>
            {
                bool pa = St.Pinned.Contains(a.Name), pb = St.Pinned.Contains(b.Name);
                if (pa != pb) return pa ? -1 : 1;
                return 0;
            });
        }
        _viewCache = list;
        _viewTick = tick;
        _viewFilter = f;
        _viewPins = St.Pinned.Count;
        return list;
    }

    public override void Paint(Graphics g, Rectangle clip)
    {
        Begin(g);
        float w = LogicalW, h = LogicalH;
        Theme.Fill(g, new Rectangle(0, 0, Bounds.Width, Bounds.Height), Theme.Panel);
        var (cx, cg, cgp, cm, ct, ch, csp) = Cols();
        var procs = View();

        // ── 头部 ──
        var head = Rc(0, 0, w, HeaderH);
        Theme.VGrad(g, head, Theme.HeadTop, Theme.HeadBot);
        using (var pen = new Pen(Theme.Line2))
        {
            g.DrawLine(pen, 0, 0, Bounds.Width, 0);
            g.DrawLine(pen, 0, head.Bottom - 1, Bounds.Width, head.Bottom - 1);
        }
        Theme.Text(g, "个股列表", Theme.UiL, Theme.TextHi, new Rectangle(P(12), 0, P(80), P(HeaderH)), Theme.AL);
        Theme.Text(g, "进程", Theme.UiXS, Theme.GoldDim, new Rectangle(P(78), 0, P(40), P(HeaderH)), Theme.AL);
        string cnt = St.ProcFilter.Length > 0
            ? $"筛选 {procs.Count} / {St.Procs.Count}"
            : $"{St.Procs.Count} 只 · 总内存 {St.Snap.MemUsedGB:0.0}G";
        Theme.Text(g, cnt, Theme.UiXS, St.ProcFilter.Length > 0 ? Theme.Gold : Theme.TextFade,
            new Rectangle(P(112), 0, P(200), P(HeaderH)), Theme.AL);

        // 搜索框
        float searchX = Math.Min(328f, Math.Max(240f, w - 640f));
        _searchBox = Rc(searchX, 7, 210, 20);
        bool active = St.SearchActive;
        Theme.RoundedRect(g, _searchBox, 3f,
            active ? Theme.SearchBg : Theme.SearchBg2,
            active ? Theme.Gold : Theme.Line2, 1f);
        // 放大镜
        int mx = _searchBox.X + P(8), my = _searchBox.Y + P(10);
        using (var pen = new Pen(active ? Theme.Gold : Theme.TextFade, Math.Max(1f, 1.2f * Theme.S)))
        {
            g.DrawEllipse(pen, mx, my - P(3), P(7), P(7));
            g.DrawLine(pen, mx + P(6), my + P(3), mx + P(10), my + P(7));
        }
        string shown = St.ProcFilter.Length > 0 ? St.ProcFilter : "搜索进程 / PID";
        Theme.Text(g, shown, Theme.UiXS, St.ProcFilter.Length > 0 ? Theme.TextHi : Theme.TextFade,
            new Rectangle(_searchBox.X + P(24), _searchBox.Y, _searchBox.Width - P(44), _searchBox.Height), Theme.AL);
        if (active && (St.Frame % 1.0) < 0.55)
        {
            int tw = Theme.Measure(g, St.ProcFilter, Theme.UiXS);
            int cxp = _searchBox.X + P(25) + tw;
            Theme.Fill(g, new Rectangle(cxp, _searchBox.Y + P(4), Math.Max(1, P(1)), _searchBox.Height - P(8)), Theme.Gold);
        }
        if (St.ProcFilter.Length > 0)
        {
            _searchClear = new Rectangle(_searchBox.Right - P(18), _searchBox.Y + P(5), P(12), P(12));
            Theme.Text(g, "×", Theme.UiXS, Theme.TextDim, _searchClear, Theme.AC);
        }
        else _searchClear = Rectangle.Empty;

        float sx = w - 4;
        for (int i = 3; i >= 0; i--)
        {
            float cw2 = 46;
            _sortChips[i] = Rc(sx - cw2, 7, cw2 - 4, 20);
            sx -= cw2;
            Chip(g, _sortChips[i], SortNames[i], St.ProcSort == i, _hoverChip == i, Theme.Gold, Theme.UiXS);
        }
        Theme.Text(g, "排序", Theme.UiXS, Theme.TextFade, Rc(sx - 34, 0, 30, HeaderH), Theme.AR);

        // ── 列头 ──
        float y = HeaderH;
        var ch2 = Rc(0, y, w, ColH);
        Theme.Fill(g, ch2, Theme.HeadBot2);
        using (var pen = new Pen(Theme.Line)) g.DrawLine(pen, 0, ch2.Bottom - 1, Bounds.Width, ch2.Bottom - 1);
        var hf = Theme.UiXS;
        Theme.Text(g, "代码", hf, Theme.TextFade, new Rectangle(P(12), ch2.Y, P(60), ch2.Height), Theme.AL);
        Theme.Text(g, "名称 / 进程", hf, Theme.TextFade, new Rectangle(P(NameX), ch2.Y, P(120), ch2.Height), Theme.AL);
        Theme.Text(g, "现价 CPU%", hf, Theme.TextFade, new Rectangle((int)cx - P(64), ch2.Y, P(64), ch2.Height), Theme.AR);
        Theme.Text(g, "涨跌", hf, Theme.TextFade, new Rectangle((int)cg - P(54), ch2.Y, P(54), ch2.Height), Theme.AR);
        Theme.Text(g, "GPU%", hf, Theme.TextFade, new Rectangle((int)cgp - P(58), ch2.Y, P(58), ch2.Height), Theme.AR);
        Theme.Text(g, "内存", hf, Theme.TextFade, new Rectangle((int)cm - P(78), ch2.Y, P(78), ch2.Height), Theme.AR);
        Theme.Text(g, "线程", hf, Theme.TextFade, new Rectangle((int)ct - P(50), ch2.Y, P(50), ch2.Height), Theme.AR);
        Theme.Text(g, "句柄", hf, Theme.TextFade, new Rectangle((int)ch - P(58), ch2.Y, P(58), ch2.Height), Theme.AR);
        Theme.Text(g, "K线走势", hf, Theme.TextFade, new Rectangle((int)csp, ch2.Y, P(SparkW), ch2.Height), Theme.AL);
        y += ColH;

        // ── 行 ──
        int rows = Math.Max(1, (int)((LogicalH - HeaderH - ColH - 2) / RowH));
        int maxScroll = Math.Max(0, procs.Count - rows);
        St.ProcScroll = Math.Clamp(St.ProcScroll, 0, maxScroll);
        if (_scrollAnim > maxScroll) _scrollAnim = maxScroll;

        double maxCpu = 1;
        for (int i = St.ProcScroll; i < Math.Min(procs.Count, St.ProcScroll + rows + 1); i++)
            if (procs[i].CpuPct > maxCpu) maxCpu = procs[i].CpuPct;

        int firstRow = Math.Max(0, (int)Math.Floor(_scrollAnim) - 1);
        for (int i = firstRow; i < procs.Count; i++)
        {
            float yy = HeaderH + ColH + (i - _scrollAnim) * RowH;
            var rr = Rc(0, yy, w, RowH);
            if (rr.Bottom < P(HeaderH + ColH)) continue;
            if (rr.Y > Bounds.Height) break;

            int idx = i;
            var p = procs[idx];
            bool hovered = idx == St.HoverProc && _hoverChip < 0;
            if (idx == St.SelectedProc)
            {
                Theme.Fill(g, rr, Theme.RowSel);
                Theme.Fill(g, new Rectangle(rr.X, rr.Y, P(2.5f), rr.Height), Theme.Gold);
            }
            else if (hovered) Theme.Fill(g, rr, Theme.RowHover);
            else if (idx % 2 == 1) Theme.Fill(g, rr, Theme.Stripe);

            Theme.Text(g, p.Pid.ToString(), Theme.MockMono, Theme.TextFade,
                new Rectangle(P(12), rr.Y, P(58), rr.Height), Theme.AL);

            bool pinned = St.Pinned.Contains(p.Name);
            float nx = NameX;
            if (pinned)
            {
                var star = new Rectangle(P(74), rr.Y + P(6), P(10), P(10));
                Theme.Text(g, "★", Theme.UiXS, Theme.Gold, star, Theme.AC);
            }
            Theme.TextEllipsis(g, p.Name, Theme.UiS, pinned ? Theme.GoldSoft : Theme.TxtHi2,
                new Rectangle(P(nx), rr.Y, (int)cx - P(78) - P(nx), rr.Height), Theme.AL);

            double frac = Math.Clamp(p.CpuPct / maxCpu, 0, 1);
            if (frac > 0.001)
            {
                var bar = new Rectangle((int)cx - P(64), rr.Y + P(4), (int)(P(64) * frac), P(14));
                using var lg = new LinearGradientBrush(new Rectangle(bar.X, bar.Y, Math.Max(4, bar.Width), bar.Height),
                    Color.FromArgb(30, Theme.Up), Color.FromArgb(1, Theme.Up), 0f);
                g.FillRectangle(lg, bar);
            }
            var cc = p.CpuPct >= 40 ? Theme.Up : p.CpuPct >= 8 ? Theme.GoldSoft : Theme.Txt;
            Theme.Text(g, p.CpuPct.ToString("0.0"), Theme.NumXS, cc, new Rectangle((int)cx - P(64), rr.Y, P(60), rr.Height), Theme.AR);

            string d = (p.CpuDelta >= 0 ? "+" : "") + p.CpuDelta.ToString("0.0");
            var dc = Theme.ChgColor(p.CpuDelta);
            Theme.Text(g, Math.Abs(p.CpuDelta) < 0.05 ? "—" : d, Theme.NumXXS,
                Math.Abs(p.CpuDelta) < 0.05 ? Theme.TextFade : dc,
                new Rectangle((int)cg - P(54), rr.Y, P(50), rr.Height), Theme.AR);

            Theme.Text(g, p.GpuPct > 0.05 ? p.GpuPct.ToString("0.0") : "—", Theme.NumXXS,
                p.GpuPct > 30 ? Theme.Purple : Theme.Purple,
                new Rectangle((int)cgp - P(58), rr.Y, P(54), rr.Height), Theme.AR);

            string mem = p.MemMB >= 1024 ? (p.MemMB / 1024).ToString("0.00") + "G" : p.MemMB.ToString("0") + "M";
            Theme.Text(g, mem, Theme.NumXXS, Theme.TextDim,
                new Rectangle((int)cm - P(78), rr.Y, P(74), rr.Height), Theme.AR);
            Theme.Text(g, p.Threads.ToString(), Theme.NumXXS, Theme.TextDim,
                new Rectangle((int)ct - P(50), rr.Y, P(46), rr.Height), Theme.AR);
            Theme.Text(g, p.Handles.ToString(), Theme.NumXXS, Theme.TextDim,
                new Rectangle((int)ch - P(58), rr.Y, P(54), rr.Height), Theme.AR);

            if (St.ProcHist.TryGetValue(p.Pid, out var hist) && hist.Len > 2)
                Theme.MiniCandles(g, new Rectangle((int)csp, rr.Y + P(4), P(SparkW), P(14)), hist.Snapshot(), 10);
        }

        // 滚动条
        if (procs.Count > rows)
        {
            float trackY = HeaderH + ColH;
            float trackH = LogicalH - trackY - 2;
            float thumbH = Math.Max(20, trackH * rows / procs.Count);
            float t = maxScroll == 0 ? 0 : (float)St.ProcScroll / maxScroll;
            float ty = trackY + t * (trackH - thumbH);
            Theme.RoundedFill(g, new Rectangle(Bounds.Width - P(5), (int)ty, P(3), (int)thumbH), 1.5f, Theme.Line2);
        }

        using (var pen = new Pen(Theme.Line2)) g.DrawLine(pen, Bounds.Width - 1, 0, Bounds.Width - 1, Bounds.Height);
        End(g);
    }

    private int RowAtLocal(Point l)
    {
        int topPx = P(HeaderH + ColH);
        if (l.Y < topPx) return -1;
        int i = (int)((l.Y - topPx) / P(RowH) + _scrollAnim);
        return i >= 0 && i < View().Count ? i : -1;
    }

    public override void MouseMove(Point p)
    {
        var l = Local(p);
        int h2 = RowAtLocal(l);
        int chip = -1;
        for (int i = 0; i < 4; i++) if (_sortChips[i].Contains(l)) chip = i;
        if (h2 != St.HoverProc || chip != _hoverChip)
        {
            St.HoverProc = h2;
            _hoverChip = chip;
            F.Invalidate(Bounds);
        }
    }

    public override void MouseLeave()
    {
        if (St.HoverProc != -1 || _hoverChip != -1)
        {
            St.HoverProc = -1;
            _hoverChip = -1;
            F.Invalidate(Bounds);
        }
    }

    public override Cursor CursorAt(Point p)
    {
        var l = Local(p);
        if (_searchBox.Contains(l)) return Cursors.IBeam;
        for (int i = 0; i < 4; i++) if (_sortChips[i].Contains(l)) return Cursors.Hand;
        return RowAtLocal(l) >= 0 ? Cursors.Hand : Cursors.Default;
    }

    public override bool Click(Point p, MouseButtons b)
    {
        var l = Local(p);
        if (_searchBox.Contains(l))
        {
            if (_searchClear != Rectangle.Empty && _searchClear.Contains(l)) F.SetProcFilter("");
            else F.SetSearchActive(true);
            return true;
        }
        for (int i = 0; i < 4; i++)
            if (_sortChips[i].Contains(l)) { F.SetProcSort(i); return true; }

        int idx = RowAtLocal(l);
        if (idx >= 0)
        {
            var view = View();
            if (idx < view.Count)
            {
                St.SelectedProc = idx;
                F.Invalidate(Bounds);
            }
            return true;
        }
        return false;
    }

    public override void RightClick(Point p)
    {
        var l = Local(p);
        int idx = RowAtLocal(l);
        if (idx < 0) return;
        var view = View();
        if (idx >= view.Count) return;
        St.SelectedProc = idx;
        F.Invalidate(Bounds);
        F.ShowProcMenu(view[idx].Pid, p);
    }

    public override void Wheel(Point p, int delta)
    {
        int rows = Math.Max(1, (int)((LogicalH - HeaderH - ColH - 2) / RowH));
        int max = Math.Max(0, View().Count - rows);
        St.ProcScroll = Math.Clamp(St.ProcScroll - delta / 120 * 3, 0, max);
        F.Invalidate(Bounds);
    }
}
