namespace Zerg.Core.Charts;

/// <summary>A rectangle in the chart's own coordinates (device-independent pixels).</summary>
public readonly record struct Box(double X, double Y, double W, double H)
{
    public double Right => X + W;
    public double Bottom => Y + H;
}

/// <summary>One axis label: the value, where it sits along the axis, and its text.</summary>
public sealed record AxisTick(double Value, double Pos, string Label);

/// <summary>
/// One row of a hover card. <see cref="Series"/> says whose swatch leads the
/// row (an index into the chart's series), or -1 for none.
/// </summary>
public sealed record HoverRow(string Label, string Value, int Series = -1);

/// <summary>Things every chart agrees on.</summary>
public static class TextFit
{
    public static readonly string Ellipsis = ((char)0x2026).ToString();

    /// <summary>The text, cut short with an ellipsis if it is wider than
    /// <paramref name="maxWidth"/>.</summary>
    public static string Clip(string text, double maxWidth, Func<string, double> width)
    {
        if (width(text) <= maxWidth) return text;
        var t = text;
        while (t.Length > 1 && width(t + Ellipsis) > maxWidth) t = t[..^1];
        return t + Ellipsis;
    }
}

/// <summary>Where a hover card goes.</summary>
public static class HoverCard
{
    /// <summary>
    /// The card's top-left corner for a point at (<paramref name="x"/>,
    /// <paramref name="y"/>): to the right of the point, or to its left when
    /// the right side has no room, and always inside the chart. In a short
    /// chart a card hung below the pointer would run off the bottom.
    /// </summary>
    public static (double Left, double Top) Place(double x, double y, double cardW, double cardH,
                                                  double chartW, double chartH)
    {
        var left = x + 14;
        if (left + cardW > chartW - 4) left = x - cardW - 14;
        if (left < 4) left = 4;
        var top = y - 12;
        if (chartH != 0 && top + cardH > chartH - 4) top = chartH - cardH - 4;
        return (left, Math.Max(4, top));
    }
}
