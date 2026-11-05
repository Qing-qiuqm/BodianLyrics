using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

class LyricsLine
{
    public double Start;
    public double End;
    public string Text = "";
    public double[] Times;
    public bool Timed = false;
}

class LyricsData
{
    public string Title = "";
    public string Artist = "";
    public List<LyricsLine> Lines = new List<LyricsLine>();

    public int FindLine(double pos)
    {
        int lo = 0, hi = Lines.Count - 1, ans = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (Lines[mid].Start <= pos) { ans = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return ans;
    }

    public static int FindChar(LyricsLine line, double pos)
    {
        if (line == null || line.Times == null || line.Times.Length == 0) return -1;
        int lo = 0, hi = line.Times.Length - 1, ans = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (line.Times[mid] <= pos) { ans = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return ans;
    }
}

static class LrcParser
{
    static readonly Regex TimeRx = new Regex(@"\[(\d+):(\d+(?:\.\d+)?)\]");
    static readonly Regex TagRx = new Regex(@"<(\d+),(-?\d+)>");
    static readonly Regex OffsetRx = new Regex(@"\[offset:(-?\d+)\]", RegexOptions.IgnoreCase);
    static readonly Regex TiRx = new Regex(@"\[ti:([^\]]*)\]", RegexOptions.IgnoreCase);
    static readonly Regex ArRx = new Regex(@"\[ar:([^\]]*)\]", RegexOptions.IgnoreCase);

    public static LyricsData Parse(string raw)
    {
        LyricsData data = new LyricsData();
        if (raw == null || raw.Length == 0) return data;
        string[] rows = raw.Replace("\r", "").Split('\n');
        double offset = 0;
        for (int i = 0; i < rows.Length; i++)
        {
            Match off = OffsetRx.Match(rows[i]);
            if (off.Success) offset = int.Parse(off.Groups[1].Value) / 1000.0;
            Match ti = TiRx.Match(rows[i]);
            if (ti.Success && data.Title.Length == 0) data.Title = ti.Groups[1].Value.Trim();
            Match ar = ArRx.Match(rows[i]);
            if (ar.Success && data.Artist.Length == 0) data.Artist = ar.Groups[1].Value.Trim();
        }
        for (int i = 0; i < rows.Length; i++)
        {
            string row = rows[i];
            Match tm = TimeRx.Match(row);
            if (!tm.Success) continue;
            double lineStart = int.Parse(tm.Groups[1].Value) * 60.0 + double.Parse(tm.Groups[2].Value);
            lineStart += offset;
            string content = row.Substring(tm.Index + tm.Length);
            List<double> times = new List<double>();
            StringBuilder text = new StringBuilder();
            MatchCollection tags = TagRx.Matches(content);
            if (tags.Count == 0)
            {
                string plain = content;
                if (plain.Length == 0) continue;
                for (int c = 0; c < plain.Length; c++) times.Add(lineStart);
                text.Append(plain);
            }
            else
            {
                int firstTag = tags[0].Index;
                string prefix = content.Substring(0, firstTag);
                if (prefix.Length > 0) { for (int c = 0; c < prefix.Length; c++) times.Add(lineStart); text.Append(prefix); }
                for (int t = 0; t < tags.Count; t++)
                {
                    Match m = tags[t];
                    int segStart = m.Index + m.Length;
                    int segEnd = (t + 1 < tags.Count) ? tags[t + 1].Index : content.Length;
                    string seg = content.Substring(segStart, segEnd - segStart);
                    if (seg.Length == 0) continue;
                    double st = lineStart + int.Parse(m.Groups[1].Value) / 1000.0;
                    for (int c = 0; c < seg.Length; c++) times.Add(st);
                    text.Append(seg);
                }
            }
            string full = text.ToString();
            int head = 0, tail = full.Length;
            while (head < tail && char.IsWhiteSpace(full[head])) head++;
            while (tail > head && char.IsWhiteSpace(full[tail - 1])) tail--;
            if (tail <= head) continue;
            LyricsLine line = new LyricsLine();
            line.Start = lineStart;
            line.Timed = tags.Count > 0;
            line.Text = full.Substring(head, tail - head);
            line.Times = new double[line.Text.Length];
            for (int c = 0; c < line.Text.Length; c++) { int src = head + c; line.Times[c] = (src < times.Count) ? times[src] : lineStart; }
            for (int c = 1; c < line.Times.Length; c++) { if (line.Times[c] < line.Times[c - 1] + 0.02) line.Times[c] = line.Times[c - 1] + 0.02; }
            data.Lines.Add(line);
        }
        data.Lines.Sort(delegate(LyricsLine a, LyricsLine b) { return a.Start.CompareTo(b.Start); });
        for (int i = 0; i < data.Lines.Count; i++)
        {
            if (i + 1 < data.Lines.Count) data.Lines[i].End = data.Lines[i + 1].Start;
            else data.Lines[i].End = data.Lines[i].Start + 6.0;
        }
        for (int i = 0; i < data.Lines.Count; i++)
        {
            LyricsLine ln = data.Lines[i];
            if (!ln.Timed || ln.Times == null || ln.Times.Length < 2) continue;
            double t0 = ln.Times[0];
            double tN = ln.Times[ln.Times.Length - 1];
            double span = tN - t0;
            double real = ln.End - ln.Start;
            if (span < 0.3 || real < 0.2) continue;
            for (int c = 0; c < ln.Times.Length; c++) ln.Times[c] = ln.Start + (ln.Times[c] - t0) / span * real;
        }
        return data;
    }
}

class LyricsProvider
{
    readonly string cacheDir;
    public LyricsProvider() { cacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache"); }

    public LyricsData Get(string id, string title)
    {
        if (id == null || id.Length == 0) return null;
        string file = Path.Combine(cacheDir, id + ".lrc");
        try
        {
            if (File.Exists(file))
            {
                string cached = File.ReadAllText(file, Encoding.UTF8);
                if (cached == "NO_LYRICS") return null;
                if (cached.Length > 0) { LyricsData d = LrcParser.Parse(cached); if (d.Lines.Count > 0) return d; }
            }
        }
        catch { }
        string text = Request(id, title);
        if (text == null) text = Request(id, "");
        try
        {
            Directory.CreateDirectory(cacheDir);
            if (text == null) File.WriteAllText(file, "NO_LYRICS", Encoding.UTF8);
            else File.WriteAllText(file, text, Encoding.UTF8);
        }
        catch { }
        if (text == null) return null;
        return LrcParser.Parse(text);
    }

    string Request(string id, string songName)
    {
        try
        {
            string q = "type=lyric&req=2&lrcx=1&rid=" + id + "&songname=" + songName + "&artist=&corp=kuwo&fromchannel=bodian";
            string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(q));
            string url = "http://mlyric.kuwo.cn/mobi.s?f=bodian&q=" + Uri.EscapeDataString(b64);
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = 8000; req.ReadWriteTimeout = 8000;
            req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";
            string json;
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (Stream s = resp.GetResponseStream())
            using (StreamReader sr = new StreamReader(s, Encoding.UTF8)) { json = sr.ReadToEnd(); }
            Match m = Regex.Match(json, "\"content\":\"([^\"]+)\"");
            if (!m.Success) return null;
            string lrc = Encoding.UTF8.GetString(Convert.FromBase64String(m.Groups[1].Value));
            if (lrc.Length == 0) return null;
            return lrc;
        }
        catch { return null; }
    }
}