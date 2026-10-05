using System.Globalization;

namespace Zerg.Core.Charts;

/// <summary>One column: the bin it stands for, whose it is (0 is run A, 1 is
/// run B), and its box. The top corners are rounded by <see cref="Radius"/>.</summary>
public sealed record PairedColumn(int Bin, int Run, double X, double Y, double W, double H, double Radius);

/// <summary>The rule marking one run's mean, and its label.</summary>
public sealed record RunMean(int Run, MeanRule Rule);

/// <summary>
/// Where everything on a histogram of two runs goes. The runs share the bins,
/// and each bin holds a column for A and one for B, side by side. A column's
/// height is its share of that run's hits, not a count: the runs are seldom
/// the same length, and by count the longer one would dwarf the other whatever
/// their shapes. Each run's mean is called out by a rule.
/// </summary>
public sealed class PairedHistogramLayout
{
    /// <summary>What a mean's label keeps clear ahead of its text, for the run's swatch.</summary>
    public const double SwatchRoom = 12;

    static readonly string Dash = ((char)0x2013).ToString();   // en dash

    IReadOnlyList<PairBin> bins = [];
    string unit = "hit";
    readonly int[] totals = new int[2];

    public bool Empty { get; private set; }
    public Box Plot { get; private set; }
    /// <summary>How wide each bin's slot is.</summary>
    public double Slot { get; private set; }
    public List<AxisTick> YTicks { get; } = [];
    public List<PairedColumn> Columns { get; } = [];
    public List<HistogramLabel> XLabels { get; } = [];
    public double BaselineY { get; private set; }
    public List<RunMean> Means { get; } = [];

    /// <param name="avgA">Run A's mean, or null when it has no hit to average.</param>
    /// <param name="countA">How many hits run A's columns are shares of.</param>
    /// <param name="textWidth">How wide a mean's label is drawn.</param>
    /// <param name="unit">What one of the things counted is called: a heal's are casts.</param>
    public static PairedHistogramLayout Compute(double width, double height, IReadOnlyList<PairBin>? bins,
                                                double? avgA, double? avgB, int countA, int countB,
                                                Func<string, double> textWidth, string unit = "hit")
    {
        var l = new PairedHistogramLayout { bins = bins ?? [], unit = unit };
        (l.totals[0], l.totals[1]) = (countA, countB);
        const double padTop = 22, raise = 16, padRight = 16, padBottom = 34, padLeft = 46;
        double plotW = Math.Max(10, width - padLeft - padRight);
        Box At(double top) => new(padLeft, top, plotW, Math.Max(10, height - top - padBottom));

        if (bins == null || bins.Count == 0 || countA + countB == 0)
        {
            l.Empty = true;
            l.Plot = At(padTop);
            return l;
        }

        double lo = bins[0].Lo, hi = bins[^1].Hi;
        if (hi <= lo) hi = lo + 1;
        double Bx(double v) => padLeft + (v - lo) / (hi - lo) * plotW;

        // The means first: a label lifted clear of the other run's takes a
        // second line above the plot, and the plot gives up that much height.
        var marks = new List<Mark>();
        foreach (var (run, avg, count) in new[] { (0, avgA, countA), (1, avgB, countB) })
        {
            if (avg is not double v || count == 0) continue;
            var x = Js.Round(Bx(v)) + 0.5;
            if (x < padLeft || x > padLeft + plotW) continue;
            var text = (run == 0 ? "A" : "B") + " avg " + Format.Int(v);
            marks.Add(new Mark(run, x, text, SwatchRoom + textWidth(text)));
        }
        Place(marks, padLeft, width);
        var plot = l.Plot = At(marks.Any(m => m.Raised) ? padTop + raise : padTop);
        foreach (var m in marks)
            l.Means.Add(new RunMean(m.Run, new MeanRule(m.X, plot.Y - 6 - (m.Raised ? raise : 0), plot.Y + plot.H, m.Text,
                                                        m.X + (m.Left ? -4 : 4), m.Left ? LabelAlign.Right : LabelAlign.Left)));

        double Part(int n, int run) => l.totals[run] != 0 ? (double)n / l.totals[run] : 0;
        double smax = 0;
        foreach (var b in bins) smax = Math.Max(smax, Math.Max(Part(b.A, 0), Part(b.B, 1)));
        if (smax == 0) smax = 1;
        double By(double share) => plot.Y + plot.H - share / smax * plot.H;

        foreach (var v in Ticks.Nice(0, smax * 100, 4))
            l.YTicks.Add(new AxisTick(v, Js.Round(By(v / 100)) + 0.5, Js.NumberToString(v) + "%"));

        // A bin's two columns fill it between them, as one column fills a
        // single histogram's bin: 2 px to the next bin, 1 px between the pair.
        var slot = l.Slot = plotW / bins.Count;
        var cw = Math.Max(1, (slot - 5) / 2);
        for (int i = 0; i < bins.Count; i++)
        {
            for (int run = 0; run < 2; run++)
            {
                int n = run == 0 ? bins[i].A : bins[i].B;
                if (n == 0) continue;
                var y = By(Part(n, run));
                var h = plot.Y + plot.H - y;
                l.Columns.Add(new PairedColumn(i, run, plot.X + slot * i + 2 + run * (cw + 1), y, cw, h,
                                               Math.Max(0, Math.Min(4, Math.Min(h / 2, cw / 2)))));
            }
        }

        l.BaselineY = Js.Round(plot.Y + plot.H) + 0.5;
        l.XLabels.AddRange(HistogramLayout.ValueLabels(plot, lo, hi));
        return l;
    }

    /// <summary>A mean on its way to a rule: where it is, and where its label goes.</summary>
    sealed class Mark(int run, double x, string text, double w)
    {
        public int Run => run;
        public double X => x;
        public string Text => text;
        public double W => w;
        /// <summary>The label ends at the rule rather than starting from it.</summary>
        public bool Left { get; set; }
        /// <summary>The label is on the line above the other's.</summary>
        public bool Raised { get; set; }
        public double From => Left ? x - 4 - w : x + 4;
        public double To => Left ? x - 4 : x + 4 + w;
    }

    /// <summary>
    /// Two labels turn their backs on each other: the lower mean's reads to
    /// the left of its rule and the higher's to the right, so neither can
    /// cover the other or cross its rule. One with no room on its own side is
    /// turned round, and if that puts it in the other's way it is lifted a
    /// line. A mean alone reads to the right, as a single histogram's does.
    /// </summary>
    /// <param name="left">Where the plot begins: left of it are the axis's own labels.</param>
    static void Place(List<Mark> marks, double left, double width)
    {
        if (marks.Count == 1)
        {
            marks[0].Left = marks[0].X + 4 + marks[0].W > width;
            return;
        }
        if (marks.Count != 2) return;
        var (low, high) = marks[0].X <= marks[1].X ? (marks[0], marks[1]) : (marks[1], marks[0]);
        low.Left = low.X - 4 - low.W >= left;
        high.Left = high.X + 4 + high.W > width;
        if (!low.Left && low.To + 4 > (high.Left ? high.From : high.X)) low.Raised = true;
        else if (high.Left && high.From - 4 < low.X) high.Raised = true;
    }

    /// <summary>What the pointer reads at (<paramref name="mx"/>,
    /// <paramref name="my"/>): the bin's range, and for each run how many of
    /// its hits fell in it and their share. Null outside the plot.</summary>
    public HistogramHover? Hover(double mx, double my)
    {
        if (Empty) return null;
        var plot = Plot;
        if (mx < plot.X || mx > plot.X + plot.W || my < plot.Y - 10 || my > plot.Y + plot.H + 10) return null;
        int k = (int)Math.Max(0, Math.Min(bins.Count - 1, Math.Floor((mx - plot.X) / Slot)));
        var b = bins[k];
        // A run with no hits at all has no share to give: a dash, not 0%.
        string Row(int n, int run) => totals[run] == 0 ? CompareSheet.Dash :
            n.ToString(CultureInfo.InvariantCulture) + " " + unit + (n == 1 ? "" : "s") + " · " +
            Format.Num((double)n / totals[run] * 100, 1) + "%";
        return new HistogramHover(k, Format.Int(b.Lo) + " " + Dash + " " + Format.Int(b.Hi),
                                  [new HoverRow("A", Row(b.A, 0), 0), new HoverRow("B", Row(b.B, 1), 1)]);
    }
}
