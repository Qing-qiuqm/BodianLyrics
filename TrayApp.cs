using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

class TrayApp
{
    Config cfg;
    PlayerState state;
    LyricsProvider provider;
    LogWatcher watcher;
    BodianController controller;
    LyricOverlay overlay;
    NotifyIcon tray;
    Timer pollTimer;
    Timer syncTimer;
    ToolStripMenuItem miAutoStart;
    SettingsForm settings;

    double oddPos = -1;
    int oddCount = 0;
    bool latencyLearned = false;
    int latencySaves = 0;

    string refSong = "";
    double refRaw = 0;
    double refRatio = 0;
    bool refValid = false;
    double latSum = 0;
    int latCount = 0;
    double prevRatio = -1;
    int sameRatio = 0;

    public void Start()
    {
        cfg = new Config();
        state = new PlayerState();
        state.SetLatency(cfg.Latency);
        provider = new LyricsProvider();
        controller = new BodianController();
        watcher = new LogWatcher(state, provider, cfg);
        overlay = new LyricOverlay(cfg, state, controller);
        overlay.ShowOverlay();

        BuildTray();

        pollTimer = new Timer();
        pollTimer.Interval = 200;
        pollTimer.Tick += new EventHandler(delegate { watcher.Poll(); });
        pollTimer.Start();

        syncTimer = new Timer();
        syncTimer.Interval = 1000;
        syncTimer.Tick += new EventHandler(delegate { SyncProgress(); });
        syncTimer.Start();

        ApplyAutoStart(cfg.AutoStart);

        // 启动时若距上次检查超过 7 天（144h），自动检查更新
        DateTime last;
        bool needCheck = !DateTime.TryParse(cfg.LastUpdateCheck, out last)
            || (DateTime.Now - last).TotalHours >= 144;
        if (needCheck) CheckUpdate(false);
    }

    void BuildTray()
    {
        tray = new NotifyIcon();
        tray.Icon = BuildIcon();
        tray.Text = "波点音乐任务栏歌词 v" + Program.VERSION;
        tray.Visible = true;

        ContextMenuStrip menu = new ContextMenuStrip();

        miAutoStart = new ToolStripMenuItem("开机自启");
        miAutoStart.CheckOnClick = true;
        miAutoStart.Checked = cfg.AutoStart;
        miAutoStart.Click += new EventHandler(delegate { SetAutoStart(miAutoStart.Checked); });

        ToolStripMenuItem restart = new ToolStripMenuItem("重启");
        restart.Click += new EventHandler(delegate { Restart(); });

        ToolStripMenuItem exit = new ToolStripMenuItem("退出");
        exit.Click += new EventHandler(delegate { Shutdown(); });

        menu.Items.Add(miAutoStart);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(restart);
        menu.Items.Add(exit);

        menu.Opening += new System.ComponentModel.CancelEventHandler(delegate
        {
            miAutoStart.Checked = cfg.AutoStart;
        });

        tray.ContextMenuStrip = menu;
        tray.BalloonTipClicked += new EventHandler(delegate
        {
            try { System.Diagnostics.Process.Start("https://github.com/Qing-qiuqm/BodianLyrics/releases/latest"); }
            catch { }
        });
        // 左键单击弹出设置窗口
        tray.MouseClick += new MouseEventHandler(delegate(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) ShowSettings();
        });
    }

    // ---- 供设置窗口调用的公开接口 ----

    public Config Cfg
    {
        get { return cfg; }
    }

    /// <summary>把配置写回 config.ini，并立即重排歌词窗</summary>
    public void ApplyConfig()
    {
        cfg.Save();
        overlay.Reposition();
    }

    public void SetLyricsVisible(bool on)
    {
        overlay.SetVisible(on);
    }

    public void PlayPrev() { controller.ClickPrevious(); }

    public void PlayPause() { controller.ClickPlayPause(); }

    public void PlayNext() { controller.ClickNext(); }

    public void SetAutoStart(bool on)
    {
        cfg.AutoStart = on;
        cfg.Save();
        ApplyAutoStart(on);
    }

    public void CheckUpdateManual()
    {
        CheckUpdate(true);
    }

    void ShowSettings()
    {
        try
        {
            if (settings != null && !settings.IsDisposed)
            {
                settings.LoadValues();
                settings.Show();
                settings.WindowState = FormWindowState.Normal;
                settings.Activate();
                return;
            }
            settings = new SettingsForm(this);
            settings.Show();
        }
        catch { }
    }

    /// <summary>重启自身：先释放单实例互斥体，再拉起新进程</summary>
    void Restart()
    {
        try
        {
            cfg.Save();
            Program.ReleaseInstanceMutex();
            System.Diagnostics.Process.Start(Application.ExecutablePath);
        }
        catch { }
        Shutdown();
    }

    static Icon BuildIcon()
    {
        Bitmap b = new Bitmap(32, 32);
        using (Graphics g = Graphics.FromImage(b))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(24, 24, 28)))
            {
                g.FillEllipse(brush, 1, 1, 30, 30);
            }
            using (SolidBrush accent = new SolidBrush(Color.FromArgb(255, 122, 69)))
            {
                g.FillEllipse(accent, 4, 4, 24, 24);
            }
            using (Font f = new Font("Segoe UI", 18, FontStyle.Bold, GraphicsUnit.Pixel))
            using (SolidBrush w = new SolidBrush(Color.White))
            {
                StringFormat sf = new StringFormat();
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                g.DrawString("♪", f, w, new RectangleF(0, 0, 32, 32), sf);
            }
        }
        return Icon.FromHandle(b.GetHicon());
    }

    void SyncProgress()
    {
        try
        {
            if (!state.HasSong) return;

            double ratio;
            if (!controller.TryGetProgress(out ratio)) return;

            // 播放中进度条每帧都会动，连续几帧完全不动说明读到的是旧帧/错误行，直接丢弃
            if (Math.Abs(ratio - prevRatio) < 0.0008) sameRatio++;
            else sameRatio = 0;
            prevRatio = ratio;
            if (sameRatio >= 3) return;

            double raw = state.GetLogElapsed();
            string sid = state.SongId;
            if (sid != refSong)
            {
                refSong = sid;
                refValid = false;
                latencyLearned = false;
                latSum = 0;
                latCount = 0;
                prevRatio = -1;
                sameRatio = 0;
            }

            if (raw > 5 && ratio > 0.02)
            {
                if (!refValid)
                {
                    refRaw = raw;
                    refRatio = ratio;
                    refValid = true;
                }
                else
                {
                    double dr = ratio - refRatio;
                    if (dr > 0.12)
                    {
                        // 进度条比例与日志经过时间严格线性，用它反推音频真实时长，
                        // 可绕开日志里经常串曲的 xduration / DUR 字段
                        double est = (raw - refRaw) / dr;
                        double cur = state.Duration;
                        bool sane = est > 30 && est < 4000 && (cur <= 0 || Math.Abs(est - cur) < cur * 0.1);
                        if (sane)
                        {
                            state.SetDuration(est);
                            latSum += raw - ratio * est;
                            latCount++;
                            refRaw = raw;
                            refRatio = ratio;
                        }
                        else
                        {
                            // 可能用户拖动了进度条，重新取基准
                            refRaw = raw;
                            refRatio = ratio;
                        }
                    }
                }
            }

            // 多帧平均"日志播放事件 vs 真实出声"的偏差，窗口最小化时也能对拍
            if (latCount > 0 && (latCount >= 3 || raw > 20))
            {
                double lat = latSum / latCount;
                latSum = 0;
                latCount = 0;
                if (Math.Abs(lat) < 6)
                {
                    cfg.Latency = latencyLearned ? cfg.Latency * 0.7 + lat * 0.3 : lat;
                    state.SetLatency(cfg.Latency);
                    latencyLearned = true;
                    if (++latencySaves >= 2) { cfg.Save(); latencySaves = 0; }
                }
            }

            double dur = state.Duration;
            if (dur <= 0) return;

            double pos = ratio * dur;
            double diff = Math.Abs(pos - state.GetPosition());

            if (diff <= 4.0)
            {
                state.SyncPosition(pos, DateTime.Now);
                oddCount = 0;
                oddPos = -1;
            }
            else
            {
                // 用户可能拖动进度条：连续两次一致再采纳
                if (oddCount > 0 && Math.Abs(pos - oddPos) < 3.0)
                {
                    state.SyncPosition(pos, DateTime.Now);
                    oddCount = 0;
                    oddPos = -1;
                }
                else
                {
                    oddPos = pos;
                    oddCount = 1;
                }
            }
        }
        catch { }
    }

    static void ApplyAutoStart(bool on)
    {
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", true))
            {
                if (key == null) return;
                if (on) key.SetValue("BodianLyrics", "\"" + Application.ExecutablePath + "\"");
                else key.DeleteValue("BodianLyrics", false);
            }
        }
        catch { }
    }

    void CheckUpdate(bool manual)
    {
        cfg.LastUpdateCheck = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        cfg.Save();

        System.Threading.ThreadPool.QueueUserWorkItem(delegate
        {
            try
            {
                string json;
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(
                    "https://api.github.com/repos/Qing-qiuqm/BodianLyrics/releases/latest");
                req.Timeout = 8000;
                req.ReadWriteTimeout = 8000;
                req.UserAgent = "BodianLyrics/" + Program.VERSION;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (Stream s = resp.GetResponseStream())
                using (StreamReader sr = new StreamReader(s, Encoding.UTF8))
                {
                    json = sr.ReadToEnd();
                }

                Match m = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
                if (!m.Success)
                {
                    if (manual) ShowBalloon("检查更新", "无法获取版本信息。", ToolTipIcon.Warning);
                    return;
                }

                string tag = m.Groups[1].Value;
                string latest = tag.StartsWith("v") ? tag.Substring(1) : tag;
                string current = Program.VERSION;

                if (CompareVersion(latest, current) > 0)
                {
                    ShowBalloon("发现新版本 v" + latest,
                        "当前版本 v" + current + "，点击查看更新。", ToolTipIcon.Info);
                }
                else if (manual)
                {
                    ShowBalloon("已是最新版本", "当前版本 v" + current, ToolTipIcon.Info);
                }
            }
            catch
            {
                if (manual) ShowBalloon("检查更新", "网络异常，请稍后重试。", ToolTipIcon.Warning);
            }
        });
    }

    static int CompareVersion(string a, string b)
    {
        string[] pa = a.Split('.');
        string[] pb = b.Split('.');
        int n = Math.Max(pa.Length, pb.Length);
        for (int i = 0; i < n; i++)
        {
            int va = (i < pa.Length) ? ToInt(pa[i]) : 0;
            int vb = (i < pb.Length) ? ToInt(pb[i]) : 0;
            if (va != vb) return va.CompareTo(vb);
        }
        return 0;
    }

    static int ToInt(string s)
    {
        int v;
        if (int.TryParse(s, out v)) return v;
        return 0;
    }

    void ShowBalloon(string title, string text, ToolTipIcon icon)
    {
        try
        {
            if (tray == null) return;
            tray.BalloonTipTitle = title;
            tray.BalloonTipText = text;
            tray.BalloonTipIcon = icon;
            tray.ShowBalloonTip(8000);
        }
        catch { }
    }

    void Shutdown()
    {
        try
        {
            cfg.Save();
            if (pollTimer != null) pollTimer.Stop();
            if (syncTimer != null) syncTimer.Stop();
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            if (overlay != null) overlay.Dispose();
        }
        catch { }
        Application.Exit();
    }
}
