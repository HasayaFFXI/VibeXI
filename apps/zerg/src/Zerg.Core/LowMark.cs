namespace Zerg.Core;

/// <summary>
/// How a low rate is marked: four strengths, 0 to 1.
/// </summary>
/// <param name="Rule">A red rule under the figure.</param>
/// <param name="Surface">The pane's own surface laid over the row, which
/// cuts the cell out of whatever shade the row has.</param>
/// <param name="Wash">Red laid over that.</param>
/// <param name="Outline">A red line round the cut-out.</param>
public readonly record struct LowMarkLook(double Rule, double Surface, double Wash, double Outline)
{
    /// <summary>Nothing is drawn: the rate is at the threshold or over it,
    /// or there is no rate.</summary>
    public bool None => Rule <= 0 && Surface <= 0 && Wash <= 0 && Outline <= 0;

    /// <summary>The cell is cut out of its row.</summary>
    public bool CutOut => Surface > 0;
}

/// <summary>
/// The low accuracy mark: Accuracy, WS Acc and Pet Acc under a threshold are
/// marked in red, more strongly the lower they fall. With most of a party
/// under the line the mark has to stay calm, so it is graded in kind as well
/// as in strength: just under the threshold a thin rule under the figure and
/// nothing else; further down the cell is cut out of its row (filled with the
/// pane's surface, washed and outlined red), because a wash of red over a row
/// already shaded red, magenta or orange would disappear; at 60% and under it
/// is at its strongest.
///
/// <para>The threshold is a setting, 90% as installed
/// (<see cref="Installed"/>). Where the mark is strongest is not: 60%, or 15
/// points under the threshold if that is lower.</para>
///
/// <para>The design gives the bands in words and by example; the function
/// here is fitted to the nine marks its sheets draw and gives each to within
/// 0.03, with one exception: the sheet draws no cut-out at 84.4%, where the
/// fit gives a faint one, so a cut-out is not drawn until it is a tenth of
/// the way in.</para>
/// </summary>
public static class LowMark
{
    /// <summary>The threshold as installed, and the range it can be set in, in percent.</summary>
    public const int Installed = 90, Least = 50, Most = 100;

    /// <summary>Where the mark is at its strongest, unless the threshold is
    /// within <see cref="Span"/> of it.</summary>
    const double Strongest = 0.60, Span = 0.15;

    /// <summary>How far in the cut-out has to be before it is drawn at all.</summary>
    const double FirstCut = 0.1;

    const double StrongestWash = 0.34, CutWash = 0.195, StrongestOutline = 0.72, OutlineOverWash = 2.12;

    /// <summary>A threshold as it may be set: a whole percentage,
    /// <see cref="Least"/> to <see cref="Most"/>.</summary>
    public static int Clamp(int percent) => Math.Clamp(percent, Least, Most);

    /// <summary>
    /// The mark for a rate (0 to 1, or null where there is nothing to
    /// measure) under a threshold given in percent.
    /// </summary>
    public static LowMarkLook Of(double? rate, int thresholdPercent)
    {
        if (rate is not double r || double.IsNaN(r)) return default;
        double top = Clamp(thresholdPercent) / 100.0;
        if (r >= top) return default;

        double bottom = Math.Min(Strongest, top - Span);
        // How far down from the threshold to where the mark is strongest.
        double t = Math.Clamp((top - r) / (top - bottom), 0, 1);
        // How far in the cut-out is: none over the first sixth of the way,
        // whole from halfway.
        double k = Math.Clamp((t - 1.0 / 6) / (1.0 / 3), 0, 1);

        double rule = k <= 0 ? 0.55 + 0.30 * t : k >= 1 ? 0 : Math.Max(0, 0.60 - 0.55 * k);
        if (k < FirstCut) return new LowMarkLook(rule, 0, 0, 0);

        double wash = k < 1 ? CutWash * k : CutWash + (StrongestWash - CutWash) * (t - 0.5) / 0.5;
        double outline = Math.Min(StrongestOutline, OutlineOverWash * wash);
        return new LowMarkLook(rule, k, wash, outline);
    }
}
