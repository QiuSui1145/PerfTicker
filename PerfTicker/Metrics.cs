using System.Globalization;

namespace PerfTicker;

internal struct Tick
{
    public long Ms;
    public double V;
    public double Vol;
}

internal sealed class Candle
{
    public long Bucket;
    public double O, H, L, C, Vol;
}

/// <summary>一条「个股」：把一项性能指标包装成行情品种（带分时、K 线、涨跌幅）。</summary>
internal sealed class Metric
{
    public string Code = "";
    public string Name = "";
    public string Group = "";
    public string Unit = "";
    public int Decimals = 2;
    public Func<Snapshot, double> Read;

    public readonly List<Tick> Ticks = new(4096);
    public readonly List<Candle> Candles = new(512);

    public double Value;
    public double Prev;
    public double Open;
    public double High = double.MinValue;
    public double Low = double.MaxValue;
    public double Sum;
    public int Count;

    public double FlashUntil;
    public int FlashDir;
    /// <summary>用于显示的平滑值（逐帧逼近 Value，做出行情跳动的过渡感）。</summary>
    public double Shown;

    public bool Tween()
    {
        double d = Value - Shown;
        if (Math.Abs(d) < 1e-6) { Shown = Value; return false; }
        Shown += d * 0.42;
        if (Math.Abs(Value - Shown) < 1e-4) Shown = Value;
        return true;
    }

    public double ShownChg => Shown - Open;
    public string ShownText => Fmt(Shown);

    public double Chg => Value - Open;
    public double ChgPct => Math.Abs(Open) > 1e-9 ? (Value - Open) / Math.Abs(Open) * 100.0 : 0;
    public double HighPct => Math.Abs(Open) > 1e-9 ? (High - Open) / Math.Abs(Open) * 100.0 : 0;
    public double LowPct => Math.Abs(Open) > 1e-9 ? (Low - Open) / Math.Abs(Open) * 100.0 : 0;

    /// <summary>涨跌额：百分比指标用「pt」，计数用整数，其余用自带单位。</summary>
    public string DeltaText(double d)
    {
        string sign = d >= 0 ? "+" : "-";
        switch (Unit)
        {
            case "%": return sign + Math.Abs(d).ToString("0.00", CultureInfo.InvariantCulture) + "pt";
            case "": return sign + Math.Abs(d).ToString("0", CultureInfo.InvariantCulture);
            default:
                return sign + Math.Abs(d).ToString(Decimals <= 0 ? "0" : "0.00", CultureInfo.InvariantCulture) + Unit;
        }
    }

    public string ChgText => DeltaText(Chg);
    public string ShownChgText => DeltaText(ShownChg);

    public double ChgValue => Unit == "%" ? Chg : ChgPct;

    public string Fmt(double v)
    {
        double a = Math.Abs(v);
        if (a >= 10000) return v.ToString("N0", CultureInfo.InvariantCulture);
        return v.ToString("F" + Decimals, CultureInfo.InvariantCulture);
    }

    public string Display => Fmt(Value);

    public void Push(double v, long ms, double vol)
    {
        if (double.IsNaN(v) || double.IsInfinity(v)) return;
        Prev = Count == 0 ? v : Value;
        Value = v;
        if (Count == 0) { Open = v; High = v; Low = v; }
        if (v > High) High = v;
        if (v < Low) Low = v;
        Sum += v;
        Count++;
        // 开盘价用前 6 个采样的均值平滑，避免首个样本异常导致涨跌幅失真
        if (Count <= 60) Open = Sum / Count;
        if (Math.Abs(v - Prev) > 1e-9 && Count > 1)
        {
            FlashDir = v > Prev ? 1 : -1;
            FlashUntil = ms + 700;
        }
        if (Count <= 1) Shown = v;
        Ticks.Add(new Tick { Ms = ms, V = v, Vol = vol });
        if (Ticks.Count > 6000) Ticks.RemoveRange(0, 1500);
    }

    public void RebuildCandles(long periodMs)
    {
        Candles.Clear();
        if (periodMs < 1) periodMs = 1000;
        long curBucket = -1;
        Candle c = null;
        foreach (var t in Ticks)
        {
            long b = t.Ms / periodMs;
            if (c == null || b != curBucket)
            {
                if (c != null) { Candles.Add(c); }
                c = new Candle { Bucket = b, O = t.V, H = t.V, L = t.V, C = t.V, Vol = t.Vol };
                curBucket = b;
            }
            else
            {
                if (t.V > c.H) c.H = t.V;
                if (t.V < c.L) c.L = t.V;
                c.C = t.V;
                c.Vol += t.Vol;
            }
        }
        if (c != null) Candles.Add(c);
        if (Candles.Count > 900) Candles.RemoveRange(0, Candles.Count - 900);
    }

    public double[] Spark(int n)
    {
        n = Math.Min(n, Ticks.Count);
        if (n <= 0) return Array.Empty<double>();
        var arr = new double[n];
        for (int i = 0; i < n; i++) arr[i] = Ticks[Ticks.Count - n + i].V;
        return arr;
    }
}

internal static class Catalog
{
    public static List<Metric> Build(int cores)
    {
        var list = new List<Metric>(64);

        void Add(string code, string name, string group, string unit, int dec, string title, Func<Snapshot, double> read)
        {
            list.Add(new Metric { Code = code, Name = name, Group = group, Unit = unit, Decimals = dec, Read = read });
            list[^1].Name = name;
        }

        // 指数（三大指数）
        Add("000001", "CPU 综合指数", "指数", "GHz", 2, "处理器实时平均主频", s => s.CpuAvgMHz / 1000.0);
        Add("000002", "CPU 负载", "指数", "%", 1, "整机 CPU 占用率", s => s.CpuPct);
        Add("000003", "内存占用", "指数", "%", 1, "物理内存占用率", s => s.MemPct);

        // 处理器各核心
        for (int i = 0; i < cores; i++)
        {
            int idx = i;
            Add(("0001" + (i + 1).ToString("D2")), $"核心 {i + 1:D2}", "处理器", "GHz", 2,
                $"逻辑处理器 {i + 1} 实时主频", s => idx < s.CoreMHz.Length ? s.CoreMHz[idx] / 1000.0 : 0);
        }

        // 图形
        Add("600519", "GPU 核心频率", "图形", "MHz", 0, "显卡核心实时频率", s => s.Gpu.ClockMHz);
        Add("600520", "GPU 显存频率", "图形", "MHz", 0, "显存实时频率", s => s.Gpu.MemClockMHz);
        Add("600521", "GPU 负载", "图形", "%", 1, "显卡引擎占用率", s => s.Gpu.UtilPct);
        Add("600522", "显存占用", "图形", "%", 1, "显存使用率", s => s.Gpu.MemTotalMB > 0 ? s.Gpu.MemUsedMB / s.Gpu.MemTotalMB * 100.0 : 0);
        Add("600523", "GPU 温度", "图形", "°C", 0, "显卡核心温度", s => s.Gpu.TempC);
        Add("600524", "GPU 功耗", "图形", "W", 1, "显卡实时功耗", s => s.Gpu.PowerW);
        Add("600525", "GPU 风扇", "图形", "%", 0, "显卡风扇转速百分比", s => s.Gpu.FanPct);

        // 内存
        Add("000858", "已用内存", "内存", "GB", 2, "物理内存使用量", s => s.MemUsedGB);
        Add("000859", "提交内存", "内存", "GB", 2, "系统提交大小（含页面文件）", s => s.PageUsedGB);

        // 存储 / 网络
        Add("601398", "磁盘读取", "存储网络", "MB/s", 2, "物理磁盘读取吞吐", s => s.DiskReadMBps);
        Add("601399", "磁盘写入", "存储网络", "MB/s", 2, "物理磁盘写入吞吐", s => s.DiskWriteMBps);
        Add("600036", "网络下行", "存储网络", "MB/s", 2, "网络接收吞吐", s => s.NetRxMBps);
        Add("600037", "网络上行", "存储网络", "MB/s", 2, "网络发送吞吐", s => s.NetTxMBps);

        // 系统
        Add("000300", "上下文切换", "系统", "K/s", 1, "每秒上下文切换次数", s => s.CtxSwitchPerSec / 1000.0);
        Add("000301", "进程数", "系统", "", 0, "当前进程总数", s => s.ProcCount);
        Add("000302", "线程数", "系统", "", 0, "当前线程总数", s => s.Threads);
        Add("000303", "句柄数", "系统", "", 0, "当前句柄总数", s => s.Handles);

        return list;
    }

    public static IReadOnlyList<string> GroupOrder = new[]
    {
        "指数", "处理器", "图形", "内存", "存储网络", "系统"
    };
}
