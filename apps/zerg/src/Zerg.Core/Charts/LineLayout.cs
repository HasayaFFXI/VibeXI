namespace Zerg.Core.Charts;

/// <summary>One line of a cumulative chart, as the layout needs it.</summary>
public interface ILineSeries
{
    string Name { get; }
    /// <summary>One value per grid time. A value that is not finite means
    /// "no data here" and lifts the pen.</summary>
    double[] Values { get; }
    /// <summary>The line stands for several characters at once ("3 others"):
    /// drawn dashed, in a neutral ink instead of anyone's colour.</summary>
    bool Group { get; }
}

/// <summary>The dot at a line's last sample.</summary>
public sealed record EndMarker(int Series, double X, double Y);

/// <summary>
/// The thin line from a marker to a label that was moved clear of its
/// neighbours: out from the marker at (<see cref="X0"/>, <see cref="Y0"/>),
/// across to the label's height at <see cref="X1"/>, then level to
/// <see cref="X2"/>, just short of the text.
/// </summary>
public readonly record struct Elbow(double X0, double Y0, double X1, double X2, double Y1);

/// <summary>
/// The label at a line's end: its text starts at <see cref="X"/>, and
/// <see cref="Y"/> is the text's middle. <see cref="Named"/> is the form that
/// stands in for a legend, a name in the margin right of the plot; without
/// it the text is the line's total alone, beside its own marker.
/// </summary>
public sealed record EndLabel(int Series, double X, double Y, string Text, bool Named)
{
    /// <summary>The line's total, printed after the name where the chart is
    /// wide enough to give up the room; empty where it is not.</summary>
    public string Total { get; init; } = "";
    /// <summary>Where the total ends: the totals are a column of their own,
    /// set against its right-hand edge.</summary>
    public double TotalRight { get; init; }
    /// <summary>The leading line's label, which is drawn stronger.</summary>
    public bool Lead { get; init; }
    /// <summary>From the marker to the label, when the label is not level with it.</summary>
    public Elbow? Elbow { get; init; }
}

/// <summary>What the crosshair reads at one grid time.</summary>
public sealed class LineHover
{
    /// <summary>The grid index under the pointer.</summary>
    public int Index { get; init; }
    /// <summary>The crosshair line, on a pixel centre.</summary>
    public double X { get; init; }
    /// <summary>Where the card is anchored.</summary>
    public double AnchorX { get; init; }
    public string Head { get; init; } = "";
    /// <summary>Every line with a value at this time, largest first.</summary>
    public List<HoverRow> Rows { get; init; } = [];
    /// <summary>A dot per row, in series order.</summary>
    public List<EndMarker> Dots { get; init; } = [];
}

/// <summary>
/// Where everything on a cumulative line chart goes: the plot, both axes'
/// labels, the end markers and end labels, and what the crosshair reads.
/// No drawing here; the chart element paints what this says.
///
/// <para>All series share one time grid, so the crosshair reads every line at
/// the same instant.</para>
///
/// <para>Only the time axis depends on the grid's last time. A live chart
/// steps that time many times a second (<see cref="Retime"/>), and the plot,
/// the value axis, the markers and the labels are left as a count made
/// them.</para>
/// </summary>
public sealed class LineLayout
{
    /// <summary>The dot at a line's end, and on the crosshair.</summary>
    public const double MarkerRadius = 3;
    /// <summary>From the plot's left edge to a value label, and from its floor
    /// to the top of a time label.</summary>
    public const double YLabelGap = 6, XLabelGap = 4;
    /// <summary>How far a time's tick hangs under the baseline.</summary>
    public const double TickLength = 3;
    /// <summary>The least run of plot a time label is given.</summary>
    public const double TimeLabelRoom = 45;
    /// <summary>The longest a name may run in an end label before it is cut.</summary>
    public const double NameWidth = 96;
    /// <summary>From the plot's right edge to a named label, and from one
    /// label's middle to the next when they are moved apart.</summary>
    public const double LabelLead = 14, LabelPitch = 12;
    /// <summary>Between the names and the column of totals.</summary>
    public const double TotalGap = 6;
    /// <summary>The narrowest plot that still gives up room for the totals
    /// after the names. Under it the labels are names alone.</summary>
    public const double TotalsFrom = 400;
    /// <summary>A label this far from level with its marker, or further, gets
    /// a line back to it.</summary>
    public const double ElbowFrom = 2;
    /// <summary>From a marker's middle to a total printed beside it.</summary>
    public const double BesideMarker = 9;

    double[] times = [];
    IReadOnlyList<ILineSeries> series = [];

    /// <summary>Nothing to draw: no grid, or no lines.</summary>
    public bool Empty { get; private set; }
    public double Width { get; private set; }
    public double Height { get; private set; }
    public Box Plot { get; private set; }
    public double T0 { get; private set; }
    public double T1 { get; private set; }
    /// <summary>The value at the top of the plot.</summary>
    public double YTop { get; private set; }
    public List<AxisTick> YTicks { get; private set; } = [];
    public List<AxisTick> XTicks { get; private set; } = [];
    /// <summary>The baseline, on a pixel centre.</summary>
    public double AxisY { get; private set; }
    public List<EndMarker> Markers { get; } = [];
    public List<EndLabel> Labels { get; } = [];
    /// <summary>The line that leads, drawn stronger than the pack: the
    /// largest that is one character's. -1 on a chart with no named labels,
    /// whose lines are equals (two runs compared).</summary>
    public int Leader { get; private set; } = -1;

    public double Px(double t) => Plot.X + (t - T0) / (T1 - T0) * Plot.W;
    public double Py(double v) => Plot.Y + Plot.H - v / YTop * Plot.H;

    /// <summary>Index of the last finite sample, or -1. Every live line ends at
    /// its last sample; only a compared run that ended early stops short.</summary>
    public static int LastIndex(double[] values)
    {
        for (int i = values.Length - 1; i >= 0; i--)
            if (double.IsFinite(values[i])) return i;
        return -1;
    }

    public static double LastValue(double[] values)
    {
        var i = LastIndex(values);
        return i < 0 ? 0 : values[i];
    }

    /// <param name="compact">Tighter margins, for a chart floating over the game.</param>
    /// <param name="endLabels">Name every line at its end, in place of a
    /// legend. Without it, a chart of four lines or fewer gets its totals
    /// alone, and only when they don't collide.</param>
    /// <param name="labelWidth">The width of a string in the end labels' type;
    /// the second argument asks for the stronger weight the leader's is drawn in.</param>
    public static LineLayout Compute(double width, double height, double[] times,
                                     IReadOnlyList<ILineSeries> series, bool compact, bool endLabels,
                                     Func<string, bool, double> labelWidth)
    {
        var l = new LineLayout { times = times, series = series, Width = width, Height = height };

        double padTop = compact ? 10 : 14, padRight = compact ? 12 : 60, padBottom = 22, padLeft = compact ? 40 : 42;
        // Between a label's end and the chart's own edge. A panel's body has
        // a margin of its own outside the chart.
        double air = compact ? 4 : 10;
        double plotH = Math.Max(10, height - padTop - padBottom);

        // Who is named, when the lines are: as many as the plot's height has
        // room for at one pitch each, the largest first. An alliance in a
        // short chart keeps its leaders' names; the rest are still lines,
        // and the hover card still reads them all.
        var named = new List<int>();
        var names = new string[series.Count];
        double nameW = 0, totalW = 0;
        bool totals = false;
        if (endLabels && series.Count > 0)
        {
            for (int i = 0; i < series.Count; i++)
            {
                if (LastIndex(series[i].Values) < 0) continue;
                named.Add(i);
                if (!series[i].Group && (l.Leader < 0 || LastValue(series[i].Values) >= LastValue(series[l.Leader].Values)))
                    l.Leader = i;
            }
            Js.StableSort(named, (a, b) => LastValue(series[b].Values) - LastValue(series[a].Values));
            int room = (int)Math.Floor(plotH / LabelPitch) + 1;
            if (named.Count > room) named.RemoveRange(room, named.Count - room);

            foreach (var i in named)
            {
                bool lead = i == l.Leader;
                names[i] = TextFit.Clip(series[i].Name, NameWidth, s => labelWidth(s, lead));
                nameW = Math.Max(nameW, labelWidth(names[i], lead));
                totalW = Math.Max(totalW, labelWidth(Format.Compact(LastValue(series[i].Values)), false));
            }
            nameW = Math.Ceiling(nameW);
            totalW = Math.Ceiling(totalW);

            // The right margin is sized to the widest label, so a long name
            // costs plot width instead of being cut off by the edge. The
            // totals are printed too where the plot can spare their column.
            double withNames = LabelLead + nameW + air, withTotals = withNames + TotalGap + totalW;
            totals = named.Count > 0 && width - padLeft - withTotals >= TotalsFrom;
            padRight = totals ? withTotals : withNames;
        }

        l.Plot = new Box(padLeft, padTop, Math.Max(10, width - padLeft - padRight), plotH);
        var plot = l.Plot;

        if (times.Length == 0 || series.Count == 0)
        {
            l.Empty = true;
            l.Leader = -1;
            return l;
        }

        l.T0 = times[0];
        l.T1 = times[^1];
        if (l.T1 == l.T0) l.T1 = l.T0 + 1000;

        // The largest value anywhere, not the last one: a line can end early,
        // and a running total peaks wherever it stops.
        double vmax = 0;
        foreach (var s in series)
            foreach (var v in s.Values)
                if (v > vmax) vmax = v;
        if (vmax <= 0) vmax = 1;

        // Three gridlines, not five, once the plot is short enough that five
        // would sit closer together than their labels are tall.
        var yTicks = Ticks.Nice(0, vmax, plot.H < 200 ? 3 : 5);
        l.YTop = Math.Max(vmax, yTicks[^1]);
        foreach (var v in yTicks)
            l.YTicks.Add(new AxisTick(v, Js.Round(l.Py(v)) + 0.5, Format.Compact(v)));

        l.XTicks = l.TimeTicks();

        l.AxisY = Js.Round(plot.Y + plot.H) + 0.5;

        for (int i = 0; i < series.Count; i++)
        {
            var li = LastIndex(series[i].Values);
            if (li < 0) continue;
            l.Markers.Add(new EndMarker(i, l.Px(times[li]), l.Py(series[i].Values[li])));
        }

        if (endLabels)
        {
            // Where two would collide they are moved apart instead of
            // dropped: downwards in order, then settled back up from the
            // floor if that pushed the last one off the plot. There is room:
            // no more are named than the plot has pitches for.
            var floor = plot.Y + plot.H;
            var tags = named.Select(i => (Series: i, Y: l.Py(LastValue(series[i].Values)))).ToList();
            Js.StableSort(tags, (a, b) => a.Y - b.Y);
            for (int i = 1; i < tags.Count; i++)
                tags[i] = tags[i] with { Y = Math.Max(tags[i].Y, tags[i - 1].Y + LabelPitch) };
            if (tags.Count > 0 && tags[^1].Y > floor)
            {
                tags[^1] = tags[^1] with { Y = floor };
                for (int i = tags.Count - 2; i >= 0; i--)
                    tags[i] = tags[i] with { Y = Math.Min(tags[i].Y, tags[i + 1].Y - LabelPitch) };
            }

            double edge = plot.X + plot.W, tx = edge + LabelLead;
            foreach (var t in tags)
            {
                var v = series[t.Series].Values;
                // A line back to the marker for a label that was moved, but
                // only from a marker at the plot's edge: a line that stopped
                // early has its marker somewhere inside the plot.
                double my = l.Py(LastValue(v));
                bool atEdge = LastIndex(v) == times.Length - 1;
                l.Labels.Add(new EndLabel(t.Series, tx, t.Y, names[t.Series], Named: true)
                {
                    Total = totals ? Format.Compact(LastValue(v)) : "",
                    TotalRight = totals ? tx + nameW + TotalGap + totalW : 0,
                    Lead = t.Series == l.Leader,
                    Elbow = atEdge && Math.Abs(t.Y - my) > ElbowFrom
                        ? new Elbow(edge + MarkerRadius + 1, my, edge + 8, edge + LabelLead - 2, t.Y)
                        : null,
                });
            }
        }
        else if (series.Count <= 4)
        {
            var labels = new List<(int Series, double X, double Y, string Text)>();
            for (int i = 0; i < series.Count; i++)
            {
                var v = series[i].Values;
                // Beside its own end marker: the plot's right edge, unless the
                // line stopped early.
                labels.Add((i, l.Px(times[Math.Max(0, LastIndex(v))]), l.Py(LastValue(v)), Format.Compact(LastValue(v))));
            }
            Js.StableSort(labels, (a, b) => a.Y - b.Y);
            bool collide = false;
            for (int i = 1; i < labels.Count; i++)
                if (labels[i].Y - labels[i - 1].Y < LabelPitch) { collide = true; break; }
            if (!collide)
                foreach (var t in labels) l.Labels.Add(new EndLabel(t.Series, t.X + BesideMarker, t.Y, t.Text, Named: false));
        }

        return l;
    }

    /// <summary>
    /// The grid's times have changed and nothing else has: a live chart's
    /// edge has stepped on. Only the time axis is worked out again (the
    /// span, and so <see cref="Px"/>, the time labels and what the crosshair
    /// reads). The plot, the value axis, the markers and the labels stand,
    /// which is right while every line runs to the grid's last time, as
    /// every line of a live chart does: its marker is at the plot's edge
    /// whatever that time is.
    /// </summary>
    public void Retime(double[] times)
    {
        this.times = times;
        if (Empty || times.Length == 0) return;
        T0 = times[0];
        T1 = times[^1];
        if (T1 == T0) T1 = T0 + 1000;
        XTicks = TimeTicks();
    }

    List<AxisTick> TimeTicks()
    {
        // A label to every 45 units or more of plot: the longest ("1:00:00")
        // still has air either side. At least three: asked for two over a
        // 4:58 pull, the step rounds up to five minutes and the only tick
        // left is 0:00.
        var ticks = new List<AxisTick>();
        foreach (var t in Ticks.Time(T0, T1, Math.Max(3, Math.Floor(Plot.W / TimeLabelRoom))))
            ticks.Add(new AxisTick(t, Js.Round(Px(t)) + 0.5, Format.Elapsed(t)));
        return ticks;
    }

    /// <summary>
    /// What the crosshair reads with the pointer at (<paramref name="mx"/>,
    /// <paramref name="my"/>), or null when the pointer is more than 8 px
    /// outside the plot. The crosshair sits on the nearest grid time.
    /// </summary>
    public LineHover? Hover(double mx, double my)
    {
        if (Empty) return null;
        var plot = Plot;
        if (mx < plot.X - 8 || mx > plot.X + plot.W + 8 || my < plot.Y - 8 || my > plot.Y + plot.H + 8) return null;

        var frac = (mx - plot.X) / plot.W;
        int idx = (int)Math.Max(0, Math.Min(times.Length - 1, Js.Round(frac * (times.Length - 1))));
        var px = Px(times[idx]);
        var x = Js.Round(px) + 0.5;

        var at = new List<int>();
        var dots = new List<EndMarker>();
        for (int i = 0; i < series.Count; i++)
        {
            var v = series[i].Values[idx];
            if (!double.IsFinite(v)) continue;   // a compared run past its own end
            at.Add(i);
            dots.Add(new EndMarker(i, x, Py(v)));
        }
        Js.StableSort(at, (a, b) => series[b].Values[idx] - series[a].Values[idx]);

        return new LineHover
        {
            Index = idx,
            X = x,
            AnchorX = px,
            Head = Format.Elapsed(times[idx]),
            Rows = at.Select(i => new HoverRow(series[i].Name, Format.Int(series[i].Values[idx]), i)).ToList(),
            Dots = dots,
        };
    }
}
