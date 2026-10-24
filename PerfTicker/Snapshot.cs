namespace PerfTicker;

internal sealed class ProcSample
{
    public int Pid;
    public string Name = "";
    public double CpuPct;      // 单核百分比（100 = 占满一颗核心）
    public double GpuPct;      // GPU 引擎占用百分比
    public double MemMB;       // 工作集
    public int Threads;
    public int Handles;
    public double CpuNorm;     // 占整机百分比
    public double CpuDelta;    // 相对上一采样的 CPU% 变化（「涨跌」）
}

internal sealed class GpuInfo
{
    public bool Available;
    public string Name = "图形处理器";
    public double UtilPct;
    public double MemUtilPct;
    public double ClockMHz;
    public double MemClockMHz;
    public double MaxClockMHz;
    public double MemUsedMB;
    public double MemTotalMB;
    public double TempC;
    public double PowerW;
    public double PowerLimitW;
    public double FanPct;
    public bool Nvml;
}

internal sealed class Snapshot
{
    public long Tick;          // 单调递增的采样序号
    public double WallSeconds;

    public double CpuPct;                 // 整机
    public double[] CorePct = Array.Empty<double>();
    public double[] CoreMHz = Array.Empty<double>();
    public double CpuAvgMHz;
    public double CpuMaxMHz;
    public double CpuPeakMHz;             // 硬件最大频率（标称）
    public string CpuName = "处理器";

    public double MemUsedGB, MemTotalGB, MemPct;
    public double PageUsedGB, PageTotalGB;

    public GpuInfo Gpu = new();

    public double DiskReadMBps, DiskWriteMBps, DiskBusyPct;
    public double NetRxMBps, NetTxMBps;

    public double CtxSwitchPerSec;
    public long Threads, Handles, ProcCount;
    public double UptimeSeconds;

    public List<ProcSample> Procs = new();
}
