namespace Zerg.Core;

/// <summary>
/// The lengths of the marks drawn in a table row, as shares of the room the
/// mark has: no drawing. One scale holds everywhere: a row's share is drawn
/// to its value over the largest of its siblings, never over the sum, so the
/// longest is the whole room and the differences between rows are as large
/// as they can be drawn. The figure beside it carries the share of the
/// whole.
/// </summary>
public static class RowMarks
{
    /// <summary>
    /// How long a row's shade or small bar is: its total out of the largest
    /// total among the rows it stands with, 0 to 1. A character's out of the
    /// leader's; an action's out of that character's largest action. 0 with
    /// nothing to be a share of.
    /// </summary>
    public static double Fraction(double value, double largest) =>
        largest > 0 && !double.IsNaN(value) && !double.IsInfinity(value) && !double.IsInfinity(largest)
            ? Math.Clamp(value / largest, 0, 1)
            : 0;

    /// <summary>The largest of a set of totals, or 0 with none: what
    /// <see cref="Fraction"/> is taken out of.</summary>
    public static double Largest(IEnumerable<double> totals)
    {
        double most = 0;
        foreach (var t in totals)
            if (t > most && !double.IsInfinity(t)) most = t;
        return most;
    }

    /// <summary>
    /// Where an action's least hit, average and greatest hit stand on a line
    /// as long as the character's biggest hit of any action, each 0 to 1, in
    /// order. All three 0 when the character has hit for nothing.
    /// </summary>
    public static (double Min, double Avg, double Max) Spread(double min, double avg, double max, double biggest)
    {
        double lo = Fraction(min, biggest), hi = Math.Max(lo, Fraction(max, biggest));
        return (lo, Math.Clamp(Fraction(avg, biggest), lo, hi), hi);
    }

    /// <summary>The furthest any of the values stands from the average, or
    /// 0 with none: what <see cref="Offset"/> is taken out of.</summary>
    public static double Furthest(IEnumerable<double> values, double average)
    {
        double most = 0;
        foreach (var v in values)
        {
            double away = Math.Abs(v - average);
            if (away > most && !double.IsInfinity(away)) most = away;
        }
        return most;
    }

    /// <summary>
    /// How far one hit fell from the average, out of the furthest any hit in
    /// the list fell: over the average up to 1, under it down to -1, 0 on
    /// it or when every hit is the same.
    /// </summary>
    public static double Offset(double value, double average, double furthest) =>
        furthest > 0 && !double.IsNaN(value) && !double.IsNaN(average)
            ? Math.Clamp((value - average) / furthest, -1, 1)
            : 0;
}
