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
/// The label at a line's end. With <see cref="Swatch"/> it is the named form
/// (a swatch at <see cref="X"/>, the text 15 px to its right); without, the
/// text starts at <see cref="X"/>. <see cref="Y"/> is the text's middle.
/// </summary>
public sealed record EndLabel(int Series, double X, double Y, string Text, bool Swatch);

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
/// </summary>
public sealed class LineLayout
{
    /// <summary>The dot at a line's end, and on the crosshair.</summary>
    public const double MarkerRadius = 4.5;
    /// <summary>The swatch leading a named end label.</summary>
    public const double SwatchWidth = 10, SwatchHeight = 3, SwatchToText = 15;
    /// <summary>From the plot's edge to an axis label.</summary>
    public const double LabelGap = 10;
    /// <summary>The longest a name may run in an end label before it is cut.</summary>
    public const double NameWidth = 104;

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
    /// <param name="endLabels">Print every line's name and total at its end, in
    /// place of a legend. Without it, a chart of four lines or fewer gets its
    /// totals alone, and only when they don't collide.</param>
    /// <param name="labelWidth">The width of a string in the end labels' type.</param>
    public static LineLayout Compute(double width, double height, double[] times,
                                     IReadOnlyList<ILineSeries> series, bool compact, bool endLabels,
                                     Func<string, double> labelWidth)
    {
        var l = new LineLayout { times = times, series = series, Width = width, Height = height };

        double padTop = compact ? 10 : 18, padRight = compact ? 12 : 76,
               padBottom = compact ? 24 : 34, padLeft = compact ? 46 : 68;

        string LabelText(ILineSeries s) =>
            TextFit.Clip(s.Name, NameWidth, labelWidth) + " " + Format.Compact(LastValue(s.Values));

        // The right margin is sized to the widest label, so a long name costs
        // plot width instead of being cut off by the edge.
        if (endLabels && series.Count > 0)
        {
            double widest = 0;
            foreach (var s in series) widest = Math.Max(widest, labelWidth(LabelText(s)));
            padRight = Math.Ceiling(widest) + 34;   // gap, swatch, gap, text, air
        }

        l.Plot = new Box(padLeft, padTop,
                         Math.Max(10, width - padLeft - padRight),
                         Math.Max(10, height - padTop - padBottom));
        var plot = l.Plot;

        if (times.Length == 0 || series.Count == 0)
        {
            l.Empty = true;
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

        // At least three: asked for two over a 4:58 pull, the step rounds up
        // to five minutes and the only tick left is 0:00.
        foreach (var t in Ticks.Time(l.T0, l.T1, Math.Max(3, Math.Floor(plot.W / 90))))
            l.XTicks.Add(new AxisTick(t, Js.Round(l.Px(t)) + 0.5, Format.Elapsed(t)));

        l.AxisY = Js.Round(plot.Y + plot.H) + 0.5;

        for (int i = 0; i < series.Count; i++)
        {
            var li = LastIndex(series[i].Values);
            if (li < 0) continue;
            l.Markers.Add(new EndMarker(i, l.Px(times[li]), l.Py(series[i].Values[li])));
        }

        if (endLabels)
        {
            // Every line is printed, so where two would collide they are nudged
            // apart instead of dropped: downwards in order, then settled back
            // up from the floor if that pushed the last one off the plot.
            const double gap = 13;
            var floor = plot.Y + plot.H;
            var tags = new List<(int Series, double Y, string Text)>();
            for (int i = 0; i < series.Count; i++)
                tags.Add((i, l.Py(LastValue(series[i].Values)), LabelText(series[i])));
            Js.StableSort(tags, (a, b) => a.Y - b.Y);
            for (int i = 1; i < tags.Count; i++)
                tags[i] = tags[i] with { Y = Math.Max(tags[i].Y, tags[i - 1].Y + gap) };
            if (tags[^1].Y > floor)
            {
                tags[^1] = tags[^1] with { Y = floor };
                for (int i = tags.Count - 2; i >= 0; i--)
                    tags[i] = tags[i] with { Y = Math.Min(tags[i].Y, tags[i + 1].Y - gap) };
            }
            var tx = plot.X + plot.W + 11;
            foreach (var t in tags) l.Labels.Add(new EndLabel(t.Series, tx, t.Y, t.Text, Swatch: true));
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
                if (labels[i].Y - labels[i - 1].Y < 14) { collide = true; break; }
            if (!collide)
                foreach (var t in labels) l.Labels.Add(new EndLabel(t.Series, t.X + 11, t.Y, t.Text, Swatch: false));
        }

        return l;
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
