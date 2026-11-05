using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

static class Program
{
    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

    [STAThread]
    static void Main(string[] args)
    {
        try { SetProcessDpiAwarenessContext(new IntPtr(-4)); }
        catch
        {
            try { SetProcessDPIAware(); }
            catch { }
        }

        if (args.Length > 0 && args[0] == "--probe")
        {
            Probe.Run();
            return;
        }

        bool created = false;
        Mutex mutex = new Mutex(true, "BodianLyrics_SingleInstance", out created);
        if (!created) return;

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        TrayApp app = new TrayApp();
        app.Start();
        Application.Run();
    }
}
