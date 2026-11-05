using System;
using System.Drawing;
using System.Windows.Forms;

/// <summary>托盘左键单击弹出的设置窗口，所有改动即时生效并写回 config.ini</summary>
class SettingsForm : Form
{
    const string AutoFont = "（自动：优先苹方，回退微软雅黑）";

    readonly TrayApp app;
    readonly Config cfg;

    // 载入配置时置位，避免控件赋值反过来触发保存
    bool loading;

    CheckBox cbVisible;
    CheckBox cbIcons;
    CheckBox cbPill;
    CheckBox cbAutoStart;
    NumericUpDown numWidth;
    NumericUpDown numRight;
    NumericUpDown numOffset;
    NumericUpDown numFont;
    ComboBox cbFont;
    Button btnColor;
    Label lblColor;

    public SettingsForm(TrayApp app)
    {
        this.app = app;
        this.cfg = app.Cfg;

        Text = "波点音乐任务栏歌词 · 设置";
        Font = new Font("Microsoft YaHei UI", 9f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(424, 550);
        BackColor = Color.FromArgb(250, 250, 252);

        BuildUi();
        LoadValues();
    }

    void BuildUi()
    {
        // ---- 显示 ----
        GroupBox g1 = MakeGroup("显示", 12, 10, 400, 90);

        cbVisible = MakeCheck(g1, "显示歌词", 14, 26, 170);
        cbVisible.CheckedChanged += new EventHandler(delegate
        {
            if (loading) return;
            app.SetLyricsVisible(cbVisible.Checked);
        });

        cbIcons = MakeCheck(g1, "显示播放图标", 208, 26, 180);
        cbIcons.CheckedChanged += new EventHandler(delegate
        {
            if (loading) return;
            cfg.ShowIcons = cbIcons.Checked;
            app.ApplyConfig();
        });

        cbPill = MakeCheck(g1, "胶囊底（半透明背景）", 14, 54, 240);
        cbPill.CheckedChanged += new EventHandler(delegate
        {
            if (loading) return;
            cfg.ShowPill = cbPill.Checked;
            app.ApplyConfig();
        });

        // ---- 位置与字号 ----
        GroupBox g2 = MakeGroup("位置与字号", 12, 108, 400, 158);

        numWidth = MakeNum(g2, "窗口宽度（px）", 14, 24, 200, 1250, cfg.Width, 10, 80);
        numWidth.ValueChanged += new EventHandler(delegate
        {
            if (loading) return;
            cfg.Width = (int)numWidth.Value;
            app.ApplyConfig();
        });

        numRight = MakeNum(g2, "距屏幕右端（px）", 14, 56, 0, 2000, cfg.RightMargin, 5, 80);
        numRight.ValueChanged += new EventHandler(delegate
        {
            if (loading) return;
            cfg.RightMargin = (int)numRight.Value;
            app.ApplyConfig();
        });

        numOffset = MakeNum(g2, "垂直偏移（px）", 14, 88, -40, 40, cfg.OffsetY, 1, 80);
        numOffset.ValueChanged += new EventHandler(delegate
        {
            if (loading) return;
            cfg.OffsetY = (int)numOffset.Value;
            app.ApplyConfig();
        });

        numFont = MakeNum(g2, "字号（px）", 14, 120, 11, 32, (decimal)cfg.FontSize, 1, 80);
        numFont.ValueChanged += new EventHandler(delegate
        {
            if (loading) return;
            cfg.FontSize = (float)numFont.Value;
            app.ApplyConfig();
        });

        Button btnReset = MakeButton(g2, "恢复默认位置", 250, 88, 130, 28);
        btnReset.Click += new EventHandler(delegate
        {
            cfg.RightMargin = 460;
            cfg.OffsetY = 0;
            app.ApplyConfig();
            LoadValues();
        });

        // ---- 字体 ----
        GroupBox g3 = MakeGroup("字体", 12, 274, 400, 68);

        cbFont = new ComboBox();
        cbFont.DropDownStyle = ComboBoxStyle.DropDownList;
        cbFont.SetBounds(14, 28, 370, 24);
        cbFont.Items.Add(AutoFont);
        try
        {
            System.Drawing.Text.InstalledFontCollection col = new System.Drawing.Text.InstalledFontCollection();
            string[] names = new string[col.Families.Length];
            for (int i = 0; i < col.Families.Length; i++) names[i] = col.Families[i].Name;
            Array.Sort(names, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < names.Length; i++) cbFont.Items.Add(names[i]);
        }
        catch { }
        g3.Controls.Add(cbFont);
        cbFont.SelectedIndexChanged += new EventHandler(delegate
        {
            if (loading) return;
            string s = (string)cbFont.SelectedItem;
            cfg.FontName = (s == AutoFont) ? "" : s;
            app.ApplyConfig();
        });

        // ---- 主题色 ----
        GroupBox g4 = MakeGroup("主题色（已唱文字）", 12, 350, 400, 68);

        btnColor = MakeButton(g4, "选择颜色", 14, 26, 96, 28);
        btnColor.FlatStyle = FlatStyle.Flat;
        btnColor.Click += new EventHandler(delegate
        {
            using (ColorDialog d = new ColorDialog())
            {
                d.FullOpen = true;
                d.Color = Color.FromArgb(cfg.AccentR, cfg.AccentG, cfg.AccentB);
                if (d.ShowDialog(this) != DialogResult.OK) return;
                cfg.AccentR = d.Color.R;
                cfg.AccentG = d.Color.G;
                cfg.AccentB = d.Color.B;
                UpdateColorLabel();
                app.ApplyConfig();
            }
        });

        lblColor = new Label();
        lblColor.SetBounds(120, 32, 220, 20);
        lblColor.TextAlign = ContentAlignment.MiddleLeft;
        g4.Controls.Add(lblColor);

        // ---- 播放控制 ----
        GroupBox g5 = MakeGroup("播放控制", 12, 426, 400, 66);

        Button btnPrev = MakeButton(g5, "上一首", 14, 22, 96, 28);
        btnPrev.Click += new EventHandler(delegate { app.PlayPrev(); });

        Button btnPlay = MakeButton(g5, "播放 / 暂停", 120, 22, 110, 28);
        btnPlay.Click += new EventHandler(delegate { app.PlayPause(); });

        Button btnNext = MakeButton(g5, "下一首", 240, 22, 96, 28);
        btnNext.Click += new EventHandler(delegate { app.PlayNext(); });

        // ---- 底部 ----
        cbAutoStart = MakeCheck(this, "开机自启", 14, 508, 150);
        cbAutoStart.CheckedChanged += new EventHandler(delegate
        {
            if (loading) return;
            app.SetAutoStart(cbAutoStart.Checked);
        });

        Button btnUpdate = MakeButton(this, "检查更新", 170, 504, 110, 30);
        btnUpdate.Click += new EventHandler(delegate { app.CheckUpdateManual(); });

        Button btnClose = MakeButton(this, "关闭", 292, 504, 118, 30);
        btnClose.Click += new EventHandler(delegate { Close(); });

        AcceptButton = btnClose;
        CancelButton = btnClose;
    }

    /// <summary>把 config.ini 的当前值刷新到界面上</summary>
    public void LoadValues()
    {
        loading = true;

        cbVisible.Checked = cfg.Enabled;
        cbIcons.Checked = cfg.ShowIcons;
        cbPill.Checked = cfg.ShowPill;
        cbAutoStart.Checked = cfg.AutoStart;

        numWidth.Value = Clamp(cfg.Width, numWidth);
        numRight.Value = Clamp(cfg.RightMargin, numRight);
        numOffset.Value = Clamp(cfg.OffsetY, numOffset);
        numFont.Value = Clamp((int)cfg.FontSize, numFont);

        int idx = 0;
        if (cfg.FontName != null && cfg.FontName.Length > 0)
        {
            int found = cbFont.Items.IndexOf(cfg.FontName);
            idx = (found >= 0) ? found : 0;
        }
        cbFont.SelectedIndex = idx;

        UpdateColorLabel();

        loading = false;
    }

    static decimal Clamp(int v, NumericUpDown n)
    {
        decimal d = v;
        if (d < n.Minimum) d = n.Minimum;
        if (d > n.Maximum) d = n.Maximum;
        return d;
    }

    void UpdateColorLabel()
    {
        Color c = Color.FromArgb(cfg.AccentR, cfg.AccentG, cfg.AccentB);
        lblColor.Text = string.Format("R {0}   G {1}   B {2}", cfg.AccentR, cfg.AccentG, cfg.AccentB);
        btnColor.BackColor = c;
        btnColor.ForeColor = (c.R + c.G + c.B > 400) ? Color.Black : Color.White;
    }

    GroupBox MakeGroup(string text, int x, int y, int w, int h)
    {
        GroupBox g = new GroupBox();
        g.Text = text;
        g.SetBounds(x, y, w, h);
        Controls.Add(g);
        return g;
    }

    static CheckBox MakeCheck(Control parent, string text, int x, int y, int w)
    {
        CheckBox c = new CheckBox();
        c.Text = text;
        c.SetBounds(x, y, w, 20);
        parent.Controls.Add(c);
        return c;
    }

    static Button MakeButton(Control parent, string text, int x, int y, int w, int h)
    {
        Button b = new Button();
        b.Text = text;
        b.SetBounds(x, y, w, h);
        parent.Controls.Add(b);
        return b;
    }

    static NumericUpDown MakeNum(Control parent, string text, int x, int y,
        int min, int max, decimal val, decimal step, int width)
    {
        Label l = new Label();
        l.Text = text;
        l.SetBounds(x, y + 3, 116, 20);
        l.TextAlign = ContentAlignment.MiddleLeft;
        parent.Controls.Add(l);

        NumericUpDown n = new NumericUpDown();
        n.SetBounds(x + 118, y, width, 24);
        n.Minimum = min;
        n.Maximum = max;
        n.Increment = step;
        if (val < min) val = min;
        if (val > max) val = max;
        n.Value = val;
        parent.Controls.Add(n);
        return n;
    }
}
