using System.Text.Json;

namespace PerfTicker;

/// <summary>轻量设置持久化：位置、周期、配色、选中品种等，存于 %LOCALAPPDATA%\PerfTicker。</summary>
internal sealed class Settings
{
    public int SchemaVersion { get; set; } = 1;

    public int X { get; set; } = int.MinValue;
    public int Y { get; set; } = int.MinValue;
    public int W { get; set; }
    public int H { get; set; }
    public bool Maximized { get; set; }

    public int IntervalMs { get; set; } = 1000;
    public long PeriodMs { get; set; } = 2000;
    public bool RedUp { get; set; } = true;
    public bool TopMost { get; set; }
    public bool EconomyOn { get; set; }
    public bool Gadget { get; set; }
    public int GadgetX { get; set; } = -1;
    public int GadgetY { get; set; } = -1;
    public bool AutoStart { get; set; }
    public bool Marquee { get; set; } = true;
    public int ThemeMode { get; set; } = 1;      // 0=深色 1=浅色 2=跟随系统
    public double EcoTotal { get; set; }
    public double EcoPeak { get; set; }
    public double EcoSeconds { get; set; }
    public string SelectedCode { get; set; } = "000002";
    public int ProcSort { get; set; } = 1;
    public bool ProcSortDesc { get; set; } = true;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PerfTicker", "settings.json");

    /// <summary>读取配置；损坏时备份为 .bak-时间戳 并通过 warning 返回原因。</summary>
    public static Settings Load(out string warning)
    {
        warning = null;
        if (!File.Exists(FilePath)) return new Settings();
        try
        {
            var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath));
            if (s != null)
            {
                s.SchemaVersion = Math.Max(s.SchemaVersion, 1);
                return s;
            }
            warning = "配置内容为空，已使用默认设置";
        }
        catch (Exception ex)
        {
            warning = "配置文件损坏，已备份并恢复默认";
            Diag.Error("SETTINGS", ex);
            try
            {
                string bak = FilePath + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.Copy(FilePath, bak, true);
                warning += "（" + Path.GetFileName(bak) + "）";
            }
            catch { }
        }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            string dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }),
                new System.Text.UTF8Encoding(false));
        }
        catch (Exception ex) { Diag.Error("SETTINGS-SAVE", ex); }
    }
}
