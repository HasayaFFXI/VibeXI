namespace Zerg.Core;

/// <summary>
/// The draw frequency: how many times a second everything is redrawn that
/// the clock moves and not an event. That is the elapsed time, every DPS and
/// HPS (each falls as the time under it grows), and the live edge of the two
/// cumulative charts, which runs on through a quiet stretch. Whatever an
/// event moves is redrawn when the event arrives, and this has no say in it.
///
/// <para>Set on the Settings page. More is smoother and costs more processor
/// time; the top of the range is half what these monitors refresh at, because
/// a chart redrawn at every refresh costs a whole core.</para>
/// </summary>
public static class DrawRate
{
    public const int Min = 1, Max = 60;

    /// <summary>Smooth to the eye, at a quarter of the cost of every refresh.</summary>
    public const int Initial = 30;

    /// <summary>A whole number of times a second within range. Nothing usable
    /// (zero, or not a number: what a damaged settings file reads as) is the
    /// fallback, never the slowest, which would look like a chart that stuck.</summary>
    public static int Clamp(double value, int fallback = Initial)
    {
        if (!double.IsFinite(value)) return fallback;
        var v = Js.Round(value);
        return v == 0 ? fallback : (int)Math.Clamp(v, Min, Max);
    }

    /// <summary>The time from one draw to the next.</summary>
    public static TimeSpan Interval(int perSecond) => TimeSpan.FromSeconds(1.0 / Clamp(perSecond));
}
