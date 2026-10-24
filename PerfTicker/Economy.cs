namespace PerfTicker;

/// <summary>经济系统中的一个「经营单元」：某项占用率对应的产能与盈亏。</summary>
internal sealed class EcoPart
{
    public string Name = "";
    public double Load;        // 当前占用率 0..100
    public double LoadShown;   // 显示用平滑值
    public double Full;        // 满载收益（元/秒）
    public double Net;         // 当前净收益（元/秒，平滑显示）
    public double Total;       // 累计净收益
    public double BreakEven;   // 盈亏平衡占用率（%）
}

/// <summary>
/// 算力经营账本：每个部件按占用率产生「营收」，
/// 低于盈亏平衡点即开始亏损（空转成本），高负载越久赚得越多。
/// </summary>
internal sealed class Economy
{
    public bool Enabled;

    public double Total;       // 累计营收（元）
    public double Rate;        // 当前净收益（元/秒，平滑）
    public double PeakRate;    // 峰值净收益
    public double Seconds;     // 本时段时长
    public double TempPenalty = 1.0;

    public readonly EcoPart[] Parts =
    {
        new() { Name = "CPU 综合", Full = 22.0, BreakEven = 30 },
        new() { Name = "GPU 引擎", Full = 30.0, BreakEven = 28 },
        new() { Name = "内存", Full = 12.0, BreakEven = 35 },
        new() { Name = "磁盘 IO", Full = 7.2, BreakEven = 25 },
        new() { Name = "网络", Full = 7.2, BreakEven = 25 },
    };

    public readonly List<double> RateHist = new(240);
    private double _histAcc;
    private const double K = 1.7;   // 负载弹性：越接近满载边际收益越高

    public void Restore(double total, double peak, double seconds)
    {
        Total = total;
        PeakRate = peak;
        Seconds = seconds;
        Rate = 0;
    }

    public void Reset()
    {
        Total = 0;
        Rate = 0;
        PeakRate = 0;
        Seconds = 0;
        RateHist.Clear();
        _histAcc = 0;
        foreach (var p in Parts)
        {
            p.Load = 0;
            p.LoadShown = 0;
            p.Net = 0;
            p.Total = 0;
        }
    }

    private static double Clamp(double v) => v < 0 ? 0 : v > 100 ? 100 : v;

    /// <summary>每个部件在给定占用率下的净收益（元/秒）。</summary>
    public static double NetOf(EcoPart p, double load, double penalty)
    {
        double x = load / 100.0;
        double gross = p.Full * Math.Pow(x, K);
        double cost = p.Full * Math.Pow(p.BreakEven / 100.0, K);
        return gross * penalty - cost;
    }

    public void Accrue(double dt, Snapshot s)
    {
        if (!Enabled || dt <= 0) return;
        if (dt > 0.5) dt = 0.5;   // 防止长时间挂起后一次性灌入大量营收

        Seconds += dt;

        double cpu = Clamp(s.CpuPct);
        double gpu = s.Gpu.Available ? Clamp(s.Gpu.UtilPct) : 0;
        double mem = Clamp(s.MemPct);
        double dsk = Clamp((s.DiskReadMBps + s.DiskWriteMBps) * 10.0);
        double net = Clamp((s.NetRxMBps + s.NetTxMBps) * 20.0);
        Span<double> loads = stackalloc double[5] { cpu, gpu, mem, dsk, net };

        // 过热降频：温度越高产出打折
        double penalty = 1.0;
        if (s.Gpu.Available)
        {
            if (s.Gpu.TempC >= 84) penalty = 0.78;
            else if (s.Gpu.TempC >= 78) penalty = 0.88;
            else if (s.Gpu.TempC >= 72) penalty = 0.95;
        }
        TempPenalty = penalty;

        double sum = 0;
        for (int i = 0; i < Parts.Length; i++)
        {
            var p = Parts[i];
            double pen = i <= 1 ? penalty : 1.0;   // 只有 CPU/GPU 受温度影响
            double n = NetOf(p, loads[i], pen);
            p.Load = loads[i];
            p.LoadShown += (loads[i] - p.LoadShown) * 0.12;
            p.Net += (n - p.Net) * 0.12;
            p.Total += n * dt;
            sum += n;
        }

        Total += sum * dt;
        Rate += (sum - Rate) * 0.12;
        if (Rate > PeakRate) PeakRate = Rate;

        _histAcc += dt;
        if (_histAcc >= 1.0)
        {
            _histAcc = 0;
            RateHist.Add(Rate);
            if (RateHist.Count > 200) RateHist.RemoveAt(0);
        }
    }
}
