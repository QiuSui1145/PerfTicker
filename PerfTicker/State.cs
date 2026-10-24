using System.Drawing;

namespace PerfTicker;

internal sealed class TapeEvent
{
    public long Ms;
    public string Time = "";
    public string Text = "";
    public Color Color;
}

internal sealed class ProcHist
{
    public readonly double[] V = new double[36];
    public int Len;

    public void Push(double x)
    {
        Array.Copy(V, 1, V, 0, V.Length - 1);
        V[^1] = x;
        if (Len < V.Length) Len++;
    }

    public double[] Snapshot()
    {
        if (Len <= 0) return Array.Empty<double>();
        var a = new double[Len];
        Array.Copy(V, V.Length - Len, a, 0, Len);
        return a;
    }
}

internal sealed class AppState
{
    public List<Metric> Metrics = new();
    public Snapshot Snap = new();

    public int SelectedIndex = 1;
    public Metric Selected => Metrics[Math.Clamp(SelectedIndex, 0, Metrics.Count - 1)];

    public long PeriodMs = 5000;
    public int IntervalMs = 1000;
    public bool Paused;
    public bool TopMost;
    public bool RedUp = true;
    public bool Marquee = true;

    public readonly List<TapeEvent> Tape = new(256);
    public readonly Dictionary<int, ProcHist> ProcHist = new(512);
    public List<ProcSample> Procs = new();

    public int WatchScroll;
    public bool SearchActive;
    public string ProcFilter = "";
    public readonly HashSet<string> Pinned = new(StringComparer.OrdinalIgnoreCase);
    public int ProcScroll;
    public int ProcSort;        // 0=名称 1=CPU 2=GPU 3=内存 4=线程
    public bool ProcSortDesc = true;
    public int HoverProc = -1;
    public int SelectedProc = -1;

    public double Frame;        // 动画相位（秒）
    public long StartMs = Environment.TickCount64;
    public string CpuName = "";
    public string GpuName = "";

    public double VolPerSample;
    public readonly Economy Economy = new();

    public Metric ByCode(string code) => Metrics.FirstOrDefault(m => m.Code == code);

    public void ApplyColorMode()
    {
        if (RedUp)
        {
            Theme.Up = Color.FromArgb(0xFF, 0x4D, 0x4F);
            Theme.Down = Color.FromArgb(0x00, 0xC0, 0x8B);
        }
        else
        {
            Theme.Up = Color.FromArgb(0x00, 0xC0, 0x8B);
            Theme.Down = Color.FromArgb(0xFF, 0x4D, 0x4F);
        }
    }
}
