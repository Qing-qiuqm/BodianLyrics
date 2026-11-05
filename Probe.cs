using System;
using System.IO;
using System.Text;
using System.Threading;

static class Probe
{
    public static void Run()
    {
        Config cfg = new Config();
        PlayerState state = new PlayerState();
        LyricsProvider provider = new LyricsProvider();
        LogWatcher watcher = new LogWatcher(state, provider, cfg);
        BodianController controller = new BodianController();

        string outFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "probe.txt");
        StringBuilder sb = new StringBuilder();

        for (int i = 0; i < 10; i++)
        {
            watcher.Poll();
            Thread.Sleep(500);
        }

        double ratio = -1;
        bool ok = controller.TryGetProgress(out ratio);
        double ratio2 = -1;
        if (ok)
        {
            Thread.Sleep(8000);
            controller.TryGetProgress(out ratio2);
        }

        sb.AppendLine("time=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("hasSong=" + state.HasSong);
        sb.AppendLine("songId=" + state.SongId);
        sb.AppendLine("title=" + state.Title);
        sb.AppendLine("artist=" + state.Artist);
        sb.AppendLine("duration=" + state.Duration.ToString("0.000"));
        sb.AppendLine("playing=" + state.Playing);
        sb.AppendLine("position=" + state.GetPosition().ToString("0.000"));
        sb.AppendLine("bodianFound=" + controller.Find());

        LyricsData data = state.Lyrics;
        if (data == null)
        {
            sb.AppendLine("lyrics=none");
        }
        else
        {
            sb.AppendLine("lyricsLines=" + data.Lines.Count);
            sb.AppendLine("lyricsTitle=" + data.Title);
            sb.AppendLine("lyricsArtist=" + data.Artist);
            double pos = state.GetPosition();
            int li = data.FindLine(pos);
            if (li >= 0)
            {
                LyricsLine line = data.Lines[li];
                int ci = LyricsData.FindChar(line, pos);
                sb.AppendLine("lineIndex=" + li);
                sb.AppendLine("lineStart=" + line.Start.ToString("0.000"));
                sb.AppendLine("lineEnd=" + line.End.ToString("0.000"));
                sb.AppendLine("lineText=" + line.Text);
                sb.AppendLine("highlight=" + (ci + 1));
            }
            else
            {
                sb.AppendLine("lineIndex=-1");
            }
        }

        sb.AppendLine("progressOk=" + ok);
        if (ok) sb.AppendLine("progressRatio=" + ratio.ToString("0.000"));
        if (ok) sb.AppendLine("progressRatio2=" + ratio2.ToString("0.000"));

        try
        {
            File.WriteAllText(outFile, sb.ToString(), Encoding.UTF8);
        }
        catch { }
    }
}
