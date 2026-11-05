using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

class LayerForm : Form
{
    /// <summary>返回 0=不响应，1=播放/暂停，2=上一首，3=下一首</summary>
    public Func<int, int, int> ZoneAt;

    const int WM_NCHITTEST = 0x0084;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle = cp.ExStyle | 0x00080000; // WS_EX_LAYERED
            cp.ExStyle = cp.ExStyle | 0x08000000; // WS_EX_NOACTIVATE
            cp.ExStyle = cp.ExStyle | 0x00000080; // WS_EX_TOOLWINDOW
            return cp;
        }
    }

    protected override bool ShowWithoutActivation
    {
        get { return true; }
    }

    // 只有歌词胶囊区域吃鼠标，其余位置穿透给下面的任务栏
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_NCHITTEST)
        {
            int zone = 0;
            if (ZoneAt != null)
            {
                int lp = m.LParam.ToInt32();
                Point p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                zone = ZoneAt(p.X, p.Y);
            }
            m.Result = (IntPtr)(zone > 0 ? 1 : -1); // HTCLIENT / HTTRANSPARENT
            return;
        }
        base.WndProc(ref m);
    }
}

class LyricOverlay
{
    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct APPBARDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    struct SIZE { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential)]
    struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; public int bmiColors; }

    [DllImport("shell32.dll")] static extern uint SHAppBarMessage(uint msg, ref APPBARDATA data);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst,
        ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage,
        out IntPtr bits, IntPtr hSection, uint offset);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    readonly Config cfg;
    readonly PlayerState state;
    readonly BodianController controller;
    readonly LayerForm form;
    readonly Timer timer;
    readonly Timer moveTimer;

    // 文字与胶囊的横向范围（窗口客户区坐标），用于鼠标分区
    float textL = 0;
    float textR = 0;
    float pillL = 0;
    float pillR = 0;

    bool visible = true;
    int screenX = -4000;
    int screenY = -4000;
    int winH = 48;

    Bitmap canvas;
    Graphics canvasG;
    int canvasW = 0;

    IntPtr memDc = IntPtr.Zero;
    IntPtr hDib = IntPtr.Zero;
    IntPtr dibBits = IntPtr.Zero;
    IntPtr oldObj = IntPtr.Zero;
    int dibW = 0;
    int dibH = 0;

    public LyricOverlay(Config cfg, PlayerState state, BodianController controller)
    {
        this.cfg = cfg;
        this.state = state;
        this.controller = controller;

        form = new LayerForm();
        form.ZoneAt = HitZone;
        form.FormBorderStyle = FormBorderStyle.None;
        form.ShowInTaskbar = false;
        form.TopMost = true;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-4000, -4000);
        form.Size = new Size(cfg.Width, 48);
        form.BackColor = Color.Black;
        form.MouseUp += new MouseEventHandler(OnMouseUp);

        timer = new Timer();
        timer.Interval = 40;
        timer.Tick += new EventHandler(OnTick);

        moveTimer = new Timer();
        moveTimer.Interval = 2000;
        moveTimer.Tick += new EventHandler(delegate { Reposition(); });
    }

    public void ShowOverlay()
    {
        Reposition();
        form.Show();
        ShowWindow(form.Handle, 4); // SW_SHOWNOACTIVATE
        visible = cfg.Enabled;
        if (visible)
        {
            timer.Start();
            moveTimer.Start();
        }
        else
        {
            ShowWindow(form.Handle, 0);
        }
    }

    public void SetVisible(bool on)
    {
        cfg.Enabled = on;
        cfg.Save();
        if (on)
        {
            form.Show();
            ShowWindow(form.Handle, 4);
            timer.Start();
            moveTimer.Start();
        }
        else
        {
            timer.Stop();
            moveTimer.Stop();
            ShowWindow(form.Handle, 0);
        }
    }

    public void Reposition()
    {
        try
        {
            APPBARDATA data = new APPBARDATA();
            data.cbSize = (uint)Marshal.SizeOf(typeof(APPBARDATA));
            uint ok = SHAppBarMessage(5, ref data); // ABM_GETTASKBARPOS

            Rectangle screen = Screen.PrimaryScreen.Bounds;
            int height = 48;
            int edge = 3;

            if (ok != 0 && data.rc.Right > data.rc.Left)
            {
                height = data.rc.Bottom - data.rc.Top;
                edge = (int)data.uEdge;
                if (height < 30 || height > 90) height = 48;
            }

            int x, y;
            if (edge == 3) // bottom
            {
                x = data.rc.Right - cfg.RightMargin - cfg.Width;
                y = data.rc.Top + cfg.OffsetY;
            }
            else if (edge == 1) // top
            {
                x = data.rc.Right - cfg.RightMargin - cfg.Width;
                y = data.rc.Top + cfg.OffsetY;
            }
            else
            {
                x = (screen.Width - cfg.Width) / 2;
                y = screen.Bottom - 48 + cfg.OffsetY;
            }

            if (x < 0) x = 0;
            screenX = x;
            screenY = y;
            winH = height;

            SetWindowPos(form.Handle, (IntPtr)(-1), screenX, screenY, cfg.Width, winH, 0x0010); // HWND_TOPMOST, SWP_NOACTIVATE
        }
        catch { }
    }

    void OnTick(object sender, EventArgs e)
    {
        try
        {
            RenderFrame();
        }
        catch (Exception ex)
        {
            Debug("tick-ex: " + ex.Message);
        }
    }

    // 歌词文字本身=播放/暂停，文字左侧=上一首，文字右侧=下一首
    int HitZone(int cx, int cy)
    {
        if (textR <= textL || cy < 0 || cy > winH) return 0;
        if (cx < pillL || cx > pillR) return 0;
        if (cx >= textL - 6 && cx <= textR + 6) return 1;
        if (cx < textL - 6) return 2;
        return 3;
    }

    void OnMouseUp(object sender, MouseEventArgs e)
    {
        try
        {
            int zone = HitZone(e.X, e.Y);
            if (zone == 1) controller.ClickPlayPause();
            else if (zone == 2) controller.ClickPrevious();
            else if (zone == 3) controller.ClickNext();
        }
        catch (Exception ex)
        {
            Debug("click-ex: " + ex.Message);
        }
    }

    static DateTime lastDebug = DateTime.MinValue;
    static void Debug(string msg)
    {
        try
        {
            if ((DateTime.Now - lastDebug).TotalMilliseconds < 500) return;
            lastDebug = DateTime.Now;
            System.IO.File.AppendAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug.log"),
                DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + "\r\n");
        }
        catch { }
    }

    void RenderFrame()
    {
        if (!state.HasSong)
        {
            RenderText("", 0, -1);
            return;
        }

        double pos = state.GetPosition();
        LyricsData data = state.Lyrics;
        string text = "";
        int hi = 0;

        if (data != null && data.Lines.Count > 0)
        {
            int li = data.FindLine(pos);
            if (li >= 0)
            {
                LyricsLine line = data.Lines[li];
                if (pos <= line.End + 0.3)
                {
                    text = line.Text;
                    if (pos >= line.Start)
                    {
                        int ci = LyricsData.FindChar(line, pos);
                        hi = ci + 1;
                    }
                }
            }
        }
        else
        {
            text = state.Title;
        }

        // 前奏/间奏/无歌词的空档显示当前歌曲名
        if (text.Length == 0)
        {
            text = state.Title;
            hi = 0;
            if (text.Length == 0)
            {
                RenderText("", 0, -1);
                return;
            }
        }

        RenderText(text, hi, 0);
    }

    static string fontName = null;
    static FontStyle fontStyle = FontStyle.Regular;
    static string lastConfiguredFont = null;

    // 若 config 指定了字体则用之；否则优先苹方，缺失时退回微软雅黑
    static void EnsureFont(string configuredFont)
    {
        if (fontName != null && lastConfiguredFont == configuredFont) return;
        lastConfiguredFont = configuredFont;
        fontName = null;

        // 用户显式指定了字体：校验是否已安装
        if (configuredFont != null && configuredFont.Length > 0)
        {
            try
            {
                System.Drawing.Text.InstalledFontCollection col = new System.Drawing.Text.InstalledFontCollection();
                for (int j = 0; j < col.Families.Length; j++)
                {
                    if (col.Families[j].Name.Equals(configuredFont, StringComparison.OrdinalIgnoreCase))
                    {
                        fontName = configuredFont;
                        fontStyle = FontStyle.Regular;
                        return;
                    }
                }
            }
            catch { }
        }

        // 自动选择：苹方优先，回退微软雅黑粗体
        fontName = "Microsoft YaHei UI";
        fontStyle = FontStyle.Bold;
        try
        {
            string[] prefer = new string[] { "苹方 中等", "苹方 常规", "PingFang SC" };
            System.Drawing.Text.InstalledFontCollection col = new System.Drawing.Text.InstalledFontCollection();
            for (int i = 0; i < prefer.Length; i++)
            {
                for (int j = 0; j < col.Families.Length; j++)
                {
                    if (col.Families[j].Name == prefer[i])
                    {
                        fontName = prefer[i];
                        fontStyle = FontStyle.Regular;
                        return;
                    }
                }
            }
        }
        catch { }
    }

    void RenderText(string text, int highlight, int mode)
    {
        int w = cfg.Width;
        int h = winH;
        if (h < 30) h = 48;

        if (canvas == null || canvasW != w || canvas.Height != h)
        {
            if (canvas != null) canvas.Dispose();
            canvas = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            if (canvasG != null) canvasG.Dispose();
            canvasG = Graphics.FromImage(canvas);
            canvasW = w;
        }

        Graphics g = canvasG;
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        if (text.Length > 0)
        {
            StringFormat sf = StringFormat.GenericTypographic;
            sf.FormatFlags = sf.FormatFlags | StringFormatFlags.NoWrap | StringFormatFlags.NoClip;

            Font font = null;
            float tw = 0;
            EnsureFont(cfg.FontName);
            float maxWidth = w - 34;
            for (float size = cfg.FontSize; size >= 11; size -= 1)
            {
                Font f = new Font(fontName, size, fontStyle, GraphicsUnit.Pixel);
                float mw = g.MeasureString(text, f, new PointF(0, 0), sf).Width;
                if (font != null) font.Dispose();
                font = f;
                tw = mw;
                if (mw <= maxWidth) break;
            }
            if (font == null)
            {
                font = new Font(fontName, 11, fontStyle, GraphicsUnit.Pixel);
                tw = g.MeasureString(text, font, new PointF(0, 0), sf).Width;
            }

            float th = font.GetHeight(g);
            float x = (w - tw) / 2f;
            float y = (h - th) / 2f - 1f;
            if (x < 10) x = 10;

            // 文字两侧各留一段固定宽度，作为上一首 / 下一首的点击区
            float pad = 46;
            float pl = x - pad;
            float pr = x + tw + pad;
            if (pl < 2) pl = 2;
            if (pr > w - 2) pr = w - 2;
            textL = x;
            textR = x + tw;
            pillL = pl;
            pillR = pr;

            if (cfg.ShowPill)
            {
                RectangleF pill = new RectangleF(pl, (h - th) / 2f - 6, pr - pl, th + 10);
                using (GraphicsPath path = RoundedRect(pill, 9))
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(88, 8, 8, 10)))
                {
                    g.FillPath(brush, path);
                }
            }

            if (cfg.ShowIcons)
            {
                // 图标与文字在同一垂直中线上
                float cy = y + th / 2f;
                float scale = th / 24f;
                using (SolidBrush iconBrush = new SolidBrush(Color.FromArgb(100, 235, 235, 235)))
                {
                    if (x - pl > 22) MediaIcon(g, iconBrush, (pl + x) / 2f, cy, true, scale);
                    if (pr - (x + tw) > 22) MediaIcon(g, iconBrush, (x + tw + pr) / 2f, cy, false, scale);
                }
            }

            using (SolidBrush shadow = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
            {
                g.DrawString(text, font, shadow, x + 1.2f, y + 1.2f, sf);
            }

            using (SolidBrush baseBrush = new SolidBrush(Color.FromArgb(165, 240, 240, 240)))
            {
                g.DrawString(text, font, baseBrush, x, y, sf);
            }

            if (highlight > 0)
            {
                if (highlight > text.Length) highlight = text.Length;
                string prefix = text.Substring(0, highlight);
                using (SolidBrush accent = new SolidBrush(Color.FromArgb(255, cfg.AccentR, cfg.AccentG, cfg.AccentB)))
                {
                    g.DrawString(prefix, font, accent, x, y, sf);
                }
            }

            font.Dispose();
        }
        else
        {
            textL = 0;
            textR = 0;
            pillL = 0;
            pillR = 0;
        }

        PushLayer();
    }

    // 上一首 / 下一首：竖条 + 三角的标准媒体图标
    static void MediaIcon(Graphics g, Brush brush, float cx, float cy, bool prev, float scale)
    {
        float bh = 4.4f * scale;   // 三角半高
        float bw = 1.7f * scale;   // 竖条宽
        float tw = 5.6f * scale;   // 三角宽
        float gap = 0.5f * scale;

        // 竖条 + 三角整体居中
        float left = -(bw + gap + tw) / 2f;
        float barX = left;
        float apexX = left + bw + gap;

        if (prev)
        {
            g.FillRectangle(brush, cx + barX, cy - bh, bw, bh * 2);
            PointF[] tri = new PointF[]
            {
                new PointF(cx + apexX, cy),
                new PointF(cx + apexX + tw, cy - bh),
                new PointF(cx + apexX + tw, cy + bh)
            };
            g.FillPolygon(brush, tri);
        }
        else
        {
            float barRight = -left;
            g.FillRectangle(brush, cx + barRight - bw, cy - bh, bw, bh * 2);
            float apex = barRight - bw - gap;
            PointF[] tri = new PointF[]
            {
                new PointF(cx + apex, cy),
                new PointF(cx + apex - tw, cy - bh),
                new PointF(cx + apex - tw, cy + bh)
            };
            g.FillPolygon(brush, tri);
        }
    }

    static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        GraphicsPath p = new GraphicsPath();
        float d = radius * 2;
        if (d > r.Width) d = r.Width;
        if (d > r.Height) d = r.Height;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    void PushLayer()
    {
        int w = canvas.Width;
        int h = canvas.Height;
        EnsureDib(w, h);

        Rectangle rect = new Rectangle(0, 0, w, h);
        BitmapData data = canvas.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        byte[] buffer = new byte[w * h * 4];
        int stride = data.Stride;
        byte[] srcBytes = new byte[stride * h];
        try
        {
            Marshal.Copy(data.Scan0, srcBytes, 0, srcBytes.Length);
        }
        finally
        {
            canvas.UnlockBits(data);
        }
        for (int y = 0; y < h; y++)
        {
            int srcRow = y * stride;
            int dstRow = y * w * 4;
            for (int x = 0; x < w; x++)
            {
                int si = srcRow + x * 4;
                int di = dstRow + x * 4;
                byte b = srcBytes[si];
                byte gg = srcBytes[si + 1];
                byte r = srcBytes[si + 2];
                byte a = srcBytes[si + 3];
                if (a != 255)
                {
                    b = (byte)((b * a) / 255);
                    gg = (byte)((gg * a) / 255);
                    r = (byte)((r * a) / 255);
                }
                buffer[di] = b;
                buffer[di + 1] = gg;
                buffer[di + 2] = r;
                buffer[di + 3] = a;
            }
        }
        Marshal.Copy(buffer, 0, dibBits, buffer.Length);

        IntPtr screenDc = GetDC(IntPtr.Zero);
        POINT dst = new POINT(); dst.X = screenX; dst.Y = screenY;
        SIZE size = new SIZE(); size.cx = w; size.cy = h;
        POINT srcPt = new POINT(); srcPt.X = 0; srcPt.Y = 0;
        BLENDFUNCTION blend = new BLENDFUNCTION();
        blend.BlendOp = 0;
        blend.BlendFlags = 0;
        blend.SourceConstantAlpha = 255;
        blend.AlphaFormat = 1;

        bool ok = UpdateLayeredWindow(form.Handle, screenDc, ref dst, ref size, memDc, ref srcPt, 0, ref blend, 2);
        ReleaseDC(IntPtr.Zero, screenDc);
        if (!ok) Debug("ulw-fail");
    }

    void EnsureDib(int w, int h)
    {
        if (hDib != IntPtr.Zero && dibW == w && dibH == h) return;
        ReleaseDib();

        BITMAPINFO bmi = new BITMAPINFO();
        bmi.bmiHeader.biSize = 40;
        bmi.bmiHeader.biWidth = w;
        bmi.bmiHeader.biHeight = -h;
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;
        bmi.bmiHeader.biCompression = 0;
        bmi.bmiHeader.biSizeImage = w * h * 4;

        hDib = CreateDIBSection(IntPtr.Zero, ref bmi, 0, out dibBits, IntPtr.Zero, 0);
        memDc = CreateCompatibleDC(IntPtr.Zero);
        oldObj = SelectObject(memDc, hDib);
        dibW = w;
        dibH = h;
    }

    void ReleaseDib()
    {
        if (memDc != IntPtr.Zero)
        {
            if (oldObj != IntPtr.Zero) SelectObject(memDc, oldObj);
            DeleteDC(memDc);
            memDc = IntPtr.Zero;
            oldObj = IntPtr.Zero;
        }
        if (hDib != IntPtr.Zero)
        {
            DeleteObject(hDib);
            hDib = IntPtr.Zero;
            dibBits = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        timer.Stop();
        moveTimer.Stop();
        ReleaseDib();
        if (canvasG != null) canvasG.Dispose();
        if (canvas != null) canvas.Dispose();
        form.Close();
        form.Dispose();
    }
}