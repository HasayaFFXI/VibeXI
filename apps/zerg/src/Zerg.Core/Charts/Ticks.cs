namespace Zerg.Core.Charts;

/// <summary>Where an axis puts its labels.</summary>
public static class Ticks
{
    const double Ln10 = 2.302585092994046;

    /// <summary>
    /// About <paramref name="count"/> round values from <paramref name="min"/>
    /// to <paramref name="max"/>: steps of 1, 2, 5 or 10 times a power of ten,
    /// so every label is a number a reader would have picked.
    /// </summary>
    public static List<double> Nice(double min, double max, double count)
    {
        if (!double.IsFinite(min) || !double.IsFinite(max) || max <= min)
            return [min != 0 && !double.IsNaN(min) ? min : 0];
        var span = max - min;
        var raw = span / Math.Max(1, count);
        var mag = Math.Pow(10, Math.Floor(Math.Log(raw) / Ln10));
        var norm = raw / mag;
        var step = (norm <= 1 ? 1 : norm <= 2 ? 2 : norm <= 5 ? 5 : 10) * mag;
        var output = new List<double>();
        if (!(step > 0)) return output;
        for (var v = Math.Ceiling(min / step) * step; v <= max + step * 1e-9; v += step)
            output.Add(Js.Round(v / step) * step);
        return output;
    }

    static readonly double[] TimeSteps = [1, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600, 7200, 14400];

    /// <summary>
    /// Ticks on the session clock, in milliseconds: multiples of a round step
    /// (a second up to four hours) counted from zero, the instant the session
    /// began. The round number a reader wants is 1:00 into the pull, not a
    /// round time of day, and zero is a real point on this axis.
    /// </summary>
    public static List<double> Time(double t0, double t1, double count)
    {
        var want = (t1 - t0) / 1000 / Math.Max(1, count);
        var step = TimeSteps[^1];
        foreach (var s in TimeSteps)
            if (s >= want) { step = s; break; }
        var output = new List<double>();
        var first = Math.Ceiling(t0 / (step * 1000)) * step * 1000;
        for (var t = first; t <= t1; t += step * 1000) output.Add(t);
        return output;
    }
}
