using System.Drawing.Drawing2D;

namespace PerfTicker;

/// <summary>壁纸模式下的悬浮控制条：壁纸窗口在图标之下无法接收点击，操作都放在这里。</summary>
internal sealed class WallBar : Form
{
    private readonly AppForm _app;
    private readonly Rectangle[] _btns = new Rectangle[4];
    private readonly string[] _labels = { "营收", "暂停", "设置", "退出壁纸" };
    private int _hover = -1;
    private bool _dragging;
    private Point _dragStart;

    public WallBar(AppForm app)
    {
        _app = app;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.FromArgb(0x0B, 0x0F, 0x16);
        ClientSize = new Size(Theme.Px(392), Theme.Px(40));
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        var wa = Screen.PrimaryScreen.WorkingArea;
        Location = new Point(wa.Right - Width - Theme.Px(24), wa.Bottom - Height - Theme.Px(24));
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x00020000;   // CS_DROPSHADOW
            cp.ExStyle |= 0x00000080;      // WS_EX_TOOLWINDOW：不占 Alt+Tab
            return cp;
        }
    }

    private void Layout()
    {
        int pad = Theme.Px(8), gap = Theme.Px(6);
        int brand = Theme.Px(96);
        int x = Theme.Px(10) + brand;
        int h = Theme.Px(26);
        int y = (ClientSize.Height - h) / 2;
        int[] widths = { Theme.Px(52), Theme.Px(52), Theme.Px(52), Theme.Px(80) };
        for (int i = 0; i < _btns.Length; i++)
        {
            _btns[i] = new Rectangle(x, y, widths[i], h);
            x += widths[i] + gap;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        Theme.TX = 0; Theme.TY = 0;
        Layout();

        var full = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
        using (var p = Theme.RoundRect(new RectangleF(0, 0, full.Width, full.Height), Theme.Px(8)))
        using (var b = new LinearGradientBrush(full, Color.FromArgb(0x16, 0x1C, 0x27), Color.FromArgb(0x0C, 0x10, 0x17), 90f))
            g.FillPath(b, p);
        using (var pen = new Pen(Color.FromArgb(0x38, 0x42, 0x53), 1f)) g.DrawPath(pen, Theme.RoundRect(new RectangleF(0.5f, 0.5f, full.Width - 1, full.Height - 1), Theme.Px(8)));

        // 品牌 + 拖拽提示
        Theme.Text(g, "性能行情", Theme.UiS, Theme.TextHi, new Rectangle(Theme.Px(12), 0, Theme.Px(70), ClientSize.Height), Theme.AL);
        Theme.Text(g, "壁纸", Theme.UiXS, Theme.Gold, new Rectangle(Theme.Px(64), 0, Theme.Px(36), ClientSize.Height), Theme.AL);

        bool econ = _app.St.Economy.Enabled;
        bool paused = _app.St.Paused;
        for (int i = 0; i < _btns.Length; i++)
        {
            var r = _btns[i];
            bool act = (i == 0 && econ) || (i == 1 && paused);
            var accent = i == 3 ? Theme.Up : (i == 1 ? Theme.Gold : i == 0 ? Theme.Gold : Theme.Cyan);
            Theme.RoundedRect(g, r, 3f,
                act ? Color.FromArgb(46, accent) : _hover == i ? Theme.PanelSoft : Color.FromArgb(18, 255, 255, 255),
                act ? Color.FromArgb(150, accent) : Theme.Line2, 1f);
            string label = i == 0 ? (econ ? "营收" : "营收") : i == 1 ? (paused ? "继续" : "暂停") : _labels[i];
            Theme.Text(g, label, Theme.UiXS,
                act ? accent : _hover == i ? Theme.TextHi : Theme.TextDim, r, Theme.AC);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Layout();
        if (_dragging)
        {
            var screen = Cursor.Position;
            Location = new Point(screen.X - _dragStart.X, screen.Y - _dragStart.Y);
            return;
        }
        int h = -1;
        for (int i = 0; i < _btns.Length; i++) if (_btns[i].Contains(e.Location)) h = i;
        if (h != _hover) { _hover = h; Invalidate(); }
        Cursor = h >= 0 ? Cursors.Hand : Cursors.SizeAll;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        Layout();
        for (int i = 0; i < _btns.Length; i++)
        {
            if (!_btns[i].Contains(e.Location)) continue;
            switch (i)
            {
                case 0: _app.ToggleEconomy(); break;
                case 1: _app.TogglePause(); break;
                case 2: _app.ExitWallpaperToSettings(); break;
                case 3: _app.ExitWallpaper(); break;
            }
            Invalidate();
            return;
        }
        _dragging = true;
        _dragStart = new Point(Cursor.Position.X - Location.X, Cursor.Position.Y - Location.Y);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button == MouseButtons.Left && e.Location.X < Theme.Px(104)) _app.ExitWallpaper();
    }
}
