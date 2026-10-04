using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows.Media;

namespace ChartSpike;

/// <summary>One line on the chart. Values are running totals on the shared grid;
/// NaN means "no data here" (Compare's shorter run) and lifts the pen.</summary>
public sealed class Series
{
    public required string Name;
    public required double[] Values;
    public Color Color;
    public bool Group;
}

public sealed class LineModel
{
    public double[] Times = [];          // ms since the session's zero; the last one is the live edge
    public List<Series> Series = [];
    public bool Live;
}

/// <summary>Just enough of the counting to feed the chart the same data the web
/// build draws: hits by player actors, binned the way stats.cumulative bins.
/// Not the N1 port; the spike's fixture has no pets, chains or monsters.</summary>
public static class Data
{
    public record Hit(double T, string Actor, double Dmg);

    public static List<Hit> Load(string path)
    {
        var hits = new List<Hit>();
        foreach (var line in File.ReadLines(path))
        {
            using var doc = JsonDocument.Parse(line);
            var r = doc.RootElement;
            string kind = r.GetProperty("kind").GetString()!;
            if (kind is "job" or "meta" or "heal") continue;
            if (!r.GetProperty("hit").GetBoolean()) continue;
            double dmg = r.GetProperty("dmg").GetDouble();
            if (dmg <= 0) continue;
            hits.Add(new Hit(r.GetProperty("t").GetDouble() * 1000, r.GetProperty("actor").GetString()!, dmg));
        }
        return hits;
    }

    /// <summary>stats.cumulative: at most ~400 bins of whole seconds from the
    /// zero, plus one live sample pinned to `now`.</summary>
    public static LineModel Cumulative(List<Hit> hits, double zero, double now, int maxPoints = 400)
    {
        var names = hits.GroupBy(h => h.Actor).Select(g => g.Key).ToList();
        double t0 = 0, t1 = Math.Max(hits.Max(h => h.T) - zero, now);
        if (t1 <= t0) t1 = t0 + 1000;
        double step = Math.Max(1000, Math.Ceiling((t1 - t0) / maxPoints / 1000) * 1000);
        int nb = (int)Math.Floor((t1 - t0) / step) + 1, n = nb + 1;
        var m = new LineModel { Times = new double[n], Live = true };
        for (int i = 0; i < nb; i++) m.Times[i] = t0 + i * step;
        m.Times[nb] = t1;
        var idx = new Dictionary<string, Series>();
        foreach (var name in names)
        {
            var s = new Series { Name = name, Values = new double[n] };
            idx[name] = s;
            m.Series.Add(s);
        }
        foreach (var h in hits)
        {
            int bin = Math.Clamp((int)Math.Floor((h.T - zero - t0) / step), 0, nb - 1);
            idx[h.Actor].Values[bin] += h.Dmg;
        }
        foreach (var s in m.Series)
        {
            double run = 0;
            for (int j = 0; j < n; j++) { run += s.Values[j]; s.Values[j] = run; }
        }
        // Largest total last, so the leader is drawn on top.
        m.Series.Sort((a, b) => a.Values[^1].CompareTo(b.Values[^1]));
        return m;
    }

    /// <summary>app.js groupSmall: everyone under 5% of the total, except the
    /// owner, folded into one dashed "N others" series.</summary>
    public static void GroupSmall(LineModel m, string owner)
    {
        double total = m.Series.Sum(s => s.Values[^1]);
        var small = m.Series.Where(s => s.Values[^1] / total < 0.05 && s.Name != owner).ToList();
        if (small.Count < 2) return;
        var sum = new double[m.Times.Length];
        foreach (var s in small) for (int i = 0; i < sum.Length; i++) sum[i] += s.Values[i];
        m.Series.RemoveAll(small.Contains);
        m.Series.Add(new Series { Name = $"{small.Count} others", Values = sum, Group = true });
        m.Series.Sort((a, b) => a.Values[^1].CompareTo(b.Values[^1]));
    }

    // The 18-slot fallback palette (shared-ui --series-1..18), per theme.
    static readonly string[] Dark =
    {
        "#3987e5", "#d95926", "#199e70", "#c98500", "#d55181", "#4f9e2f", "#9085e9", "#e66767", "#9968a4",
        "#55712d", "#9546b2", "#30a2bb", "#a35450", "#4261cd", "#ce64c1", "#067396", "#b13a80", "#028f95",
    };
    static readonly string[] Light =
    {
        "#2a78d6", "#c94f18", "#12855c", "#9a6800", "#c23a6d", "#3d7f21", "#5a4bc4", "#cf3b3a", "#b056b8",
        "#3b5b0b", "#6d3c78", "#098aa7", "#9e0822", "#284a99", "#bb76aa", "#00749b", "#98005f", "#129d9c",
    };

    public static Color Slot(int slot, bool dark) =>
        (Color)ColorConverter.ConvertFromString((dark ? Dark : Light)[slot % 18]);
}

/// <summary>stats.js's formatters and chart.js's tick pickers.</summary>
public static class Fmt
{
    static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");

    public static string Int(double n) => Math.Round(n, MidpointRounding.AwayFromZero).ToString("N0", En);

    public static string Num(double n, int dp) => n.ToString("N" + dp, En);

    public static string Compact(double n)
    {
        double a = Math.Abs(n);
        if (a >= 1e6) return Num(n / 1e6, 1) + "M";
        if (a >= 1e4) return Num(n / 1e3, 1) + "K";
        return Int(n);
    }

    public static string Elapsed(double ms)
    {
        bool neg = ms < 0;
        long sec = (long)Math.Max(0, Math.Floor(Math.Abs(ms) / 1000));
        long h = sec / 3600, m = sec % 3600 / 60, s = sec % 60;
        string o = h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m}:{s:00}";
        return neg ? "-" + o : o;
    }

    public static List<double> NiceTicks(double min, double max, int count)
    {
        if (!double.IsFinite(min) || !double.IsFinite(max) || max <= min) return [min];
        double raw = (max - min) / Math.Max(1, count);
        double mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double norm = raw / mag;
        double step = (norm <= 1 ? 1 : norm <= 2 ? 2 : norm <= 5 ? 5 : 10) * mag;
        var o = new List<double>();
        for (double v = Math.Ceiling(min / step) * step; v <= max + step * 1e-9; v += step)
            o.Add(Math.Round(v / step) * step);
        return o;
    }

    static readonly int[] TimeSteps = [1, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600, 7200, 14400];

    public static List<double> TimeTicks(double t0, double t1, int count)
    {
        double want = (t1 - t0) / 1000 / Math.Max(1, count);
        int step = TimeSteps[^1];
        foreach (var s in TimeSteps) if (s >= want) { step = s; break; }
        var o = new List<double>();
        for (double t = Math.Ceiling(t0 / (step * 1000.0)) * step * 1000; t <= t1; t += step * 1000) o.Add(t);
        return o;
    }
}
