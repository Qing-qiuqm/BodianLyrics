using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

static class Program
{
    public const string VERSION = "1.0.0";

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
        instanceMutex = new Mutex(true, "BodianLyrics_SingleInstance", out created);
        if (!created)
        {
            MessageBox.Show("波点音乐任务栏歌词已在运行中。", "BodianLyrics",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        TrayApp app = new TrayApp();
        app.Start();
        Application.Run();
    }

    static Mutex instanceMutex;

    /// <summary>重启前释放单实例互斥体，否则新进程会被判定为重复启动</summary>
    public static void ReleaseInstanceMutex()
    {
        Mutex m = instanceMutex;
        instanceMutex = null;
        if (m == null) return;
        try
        {
            m.ReleaseMutex();
            m.Close();
        }
        catch { }
    }
}
