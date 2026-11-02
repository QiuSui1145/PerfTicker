using System.Runtime.InteropServices;
using System.Text;

namespace PerfTicker;

/// <summary>
/// 把窗口挂到桌面壁纸层（WorkerW，位于桌面图标之下）：
/// 图标可见、右键是桌面原生菜单，但窗口本身不再接收点击。
/// </summary>
internal static class DesktopHost
{
    private const uint WM_SPAWN_WORKER = 0x052C;
    private const int GWL_STYLE = -16;
    private const int WS_CHILD = unchecked((int)0x40000000);
    private const int WS_POPUP = unchecked((int)0x80000000);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string cls, string win);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string win);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
        uint flags, uint timeout, out IntPtr result);

    [DllImport("user32.dll")]
    private static extern IntPtr SetParent(IntPtr child, IntPtr parent);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr child);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumProc cb, IntPtr l);

    private delegate bool EnumProc(IntPtr h, IntPtr l);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr h, int idx);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr h, int idx, int val);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr h, out RECT r);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    public static IntPtr Host { get; private set; }

    /// <summary>找到承载壁纸的 WorkerW（图标视图的下一个兄弟）。</summary>
    public static IntPtr FindWallpaperWindow()
    {
        IntPtr progman = FindWindow("Progman", null);
        // 让 Progman 生成 WorkerW（Win10/11 通用做法）
        if (progman != IntPtr.Zero)
        {
            for (int i = 0; i < 3; i++)
            {
                SendMessageTimeout(progman, WM_SPAWN_WORKER, new IntPtr(0xD), new IntPtr(0x1), 0, 1000, out _);
                System.Threading.Thread.Sleep(60);
            }
        }

        IntPtr worker = IntPtr.Zero;
        EnumWindows((h, l) =>
        {
            // 找到带 SHELLDLL_DefView 的窗口，它的下一个兄弟 WorkerW 就是壁纸层
            IntPtr defView = FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView != IntPtr.Zero)
            {
                IntPtr next = FindWindowEx(IntPtr.Zero, h, "WorkerW", null);
                if (next != IntPtr.Zero) { worker = next; return false; }
                worker = progman;
                return false;
            }
            return true;
        }, IntPtr.Zero);

        if (worker == IntPtr.Zero) worker = progman;
        return worker;
    }

    /// <summary>把窗口挂到壁纸层并铺满。</summary>
    public static bool Attach(IntPtr hwnd, out string detail)
    {
        detail = "";
        try
        {
            IntPtr host = FindWallpaperWindow();
            if (host == IntPtr.Zero) { detail = "未找到桌面壁纸宿主"; return false; }

            int style = GetWindowLong(hwnd, GWL_STYLE);
            SetWindowLong(hwnd, GWL_STYLE, (style | WS_CHILD) & ~WS_POPUP);
            IntPtr old = SetParent(hwnd, host);

            GetClientRect(host, out RECT cr);
            int w = Math.Max(1, cr.Right - cr.Left);
            int h = Math.Max(1, cr.Bottom - cr.Top);
            // 关键：插入到 SHELLDLL_DefView（桌面图标）之下、壁纸之上
            IntPtr defView = FindWindowEx(host, IntPtr.Zero, "SHELLDLL_DefView", null);
            SetWindowPos(hwnd, defView, 0, 0, w, h, 0x0040 /*SWP_SHOWWINDOW*/ | 0x0020 /*SWP_FRAMECHANGED*/);

            Host = host;
            detail = $"已挂到壁纸层 ({w}×{h})";
            return GetParent(hwnd) == host;
        }
        catch (Exception ex)
        {
            detail = ex.Message;
            return false;
        }
    }

    /// <summary>保持「在图标之下、壁纸之上」的层序（壁纸软件可能插队）。</summary>
    public static void Reassert(IntPtr hwnd)
    {
        try
        {
            // Explorer 重启后父窗口会失效 → 自动重新挂载
            if (Host == IntPtr.Zero || GetParent(hwnd) != Host)
            {
                Attach(hwnd, out _);
                return;
            }
            IntPtr defView = FindWindowEx(Host, IntPtr.Zero, "SHELLDLL_DefView", null);
            GetClientRect(Host, out RECT cr);
            SetWindowPos(hwnd, defView, 0, 0, Math.Max(1, cr.Right - cr.Left), Math.Max(1, cr.Bottom - cr.Top), 0x0040);
        }
        catch { }
    }

    /// <summary>解除父子关系，恢复为普通顶层窗口。</summary>
    public static void Detach(IntPtr hwnd, Rectangle restore, bool topMost)
    {
        try
        {
            int style = GetWindowLong(hwnd, GWL_STYLE);
            SetWindowLong(hwnd, GWL_STYLE, (style | WS_POPUP) & ~WS_CHILD);
            SetParent(hwnd, IntPtr.Zero);
            SetWindowPos(hwnd, topMost ? new IntPtr(-1) : IntPtr.Zero,
                restore.X, restore.Y, restore.Width, restore.Height,
                0x0040 | 0x0020);
        }
        catch { }
        Host = IntPtr.Zero;
    }
}
