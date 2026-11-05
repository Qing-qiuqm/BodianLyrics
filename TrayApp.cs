using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
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
    ToolStripMenuItem miShow;
    ToolStripMenuItem miAutoStart;

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
    }

    void BuildTray()
    {
        tray = new NotifyIcon();
        tray.Icon = BuildIcon();
        tray.Text = "波点音乐任务栏歌词 v" + Program.VERSION;
        tray.Visible = true;

        ContextMenuStrip menu = new ContextMenuStrip();

        miShow = new ToolStripMenuItem("显示歌词");
        miShow.CheckOnClick = true;
        miShow.Checked = cfg.Enabled;
        miShow.Click += new EventHandler(delegate { overlay.SetVisible(miShow.Checked); });

        ToolStripMenuItem play = new ToolStripMenuItem("播放 / 暂停");
        play.Click += new EventHandler(delegate { controller.ClickPlayPause(); });

        ToolStripMenuItem prev = new ToolStripMenuItem("上一首");
        prev.Click += new EventHandler(delegate { controller.ClickPrevious(); });

        ToolStripMenuItem next = new ToolStripMenuItem("下一首");
        next.Click += new EventHandler(delegate { controller.ClickNext(); });

        ToolStripMenuItem left = new ToolStripMenuItem("左移 20px");
        left.Click += new EventHandler(delegate { cfg.RightMargin += 20; cfg.Save(); overlay.Reposition(); });

        ToolStripMenuItem right = new ToolStripMenuItem("右移 20px");
        right.Click += new EventHandler(delegate { cfg.RightMargin -= 20; cfg.Save(); overlay.Reposition(); });

        ToolStripMenuItem up = new ToolStripMenuItem("上移 5px");
        up.Click += new EventHandler(delegate { cfg.OffsetY -= 5; cfg.Save(); overlay.Reposition(); });

        ToolStripMenuItem down = new ToolStripMenuItem("下移 5px");
        down.Click += new EventHandler(delegate { cfg.OffsetY += 5; cfg.Save(); overlay.Reposition(); });

        ToolStripMenuItem reset = new ToolStripMenuItem("恢复默认位置");
        reset.Click += new EventHandler(delegate { cfg.RightMargin = 460; cfg.OffsetY = 0; cfg.Save(); overlay.Reposition(); });

        ToolStripMenuItem bigger = new ToolStripMenuItem("字号 +1");
        bigger.Click += new EventHandler(delegate
        {
            cfg.FontSize = Math.Min(32f, cfg.FontSize + 1f);
            cfg.Save();
        });

        ToolStripMenuItem smaller = new ToolStripMenuItem("字号 -1");
        smaller.Click += new EventHandler(delegate
        {
            cfg.FontSize = Math.Max(12f, cfg.FontSize - 1f);
            cfg.Save();
        });

        miAutoStart = new ToolStripMenuItem("开机自启");
        miAutoStart.CheckOnClick = true;
        miAutoStart.Checked = cfg.AutoStart;
        miAutoStart.Click += new EventHandler(delegate
        {
            cfg.AutoStart = miAutoStart.Checked;
            cfg.Save();
            ApplyAutoStart(cfg.AutoStart);
        });

        ToolStripMenuItem miIcons = new ToolStripMenuItem("显示播放图标");
        miIcons.CheckOnClick = true;
        miIcons.Checked = cfg.ShowIcons;
        miIcons.Click += new EventHandler(delegate
        {
            cfg.ShowIcons = miIcons.Checked;
            cfg.Save();
        });

        ToolStripMenuItem exit = new ToolStripMenuItem("退出");
        exit.Click += new EventHandler(delegate { Shutdown(); });

        menu.Items.Add(miShow);
        menu.Items.Add(miIcons);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(play);
        menu.Items.Add(prev);
        menu.Items.Add(next);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(left);
        menu.Items.Add(right);
        menu.Items.Add(up);
        menu.Items.Add(down);
        menu.Items.Add(reset);
        menu.Items.Add(bigger);
        menu.Items.Add(smaller);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(miAutoStart);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exit);

        menu.Opening += new System.ComponentModel.CancelEventHandler(delegate
        {
            miShow.Checked = cfg.Enabled;
            miAutoStart.Checked = cfg.AutoStart;
            play.Text = state.Playing ? "暂停" : "播放";
        });

        tray.ContextMenuStrip = menu;
        tray.DoubleClick += new EventHandler(delegate
        {
            overlay.SetVisible(!cfg.Enabled);
            miShow.Checked = cfg.Enabled;
        });
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