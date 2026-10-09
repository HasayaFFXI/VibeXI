namespace Zerg.Core.Charts;

/// <summary>
/// A session in two small marks: how much was dealt (or healed) in each
/// stretch of it, and what the rate over the whole session had come to by
/// the end of each stretch.
/// </summary>
/// <param name="Bucket">How long each stretch is, in milliseconds.</param>
/// <param name="Bars">The amount in each stretch, first to last. The last
/// is usually still filling.</param>
/// <param name="Running">Everything up to the end of each stretch, over the
/// time up to it, per second: the party's DPS (or HPS) as it stood then. The
/// last is over the whole span, so it is the figure printed beside it.</param>
public sealed record Pulse(double Bucket, IReadOnlyList<double> Bars, IReadOnlyList<double> Running);

/// <summary>
/// Where the main window's bands put things, as numbers: the marks beside
/// three of the five figures, how many figures stand in a row, and how many
/// of the characters' chips one line holds. No drawing; <c>Zerg/Charts/Spark</c>
/// and the panels in <c>Zerg/Views</c> paint what this decides.
/// </summary>
public static class BandMarks
{
    // ------------------------------------------------------------ the figures

    /// <summary>Five figures in a row need the width for it; a narrower band
    /// stands them in threes, then in twos.</summary>
    public static int Columns(double width) => width >= 1040 ? 5 : width >= 640 ? 3 : 2;

    /// <summary>The same for Compare's band of six: six in a row from 1200
    /// units, each cell then 200 wide or more; narrower, threes, then twos,
    /// so every row is full.</summary>
    public static int PairColumns(double width) => width >= 1200 ? 6 : width >= 640 ? 3 : 2;

    /// <summary>A figure's cell has room for its mark beside the figure.</summary>
    public static bool RoomForMark(double cellWidth) => cellWidth >= 240;

    // ------------------------------------------------------- per 20 s, running

    /// <summary>The shortest stretch a bar stands for: 20 seconds.</summary>
    public const double ShortestBucket = 20_000;

    /// <summary>How many bars the mark has room for.</summary>
    public const int MostBars = 44;

    /// <summary>
    /// How long a stretch each bar stands for in a session this long: 20
    /// seconds, doubled until the session needs no more than
    /// <see cref="MostBars"/> of them. Fourteen minutes and forty seconds is
    /// the longest session drawn in 20 second bars; a two-hour one is drawn
    /// in bars of 320 seconds.
    /// </summary>
    public static double Bucket(double span)
    {
        double bucket = ShortestBucket;
        if (double.IsNaN(span) || double.IsInfinity(span)) return bucket;
        while (Math.Ceiling(span / bucket) > MostBars) bucket *= 2;
        return bucket;
    }

    /// <summary>What the bars are captioned: "per 20 s", "per 40 s", ...</summary>
    public static string Caption(double bucket) =>
        "per " + Format.Int(Math.Round(bucket / 1000)) + " s";

    /// <summary>
    /// The two marks for these rows, which are on a session clock, at
    /// <paramref name="elapsed"/> milliseconds on that clock. Null when
    /// nothing in them counts: there is no mark to draw.
    ///
    /// <para>A row counts here exactly when it counts toward a line of the
    /// cumulative chart (<see cref="Counting.Cumulative"/>): it hit, for
    /// something. So the bars add up to what those lines end at.</para>
    ///
    /// <para>The span is the clock, or the newest row if that is later: rows
    /// played in from a file can be dated ahead of the clock.</para>
    /// </summary>
    public static Pulse? Pulse(IReadOnlyList<CombatEvent> events, double elapsed)
    {
        double span = double.IsNaN(elapsed) ? 0 : Math.Max(0, elapsed);
        bool any = false;
        foreach (var e in events)
        {
            if (!Counts(e)) continue;
            any = true;
            if (e.T > span) span = e.T;
        }
        if (!any || span <= 0) return null;

        double bucket = Bucket(span);
        int n = Math.Max(1, (int)Math.Ceiling(span / bucket));
        var bars = new double[n];
        foreach (var e in events)
        {
            if (!Counts(e)) continue;
            bars[(int)Math.Clamp(Math.Floor(e.T / bucket), 0, n - 1)] += e.Dmg;
        }

        var running = new double[n];
        double run = 0;
        for (int i = 0; i < n; i++)
        {
            run += bars[i];
            // The last stretch ends where the session has got to.
            double end = Math.Min((i + 1) * bucket, span);
            running[i] = end > 0 ? run / (end / 1000) : 0;
        }
        return new Pulse(bucket, bars, running);
    }

    static bool Counts(CombatEvent e) => e.Hit && e.Dmg != 0 && !double.IsNaN(e.Dmg);

    // ----------------------------------------------------------- party share

    /// <summary>
    /// One bar <paramref name="width"/> long, cut into a segment per amount
    /// in the order given, each as long as its share of the whole, with
    /// <paramref name="gap"/> between two segments. Returns where each
    /// begins and how long it is; an amount that is not above zero gets no
    /// length and no gap.
    ///
    /// <para>No segment is shorter than <paramref name="least"/>: a share
    /// too small to see is given that much, and the others give it up in
    /// proportion. When there is not room even for that, the gaps go first.</para>
    /// </summary>
    public static IReadOnlyList<(double X, double Width)> Shares(IReadOnlyList<double> amounts, double width,
                                                                 double gap = 1, double least = 1)
    {
        var result = new (double X, double Width)[amounts.Count];
        var shown = new List<int>();
        for (int i = 0; i < amounts.Count; i++)
            if (amounts[i] > 0 && !double.IsInfinity(amounts[i])) shown.Add(i);
        int n = shown.Count;
        if (n == 0 || !(width > 0)) return result;

        double room = width - gap * (n - 1);
        if (room < least * n)
        {
            gap = 0;
            room = width;
            least = Math.Min(least, room / n);
        }

        // Whoever would be shorter than the least is given the least, and
        // the rest share what is left; which may push another under it.
        var small = new HashSet<int>();
        var lengths = new double[amounts.Count];
        while (true)
        {
            double left = room - least * small.Count, sum = 0;
            foreach (int i in shown)
                if (!small.Contains(i)) sum += amounts[i];
            bool again = false;
            foreach (int i in shown)
            {
                if (small.Contains(i)) { lengths[i] = least; continue; }
                lengths[i] = sum > 0 ? left * amounts[i] / sum : 0;
                if (lengths[i] < least) { small.Add(i); again = true; }
            }
            if (!again) break;
        }

        double x = 0;
        foreach (int i in shown)
        {
            result[i] = (x, lengths[i]);
            x += lengths[i] + gap;
        }
        return result;
    }

    // ------------------------------------------------------------ characters

    /// <summary>
    /// How many chips, first to last, one line holds. All of them when all
    /// fit in <paramref name="room"/>. Otherwise as many as fit with the
    /// "+N" chip after them, which is <paramref name="more"/> wide; that may
    /// be none.
    /// </summary>
    /// <param name="widths">Each chip's width, in the order they are drawn.</param>
    /// <param name="gap">The room between two chips.</param>
    public static int Fit(IReadOnlyList<double> widths, double gap, double room, double more)
    {
        // Layout rounds to whole pixels: a hair over is not "does not fit".
        const double Slack = 0.01;
        double all = 0;
        for (int i = 0; i < widths.Count; i++) all += widths[i] + (i > 0 ? gap : 0);
        if (all <= room + Slack) return widths.Count;

        double x = 0;
        int count = 0;
        for (int i = 0; i < widths.Count; i++)
        {
            double end = x + (i > 0 ? gap : 0) + widths[i];
            if (end + gap + more > room + Slack) break;
            x = end;
            count++;
        }
        return count;
    }
}
