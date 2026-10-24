using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace PerfTicker;

/// <summary>PDH 性能计数器封装：只添加英文路径，避免中文系统路径差异。</summary>
internal sealed class PdhProbe : IDisposable
{
    private IntPtr _query;
    private readonly Dictionary<string, IntPtr> _counters = new();
    private readonly Dictionary<string, List<(string name, double value)>> _data = new();

    public bool IsValid { get; private set; }

    public PdhProbe()
    {
        try
        {
            if (Native.PdhOpenQueryW(null, IntPtr.Zero, out _query) == 0) IsValid = true;
        }
        catch
        {
            IsValid = false;
        }
    }

    public bool Add(string key, string englishPath)
    {
        if (!IsValid) return false;
        try
        {
            if (Native.PdhAddEnglishCounterW(_query, englishPath, IntPtr.Zero, out var counter) != 0) return false;
            _counters[key] = counter;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Collect()
    {
        if (!IsValid) return;
        try
        {
            if (Native.PdhCollectQueryData(_query) != 0) return;
            foreach (var kv in _counters)
                _data[kv.Key] = ReadArray(kv.Value);
        }
        catch
        {
            // 忽略单次采集失败
        }
    }

    private static List<(string, double)> ReadArray(IntPtr counter)
    {
        var list = new List<(string, double)>(8);
        uint fmt = Native.PDH_FMT_DOUBLE | Native.PDH_FMT_NOCAP100;
        uint size = 0, count = 0;
        uint rc = Native.PdhGetFormattedCounterArrayW(counter, fmt, ref size, out count, IntPtr.Zero);
        if (size == 0) return list;
        IntPtr buf = Marshal.AllocHGlobal((int)size);
        try
        {
            rc = Native.PdhGetFormattedCounterArrayW(counter, fmt, ref size, out count, buf);
            if (rc != 0) return list;
            int stride = Marshal.SizeOf<Native.PDH_FMT_COUNTERVALUE_ITEM_W>();
            for (int i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<Native.PDH_FMT_COUNTERVALUE_ITEM_W>(buf + i * stride);
                if (item.FmtValue.CStatus != 0 && item.FmtValue.CStatus != 1) continue;
                string name = item.SzName == IntPtr.Zero ? "" : Marshal.PtrToStringUni(item.SzName) ?? "";
                double v = item.FmtValue.DoubleValue;
                if (double.IsNaN(v) || double.IsInfinity(v)) continue;
                list.Add((name, v));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
        return list;
    }

    public double Sum(string key)
    {
        if (!_data.TryGetValue(key, out var list) || list.Count == 0) return 0;
        double v = 0;
        foreach (var it in list) v += it.value;
        return v;
    }

    public IEnumerable<(string name, double value)> Items(string key)
        => _data.TryGetValue(key, out var list) ? list : Array.Empty<(string, double)>();

    public void Dispose()
    {
        if (_query != IntPtr.Zero)
        {
            try { Native.PdhCloseQuery(_query); } catch { }
            _query = IntPtr.Zero;
        }
        IsValid = false;
    }
}

/// <summary>CPU：整机/每核心占用（NtQuerySystemInformation）+ 实时频率（CallNtPowerInformation）。</summary>
internal sealed class CpuProbe
{
    private long[] _prevIdle = Array.Empty<long>();
    private long[] _prevTotal = Array.Empty<long>();
    private IntPtr _perfBuf;
    private int _perfBufLen;
    private IntPtr _pwrBuf;
    private int _pwrBufLen;
    private uint _nominalMhz;

    public int Cores { get; private set; } = 1;
    public string Name { get; private set; } = "处理器";

    public void Init(int cores)
    {
        Cores = Math.Max(1, cores);
        _prevIdle = new long[Cores];
        _prevTotal = new long[Cores];
        int perSize = Marshal.SizeOf<Native.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION>();
        _perfBufLen = perSize * Cores;
        _perfBuf = Marshal.AllocHGlobal(_perfBufLen);
        _pwrBufLen = Marshal.SizeOf<Native.PROCESSOR_POWER_INFORMATION>() * Cores;
        _pwrBuf = Marshal.AllocHGlobal(_pwrBufLen);
        Name = ReadCpuName();
        _nominalMhz = ReadNominalMhz();
    }

    private static string ReadCpuName()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            var s = k?.GetValue("ProcessorNameString") as string;
            if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
        }
        catch { }
        return Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "处理器";
    }

    private static uint ReadNominalMhz()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            if (k?.GetValue("~MHz") is int mhz && mhz > 0) return (uint)mhz;
        }
        catch { }
        return 0;
    }

    public void Sample(Snapshot s, PdhProbe pdh)
    {
        if (_perfBuf == IntPtr.Zero) return;
        if (Native.NtQuerySystemInformation(Native.SystemProcessorPerformanceInformation,
                _perfBuf, _perfBufLen, out _) != 0) return;

        int stride = Marshal.SizeOf<Native.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION>();
        var pct = new double[Cores];
        double sum = 0;
        for (int i = 0; i < Cores; i++)
        {
            var info = Marshal.PtrToStructure<Native.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION>(_perfBuf + i * stride);
            long idle = info.IdleTime;
            long total = info.KernelTime + info.UserTime;
            long dIdle = idle - _prevIdle[i];
            long dTotal = total - _prevTotal[i];
            _prevIdle[i] = idle;
            _prevTotal[i] = total;
            double u = dTotal > 0 ? (1.0 - (double)dIdle / dTotal) * 100.0 : 0;
            if (u < 0) u = 0;
            if (u > 100) u = 100;
            pct[i] = u;
            sum += u;
        }
        s.CorePct = pct;
        s.CpuPct = sum / Cores;

        // 频率
        var mhz = new double[Cores];
        if (_pwrBuf != IntPtr.Zero &&
            Native.CallNtPowerInformation(Native.ProcessorInformation, IntPtr.Zero, 0, _pwrBuf, _pwrBufLen) == 0)
        {
            int pstride = Marshal.SizeOf<Native.PROCESSOR_POWER_INFORMATION>();
            for (int i = 0; i < Cores; i++)
            {
                var pi = Marshal.PtrToStructure<Native.PROCESSOR_POWER_INFORMATION>(_pwrBuf + i * pstride);
                mhz[i] = pi.CurrentMhz;
                if (pi.MaxMhz > _nominalMhz) _nominalMhz = pi.MaxMhz;
            }
        }

        // 优先：PDH % Processor Performance × 标称频率（与任务管理器「速度」口径一致）
        if (pdh != null && pdh.IsValid && _nominalMhz > 200)
        {
            bool any = false;
            foreach (var (name, value) in pdh.Items("cpuPerf"))
            {
                if (name == "_Total") continue;
                int comma = name.LastIndexOf(',');
                if (comma < 0) continue;
                if (!int.TryParse(name.AsSpan(comma + 1), out int ci) || ci < 0 || ci >= Cores) continue;
                double m = _nominalMhz * value / 100.0;
                m = Math.Clamp(m, _nominalMhz * 0.35, _nominalMhz * 2.10);
                mhz[ci] = m;
                any = true;
            }
            if (!any)
            {
                double total = pdh.Sum("cpuPerf");
                foreach (var (name, value) in pdh.Items("cpuPerf"))
                    if (name == "_Total") total = value;
                if (total > 5)
                {
                    double m = _nominalMhz * total / 100.0;
                    for (int i = 0; i < Cores; i++) mhz[i] = m;
                }
            }
        }

        double avg = 0, max = 0;
        for (int i = 0; i < Cores; i++)
        {
            avg += mhz[i];
            if (mhz[i] > max) max = mhz[i];
        }
        avg /= Cores;

        if (avg < 100 && pdh != null && pdh.IsValid)
        {
            double pf = pdh.Sum("cpuFreq");
            if (pf > 100)
            {
                avg = pf;
                for (int i = 0; i < Cores; i++) mhz[i] = pf;
            }
        }

        s.CoreMHz = mhz;
        s.CpuAvgMHz = avg;
        s.CpuMaxMHz = max;
        s.CpuPeakMHz = _nominalMhz > 0 ? _nominalMhz : max;
        s.CpuName = Name;
    }
}

/// <summary>GPU：NVML（NVIDIA 驱动自带的 nvml.dll，运行时动态加载）+ PDH GPU Engine 兜底。</summary>
internal sealed class GpuProbe : IDisposable
{
    // NVML 委托
    private delegate int NvInit();
    private delegate int NvShutdown();
    private delegate int NvGetHandle(uint index, out IntPtr dev);
    private delegate int NvGetName(IntPtr dev, StringBuilder name, uint len);
    private delegate int NvUtil(IntPtr dev, ref NvUtilization u);
    private delegate int NvClock(IntPtr dev, uint type, ref uint mhz);
    private delegate int NvMem(IntPtr dev, ref NvMemory m);
    private delegate int NvU32(IntPtr dev, ref uint value);
    private delegate int NvTemp(IntPtr dev, uint sensor, ref uint value);

    [StructLayout(LayoutKind.Sequential)]
    private struct NvUtilization { public uint Gpu; public uint Memory; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NvMemory { public ulong Total; public ulong Free; public ulong Used; }

    private IntPtr _lib = IntPtr.Zero;
    private IntPtr _dev = IntPtr.Zero;
    private NvGetHandle _getHandle;
    private NvUtil _util;
    private NvClock _clock, _maxClock;
    private NvMem _mem;
    private NvU32 _fan, _power, _powerLimit;
    private NvTemp _temp;
    private bool _init;
    private string _name = "图形处理器";
    public bool Available => _init && _dev != IntPtr.Zero;

    public void Init()
    {
        try
        {
            if (!NativeLibrary.TryLoad("nvml.dll", out _lib)) return;
            var init = Load<NvInit>("nvmlInit_v2") ?? Load<NvInit>("nvmlInit");
            _getHandle = Load<NvGetHandle>("nvmlDeviceGetHandleByIndex_v2") ?? Load<NvGetHandle>("nvmlDeviceGetHandleByIndex");
            var getName = Load<NvGetName>("nvmlDeviceGetName");
            _util = Load<NvUtil>("nvmlDeviceGetUtilizationRates");
            _clock = Load<NvClock>("nvmlDeviceGetClockInfo");
            _maxClock = Load<NvClock>("nvmlDeviceGetMaxClockInfo");
            _mem = Load<NvMem>("nvmlDeviceGetMemoryInfo");
            _fan = Load<NvU32>("nvmlDeviceGetFanSpeed");
            _power = Load<NvU32>("nvmlDeviceGetPowerUsage");
            _powerLimit = Load<NvU32>("nvmlDeviceGetEnforcedPowerLimit");
            _temp = Load<NvTemp>("nvmlDeviceGetTemperature");

            if (init == null || _getHandle == null || _util == null) return;
            if (init() != 0) return;
            if (_getHandle(0, out _dev) != 0) { _dev = IntPtr.Zero; return; }
            if (getName != null)
            {
                var sb = new StringBuilder(96);
                if (getName(_dev, sb, 96) == 0) _name = sb.ToString().Trim();
            }
            _init = true;
        }
        catch
        {
            _init = false;
        }
    }

    private T Load<T>(string name) where T : Delegate
    {
        if (_lib == IntPtr.Zero) return null;
        if (!NativeLibrary.TryGetExport(_lib, name, out var p) || p == IntPtr.Zero) return null;
        try { return Marshal.GetDelegateForFunctionPointer<T>(p); } catch { return null; }
    }

    public void Sample(Snapshot s, PdhProbe pdh)
    {
        var g = s.Gpu;
        g.Name = _name;
        g.Nvml = Available;

        double pdhUtil = 0;
        if (pdh != null && pdh.IsValid)
        {
            pdhUtil = pdh.Sum("gpuUtil");
            if (pdhUtil > 100) pdhUtil = 100;
        }

        if (!Available)
        {
            g.Available = pdh != null && pdh.IsValid && pdhUtil > 0;
            g.UtilPct = pdhUtil;
            g.Name = _name;
            return;
        }

        g.Available = true;
        var u = new NvUtilization();
        if (_util(_dev, ref u) == 0)
        {
            g.UtilPct = Math.Max(u.Gpu, pdhUtil);
            g.MemUtilPct = u.Memory;
        }
        else
        {
            g.UtilPct = pdhUtil;
        }

        uint v = 0;
        if (_clock != null && _clock(_dev, 1, ref v) == 0) g.ClockMHz = v;   // SM/graphics
        if (_clock != null && _clock(_dev, 2, ref v) == 0) g.MemClockMHz = v;
        if (_maxClock != null && _maxClock(_dev, 1, ref v) == 0) g.MaxClockMHz = v;

        var m = new NvMemory();
        if (_mem != null && _mem(_dev, ref m) == 0 && m.Total > 0)
        {
            g.MemTotalMB = m.Total / 1048576.0;
            g.MemUsedMB = m.Used / 1048576.0;
        }
        if (_temp != null && _temp(_dev, 0, ref v) == 0) g.TempC = v;
        if (_fan != null && _fan(_dev, ref v) == 0) g.FanPct = v;
        if (_power != null && _power(_dev, ref v) == 0) g.PowerW = v / 1000.0;
        if (_powerLimit != null && _powerLimit(_dev, ref v) == 0) g.PowerLimitW = v / 1000.0;

        if (g.ClockMHz <= 0 && pdh != null && pdh.IsValid) g.ClockMHz = 0;
    }

    public void Dispose()
    {
        try
        {
            var sd = Load<NvShutdown>("nvmlShutdown");
            if (_init && sd != null) sd();
        }
        catch { }
        _init = false;
        _dev = IntPtr.Zero;
    }
}

/// <summary>进程快照：一次 NtQuerySystemInformation 拿到全部进程的 CPU 时间 / 工作集 / 线程 / 句柄。</summary>
internal sealed class ProcessProbe
{
    private IntPtr _buf;
    private int _bufLen = 1 << 20;
    private readonly Dictionary<int, long> _prevCpu = new(512);
    private long _prevWall;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public void Sample(Snapshot s, int cores, Dictionary<int, double> gpuByPid)
    {
        if (_buf == IntPtr.Zero) _buf = Marshal.AllocHGlobal(_bufLen);

        int rc = Native.NtQuerySystemInformation(Native.SystemProcessInformation, _buf, _bufLen, out int need);
        for (int attempt = 0; attempt < 6 && rc != 0; attempt++)
        {
            int newLen = Math.Max(_bufLen * 2, need + (1 << 20));
            _buf = Marshal.ReAllocHGlobal(_buf, (IntPtr)newLen);
            _bufLen = newLen;
            rc = Native.NtQuerySystemInformation(Native.SystemProcessInformation, _buf, _bufLen, out need);
        }
        if (rc != 0) return;

        long now = _clock.ElapsedMilliseconds;
        double wall = _prevWall == 0 ? 0 : (now - _prevWall) / 1000.0;
        _prevWall = now;

        var list = new List<ProcSample>(512);
        long threads = 0, handles = 0;
        IntPtr p = _buf;
        var seen = new HashSet<int>(512);

        while (true)
        {
            var info = Marshal.PtrToStructure<Native.SYSTEM_PROCESS_INFORMATION>(p);
            int pid = info.UniqueProcessId.ToInt32();
            string name = info.ImageName.Buffer == IntPtr.Zero
                ? (pid == 0 ? "Idle" : pid == 4 ? "System" : $"PID {pid}")
                : Marshal.PtrToStringUni(info.ImageName.Buffer, info.ImageName.Length / 2) ?? "";
            long cpu = info.KernelTime + info.UserTime;

            double cpuPct = 0;
            if (wall > 0.001 && _prevCpu.TryGetValue(pid, out long prev))
            {
                double delta = (cpu - prev) / 10000000.0;              // 秒
                cpuPct = Math.Clamp(delta / (wall * cores) * 100.0, 0, 100.0);   // 占整机百分比
            }
            _prevCpu[pid] = cpu;
            seen.Add(pid);

            double memMB = info.WorkingSetSize.ToInt64() / 1048576.0;
            threads += info.NumberOfThreads;
            handles += info.HandleCount;

            var ps = new ProcSample
            {
                Pid = pid,
                Name = name,
                CpuPct = cpuPct,
                CpuNorm = cpuPct / cores,
                MemMB = memMB,
                Threads = (int)info.NumberOfThreads,
                Handles = (int)info.HandleCount,
            };
            if (gpuByPid != null && gpuByPid.TryGetValue(pid, out double gp)) ps.GpuPct = gp;
            if (pid != 0) list.Add(ps);

            if (info.NextEntryOffset == 0) break;
            p += (int)info.NextEntryOffset;
        }

        foreach (var stale in _prevCpu.Keys.Where(k => !seen.Contains(k)).ToList())
            _prevCpu.Remove(stale);

        list.Sort((a, b) => b.CpuPct.CompareTo(a.CpuPct));
        s.Procs = list;
        s.ProcCount = list.Count;
        s.Threads = threads;
        s.Handles = handles;
    }
}

/// <summary>统一采样器：后台线程按周期采集，产出不可变快照。</summary>
internal sealed class Sampler : IDisposable
{
    private readonly PdhProbe _pdh = new();
    private readonly CpuProbe _cpu = new();
    private readonly GpuProbe _gpu = new();
    private readonly ProcessProbe _proc = new();
    private readonly Thread _thread;
    private readonly object _lock = new();
    private Snapshot _latest = new();
    private volatile bool _stop;
    private int _intervalMs = 1000;
    private long _tick;
    private readonly Stopwatch _up = Stopwatch.StartNew();
    private readonly Stopwatch _ctxSw = Stopwatch.StartNew();
    private double _prevCtx = -1;
    private long _prevCtxMs;

    public int Cores { get; }
    public string CpuName => _cpu.Name;

    public Sampler()
    {
        var si = new Native.SYSTEM_INFO();
        Native.GetSystemInfo(out si);
        Cores = (int)Math.Max(1, si.dwNumberOfProcessors);
        _cpu.Init(Cores);
        _gpu.Init();

        _pdh.Add("cpuFreq", @"\Processor Information(_Total)\Processor Frequency");
        _pdh.Add("cpuPerf", @"\Processor Information(*)\% Processor Performance");
        _pdh.Add("gpuUtil", @"\GPU Engine(*)\Utilization Percentage");
        _pdh.Add("diskRead", @"\PhysicalDisk(_Total)\Disk Read Bytes/sec");
        _pdh.Add("diskWrite", @"\PhysicalDisk(_Total)\Disk Write Bytes/sec");
        _pdh.Add("diskBusy", @"\PhysicalDisk(_Total)\% Disk Time");
        _pdh.Add("netRx", @"\Network Interface(*)\Bytes Received/sec");
        _pdh.Add("netTx", @"\Network Interface(*)\Bytes Sent/sec");
        _pdh.Add("ctxSw", @"\System\Context Switches/sec");
        _pdh.Collect();  // 预热（速率型计数器需要两次采样）

        _thread = new Thread(Loop) { IsBackground = true, Name = "PerfTicker.Sampler", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    public int IntervalMs
    {
        get => Volatile.Read(ref _intervalMs);
        set => Volatile.Write(ref _intervalMs, Math.Clamp(value, 250, 20000));
    }

    public Snapshot Latest
    {
        get { lock (_lock) return _latest; }
    }

    private void Loop()
    {
        var sw = Stopwatch.StartNew();
        long next = 0;
        while (!_stop)
        {
            try
            {
                Sample();
            }
            catch
            {
                // 单次采样异常不影响后续
            }
            next += IntervalMs;
            long wait = next - sw.ElapsedMilliseconds;
            if (wait < 20) { next = sw.ElapsedMilliseconds; wait = 60; }
            Thread.Sleep((int)wait);
        }
    }

    private void Sample()
    {
        var s = new Snapshot { Tick = Interlocked.Increment(ref _tick), WallSeconds = _up.Elapsed.TotalSeconds };
        _pdh.Collect();

        _cpu.Sample(s, _pdh);

        var mem = Native.MEMORYSTATUSEX.Create();
        if (Native.GlobalMemoryStatusEx(ref mem))
        {
            s.MemTotalGB = mem.ullTotalPhys / 1073741824.0;
            s.MemUsedGB = (mem.ullTotalPhys - mem.ullAvailPhys) / 1073741824.0;
            s.MemPct = s.MemTotalGB > 0 ? s.MemUsedGB / s.MemTotalGB * 100.0 : 0;
            s.PageTotalGB = mem.ullTotalPageFile / 1073741824.0;
            s.PageUsedGB = (mem.ullTotalPageFile - mem.ullAvailPageFile) / 1073741824.0;
        }

        _gpu.Sample(s, _pdh);

        s.DiskReadMBps = Math.Max(0, _pdh.Sum("diskRead")) / 1048576.0;
        s.DiskWriteMBps = Math.Max(0, _pdh.Sum("diskWrite")) / 1048576.0;
        s.DiskBusyPct = Math.Clamp(_pdh.Sum("diskBusy") / Math.Max(1, Environment.ProcessorCount) * 0, 0, 100);
        s.NetRxMBps = Math.Max(0, _pdh.Sum("netRx")) / 1048576.0;
        s.NetTxMBps = Math.Max(0, _pdh.Sum("netTx")) / 1048576.0;

        double ctx = _pdh.Sum("ctxSw");
        if (ctx <= 0)
        {
            long ms = _ctxSw.ElapsedMilliseconds;
            if (_prevCtx >= 0 && ms > _prevCtxMs)
            {
                // 无 PDH 时用进程切换量粗估
                ctx = 0;
            }
            _prevCtxMs = ms;
        }
        s.CtxSwitchPerSec = ctx;
        s.UptimeSeconds = Environment.TickCount64 / 1000.0;

        var gpuByPid = new Dictionary<int, double>(64);
        if (_pdh.IsValid)
        {
            var acc = new Dictionary<int, double>(64);
            foreach (var (name, value) in _pdh.Items("gpuUtil"))
            {
                int pid = ParsePid(name);
                if (pid <= 0) continue;
                acc.TryGetValue(pid, out double cur);
                acc[pid] = cur + value;
            }
            foreach (var kv in acc) gpuByPid[kv.Key] = Math.Min(100, kv.Value);
        }

        _proc.Sample(s, Cores, gpuByPid);

        lock (_lock) _latest = s;
    }

    private static int ParsePid(string instance)
    {
        // 例：pid_12345_luid_0x00000000_0x0000C39F_phys_0_eng_0_engtype_3D
        if (string.IsNullOrEmpty(instance)) return -1;
        int i = instance.IndexOf("pid_", StringComparison.Ordinal);
        if (i < 0) return -1;
        i += 4;
        int j = i;
        while (j < instance.Length && char.IsDigit(instance[j])) j++;
        if (j == i) return -1;
        return int.TryParse(instance.AsSpan(i, j - i), out int pid) ? pid : -1;
    }

    public void Dispose()
    {
        _stop = true;
        try { _thread.Join(400); } catch { }
        _gpu.Dispose();
        _pdh.Dispose();
    }
}
