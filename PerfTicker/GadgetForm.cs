using System.Drawing.Drawing2D;

namespace PerfTicker;

/// <summary>桌面小挂件：常驻桌面的迷你行情面板，双击标题打开主面板。</summary>
internal sealed class GadgetForm : Form
{
    private readonly AppForm _app;
    private readonly System.Windows.Forms.Timer _timer;
    private Rectangle _closeRect, _pinRect;
    private int _hover;
    private bool _dragging;
    private Point _dragStart;
    private bool _pinned;

    private const int W = 372, H = 214;

    public GadgetForm(AppForm app)
    {
        _app = app;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = false;
        BackColor = Theme.Panel;
        ClientSize = new Size(Theme.Px(W), Theme.Px(H));
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        DoubleBuffered = true;

        int gx = _app.SettingsRef.GadgetX, gy = _app.SettingsRef.GadgetY;
        var wa = Screen.PrimaryScreen.WorkingArea;
        if (gx < 0 || gy < 0 || gx > wa.Right - 100 || gy > wa.Bottom - 100)
        {
            gx = wa.Right - Width - Theme.Px(28);
            gy = wa.Bottom - Height - Theme.Px(28);
        }
        Location = new Point(gx, gy);

        _timer = new System.Windows.Forms.Timer { Interval = 500 };
        _timer.Tick += (s, e) => { Invalidate(); };
        _timer.Start();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00000080;   // WS_EX_TOOLWINDOW
            return cp;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.None;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        Theme.TX = 0; Theme.TY = 0;

        int w = ClientSize.Width, h = ClientSize.Height;
        using (var bg = new SolidBrush(Color.FromArgb(0xF2, Theme.Panel)))
            g.FillRectangle(bg, ClientRectangle);
        using (var line = new Pen(Theme.Line2, Math.Max(1f, Theme.S)))
            g.DrawRectangle(line, 0, 0, w - 1, h - 1);
        using (var gold = new SolidBrush(Theme.Gold))
            g.FillRectangle(gold, 0, 0, w, Math.Max(2, (int)(3 * Theme.S)));

        // ── 标题行 ──
        int headH = Theme.Px(30);
        Theme.Text(g, "▮ 性能行情", Theme.UiS, Theme.TextHi, new Rectangle(Theme.Px(12), 4, Theme.Px(90), headH), Theme.AL);
        Theme.Text(g, "挂件", Theme.UiXS, Theme.Gold, new Rectangle(Theme.Px(88), 4, Theme.Px(40), headH), Theme.AL);

        _pinRect = new Rectangle(w - Theme.Px(58), Theme.Px(6), Theme.Px(22), Theme.Px(18));
        _closeRect = new Rectangle(w - Theme.Px(30), Theme.Px(6), Theme.Px(22), Theme.Px(18));
        Theme.RoundedRect(g, _pinRect, 3f, _pinned ? Color.FromArgb(40, Theme.Cyan) : (_hover == 1 ? Theme.PanelSoft : Color.Transparent), Theme.Line2, 1f);
        Theme.Text(g, "顶", Theme.UiXS, _pinned ? Theme.Cyan : Theme.TextDim, _pinRect, Theme.AC);
        Theme.RoundedRect(g, _closeRect, 3f, _hover == 2 ? Theme.PanelSoft : Color.Transparent, Theme.Line2, 1f);
        Theme.Text(g, "✕", Theme.UiXS, _hover == 2 ? Theme.Up : Theme.TextDim, _closeRect, Theme.AC);

        var s = _app.St.Snap;
        double cpu = s.CpuPct, gpu = s.Gpu.Available ? s.Gpu.UtilPct : 0;
        double mem = s.MemPct;
        int y = Theme.Px(34);

        // ── CPU / GPU 大数字 ──
        int halfW = (w - Theme.Px(24)) / 2;
        Big(g, Theme.Px(12), y, halfW, "CPU", cpu.ToString("0.0") + "%",
            (s.CpuAvgMHz > 0 ? s.CpuAvgMHz.ToString("0") + " MHz" : ""), Theme.Gold);
        Big(g, Theme.Px(12) + halfW, y, halfW, "GPU", gpu.ToString("0.0") + "%",
            (s.Gpu.Available && s.Gpu.ClockMHz > 0 ? s.Gpu.ClockMHz.ToString("0") + " MHz" : "未检测"), Theme.Cyan);

        // ── 迷你走势 ──
        y += Theme.Px(58);
        int chartH = Theme.Px(46);
        var r = new Rectangle(Theme.Px(12), y, w - Theme.Px(24), chartH);
        Theme.Fill(g, r, Theme.PanelHi);
        using (var bp = new Pen(Theme.Line2, 1f)) g.DrawRectangle(bp, r.X, r.Y, r.Width - 1, r.Height - 1);
        var data = _app.St.Metrics.Count > 0 ? _app.St.Selected.Spark(40) : Array.Empty<double>();
        if (data.Length >= 2)
        {
            double lo = data.Min(), hi = data.Max();
            if (hi - lo < 1e-9) { hi += 1; lo -= 1; }
            var pts = new PointF[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                pts[i] = new PointF(
                    r.Left + (float)i / (data.Length - 1) * r.Width,
                    r.Bottom - (float)((data[i] - lo) / (hi - lo)) * (r.Height - Theme.Px(8)) - Theme.Px(4));
            }
            using var pen = new Pen(Theme.Gold, Math.Max(1f, 1.2f * Theme.S));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.DrawLines(pen, pts);
            g.SmoothingMode = SmoothingMode.None;
        }
        Theme.Text(g, _app.St.Metrics.Count > 0 ? _app.St.Selected.Name : "", Theme.UiXS, Theme.TextFade,
            new Rectangle(r.Left, r.Top + Theme.Px(3), r.Width - Theme.Px(8), Theme.Px(16)), Theme.AR);

        // ── 底部两行 ──
        y += chartH + Theme.Px(10);
        Small(g, Theme.Px(12), y, halfW, "内存", mem.ToString("0.0") + "%");
        Small(g, Theme.Px(12) + halfW, y, halfW, "温度",
            s.Gpu.Available && s.Gpu.TempC > 0 ? s.Gpu.TempC.ToString("0") + "°C" : "--");

        y += Theme.Px(22);
        var eco = _app.St.Economy;
        string total = (eco.Total >= 0 ? "+" : "") + eco.Total.ToString("N0");
        Theme.Text(g, "营收", Theme.UiXS, Theme.TextDim, new Rectangle(Theme.Px(12), y, Theme.Px(34), Theme.Px(18)), Theme.AL);
        Theme.Text(g, "¥ " + total, Theme.NumS, eco.Total >= 0 ? Theme.Up : Theme.Down,
            new Rectangle(Theme.Px(46), y - Theme.Px(2), Theme.Px(150), Theme.Px(20)), Theme.AL);
        Theme.Text(g, eco.Enabled ? eco.Rate.ToString("+0.0;-0.0") + " 元/秒" : "已关闭", Theme.UiXS,
            eco.Enabled ? Theme.TextFade : Theme.TextDim,
            new Rectangle(w - Theme.Px(120), y, Theme.Px(108), Theme.Px(18)), Theme.AR);
    }

    private static void Big(Graphics g, int x, int y, int w, string label, string value, string sub, Color accent)
    {
        Theme.Text(g, label, Theme.UiXS, Theme.TextDim, new Rectangle(x, y, w, Theme.Px(16)), Theme.AL);
        Theme.Text(g, value, Theme.NumL, accent, new Rectangle(x, y + Theme.Px(14), w, Theme.Px(28)), Theme.AL);
        if (!string.IsNullOrEmpty(sub))
            Theme.Text(g, sub, Theme.UiXS, Theme.TextFade, new Rectangle(x, y + Theme.Px(40), w, Theme.Px(14)), Theme.AL);
    }

    private static void Small(Graphics g, int x, int y, int w, string label, string value)
    {
        Theme.Text(g, label, Theme.UiXS, Theme.TextDim, new Rectangle(x, y, Theme.Px(34), Theme.Px(18)), Theme.AL);
        Theme.Text(g, value, Theme.UiS, Theme.Txt, new Rectangle(x + Theme.Px(36), y, w - Theme.Px(36), Theme.Px(18)), Theme.AL);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        if (_closeRect.Contains(e.Location)) { Close(); return; }
        if (_pinRect.Contains(e.Location))
        {
            _pinned = !_pinned;
            TopMost = _pinned;
            Invalidate();
            return;
        }
        _dragging = true;
        _dragStart = new Point(Cursor.Position.X - Location.X, Cursor.Position.Y - Location.Y);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            Location = new Point(Cursor.Position.X - _dragStart.X, Cursor.Position.Y - _dragStart.Y);
            return;
        }
        int h = _closeRect.Contains(e.Location) ? 2 : _pinRect.Contains(e.Location) ? 1 : 0;
        if (h != _hover) { _hover = h; Invalidate(); }
        Cursor = h > 0 ? Cursors.Hand : Cursors.SizeAll;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_dragging)
        {
            _dragging = false;
            _app.SettingsRef.GadgetX = Location.X;
            _app.SettingsRef.GadgetY = Location.Y;
            _app.SaveSettingsPublic();
        }
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button == MouseButtons.Left) _app.ShowMainFromGadget();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _timer.Stop();
        _timer.Dispose();
        _app.SettingsRef.GadgetX = Location.X;
        _app.SettingsRef.GadgetY = Location.Y;
        base.OnFormClosing(e);
    }

    private ContextMenuStrip _menu;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0205)   // WM_RBUTTONUP
        {
            _menu ??= BuildMenu();
            _menu.Show(this, PointToClient(Cursor.Position));
            return;
        }
        base.WndProc(ref m);
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip { ShowImageMargin = false };
        menu.Items.Add("打开主面板", null, (s, e) => _app.ShowMainFromGadget());
        menu.Items.Add(new ToolStripSeparator());
        var top = new ToolStripMenuItem("总在最前") { Checked = _pinned };
        top.Click += (s, e) => { _pinned = !_pinned; TopMost = _pinned; Invalidate(); };
        menu.Items.Add(top);
        menu.Items.Add("关闭挂件", null, (s, e) => Close());
        return menu;
    }
}
