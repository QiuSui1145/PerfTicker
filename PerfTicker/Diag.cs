using System.Text;

namespace PerfTicker;

/// <summary>轻量诊断日志：滚动 3 份 × 1MB，写 %LOCALAPPDATA%\PerfTicker\logs。</summary>
internal static class Diag
{
    private static readonly object _lock = new();
    private static string _dir;
    private const long MaxBytes = 1024 * 1024;

    public static string LogDir
    {
        get
        {
            if (_dir != null) return _dir;
            _dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PerfTicker", "logs");
            return _dir;
        }
    }

    public static void Init()
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            Write("APP", $"启动 v{typeof(Diag).Assembly.GetName().Version} · {Environment.OSVersion} · {(Environment.Is64BitProcess ? "x64" : "x86")}");
        }
        catch { }
    }

    private static string CurrentFile => Path.Combine(LogDir, "perfticker.log");

    public static void Write(string tag, string message)
    {
        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(LogDir);
                Rotate();
                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{tag}] {message}{Environment.NewLine}";
                File.AppendAllText(CurrentFile, line, Encoding.UTF8);
            }
        }
        catch { }
    }

    public static void Error(string tag, Exception ex)
    {
        Write(tag, ex?.ToString() ?? "unknown error");
    }

    private static void Rotate()
    {
        var fi = new FileInfo(CurrentFile);
        if (!fi.Exists || fi.Length < MaxBytes) return;
        string b1 = Path.Combine(LogDir, "perfticker.1.log");
        string b2 = Path.Combine(LogDir, "perfticker.2.log");
        try { if (File.Exists(b2)) File.Delete(b2); } catch { }
        try { if (File.Exists(b1)) File.Move(b1, b2); } catch { }
        try { File.Move(CurrentFile, b1); } catch { }
    }
}
