using System.Windows.Media;
using Zerg.Core;
using Zerg.Core.Charts;

namespace Zerg.Charts;

// What the charts are given. They only draw: values, names and colours come
// in, and nothing here knows about sessions, rosters or files.

/// <summary>
/// One line of a cumulative chart: a running total per grid time, in the
/// caller's colour. A value that is not finite means "no data here" and lifts
/// the pen. A <paramref name="Group"/> line stands for several characters at
/// once ("3 others"): it is drawn dashed in a neutral ink and its colour is
/// not used.
/// </summary>
public sealed record LineSeries(string Name, double[] Values, Color Color, bool Group = false) : ILineSeries;

/// <summary>
/// The lines of a cumulative chart on their shared time grid (milliseconds on
/// the session clock), drawn in the order given, so the last is on top. When
/// <paramref name="Live"/>, the last grid time is the live edge, not an event,
/// and the chart may move it (<see cref="LineChart.Edge"/>). When
/// <paramref name="Running"/>, the session is counting, and the chart rules
/// its right-hand edge in the colour that says so; a held session's chart
/// can be live (its edge is the held clock) and is not running.
/// </summary>
public sealed record LineModel(double[] Times, IReadOnlyList<LineSeries> Series, bool Live = false, bool Running = false);

/// <summary>One segment of a share bar (<see cref="ShareBar"/>): how much,
/// and the colour it is drawn in. The brush is frozen.</summary>
public sealed record ShareSlice(double Amount, Brush Fill);

/// <summary>
/// A histogram: its bins, the mean to mark, and how many values there are;
/// and, to mark where most of them fell, the quartiles the band runs
/// between and the median (each null for no mark).
/// </summary>
public sealed record HistogramModel(IReadOnlyList<Bin> Bins, double Avg, int Count,
                                    double? Q1 = null, double? Q3 = null, double? Median = null)
{
    public static HistogramModel From(Distribution d) => d.Count > 0
        ? new(d.Bins, d.Avg, d.Count, d.Q1, d.Q3, d.Median)
        : new(d.Bins, d.Avg, d.Count);
}

/// <summary>
/// A histogram of two runs on the bins they share: for each run the mean to
/// mark (null with no hit to average) and how many hits its columns are
/// shares of, and what one of them is called ("hit", "cast"). The runs'
/// colours are the theme's, not the caller's.
/// </summary>
public sealed record PairedHistogramModel(IReadOnlyList<PairBin> Bins, double? AvgA, double? AvgB, int CountA, int CountB,
                                          string Unit = "hit")
{
    public static PairedHistogramModel From(ActionSpread s) => new(s.Bins, s.AvgA, s.AvgB, s.HitsA, s.HitsB, s.Unit);
}
