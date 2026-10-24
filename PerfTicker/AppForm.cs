using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace PerfTicker;

internal sealed class AppForm : Form
{
    public AppState St = new();
    public readonly Hardware Hw = new();

    private Sampler _sampler;
    private readonly List<Panel> _panels = new();
    private TitleBarPanel _title;
    private IndexStripPanel _index;
    private WatchPanel _watch;
    private InstrumentPanel _inst;
    private ChartPanel _chart;
    private BookPanel _book;
    private TapePanel _tape;
    private ProcPanel _proc;
    private FooterPanel _footer;
    private EconomyPanel _econ;

    private System.Windows.Forms.Timer _timer;
    private long _lastTick = -1;
    private Panel _hoverPanel;
    private readonly Dictionary<int, double> _prevCpu = new(1024);
    private HashSet<int> _prevPids = new();
    private Dictionary<int, string> _prevNames = new();
    private bool _firstIngest;
    private double _frame;
    private long _tickCount;
    private bool _flashDirty;
    private double _lastDataFrame;
    private Settings _settings;
    private NotifyIcon _notify;
    private bool _trayOk;
    private bool _reallyExit;
    private bool _settingsOpen;
    private int _settingsHover = -1;
    private Rectangle _setPanel, _setAuto, _setMarquee, _setThemeDark, _setThemeLight, _setThemeAuto;
    private double _ecoLastSave;
    private bool _hwProbed;
    private readonly List<(string text, Color color, Action action)> _menuItems = new();
    private Rectangle _menuRect;
    private int _menuHover = -1;
    private bool _menuOpen;
    private int _menuPid;
    private string _menuName = "";

    private const int WM_NCHITTEST = 0x0084;
    private const int WM_GETMINMAXINFO = 0x0024;
    private const int HTCLIENT = 1, HTCAPTION = 2;

    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "perfticker.log");
    internal static void Log(string s)
    {
        try { File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s + Environment.NewLine); } catch { }
    }

    public AppForm()    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.BgDeep;
        Text = "性能行情终端 · PerfTicker";
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        try { Icon = Art.MakeAppIcon(); } catch { }
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            int dark = 1;
            Native.DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
            int corner = 2;
            Native.DwmSetWindowAttribute(Handle, 33, ref corner, sizeof(int));
        }
        catch { }
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        float s = DeviceDpi / 96f;
        Theme.Rebuild(s);

        _settings = Settings.Load();
        St.IntervalMs = _settings.IntervalMs;
        St.PeriodMs = _settings.PeriodMs;
        if (St.PeriodMs < St.IntervalMs * 2L) St.PeriodMs = SmallestPeriodFor(St.IntervalMs);   // 防止 K 线周期细于采样间隔
        St.RedUp = _settings.RedUp;
        St.Economy.Enabled = _settings.EconomyOn;
        St.ProcSort = _settings.ProcSort;
        St.ProcSortDesc = _settings.ProcSortDesc;
        St.ApplyColorMode();

        _sampler = new Sampler { IntervalMs = St.IntervalMs };
        ApplyTheme();
        St.Marquee = _settings.Marquee;
        St.Economy.Restore(_settings.EcoTotal, _settings.EcoPeak, _settings.EcoSeconds);
        ApplyAutoStart();
        St.Metrics = Catalog.Build(_sampler.Cores);
        St.CpuName = _sampler.CpuName;
        St.SelectedIndex = Math.Min(St.SelectedIndex, St.Metrics.Count - 1);
        int byCode = St.Metrics.FindIndex(m => m.Code == _settings.SelectedCode);
        if (byCode >= 0) St.SelectedIndex = byCode;

        _title = new TitleBarPanel();
        _index = new IndexStripPanel();
        _watch = new WatchPanel();
        _inst = new InstrumentPanel();
        _chart = new ChartPanel();
        _book = new BookPanel();
        _tape = new TapePanel();
        _proc = new ProcPanel();
        _footer = new FooterPanel();
        _econ = new EconomyPanel();
        _panels.AddRange(new Panel[] { _title, _index, _watch, _inst, _chart, _econ, _book, _tape, _proc, _footer });
        foreach (var p in _panels) p.F = this;

        var wa = Screen.FromControl(this).WorkingArea;
        int w = Math.Min(Theme.Px(1640), wa.Width - Theme.Px(40));
        int h = Math.Min(Theme.Px(1010), wa.Height - Theme.Px(40));
        MinimumSize = new Size(Theme.Px(1180), Theme.Px(720));
        var rect = new Rectangle(wa.X + (wa.Width - w) / 2, wa.Y + (wa.Height - h) / 2, w, h);
        if (_settings.W > Theme.Px(600) && _settings.H > Theme.Px(400))
        {
            var saved = new Rectangle(_settings.X, _settings.Y, _settings.W, _settings.H);
            bool visible = false;
            foreach (var sc in Screen.AllScreens)
                if (sc.WorkingArea.IntersectsWith(saved)) { visible = true; break; }
            if (visible) rect = saved;
            if (_settings.Maximized) WindowState = FormWindowState.Maximized;
        }
        Bounds = rect;
        TopMost = _settings.TopMost;
        St.TopMost = _settings.TopMost;

        LayoutPanels();
        if (_settings.Maximized) WindowState = FormWindowState.Maximized;

        InitTray();
        _timer = new System.Windows.Forms.Timer { Interval = 33 };
        _timer.Tick += OnUiTick;
        _timer.Start();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_reallyExit && e.CloseReason == CloseReason.UserClosing && _trayOk)
        {
            e.Cancel = true;
            SaveSettings();
            HideToTray();
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        SaveSettings();
        try { if (_notify != null) { _notify.Visible = false; _notify.Dispose(); } } catch { }
        _timer?.Stop();
        _timer?.Dispose();
        _sampler?.Dispose();
        base.OnFormClosed(e);
    }

    // ── 设置持久化 ─────────────────────────────────────────
    private void SaveSettings()
    {
        try
        {
            if (_settings == null) return;
            bool max = WindowState == FormWindowState.Maximized;
            _settings.Maximized = max;
            if (!max && Visible)
            {
                _settings.X = Bounds.X;
                _settings.Y = Bounds.Y;
                _settings.W = Bounds.Width;
                _settings.H = Bounds.Height;
            }
            _settings.IntervalMs = St.IntervalMs;
            _settings.PeriodMs = (int)St.PeriodMs;
            _settings.RedUp = St.RedUp;
            _settings.TopMost = St.TopMost;
            _settings.EconomyOn = St.Economy.Enabled;
            _settings.Marquee = St.Marquee;
            _settings.EcoTotal = St.Economy.Total;
            _settings.EcoPeak = St.Economy.PeakRate;
            _settings.EcoSeconds = St.Economy.Seconds;
            _settings.SelectedCode = St.Selected.Code;
            _settings.ProcSort = St.ProcSort;
            _settings.ProcSortDesc = St.ProcSortDesc;
            _settings.Save();
        }
        catch { }
    }

    // ── 主题 ───────────────────────────────────────────────
    private static bool IsSystemDark()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return (k?.GetValue("AppsUseLightTheme") as int? ?? 1) == 0;
        }
        catch { return false; }
    }

    private void ApplyTheme()
    {
        bool dark = _settings.ThemeMode == 0 || (_settings.ThemeMode == 2 && IsSystemDark());
        Theme.SetTheme(dark);
        St.ApplyColorMode();
        BackColor = Theme.BgDeep;
        Invalidate(true);
    }

    public void SetThemeMode(int mode)
    {
        _settings.ThemeMode = Math.Clamp(mode, 0, 2);
        ApplyTheme();
        SaveSettings();
    }

    // ── 开机自启动 ─────────────────────────────────────────
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "PerfTicker";

    private static string ExePath => Environment.ProcessPath ?? Application.ExecutablePath;

    private void ApplyAutoStart()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (k == null) return;
            if (_settings.AutoStart) k.SetValue(RunName, "\"" + ExePath + "\"");
            else if (k.GetValue(RunName) != null) k.DeleteValue(RunName, false);
        }
        catch { }
    }

    public void ToggleAutoStart()
    {
        _settings.AutoStart = !_settings.AutoStart;
        ApplyAutoStart();
        AddTape(_settings.AutoStart ? "已设置开机自启动" : "已取消开机自启动", Theme.Gold);
        Invalidate();
        SaveSettings();
    }

    // ── 托盘 ───────────────────────────────────────────────
    private void InitTray()
    {
        try
        {
            _notify = new NotifyIcon
            {
                Icon = Icon ?? Art.MakeAppIcon(),
                Visible = true,
                Text = "性能行情终端"
            };
            var menu = new ContextMenuStrip { ShowImageMargin = false };
            menu.Items.Add("打开主窗口", null, (s, e) => RestoreFromTray());
            menu.Items.Add("营收归零", null, (s, e) => { St.Economy.Reset(); SaveSettings(); Invalidate(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, (s, e) => ExitApp());
            _notify.ContextMenuStrip = menu;
            _notify.DoubleClick += (s, e) => RestoreFromTray();
            _trayOk = true;
        }
        catch { _trayOk = false; }
    }

    private void HideToTray()
    {
        ShowInTaskbar = false;
        Hide();
        try
        {
            _notify?.ShowBalloonTip(1600, "性能行情终端", "已最小化到托盘，行情与营收继续统计", ToolTipIcon.Info);
        }
        catch { }
    }

    public void RestoreFromTray()
    {
        ShowInTaskbar = true;
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
        Invalidate();
    }

    private void ExitApp()
    {
        _reallyExit = true;
        SaveSettings();
        try { if (_notify != null) { _notify.Visible = false; } } catch { }
        Close();
    }

    public bool SettingsOpen => _settingsOpen;

    public void ShowAbout()
    {
        try
        {
            using var f = new AboutForm(Hw);
            f.ShowDialog(this);
        }
        catch (Exception ex) { Log("ABOUT FAIL " + ex); }
    }

    public void ResetEconomy()
    {
        St.Economy.Reset();
        SaveSettings();
        AddTape("营收已归零", Theme.Gold);
        Invalidate();
    }

    public void ToggleSettings()
    {
        _settingsOpen = !_settingsOpen;
        _settingsHover = -1;
        Invalidate();
    }

    public void ToggleMarquee()
    {
        St.Marquee = !St.Marquee;
        SaveSettings();
        Invalidate(_footer.Bounds);
        Invalidate();
    }
    // ── 布局 ──────────────────────────────────────────────
    private void LayoutPanels()
    {
        int W = ClientSize.Width, H = ClientSize.Height;
        int titleH = Theme.Px(42), indexH = Theme.Px(58), footerH = Theme.Px(28);
        int bottomH = Math.Clamp((int)(H * 0.238), Theme.Px(200), Theme.Px(300));
        int leftW = Theme.Px(298);
        int rightW = Theme.Px(342);
        if (W < Theme.Px(1420)) rightW = Theme.Px(300);
        int centerW = W - leftW - rightW;
        if (centerW < Theme.Px(420)) { rightW = Math.Max(Theme.Px(250), W - leftW - Theme.Px(420)); }

        int y0 = titleH + indexH;
        int footerY = H - footerH;
        int bottomTop = footerY - bottomH;

        _title.Bounds = new Rectangle(0, 0, W, titleH);
        _index.Bounds = new Rectangle(0, titleH, W, indexH);
        _watch.Bounds = new Rectangle(0, y0, leftW, Math.Max(Theme.Px(120), bottomTop - y0));
        _proc.Bounds = new Rectangle(0, bottomTop, W, H - footerH - bottomTop);
        _footer.Bounds = new Rectangle(0, footerY, W, footerH);

        int rightH = Math.Max(Theme.Px(120), bottomTop - y0);
        int bookH = Math.Min(rightH - Theme.Px(220), Theme.Px(330));
        _book.Bounds = new Rectangle(W - rightW, y0, rightW, bookH);
        _tape.Bounds = new Rectangle(W - rightW, y0 + bookH, rightW, rightH - bookH);

        int instH = Theme.Px(98);
        int ecoH = St.Economy.Enabled ? Theme.Px(96) : 0;
        _inst.Bounds = new Rectangle(leftW, y0, centerW, instH);
        _chart.Bounds = new Rectangle(leftW, y0 + instH, centerW,
            Math.Max(Theme.Px(80), bottomTop - y0 - instH - ecoH));
        _econ.Bounds = ecoH > 0
            ? new Rectangle(leftW, bottomTop - ecoH, centerW, ecoH)
            : Rectangle.Empty;

    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_title == null) return;
        LayoutPanels();
        Invalidate();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        Theme.Rebuild(e.DeviceDpiNew / 96f);
        MinimumSize = new Size(Theme.Px(1180), Theme.Px(720));
        LayoutPanels();
        Invalidate();
    }

    // ── 绘制 ──────────────────────────────────────────────
    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.CompositingQuality = CompositingQuality.HighSpeed;
        g.InterpolationMode = InterpolationMode.Low;
        using (var b = new SolidBrush(Theme.BgDeep)) g.FillRectangle(b, ClientRectangle);

        var clip = Rectangle.Round(g.VisibleClipBounds);
        foreach (var p in _panels)
        {
            if (p.Bounds.Width <= 0 || p.Bounds.Height <= 0) continue;
            if (!p.Bounds.IntersectsWith(clip)) continue;
            try { p.Paint(g, clip); }
            catch (Exception ex) { Log("PAINT FAIL " + p.GetType().Name + " clip=" + clip + " :: " + ex); }
        }

        PaintSettings(g);
        PaintMenu(g);
    }

    // ── 设置面板（自绘浮层）──────────────────────────────
    private void PaintSettings(Graphics g)
    {
        if (!_settingsOpen) return;
        int pad = Theme.Px(12), rowH = Theme.Px(38);
        int w = Theme.Px(302);
        int x = Theme.Px(10);
        int y = _title.Bounds.Bottom + Theme.Px(6);
        _setPanel = new Rectangle(x, y, w, Theme.Px(214));

        var r = _setPanel;
        using (var sh = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
            g.FillRectangle(sh, r.X + Theme.Px(4), r.Y + Theme.Px(4), r.Width, r.Height);
        Theme.RoundedFill(g, r, 5f, Theme.PopBg);
        using (var pen = new Pen(Theme.Line2)) g.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
        Theme.Fill(g, new Rectangle(r.X, r.Y, Theme.Px(3), r.Height), Theme.Gold);

        Theme.Text(g, "设置", Theme.UiL, Theme.TextHi, new Rectangle(r.X + pad, r.Y + Theme.Px(8), Theme.Px(80), Theme.Px(20)), Theme.AL);
        Theme.Text(g, "v" + (typeof(AppForm).Assembly.GetName().Version ?? new Version(1, 0, 0)).ToString(3), Theme.NumXXS, Theme.TextFade, new Rectangle(r.Right - Theme.Px(58), r.Y + Theme.Px(8), Theme.Px(46), Theme.Px(20)), Theme.AR);

        int ry = r.Y + Theme.Px(36);
        void Switch(Rectangle box, bool on, int hover, int idx)
        {
            var bg = on ? Color.FromArgb(0xF0, Theme.Gold) : Theme.PanelSoft;
            var bd = on ? Theme.Gold : Theme.Line2;
            Theme.RoundedRect(g, box, box.Height / 2f / Theme.S, bg, bd, 1f);
            int kr = box.Height - Theme.Px(4);
            int kx = on ? box.Right - kr - Theme.Px(2) : box.X + Theme.Px(2);
            using var b = new SolidBrush(on ? DarkInkOn() : Theme.TextFade);
            g.FillEllipse(b, kx, box.Y + Theme.Px(2), kr, kr);
            if (hover == idx) using (var hp = new Pen(Color.FromArgb(90, Theme.Gold))) g.DrawRectangle(hp, box.X - 1, box.Y - 1, box.Width + 1, box.Height + 1);
        }

        // 开机自启动
        Theme.Text(g, "开机自启动", Theme.UiM, _settingsHover == 1 ? Theme.TextHi : Theme.Txt, new Rectangle(r.X + pad, ry, Theme.Px(150), rowH), Theme.AL);
        _setAuto = new Rectangle(r.Right - pad - Theme.Px(38), ry + Theme.Px(9), Theme.Px(38), Theme.Px(20));
        Switch(_setAuto, _settings.AutoStart, _settingsHover, 1);
        ry += rowH;

        // 底部滚动信息
        Theme.Text(g, "底部滚动信息", Theme.UiM, _settingsHover == 2 ? Theme.TextHi : Theme.Txt, new Rectangle(r.X + pad, ry, Theme.Px(150), rowH), Theme.AL);
        _setMarquee = new Rectangle(r.Right - pad - Theme.Px(38), ry + Theme.Px(9), Theme.Px(38), Theme.Px(20));
        Switch(_setMarquee, St.Marquee, _settingsHover, 2);
        ry += rowH;

        // 主题
        Theme.Text(g, "界面主题", Theme.UiM, Theme.Txt, new Rectangle(r.X + pad, ry, Theme.Px(90), Theme.Px(22)), Theme.AL);
        int sw = Theme.Px(66), sh2 = Theme.Px(22), gapx = Theme.Px(3);
        int bx = r.Right - pad - (sw * 3 + gapx * 2);
        _setThemeDark = new Rectangle(bx, ry, sw, sh2);
        _setThemeLight = new Rectangle(bx + sw + gapx, ry, sw, sh2);
        _setThemeAuto = new Rectangle(bx + (sw + gapx) * 2, ry, sw, sh2);
        void ThemeChip(Rectangle box, string label, int mode)
        {
            bool act = _settings.ThemeMode == mode;
            bool hov = _settingsHover == 3 + mode;
            Theme.RoundedRect(g, box, 3f, act ? Color.FromArgb(40, Theme.Gold) : hov ? Theme.PanelSoft : Theme.ChipBg,
                act ? Theme.Gold : Theme.Line2, 1f);
            Theme.Text(g, label, Theme.UiXS, act ? Theme.Gold : hov ? Theme.TextHi : Theme.TextDim, box, Theme.AC);
        }
        ThemeChip(_setThemeDark, "深色", 0);
        ThemeChip(_setThemeLight, "浅色", 1);
        ThemeChip(_setThemeAuto, "跟随系统", 2);
        ry += Theme.Px(34);

        Theme.Text(g, "设置会自动保存到本地配置", Theme.UiXS, Theme.TextFade, new Rectangle(r.X + pad, ry, Theme.Px(240), Theme.Px(18)), Theme.AL);
    }

    private static Color DarkInkOn() => Theme.Dark ? Color.FromArgb(0x14, 0x18, 0x22) : Color.White;

    private void SettingsMouseMove(Point p)
    {
        int h = -1;
        if (_setAuto.Contains(p)) h = 1;
        else if (_setMarquee.Contains(p)) h = 2;
        else if (_setThemeDark.Contains(p)) h = 3;
        else if (_setThemeLight.Contains(p)) h = 4;
        else if (_setThemeAuto.Contains(p)) h = 5;
        if (h != _settingsHover) { _settingsHover = h; Invalidate(); }
    }

    private bool SettingsClick(Point p)
    {
        if (_setAuto.Contains(p)) { ToggleAutoStart(); return true; }
        if (_setMarquee.Contains(p)) { ToggleMarquee(); return true; }
        if (_setThemeDark.Contains(p)) { SetThemeMode(0); return true; }
        if (_setThemeLight.Contains(p)) { SetThemeMode(1); return true; }
        if (_setThemeAuto.Contains(p)) { SetThemeMode(2); return true; }
        if (!_setPanel.Contains(p)) { _settingsOpen = false; Invalidate(); }
        return true;
    }

    // ── 右键菜单（自绘，风格与终端一致）────────────────────
    private void PaintMenu(Graphics g)
    {
        if (!_menuOpen || _menuRect.Width <= 0) return;
        var r = _menuRect;
        int pad = Theme.Px(4), itemH = Theme.Px(30);
        using (var sh = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
            g.FillRectangle(sh, r.X + Theme.Px(3), r.Y + Theme.Px(3), r.Width, r.Height);
        Theme.RoundedFill(g, r, 4f, Theme.PopBg);
        using (var pen = new Pen(Theme.Line2)) g.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
        Theme.Fill(g, new Rectangle(r.X, r.Y, Theme.Px(2), r.Height), Theme.Gold);

        for (int i = 0; i < _menuItems.Count; i++)
        {
            var ir = new Rectangle(r.X + pad, r.Y + pad + i * itemH, r.Width - pad * 2, itemH);
            if (i == _menuHover) Theme.RoundedFill(g, ir, 3f, Color.FromArgb(0x26, 0x2D, 0x3C));
            Theme.TextEllipsis(g, _menuItems[i].text, Theme.UiM, _menuItems[i].color,
                new Rectangle(ir.X + Theme.Px(10), ir.Y, ir.Width - Theme.Px(14), ir.Height), Theme.AL);
        }
    }

    public void ShowProcMenu(int pid, Point clientPoint)
    {
        _menuPid = pid;
        _menuName = St.Procs.FirstOrDefault(p => p.Pid == pid)?.Name ?? ("PID " + pid);
        BuildProcMenu(false);

        int w = Theme.Px(206), itemH = Theme.Px(30);
        int h = _menuItems.Count * itemH + Theme.Px(8);
        int x = Math.Min(clientPoint.X, ClientSize.Width - w - Theme.Px(10));
        int y = clientPoint.Y - h - Theme.Px(10);
        if (y < Theme.Px(48)) y = clientPoint.Y + Theme.Px(20);
        y = Math.Max(Theme.Px(48), Math.Min(y, ClientSize.Height - h - Theme.Px(36)));
        x = Math.Max(Theme.Px(6), x);
        _menuRect = new Rectangle(x, y, w, h);
        _menuOpen = true;
        _menuHover = -1;
        Invalidate();
    }

    private void BuildProcMenu(bool confirm)
    {
        _menuItems.Clear();
        if (!confirm)
        {
            _menuItems.Add(("全抛 · 结束进程", Theme.Up, () => BuildProcMenu(true)));
            bool pinned = St.Pinned.Contains(_menuName);
            _menuItems.Add((pinned ? "取消置顶" : "置顶到榜首", Theme.Gold, () => { TogglePin(_menuName); CloseMenu(); }));
            _menuItems.Add(("复制进程名", Theme.Txt, () =>
            {
                try { Clipboard.SetText(_menuName); } catch { }
                CloseMenu();
            }));
        }
        else
        {
            _menuItems.Add(("确认全抛 " + Trim(_menuName) + " ?", Theme.Up, () => { KillProc(_menuPid); CloseMenu(); }));
            _menuItems.Add(("取消", Theme.Txt, CloseMenu));
        }
        Invalidate();
    }

    private void CloseMenu()
    {
        _menuOpen = false;
        _menuItems.Clear();
        Invalidate();
    }

    private void KillProc(int pid)
    {
        string name = St.Procs.FirstOrDefault(p => p.Pid == pid)?.Name ?? pid.ToString();
        if (pid == Environment.ProcessId)
        {
            AddTape("全抛被拒绝 · 不能结束行情终端自身", Theme.Warn);
            Invalidate();
            return;
        }
        try
        {
            using var proc = System.Diagnostics.Process.GetProcessById(pid);
            proc.Kill();
            AddTape($"全抛成交 · 已结束 {Trim(name)} ({pid})", Theme.Down);
        }
        catch
        {
            AddTape($"全抛失败 · {Trim(name)} ({pid}) 权限不足或已退出", Theme.Warn);
        }
        Invalidate();
    }

    public void TogglePin(string name)
    {
        if (string.IsNullOrEmpty(name)) return;
        if (!St.Pinned.Remove(name)) St.Pinned.Add(name);
        SortProcs();
        AddTape(St.Pinned.Contains(name) ? $"已置顶 {Trim(name)}" : $"已取消置顶 {Trim(name)}", Theme.Gold);
        Invalidate(_proc.Bounds);
    }

    public void SetSearchActive(bool on)
    {
        St.SearchActive = on;
        Invalidate(_proc.Bounds);
    }

    public void SetProcFilter(string f)
    {
        St.ProcFilter = f ?? "";
        St.ProcScroll = 0;
        Invalidate(_proc.Bounds);
    }

    // ── UI 心跳 ───────────────────────────────────────────
    private void OnUiTick(object sender, EventArgs e)
    {
        _frame += 0.033;
        St.Frame = _frame;
        _tickCount++;

        var snap = _sampler.Latest;
        if (!St.Paused && snap.Tick != _lastTick && snap.Tick > 0)
        {
            _lastTick = snap.Tick;
            Ingest(snap);
        }

        // 营收结算
        if (St.Economy.Enabled)
        {
            if (!St.Paused) St.Economy.Accrue(0.033, St.Snap);
            if (_tickCount % 2 == 0) Invalidate(_econ.Bounds);
        }

        // 数值缓动（行情跳动感）
        bool tweening = false;
        if (!St.Paused) foreach (var m in St.Metrics) if (m.Tween()) tweening = true;
        if (tweening && _tickCount % 2 == 0)
        {
            Invalidate(_index.Bounds);
            Invalidate(_inst.Bounds);
            Invalidate(_watch.Bounds);
        }

        if (_tickCount % 30 == 0 && _notify != null)
        {
            var sn = St.Snap;
            string tip = $"CPU {sn.CpuPct:0}% · GPU {(sn.Gpu.Available ? sn.Gpu.UtilPct : 0):0}% · 内存 {sn.MemPct:0}%";
            if (St.Economy.Enabled) tip += $" · ¥{St.Economy.Total:N0}";
            if (tip.Length > 62) tip = tip.Substring(0, 62);
            try { _notify.Text = tip; } catch { }
        }
        if (St.Economy.Enabled && St.Frame - _ecoLastSave > 15)
        {
            _ecoLastSave = St.Frame;
            SaveSettings();
        }

        if (_chart.Advance()) Invalidate(_chart.Bounds);
        if (_proc.Advance()) Invalidate(_proc.Bounds);

        if (_tickCount % 3 == 0) Invalidate(_footer.Bounds);
        if (_tickCount % 15 == 0) Invalidate(_title.Bounds);
        if (St.SearchActive && _tickCount % 6 == 0) Invalidate(_proc.Bounds);

        if (_flashDirty && St.Frame - _lastDataFrame > 0.28)
        {
            _flashDirty = false;
            Invalidate(_watch.Bounds);
            Invalidate(_index.Bounds);
        }
    }

    private void Ingest(Snapshot s)
    {
        var prev = St.Snap;
        St.Snap = s;
        long now = Environment.TickCount64;
        double vol = Math.Max(0, s.CtxSwitchPerSec);
        St.VolPerSample = vol;

        foreach (var m in St.Metrics) m.Push(m.Read(s), now, vol);

        foreach (var p in s.Procs)
        {
            if (_prevCpu.TryGetValue(p.Pid, out double pc)) p.CpuDelta = p.CpuPct - pc;
            _prevCpu[p.Pid] = p.CpuPct;
            if (!St.ProcHist.TryGetValue(p.Pid, out var h)) { h = new ProcHist(); St.ProcHist[p.Pid] = h; }
            h.Push(p.CpuPct);
        }
        if (_prevCpu.Count > 4000) _prevCpu.Clear();
        if (St.ProcHist.Count > 1200)
        {
            foreach (var key in St.ProcHist.Keys.Where(k => !_prevCpu.ContainsKey(k)).Take(600).ToList())
                St.ProcHist.Remove(key);
        }

        if (!_hwProbed && s.Procs.Count > 0)
        {
            _hwProbed = true;
            Hw.Probe(s, s.Gpu);
            St.Economy.ApplyWeights(Hw);
            AddTape($"硬件探测完成 · {Hw.LogicalCores} 线程 / {Hw.MemTotalGB:0}G 内存 / {(Hw.HasGpu ? Hw.GpuMemGB.ToString("0.#") + "G 显存" : "无独显")}", Theme.Blue);
        }

        St.Procs = s.Procs;
        SortProcs();
        DetectEvents(prev, s);
        if (St.Metrics.Count > 0) St.Selected.RebuildCandles(St.PeriodMs);
        _flashDirty = true;
        _lastDataFrame = St.Frame;
        Invalidate();
    }

    private void SortProcs()
    {
        Comparison<ProcSample> cmp = St.ProcSort switch
        {
            0 => (a, b) => b.CpuPct.CompareTo(a.CpuPct),
            1 => (a, b) => b.GpuPct.CompareTo(a.GpuPct),
            2 => (a, b) => b.MemMB.CompareTo(a.MemMB),
            _ => (a, b) => b.Threads.CompareTo(a.Threads),
        };
        if (!St.ProcSortDesc)
        {
            var inner = cmp;
            cmp = (a, b) => inner(b, a);
        }
        St.Procs.Sort(cmp);
    }

    private void DetectEvents(Snapshot prev, Snapshot s)
    {
        if (!_firstIngest)
        {
            _firstIngest = true;
            AddTape($"行情接入完成 · 已载入 {St.Metrics.Count} 项自选 · {_sampler.Cores} 逻辑核", Theme.Gold);
        }

        var curPids = new HashSet<int>(s.Procs.Count);
        var curNames = new Dictionary<int, string>(s.Procs.Count);
        foreach (var p in s.Procs) { curPids.Add(p.Pid); curNames[p.Pid] = p.Name; }

        if (_prevPids.Count > 0)
        {
            int added = 0;
            foreach (var p in s.Procs)
            {
                if (_prevPids.Contains(p.Pid) || p.Pid == 0 || added >= 2) continue;
                AddTape($"新进 {Trim(p.Name)}  ·  {p.Pid}", Theme.Blue);
                added++;
            }
            int removed = 0;
            foreach (var pid in _prevPids)
            {
                if (curPids.Contains(pid) || removed >= 2) continue;
                _prevNames.TryGetValue(pid, out var nm);
                AddTape($"退出 {Trim(nm)}  ·  {pid}", Theme.TextFade);
                removed++;
            }
        }
        _prevPids = curPids;
        _prevNames = curNames;

        double df = s.CpuAvgMHz - prev.CpuAvgMHz;
        if (Math.Abs(df) >= 220 && prev.CpuAvgMHz > 0)
        {
            string dir = df > 0 ? "拉升" : "回落";
            AddTape($"CPU 主频{dir} {(df > 0 ? "+" : "")}{df / 1000.0:0.00}GHz → {s.CpuAvgMHz / 1000.0:0.00}GHz",
                df > 0 ? Theme.Up : Theme.Down);
        }
        if (s.Gpu.Available && s.Gpu.TempC >= 82 && prev.Gpu.TempC < 82)
            AddTape($"GPU 温度告警 {s.Gpu.TempC:0}°C", Theme.Warn);
        if (s.Gpu.Available && s.Gpu.UtilPct >= 92 && prev.Gpu.UtilPct < 92)
            AddTape($"GPU 负载触顶 {s.Gpu.UtilPct:0}%", Theme.Warn);
        if (s.CpuPct >= 88 && prev.CpuPct < 88)
            AddTape($"CPU 负载预警 {s.CpuPct:0}%", Theme.Warn);
        if (s.MemPct >= 90 && prev.MemPct < 90)
            AddTape($"内存水位 {s.MemPct:0}% 逼近上限", Theme.Warn);
        if (s.DiskReadMBps >= 200 && prev.DiskReadMBps < 200)
            AddTape($"磁盘突发读取 {s.DiskReadMBps:0}MB/s", Theme.Cyan);

        var hot = s.Procs.Count > 0 ? s.Procs[0] : null;
        if (hot != null && hot.CpuPct >= 22 && _prevCpu.TryGetValue(hot.Pid, out double hp) && hot.CpuPct - hp > 8)
            AddTape($"主力异动 {Trim(hot.Name)} CPU {hot.CpuPct:0.0}%", Theme.Up);

        // 逐笔成交：按 CPU 变化排出最活跃的成交笔
        int ticks = 0;
        foreach (var p in s.Procs)
        {
            if (ticks >= 2) break;
            if (Math.Abs(p.CpuDelta) < 1.2) continue;
            AddTape($"{Trim(p.Name)}  {p.CpuPct:0.0}%  {(p.CpuDelta > 0 ? "▲" : "▼")}{Math.Abs(p.CpuDelta):0.0}",
                p.CpuDelta > 0 ? Theme.Up : Theme.Down);
            ticks++;
        }
    }

    private static string Trim(string s)
    {
        if (string.IsNullOrEmpty(s)) return "未知";
        return s.Length > 22 ? s[..22] + "…" : s;
    }

    private void AddTape(string text, Color color)
    {
        long now = Environment.TickCount64;
        if (St.Tape.Count > 0)
        {
            var last = St.Tape[^1];
            if (last.Text == text && now - last.Ms < 5000) return;
        }
        St.Tape.Add(new TapeEvent
        {
            Ms = now,
            Time = DateTime.Now.ToString("HH:mm:ss"),
            Text = text,
            Color = color
        });
        if (St.Tape.Count > 240) St.Tape.RemoveRange(0, St.Tape.Count - 240);
    }

    // ── 命令入口 ──────────────────────────────────────────
    public void SelectMetric(string code)
    {
        int idx = St.Metrics.FindIndex(m => m.Code == code);
        if (idx < 0) return;
        St.SelectedIndex = idx;
        OnMetricChanged();
    }

    public void OnMetricChanged()
    {
        St.Selected.RebuildCandles(St.PeriodMs);
        Invalidate();
    }

    public void SetChartMode(long period)
    {
        if (period < St.IntervalMs * 2L) return;   // 采样太粗时不允许更细的 K 线
        St.PeriodMs = Math.Max(500, period);
        St.Selected.RebuildCandles(St.PeriodMs);
        Invalidate();
    }

    public void SetInterval(int ms)
    {
        St.IntervalMs = ms;
        if (_sampler != null) _sampler.IntervalMs = ms;
        if (St.PeriodMs < ms * 2L) St.PeriodMs = SmallestPeriodFor(ms);
        St.Selected.RebuildCandles(St.PeriodMs);
        AddTape($"采样周期切换 {ms / 1000.0:0.0}s · K 线 {St.PeriodMs / 1000}秒", Theme.Blue);
        Invalidate();
    }

    private static long SmallestPeriodFor(int ms)
    {
        long need = ms * 2L;
        foreach (var p in new long[] { 2000, 5000, 10000, 30000, 60000 })
            if (p >= need) return p;
        return 60000;
    }

    public void TogglePause()
    {
        St.Paused = !St.Paused;
        AddTape(St.Paused ? "行情已暂停" : "行情恢复推送", St.Paused ? Theme.Warn : Theme.Down);
        Invalidate();
    }

    public void ToggleTopMost()
    {
        St.TopMost = !St.TopMost;
        TopMost = St.TopMost;
        Invalidate();
    }

    public void ToggleEconomy()
    {
        St.Economy.Enabled = !St.Economy.Enabled;
        // 注意：只有手动「归零」才清零，开关切换保持累计值
        LayoutPanels();
        AddTape(St.Economy.Enabled ? "营收系统已启动 · 按占用率结算产能" : "营收系统已关闭 · 结算 " + (St.Economy.Total >= 0 ? "+" : "") + St.Economy.Total.ToString("N2") + " 元",
            St.Economy.Enabled ? Theme.Gold : Theme.TextFade);
        Invalidate();
    }

    public void ToggleColor()
    {
        St.RedUp = !St.RedUp;
        St.ApplyColorMode();
        Invalidate();
    }

    public void SetProcSort(int i)
    {
        if (St.ProcSort == i) St.ProcSortDesc = !St.ProcSortDesc;
        else { St.ProcSort = i; St.ProcSortDesc = true; }
        SortProcs();
        Invalidate(_proc.Bounds);
    }

    // ── 输入 ──────────────────────────────────────────────
    private Panel FindPanel(Point p)
    {
        foreach (var panel in _panels)
            if (panel.Bounds.Contains(p)) return panel;
        return null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_settingsOpen)
        {
            SettingsMouseMove(e.Location);
            Cursor = _setPanel.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
            return;
        }
        if (_menuOpen)
        {
            int pad = Theme.Px(4), itemH = Theme.Px(30);
            int mi = _menuRect.Contains(e.Location)
                ? Math.Clamp((e.Location.Y - _menuRect.Y - pad) / itemH, 0, Math.Max(0, _menuItems.Count - 1))
                : -1;
            if (mi != _menuHover) { _menuHover = mi; Invalidate(); }
            Cursor = _menuRect.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
            return;
        }
        var p = FindPanel(e.Location);
        if (!ReferenceEquals(p, _hoverPanel))
        {
            _hoverPanel?.MouseLeave();
            _hoverPanel = p;
        }
        p?.MouseMove(e.Location);
        var cur = p?.CursorAt(e.Location) ?? Cursors.Default;
        if (Cursor != cur) Cursor = cur;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverPanel?.MouseLeave();
        _hoverPanel = null;
        Cursor = Cursors.Default;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (_settingsOpen)
        {
            SettingsClick(e.Location);
            return;
        }
        if (_menuOpen)
        {
            if (_menuRect.Contains(e.Location) && _menuHover >= 0 && _menuHover < _menuItems.Count)
            {
                var act = _menuItems[_menuHover].action;
                act?.Invoke();
            }
            else CloseMenu();
            return;
        }
        if (e.Button == MouseButtons.Right)
        {
            FindPanel(e.Location)?.RightClick(e.Location);
            Activate();
            return;
        }
        if (e.Button != MouseButtons.Left) return;
        if (St.SearchActive && !_proc.Bounds.Contains(e.Location)) SetSearchActive(false);
        FindPanel(e.Location)?.Click(e.Location, e.Button);
        Activate();
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        if (St.SearchActive)
        {
            if (e.KeyChar == (char)Keys.Back)
            {
                if (St.ProcFilter.Length > 0) SetProcFilter(St.ProcFilter[..^1]);
                e.Handled = true;
                return;
            }
            if (e.KeyChar == (char)Keys.Enter) { SetSearchActive(false); e.Handled = true; return; }
            if (e.KeyChar == (char)Keys.Escape) { SetSearchActive(false); SetProcFilter(""); e.Handled = true; return; }
            if (!char.IsControl(e.KeyChar))
            {
                SetProcFilter(St.ProcFilter + e.KeyChar);
                e.Handled = true;
                return;
            }
        }
        base.OnKeyPress(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        FindPanel(e.Location)?.Wheel(e.Location, e.Delta);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        FindPanel(e.Location)?.DoubleClick(e.Location);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_menuOpen && keyData == Keys.Escape) { CloseMenu(); return true; }
        if (St.SearchActive) return base.ProcessCmdKey(ref msg, keyData);
        if (keyData == (Keys.Control | Keys.F)) { SetSearchActive(true); return true; }

        switch (keyData)
        {
            case Keys.Space:
                TogglePause();
                return true;
            case Keys.D1:
                SetChartMode(2000);
                return true;
            case Keys.D2:
                SetChartMode(5000);
                return true;
            case Keys.D3:
                SetChartMode(10000);
                return true;
            case Keys.D4:
                SetChartMode(30000);
                return true;
            case Keys.D5:
                SetChartMode(60000);
                return true;
            case Keys.T:
                ToggleTopMost();
                return true;
            case Keys.E:
                ToggleEconomy();
                return true;
            case Keys.C:
                ToggleColor();
                return true;
            case Keys.F5:
                _lastTick = -1;
                return true;
            case Keys.F11:
                WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
                return true;
            case Keys.Escape:
                if (WindowState == FormWindowState.Maximized) WindowState = FormWindowState.Normal;
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ── 无边框窗口的拖拽 / 缩放 ────────────────────────────
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_GETMINMAXINFO)
        {
            base.WndProc(ref m);
            var mmi = Marshal.PtrToStructure<Native.MINMAXINFO>(m.LParam);
            var wa = Screen.FromHandle(Handle).WorkingArea;
            mmi.ptMaxPosition.X = wa.X;
            mmi.ptMaxPosition.Y = wa.Y;
            mmi.ptMaxSize.X = wa.Width;
            mmi.ptMaxSize.Y = wa.Height;
            Marshal.StructureToPtr(mmi, m.LParam, false);
            return;
        }

        if (m.Msg == 0x00A3) // WM_NCLBUTTONDBLCLK
        {
            if (_title != null)
            {
                int ax = (short)((long)m.LParam & 0xFFFF);
                int ay = (short)(((long)m.LParam >> 16) & 0xFFFF);
                var ap = PointToClient(new Point(ax, ay));
                if (_title.Bounds.Contains(ap) && _title.IsInteractive(ap)) return;   // 控件上的双击不当作标题栏
            }
            base.WndProc(ref m);
            return;
        }

        if (m.Msg == WM_NCHITTEST)
        {
            base.WndProc(ref m);
            if ((int)m.Result != HTCLIENT) return;

            int sx = (short)((long)m.LParam & 0xFFFF);
            int sy = (short)(((long)m.LParam >> 16) & 0xFFFF);
            var cp = PointToClient(new Point(sx, sy));

            if (WindowState != FormWindowState.Maximized)
            {
                int edge = Theme.Px(6);
                bool l = cp.X <= edge, r = cp.X >= ClientSize.Width - edge;
                bool t = cp.Y <= edge, b = cp.Y >= ClientSize.Height - edge;
                int hit = 0;
                if (t && l) hit = 13;
                else if (t && r) hit = 14;
                else if (b && l) hit = 16;
                else if (b && r) hit = 17;
                else if (t) hit = 12;
                else if (b) hit = 15;
                else if (l) hit = 10;
                else if (r) hit = 11;
                if (hit != 0) { m.Result = (IntPtr)hit; return; }
            }

            if (_title != null && _title.Bounds.Contains(cp) && !_title.IsInteractive(cp))
            {
                m.Result = (IntPtr)HTCAPTION;
            }
            return;
        }

        base.WndProc(ref m);
    }
}
