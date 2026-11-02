using System.Runtime.InteropServices;

namespace PerfTicker;

internal static class Program
{
    private const string WakeMessage = "PerfTicker.Wake.v1";
    private static readonly IntPtr HWND_BROADCAST = new(0xFFFF);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string name);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, @"Local\PerfTicker.SingleInstance.v1", out bool created);
        if (!created)
        {
            // 已有实例：广播唤醒消息（首实例收到后会把窗口从托盘唤出），然后自身退出
            try
            {
                uint msg = RegisterWindowMessage(WakeMessage);
                if (msg != 0) PostMessage(HWND_BROADCAST, msg, IntPtr.Zero, IntPtr.Zero);
            }
            catch { }
            return;
        }

        Diag.Init();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // 兜底：任何未捕获异常都进日志，不静默消失
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (s, e) => Diag.Error("UI-THREAD", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (s, e) => Diag.Error("DOMAIN", e.ExceptionObject as Exception);

        Application.Run(new AppForm());
    }

    internal static uint RegisterWakeMessage() => RegisterWindowMessage(WakeMessage);
}
