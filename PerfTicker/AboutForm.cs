using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace PerfTicker;

/// <summary>关于窗口：开发者 / 组织 / 主页链接 / 开源协议。</summary>
internal sealed class AboutForm : Form
{
    private readonly (string label, string url, Rectangle rect)[] _links;
    private Rectangle _closeRect;
    private int _hover = -1;
    private bool _dragging;
    private Point _dragOffset;

    private static readonly (string label, string url)[] LinkDefs =
    {
        ("GitHub  github.com/QiuSui1145", "https://github.com/QiuSui1145/"),
        ("Bilibili  space.bilibili.com/1764510273", "https://space.bilibili.com/1764510273"),
    };

    public AboutForm()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        Text = "关于 · 性能行情终端";
        BackColor = Theme.Panel;
        ClientSize = new Size(Theme.Px(460), Theme.Px(292));
        _links = LinkDefs.Select(l => (l.label, l.url, Rectangle.Empty)).ToArray();
    }

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ClassStyle |= 0x00020000; return cp; }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        Theme.TX = 0;
        Theme.TY = 0;

        using (var b = new SolidBrush(Theme.PopBg)) g.FillRectangle(b, ClientRectangle);
        using (var pen = new Pen(Theme.Line2)) g.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        Theme.Fill(g, new Rectangle(0, 0, ClientSize.Width, Theme.Px(3)), Theme.Gold);

        int pad = Theme.Px(22);
        // 图标 + 标题
        var logo = new Rectangle(pad, Theme.Px(24), Theme.Px(38), Theme.Px(38));
        using (var gp = Theme.RoundRect(new RectangleF(logo.X, logo.Y, logo.Width, logo.Height), Theme.Px(9)))
        using (var lg = new LinearGradientBrush(logo, Color.FromArgb(0xFF, 0xDF, 0x9A), Color.FromArgb(0xC2, 0x88, 0x14), 55f))
            g.FillPath(lg, gp);
        using (var br = new SolidBrush(Color.FromArgb(0x11, 0x15, 0x1E)))
        {
            g.FillRectangle(br, logo.X + Theme.Px(7), logo.Y + Theme.Px(21), Theme.Px(5), Theme.Px(11));
            g.FillRectangle(br, logo.X + Theme.Px(16), logo.Y + Theme.Px(10), Theme.Px(5), Theme.Px(22));
            g.FillRectangle(br, logo.X + Theme.Px(25), logo.Y + Theme.Px(16), Theme.Px(5), Theme.Px(16));
        }

        Theme.Text(g, "性能行情终端", Theme.UiXL, Theme.TextHi, new Rectangle(logo.Right + Theme.Px(12), Theme.Px(22), Theme.Px(300), Theme.Px(24)), Theme.AL);
        Theme.Text(g, "PerfTicker · 1.1.0", Theme.NumXXS, Theme.TextDim, new Rectangle(logo.Right + Theme.Px(12), Theme.Px(44), Theme.Px(300), Theme.Px(18)), Theme.AL);

        // 关闭按钮
        _closeRect = new Rectangle(ClientSize.Width - Theme.Px(40), Theme.Px(10), Theme.Px(28), Theme.Px(24));
        Theme.Text(g, "✕", Theme.UiM, Theme.TextDim, _closeRect, Theme.AC);

        int y = Theme.Px(86);
        void Info(string k, string v)
        {
            Theme.Text(g, k, Theme.UiS, Theme.TextDim, new Rectangle(pad, y, Theme.Px(70), Theme.Px(22)), Theme.AL);
            Theme.Text(g, v, Theme.UiM, Theme.TextHi, new Rectangle(pad + Theme.Px(72), y, Theme.Px(320), Theme.Px(22)), Theme.AL);
            y += Theme.Px(26);
        }

        Info("开发者", "QiuSui");
        Info("组织", "QiuSui_Studio");

        y += Theme.Px(6);
        Theme.Fill(g, new Rectangle(pad, y, ClientSize.Width - pad * 2, 1), Theme.Line);
        y += Theme.Px(12);

        for (int i = 0; i < _links.Length; i++)
        {
            var r = new Rectangle(pad, y, ClientSize.Width - pad * 2, Theme.Px(24));
            bool hov = _hover == i;
            Theme.Text(g, _links[i].label, Theme.UiM, hov ? Theme.Gold : Theme.Blue, r, Theme.AL);
            if (hov)
            {
                int tw = Theme.Measure(g, _links[i].label, Theme.UiM);
                Theme.Fill(g, new Rectangle(r.X, r.Bottom - Theme.Px(3), tw, Math.Max(1, Theme.Px(1))), Theme.Gold);
            }
            _links[i].rect = r;
            y += Theme.Px(28);
        }

        y += Theme.Px(6);
        Theme.Text(g, "开源协议  GPL-3.0", Theme.UiS, Theme.Txt, new Rectangle(pad, y, Theme.Px(240), Theme.Px(20)), Theme.AL);
        y += Theme.Px(24);
        Theme.Text(g, "本程序以 GPLv3 协议开源，欢迎 Fork / Star / 提 Issue", Theme.UiXS, Theme.TextFade,
            new Rectangle(pad, y, ClientSize.Width - pad * 2, Theme.Px(18)), Theme.AL);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            Location = new Point(Cursor.Position.X - _dragOffset.X, Cursor.Position.Y - _dragOffset.Y);
            return;
        }
        int h = -1;
        for (int i = 0; i < _links.Length; i++) if (_links[i].rect.Contains(e.Location)) h = i;
        if (h != _hover) { _hover = h; Invalidate(); }
        Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        if (_closeRect.Contains(e.Location)) { Close(); return; }
        for (int i = 0; i < _links.Length; i++)
        {
            if (_links[i].rect.Contains(e.Location))
            {
                try { Process.Start(new ProcessStartInfo(_links[i].url) { UseShellExecute = true }); } catch { }
                return;
            }
        }
        _dragging = true;
        _dragOffset = new Point(Cursor.Position.X - Location.X, Cursor.Position.Y - Location.Y);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) Close();
        base.OnKeyDown(e);
    }
}
