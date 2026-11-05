using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

class PlayerState
{
    readonly object sync = new object();
    string songId = "";
    string title = "";
    string artist = "";
    double duration = 0;
    bool hasSong = false;
    bool playing = false;
    double anchorPos = 0;
    DateTime anchorTime = DateTime.Now;
    double latency = 0;
    DateTime runStart = DateTime.Now;
    double pausedSeconds = 0;
    LyricsData lyrics = null;

    public string SongId { get { lock (sync) return songId; } }
    public string Title { get { lock (sync) return title; } }
    public string Artist { get { lock (sync) return artist; } }
    public double Duration { get { lock (sync) return duration; } }
    public bool HasSong { get { lock (sync) return hasSong; } }
    public bool Playing { get { lock (sync) return playing; } }
    public LyricsData Lyrics { get { lock (sync) return lyrics; } }

    public double GetPosition()
    {
        lock (sync)
        {
            double p = anchorPos;
            if (playing) p += (DateTime.Now - anchorTime).TotalSeconds;
            if (p < 0) p = 0;
            if (duration > 0 && p > duration) p = duration;
            return p;
        }
    }

    public void SetLatency(double seconds)
    {
        lock (sync)
        {
            if (Math.Abs(seconds) < 30) latency = seconds;
        }
    }

    public void ResetSong(string id, string name, DateTime t)
    {
        lock (sync)
        {
            songId = id;
            title = name;
            artist = "";
            duration = 0;
            hasSong = true;
            // 日志事件通常早于/晚于真实出声，用已学到的偏差预置起点
            anchorPos = -latency;
            anchorTime = t;
            runStart = t;
            pausedSeconds = 0;
            lyrics = null;
        }
    }

    public void SetLyrics(LyricsData data)
    {
        lock (sync)
        {
            lyrics = data;
            if (data != null && data.Artist.Length > 0) artist = data.Artist;
        }
    }

    public void SetDuration(double seconds)
    {
        lock (sync)
        {
            if (seconds > 0) duration = seconds;
        }
    }

    public void SetPlaying(bool value, DateTime t)
    {
        lock (sync)
        {
            if (playing && !value) pausedSeconds += (t - runStart).TotalSeconds;
            if (!playing && value) runStart = t;
            double nowPos = anchorPos;
            if (playing) nowPos += (t - anchorTime).TotalSeconds;
            if (nowPos < 0) nowPos = 0;
            anchorPos = nowPos;
            anchorTime = t;
            playing = value;
        }
    }

    /// <summary>纯粹按"日志播放事件至今"的时长（不含暂停），用于学习日志与真实出声的偏差</summary>
    public double GetLogElapsed()
    {
        lock (sync)
        {
            double e = pausedSeconds;
            if (playing) e += (DateTime.Now - runStart).TotalSeconds;
            return e;
        }
    }

    public void SyncPosition(double pos, DateTime t)
    {
        lock (sync)
        {
            anchorPos = pos;
            anchorTime = t;
        }
    }
}

class LogWatcher
{
    readonly PlayerState state;
    readonly LyricsProvider provider;
    readonly Config config;

    string logPath = "";
    long offset = 0;
    string pending = "";
    DateTime lastFind = DateTime.MinValue;

    public LogWatcher(PlayerState state, LyricsProvider provider, Config config)
    {
        this.state = state;
        this.provider = provider;
        this.config = config;
        Initialize();
    }

    static string CandidatePath()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                  "cn.wenyu.bodian", "bodian_pc", "bdlog");
        string f = Path.Combine(dir, "log_0.log");
        if (File.Exists(f)) return f;
        try
        {
            if (Directory.Exists(dir))
            {
                string best = "";
                DateTime bestTime = DateTime.MinValue;
                foreach (string p in Directory.GetFiles(dir, "*.log"))
                {
                    DateTime t = File.GetLastWriteTime(p);
                    if (t > bestTime) { bestTime = t; best = p; }
                }
                return best;
            }
        }
        catch { }
        return "";
    }

    void Initialize()
    {
        try
        {
            logPath = CandidatePath();
            if (logPath.Length == 0) return;
            string text = ReadAll(logPath);
            string[] rows = text.Replace("\r", "").Split('\n');
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].Length == 0) continue;
                ProcessLine(rows[i], true);
            }
            offset = new FileInfo(logPath).Length;
            if (state.HasSong && state.SongId.Length > 0) FetchLyrics(state.SongId, state.Title);
        }
        catch { }
    }

    string ReadAll(string path)
    {
        try
        {
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                byte[] all = new byte[fs.Length];
                fs.Read(all, 0, all.Length);
                return Encoding.UTF8.GetString(all);
            }
        }
        catch { return ""; }
    }

    public void Poll()
    {
        try
        {
            if (logPath.Length == 0)
            {
                if ((DateTime.Now - lastFind).TotalSeconds < 3) return;
                lastFind = DateTime.Now;
                logPath = CandidatePath();
                if (logPath.Length == 0) return;
                offset = 0;
                pending = "";
            }

            if (!File.Exists(logPath))
            {
                logPath = "";
                return;
            }

            long len = new FileInfo(logPath).Length;
            if (len < offset)
            {
                offset = 0;
                pending = "";
            }
            if (len == offset) return;

            byte[] buf;
            using (FileStream fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                fs.Seek(offset, SeekOrigin.Begin);
                long count = len - offset;
                if (count > 4 * 1024 * 1024) count = 4 * 1024 * 1024;
                buf = new byte[count];
                int read = 0;
                while (read < count)
                {
                    int n = fs.Read(buf, read, (int)(count - read));
                    if (n <= 0) break;
                    read += n;
                }
                offset += read;
            }

            string chunk = pending + Encoding.UTF8.GetString(buf, 0, buf.Length);
            string[] rows = chunk.Replace("\r", "").Split('\n');
            int lastBreak = chunk.LastIndexOf('\n');
            if (lastBreak >= 0)
            {
                pending = chunk.Substring(lastBreak + 1);
                for (int i = 0; i < rows.Length - 1; i++)
                {
                    if (rows[i].Length > 0) ProcessLine(rows[i], false);
                }
            }
            else
            {
                pending = chunk;
            }
        }
        catch { }
    }

    static DateTime ParseTime(string line)
    {
        if (line.Length < 23) return DateTime.Now;
        try
        {
            return DateTime.ParseExact(line.Substring(0, 23), "yyyy-MM-dd HH:mm:ss.fff",
                                       CultureInfo.InvariantCulture);
        }
        catch { return DateTime.Now; }
    }

    void ProcessLine(string line, bool quiet)
    {
        DateTime t = ParseTime(line);

        int pi = line.IndexOf("play: ");
        int idIdx = line.LastIndexOf(", id: ");
        if (pi >= 0 && idIdx > pi)
        {
            string idRaw = line.Substring(idIdx + 6).Trim();
            int idLen = 0;
            while (idLen < idRaw.Length && char.IsDigit(idRaw[idLen])) idLen++;
            string id = idRaw.Substring(0, idLen);
            int nameIdx = line.LastIndexOf(", name: ", idIdx);
            string name = "";
            if (nameIdx > pi)
            {
                name = line.Substring(nameIdx + 8, idIdx - nameIdx - 8).Trim();
            }
            if (id.Length > 0)
            {
                state.ResetSong(id, name, t);
                if (!quiet) FetchLyrics(id, name);
            }
            return;
        }

        int ev = line.IndexOf("mpv playing event=");
        if (ev >= 0)
        {
            bool value = line.IndexOf("true", ev) > 0;
            state.SetPlaying(value, t);
            return;
        }

        // 歌曲参数行里带 id + duration，是当前曲目最可靠的时长来源
        int param = line.IndexOf("歌曲的完整参数信息");
        if (param >= 0)
        {
            string pId = FieldDigits(line, "{id: ", param);
            int dIdx = line.IndexOf(", duration: ", param);
            if (pId.Length > 0 && dIdx > 0 && pId == state.SongId)
            {
                string d = FieldDigits(line, ", duration: ", param);
                if (d.Length > 0) ApplyDuration(int.Parse(d));
            }
            return;
        }

        // 切歌时打印的统计行，DUR/RID 同样对应当前曲目
        if (line.IndexOf("stat, BOD_PLAY_MUSIC") >= 0)
        {
            string rid = FieldDigits(line, "RID:", 0);
            string dur = FieldDigits(line, "DUR:", 0);
            if (rid.Length > 0 && dur.Length > 0 && rid == state.SongId) ApplyDuration(int.Parse(dur));
            return;
        }

        // xduration 出现在下载/预加载任务里，经常是下一首甚至别的曲目，只在没有时长时兜底
        int xd = line.IndexOf("xduration=");
        if (xd >= 0 && state.Duration <= 0)
        {
            string d = FieldDigits(line, "xduration=", 0);
            if (d.Length > 0)
            {
                double ms = double.Parse(d);
                ApplyDuration(ms / 1000.0);
            }
        }
    }

    // 从标记位置开始读取连续的十进制数字（可带小数）
    static string FieldDigits(string line, string marker, int from)
    {
        int p = line.IndexOf(marker, from);
        if (p < 0) return "";
        p += marker.Length;
        int e = p;
        while (e < line.Length && (char.IsDigit(line[e]) || line[e] == '.')) e++;
        return e > p ? line.Substring(p, e - p) : "";
    }

    void ApplyDuration(double seconds)
    {
        if (seconds > 30 && seconds < 4000) state.SetDuration(seconds);
    }

    void FetchLyrics(string id, string title)
    {
        try
        {
            Thread th = new Thread(delegate()
            {
                LyricsData data = provider.Get(id, title);
                if (state.SongId == id) state.SetLyrics(data);
            });
            th.IsBackground = true;
            th.Start();
        }
        catch { }
    }
}

class BodianController
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);

    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    IntPtr top = IntPtr.Zero;
    IntPtr view = IntPtr.Zero;

    Bitmap capture;
    int captureW = 0;
    int captureH = 0;

    static int targetPid;
    static IntPtr foundTop;
    static IntPtr foundView;
    static long foundArea;

    static bool Callback(IntPtr hWnd, IntPtr lParam)
    {
        uint pid;
        GetWindowThreadProcessId(hWnd, out pid);
        if ((int)pid != targetPid) return true;

        IntPtr child = GetWindow(hWnd, 5); // GW_CHILD
        while (child != IntPtr.Zero)
        {
            StringBuilder sb = new StringBuilder(256);
            GetClassName(child, sb, 256);
            if (sb.ToString().Equals("FLUTTERVIEW", StringComparison.OrdinalIgnoreCase))
            {
                RECT rc = new RECT();
                GetClientRect(hWnd, out rc);
                long area = (long)(rc.Right - rc.Left) * (long)(rc.Bottom - rc.Top);
                if (foundTop == IntPtr.Zero || area > foundArea)
                {
                    foundTop = hWnd;
                    foundView = child;
                    foundArea = area;
                }
            }
            child = GetWindow(child, 2); // GW_HWNDNEXT
        }
        return true;
    }

    public bool Find()
    {
        if (view != IntPtr.Zero && IsWindow(view) && IsWindow(top)) return true;

        Process[] ps = Process.GetProcessesByName("bodian_pc");
        if (ps.Length == 0) { view = IntPtr.Zero; return false; }

        targetPid = ps[0].Id;
        foundTop = IntPtr.Zero;
        foundView = IntPtr.Zero;
        foundArea = 0;
        EnumWindows_();
        top = foundTop;
        view = foundView;
        return view != IntPtr.Zero;
    }

    static void EnumWindows_()
    {
        EnumWindows(Callback, IntPtr.Zero);
    }

    static IntPtr MakeLParam(int x, int y)
    {
        return (IntPtr)((long)((y << 16) | (x & 0xFFFF)));
    }

    public bool ClickPlayPause()
    {
        return ClickAt(0);
    }

    public bool ClickPrevious()
    {
        return ClickAt(-62);
    }

    public bool ClickNext()
    {
        return ClickAt(62);
    }

    bool ClickAt(int offsetX)
    {
        try
        {
            if (!Find()) return false;
            RECT rc = new RECT();
            GetClientRect(top, out rc);
            int cw = rc.Right - rc.Left;
            int ch = rc.Bottom - rc.Top;
            if (cw < 400 || ch < 300) return false;
            int x = cw / 2 - 5 + offsetX;
            int y = ch - 56;
            IntPtr lp = MakeLParam(x, y);
            PostMessage(view, 0x0201, (IntPtr)1, lp);
            Thread.Sleep(50);
            PostMessage(view, 0x0202, IntPtr.Zero, lp);
            return true;
        }
        catch { return false; }
    }

    public bool TryGetProgress(out double ratio)
    {
        ratio = 0;
        try
        {
            if (!Find()) return false;
            if (!IsWindowVisible(top) || IsIconic(top)) return false;

            RECT rc = new RECT();
            GetClientRect(top, out rc);
            int w = rc.Right - rc.Left;
            int h = rc.Bottom - rc.Top;
            if (w < 200 || h < 100) return false;
            if (w > 3200 || h > 2000) return false;

            if (capture == null || captureW != w || captureH != h)
            {
                capture = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                captureW = w;
                captureH = h;
            }

            using (Graphics g = Graphics.FromImage(capture))
            {
                IntPtr dc = g.GetHdc();
                PrintWindow(top, dc, 2);
                g.ReleaseHdc(dc);
            }

            Rectangle rect = new Rectangle(0, 0, w, h);
            BitmapData data = capture.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                IntPtr scan = data.Scan0;
                int stride = data.Stride;
                double bg = BackgroundLum(scan, stride, w, h);
                for (int off = 8; off <= 12; off++)
                {
                    int y = h - off;
                    if (y < 0) continue;
                    double r;
                    if (ScanRow(scan, stride, w, y, bg, out r))
                    {
                        ratio = r;
                        return true;
                    }
                }
            }
            finally
            {
                capture.UnlockBits(data);
            }
        }
        catch { }
        return false;
    }

    // 用进度条上方一行的中间区域估算背景亮度
    static double BackgroundLum(IntPtr scan, int stride, int w, int h)
    {
        int y = h - 16;
        if (y < 0) y = 0;
        double sum = 0;
        int n = 0;
        int x0 = w / 3;
        int x1 = w * 2 / 3;
        for (int x = x0; x < x1; x += 8)
        {
            byte b = Marshal.ReadByte(scan, y * stride + x * 4);
            byte g = Marshal.ReadByte(scan, y * stride + x * 4 + 1);
            byte r = Marshal.ReadByte(scan, y * stride + x * 4 + 2);
            sum += 0.114 * b + 0.587 * g + 0.299 * r;
            n++;
        }
        return n > 0 ? sum / n : 60.0;
    }

    static bool ScanRow(IntPtr scan, int stride, int w, int y, double bgLum, out double ratio)
    {
        ratio = 0;
        double[] lum = new double[w];
        for (int x = 0; x < w; x++)
        {
            byte b = Marshal.ReadByte(scan, y * stride + x * 4);
            byte g = Marshal.ReadByte(scan, y * stride + x * 4 + 1);
            byte r = Marshal.ReadByte(scan, y * stride + x * 4 + 2);
            lum[x] = 0.114 * b + 0.587 * g + 0.299 * r;
        }

        List<int> steps = new List<int>();
        for (int x = 4; x < w - 6; x++)
        {
            if (Math.Abs(lum[x + 2] - lum[x]) >= 5) steps.Add(x);
        }
        if (steps.Count == 0) return false;

        List<int> centers = new List<int>();
        int start = steps[0], last = steps[0];
        for (int i = 1; i < steps.Count; i++)
        {
            int s = steps[i];
            if (s - last > 12)
            {
                centers.Add((start + last) / 2);
                start = s;
            }
            last = s;
        }
        centers.Add((start + last) / 2);

        if (centers.Count < 2) return false;

        int left = centers[0];
        if (left > w * 0.12) return false;

        int lastCenter = centers[centers.Count - 1];
        int right = (lastCenter >= w - 8) ? lastCenter : w - 5;

        int boundary = -1;
        for (int i = 1; i < centers.Count; i++)
        {
            int c = centers[i];
            if (c > left + 4 && c < right - 2) { boundary = c; break; }
        }

        if (boundary < 0)
        {
            // 没有内部边界：整条要么全满，要么全空
            double sample = 0;
            int n = 0;
            for (int x = left + 15; x < left + 45 && x < w; x++)
            {
                sample += lum[x];
                n++;
            }
            if (n == 0) return false;
            sample /= n;
            ratio = (sample > bgLum + 12) ? 1.0 : 0.0;
            return true;
        }

        double rv = (boundary - left) / (double)(right - left);
        if (rv < 0) rv = 0;
        if (rv > 1) rv = 1;
        ratio = rv;
        return true;
    }
}