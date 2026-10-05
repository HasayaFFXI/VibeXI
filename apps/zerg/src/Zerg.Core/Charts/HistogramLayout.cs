using System.Globalization;

namespace Zerg.Core.Charts;

/// <summary>One column: the bin it stands for, and its box. The top corners are
/// rounded by <see cref="Radius"/>.</summary>
public sealed record HistogramColumn(int Bin, double X, double Y, double W, double H, double Radius);

public enum LabelAlign { Left, Centre, Right }

/// <summary>A label under the histogram's baseline.</summary>
public sealed record HistogramLabel(string Text, double X, LabelAlign Align);

/// <summary>The rule marking the mean, and its label.</summary>
public sealed record MeanRule(double X, double Top, double Bottom, string Label, double LabelX, LabelAlign Align);

/// <summary>What the pointer reads over one bin.</summary>
public sealed record HistogramHover(int Bin, string Head, List<HoverRow> Rows);

/// <summary>
/// Where everything on a histogram goes. One colour for every column (a value
/// ramp would say height twice), with the mean called out by a rule.
/// </summary>
public sealed class HistogramLayout
{
    /// <summary>From the plot's left edge to a count label, and from its floor
    /// to a value label.</summary>
    public const double YLabelGap = 8, XLabelGap = 10;

    static readonly string Dash = ((char)0x2013).ToString();   // en dash

    IReadOnlyList<Bin> bins = [];
    int total;

    public bool Empty { get; private set; }
    public Box Plot { get; private set; }
    /// <summary>How wide each bin's slot is.</summary>
    public double Slot { get; private set; }
    public List<AxisTick> YTicks { get; } = [];
    public List<HistogramColumn> Columns { get; } = [];
    public List<HistogramLabel> XLabels { get; } = [];
    public double BaselineY { get; private set; }
    public MeanRule? Mean { get; private set; }

    /// <param name="count">How many values the bins hold between them.</param>
    public static HistogramLayout Compute(double width, double height, IReadOnlyList<Bin>? bins, double avg, int count)
    {
        var l = new HistogramLayout { bins = bins ?? [], total = count };
        const double padTop = 22, padRight = 16, padBottom = 34, padLeft = 46;
        l.Plot = new Box(padLeft, padTop,
                         Math.Max(10, width - padLeft - padRight),
                         Math.Max(10, height - padTop - padBottom));
        var plot = l.Plot;

        if (bins == null || bins.Count == 0)
        {
            l.Empty = true;
            return l;
        }

        double lo = bins[0].Lo, hi = bins[^1].Hi;
        if (hi <= lo) hi = lo + 1;

        double cmax = 0;
        foreach (var b in bins) cmax = Math.Max(cmax, b.Count);
        if (cmax == 0) cmax = 1;

        double Bx(double v) => plot.X + (v - lo) / (hi - lo) * plot.W;
        double By(double c) => plot.Y + plot.H - c / cmax * plot.H;

        foreach (var v in Ticks.Nice(0, cmax, 4))
            l.YTicks.Add(new AxisTick(v, Js.Round(By(v)) + 0.5, Format.Int(v)));

        // A column fills its bin: its width is the bin's interval, which is
        // data, so a categorical bar's thickness cap must not apply. Only a
        // 2 px gap separates neighbours.
        var slot = l.Slot = plot.W / bins.Count;
        var bw = Math.Max(1, slot - 2);
        for (int i = 0; i < bins.Count; i++)
        {
            if (bins[i].Count == 0) continue;
            var y = By(bins[i].Count);
            var h = plot.Y + plot.H - y;
            l.Columns.Add(new HistogramColumn(i, plot.X + slot * i + 1, y, bw, h,
                                              Math.Max(0, Math.Min(4, Math.Min(h / 2, bw / 2)))));
        }

        l.BaselineY = Js.Round(plot.Y + plot.H) + 0.5;

        l.XLabels.AddRange(ValueLabels(plot, lo, hi));

        var mx = Js.Round(Bx(avg)) + 0.5;
        if (mx >= plot.X && mx <= plot.X + plot.W)
        {
            // The label flips to the rule's left near the right edge.
            bool flip = mx > plot.X + plot.W - 46;
            l.Mean = new MeanRule(mx, plot.Y - 6, plot.Y + plot.H, "avg " + Format.Int(avg),
                                  mx + (flip ? -4 : 4), flip ? LabelAlign.Right : LabelAlign.Left);
        }
        return l;
    }

    /// <summary>The labels under a histogram's baseline: round values across
    /// the range; both ends are always shown, and a round value that would
    /// collide with an end is dropped.</summary>
    internal static List<HistogramLabel> ValueLabels(Box plot, double lo, double hi)
    {
        var labels = new List<HistogramLabel>();
        foreach (var v in Ticks.Nice(lo, hi, Math.Max(2, Math.Floor(plot.W / 110))))
        {
            var tx = plot.X + (v - lo) / (hi - lo) * plot.W;
            if (tx - plot.X < 34 || plot.X + plot.W - tx < 34) continue;
            labels.Add(new HistogramLabel(Format.Int(v), tx, LabelAlign.Centre));
        }
        labels.Add(new HistogramLabel(Format.Int(lo), plot.X, LabelAlign.Left));
        labels.Add(new HistogramLabel(Format.Int(hi), plot.X + plot.W, LabelAlign.Right));
        return labels;
    }

    /// <summary>What the pointer reads at (<paramref name="mx"/>,
    /// <paramref name="my"/>): the bin's range, how many values fell in it and
    /// their share. Null outside the plot.</summary>
    public HistogramHover? Hover(double mx, double my)
    {
        if (Empty) return null;
        var plot = Plot;
        if (mx < plot.X || mx > plot.X + plot.W || my < plot.Y - 10 || my > plot.Y + plot.H + 10) return null;
        int k = (int)Math.Max(0, Math.Min(bins.Count - 1, Math.Floor((mx - plot.X) / Slot)));
        var b = bins[k];
        return new HistogramHover(k, Format.Int(b.Lo) + " " + Dash + " " + Format.Int(b.Hi),
        [
            new HoverRow("Hits", b.Count.ToString(CultureInfo.InvariantCulture)),
            new HoverRow("Share", Format.Num(total != 0 ? (double)b.Count / total * 100 : 0, 1) + "%"),
        ]);
    }
}
