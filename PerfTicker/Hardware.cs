using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PerfTicker;

/// <summary>硬件探测：识别实际设备并按算力给出「营收基准权重」，让不同机器的手感可比。</summary>
internal sealed class Hardware
{
    public string CpuName = "处理器";
    public int LogicalCores = 1;
    public int PhysicalCores;
    public double BaseMhz;
    public double MaxMhz;

    public string GpuName = "图形处理器";
    public double GpuMemGB;
    public double GpuMaxMhz;
    public bool HasGpu;

    public double MemTotalGB;
    public int FixedDisks = 1;
    public double NetMaxGbps;

    public string OsName = "Windows";
    public string OsBuild = "";
    public string Arch = "";

    // 营收基准权重
    public double Wcpu = 1, Wgpu = 1, Wmem = 1, Wdisk = 1, Wnet = 1;

    public string CpuLine => $"{CpuName} · {(PhysicalCores > 0 ? $"{PhysicalCores}C/{LogicalCores}T" : $"{LogicalCores}T")} · {BaseMhz / 1000.0:0.0}GHz{(MaxMhz > BaseMhz + 50 ? $" (睿频 {MaxMhz / 1000.0:0.0}GHz)" : "")}";

    public string GpuLine => HasGpu
        ? $"{GpuName}{(GpuMemGB > 0 ? $" · {GpuMemGB:0.#}GB" : "")}"
        : $"{GpuName} · 未检测到独显";

    public string MemLine => $"{MemTotalGB:0.#}GB 内存 · 固定磁盘 ×{FixedDisks}" +
                             (NetMaxGbps > 0 ? $" · 网络 {NetMaxGbps:0.#}Gbps" : "");

    public string OsLine => $"{OsName} {OsBuild} · {Arch} · .NET {Environment.Version}";

    public string WeightLine =>
        $"CPU ×{Wcpu:0.00} ｜ GPU ×{Wgpu:0.00} ｜ 内存 ×{Wmem:0.00} ｜ 磁盘 ×{Wdisk:0.00} ｜ 网络 ×{Wnet:0.00}";

    public static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;

    /// <summary>采集：CPU / GPU / 内存 / 磁盘 / 网络 / 系统。</summary>
    public void Probe(Snapshot snap, GpuInfo gpu)
    {
        LogicalCores = Math.Max(1, Environment.ProcessorCount);
        PhysicalCores = CountPhysicalCores();
        if (PhysicalCores <= 0 || PhysicalCores > LogicalCores) PhysicalCores = LogicalCores;
        CpuName = string.IsNullOrWhiteSpace(snap.CpuName) ? "处理器" : snap.CpuName;
        BaseMhz = ReadRegistryMhz();
        MaxMhz = Math.Max(BaseMhz, snap.CpuPeakMHz);
        if (BaseMhz <= 0) BaseMhz = snap.CpuPeakMHz;

        HasGpu = gpu != null && gpu.Available;
        GpuName = gpu?.Name ?? "图形处理器";
        GpuMemGB = gpu != null ? gpu.MemTotalMB / 1024.0 : 0;
        GpuMaxMhz = gpu?.MaxClockMHz ?? 0;

        MemTotalGB = snap.MemTotalGB;
        try { FixedDisks = Math.Max(1, DriveInfo.GetDrives().Count(d => d.DriveType == DriveType.Fixed)); } catch { FixedDisks = 1; }

        try
        {
            double best = 0;
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (ni.Speed > best) best = ni.Speed;
            }
            NetMaxGbps = best / 1e9;
        }
        catch { NetMaxGbps = 0; }

        ReadOs();

        // ── 营收基准：按真实算力归一化（16 线程 / 8G 显存 / 32G 内存 / 1Gbps 为 1.0）──
        Wcpu = Clamp(LogicalCores / 16.0, 0.30, 4.0);
        Wgpu = HasGpu ? Clamp(Math.Max(GpuMemGB, 2) / 8.0, 0.30, 4.0) : 0.25;
        Wmem = Clamp(MemTotalGB / 32.0, 0.30, 4.0);
        Wdisk = Clamp(FixedDisks / 2.0, 0.50, 2.0);
        // 网络/磁盘是「吞吐」口径（MB/s 已绝对），不按链路速率放大，避免虚拟网卡虚高
        Wnet = 1.0;
    }

    private static double ReadRegistryMhz()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            if (k?.GetValue("~MHz") is int mhz && mhz > 0) return mhz;
        }
        catch { }
        return 0;
    }

    private void ReadOs()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            string product = k?.GetValue("ProductName") as string ?? "Windows";
            string display = k?.GetValue("DisplayVersion") as string ?? "";
            string build = k?.GetValue("CurrentBuild") as string ?? Environment.OSVersion.Version.Build.ToString();
            string ubr = k?.GetValue("UBR")?.ToString() ?? "";
            OsName = product.Replace("Microsoft ", "").Replace("Windows 10", "Windows 11");
            if (!string.IsNullOrEmpty(display)) OsName += " " + display;
            OsBuild = string.IsNullOrEmpty(ubr) ? build : $"{build}.{ubr}";
        }
        catch { OsBuild = Environment.OSVersion.Version.ToString(); }
        Arch = Environment.Is64BitProcess ? "x64" : "x86";
        if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64) Arch = "arm64";
    }

    /// <summary>用 GetLogicalProcessorInformationEx 数物理核心。</summary>
    private static int CountPhysicalCores()
    {
        try
        {
            int len = 0;
            Native.GetLogicalProcessorInformationEx(0, IntPtr.Zero, ref len);   // RelationProcessorCore
            if (len <= 0) return 0;
            IntPtr buf = Marshal.AllocHGlobal(len);
            try
            {
                if (!Native.GetLogicalProcessorInformationEx(0, buf, ref len)) return 0;
                int count = 0, off = 0;
                while (off + 8 <= len)
                {
                    int rel = Marshal.ReadInt32(buf, off);
                    int size = Marshal.ReadInt32(buf, off + 4);
                    if (size <= 0) break;
                    if (rel == 0) count++;      // RelationProcessorCore
                    off += size;
                }
                return count;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        catch { return 0; }
    }
}
