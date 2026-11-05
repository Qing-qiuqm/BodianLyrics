using System;
using System.IO;

class Config
{
    public int Width = 620;
    public int RightMargin = 460;
    public int OffsetY = 0;
    public float FontSize = 20f;
    public int AccentR = 255;
    public int AccentG = 122;
    public int AccentB = 69;
    public bool Enabled = true;
    public bool AutoStart = false;
    public bool ShowPill = false;
    /// <summary>是否显示歌词两侧的上一首 / 下一首图标</summary>
    public bool ShowIcons = true;
    /// <summary>日志播放事件与实际出声之间的偏差（秒），由进度条像素同步自动学习</summary>
    public double Latency = 0;
    /// <summary>歌词字体名，留空则自动选择（优先苹方，回退微软雅黑）</summary>
    public string FontName = "";

    public string FilePath;

    public Config()
    {
        FilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
        Load();
    }

    void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            string[] rows = File.ReadAllLines(FilePath);
            for (int i = 0; i < rows.Length; i++)
            {
                string line = rows[i].Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int p = line.IndexOf('=');
                if (p <= 0) continue;
                string k = line.Substring(0, p).Trim();
                string v = line.Substring(p + 1).Trim();
                if (k == "Width") Width = ToInt(v, Width);
                else if (k == "RightMargin") RightMargin = ToInt(v, RightMargin);
                else if (k == "OffsetY") OffsetY = ToInt(v, OffsetY);
                else if (k == "FontSize") FontSize = ToFloat(v, FontSize);
                else if (k == "AccentR") AccentR = ToInt(v, AccentR);
                else if (k == "AccentG") AccentG = ToInt(v, AccentG);
                else if (k == "AccentB") AccentB = ToInt(v, AccentB);
                else if (k == "Enabled") Enabled = ToBool(v, Enabled);
                else if (k == "AutoStart") AutoStart = ToBool(v, AutoStart);
                else if (k == "ShowPill") ShowPill = ToBool(v, ShowPill);
                else if (k == "ShowIcons") ShowIcons = ToBool(v, ShowIcons);
                else if (k == "Latency") Latency = ToDouble(v, Latency);
                else if (k == "FontName") FontName = v;
            }
        }
        catch { }
    }

    public void Save()
    {
        try
        {
            using (StreamWriter w = new StreamWriter(FilePath, false, System.Text.Encoding.UTF8))
            {
                w.WriteLine("# BodianLyrics config");
                w.WriteLine("Width=" + Width);
                w.WriteLine("RightMargin=" + RightMargin);
                w.WriteLine("OffsetY=" + OffsetY);
                w.WriteLine("FontSize=" + FontSize.ToString("0.##"));
                w.WriteLine("AccentR=" + AccentR);
                w.WriteLine("AccentG=" + AccentG);
                w.WriteLine("AccentB=" + AccentB);
                w.WriteLine("Enabled=" + (Enabled ? "1" : "0"));
                w.WriteLine("AutoStart=" + (AutoStart ? "1" : "0"));
                w.WriteLine("ShowPill=" + (ShowPill ? "1" : "0"));
                w.WriteLine("ShowIcons=" + (ShowIcons ? "1" : "0"));
                w.WriteLine("Latency=" + Latency.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                w.WriteLine("FontName=" + FontName);
            }
        }
        catch { }
    }

    static int ToInt(string s, int d)
    {
        int v;
        if (int.TryParse(s, out v)) return v;
        return d;
    }

    static float ToFloat(string s, float d)
    {
        float v;
        if (float.TryParse(s, out v)) return v;
        return d;
    }

    static bool ToBool(string s, bool d)
    {
        if (s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
        if (s == "0" || s.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
        return d;
    }

    static double ToDouble(string s, double d)
    {
        double v;
        if (double.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out v)) return v;
        return d;
    }
}