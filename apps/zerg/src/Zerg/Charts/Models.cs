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
/// and the chart may move it (<see cref="LineChart.Edge"/>).
/// </summary>
public sealed record LineModel(double[] Times, IReadOnlyList<LineSeries> Series, bool Live = false);

/// <summary>One line of a hover card: what, and how much.</summary>
public sealed record CardRow(string Label, string Value);

/// <summary>
/// One bar. <paramref name="Hover"/> is what its hover card lists; without it
/// the card shows the value alone. <paramref name="Key"/> says which bar this
/// is from one update to the next (the label, unless given), so a bar grows
/// from where it was.
/// </summary>
public sealed record BarRow(string Label, double Value, Color Color, IReadOnlyList<CardRow>? Hover = null,
                            string? Key = null) : IBarRow;

/// <summary>A histogram: its bins, the mean to mark, and how many values there are.</summary>
public sealed record HistogramModel(IReadOnlyList<Bin> Bins, double Avg, int Count)
{
    public static HistogramModel From(Distribution d) => new(d.Bins, d.Avg, d.Count);
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
