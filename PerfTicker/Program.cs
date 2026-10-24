namespace PerfTicker;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, @"Local\PerfTicker.SingleInstance.v1", out bool created);
        if (!created)
        {
            MessageBox.Show("性能行情终端已在运行中。", "PerfTicker",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new AppForm());
    }
}
