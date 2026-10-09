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

/// <summary>One row of a hover card, placed: where its swatch and its label
/// start, where its value ends, and the top of its line.</summary>
public readonly record struct CardRowPlace(double SwatchX, double LabelX, double ValueRight, double Top);

/// <summary>
/// A hover card, arranged: a heading, a rule under it, and the rows, in
/// columns when there are more than the chart is tall enough for. Everything
/// is measured from the card's own top-left corner.
/// </summary>
public sealed record CardLayout(double Width, double Height, double HeadSwatchX, double HeadX, double HeadMid,
                                double? RuleY, double RowHeight, IReadOnlyList<CardRowPlace> Rows);

/// <summary>Where a hover card goes, and where everything in it goes.</summary>
public static class HoverCard
{
    /// <summary>The square that says whose a row is, and its rounding.</summary>
    public const double Swatch = 7, SwatchRadius = 1.5;
    /// <summary>The card's own rounding.</summary>
    public const double Radius = 5;
    /// <summary>From a swatch to the text after it.</summary>
    const double SwatchGap = 5;
    /// <summary>The least room between a label and its value, and between two columns of rows.</summary>
    const double ValueGap = 14, ColumnGap = 18;

    /// <summary>
    /// Arranges a card. <paramref name="compact"/> is the denser card of a
    /// chart floating over the game. The rows are the width of each one's
    /// label and of its value, in order; with more of them than a chart
    /// <paramref name="chartHeight"/> tall has room for, they run in two or
    /// three columns: in a small floating window an alliance's eighteen
    /// would otherwise be cut off. <paramref name="snap"/> puts the card's
    /// width and height on whole device pixels (none: left as they come).
    /// </summary>
    public static CardLayout Arrange(double headWidth, bool headSwatch, IReadOnlyList<(double Label, double Value)> rows,
                                     bool swatches, double chartHeight, bool compact, Func<double, double>? snap = null)
    {
        // The heading has a band of its own, ruled off from the rows.
        double padX = compact ? 8 : 10, headBand = compact ? 20 : 23, rowH = compact ? 13 : 15;
        const double afterRule = 3, foot = 2;
        double lead = swatches ? Swatch + SwatchGap : 0;
        double headLead = headSwatch ? Swatch + SwatchGap : 0;
        int count = rows.Count;
        double top = count > 0 ? headBand + afterRule : headBand;

        double room = chartHeight - 8 - top - foot;
        int perColumn = Math.Max(1, (int)Math.Floor(room / rowH));
        int columns = Math.Clamp((int)Math.Ceiling(count / (double)perColumn), 1, 3);
        perColumn = Math.Max(1, (int)Math.Ceiling(count / (double)columns));

        var labelW = new double[columns];
        var valueW = new double[columns];
        for (int i = 0; i < count; i++)
        {
            int c = i / perColumn;
            labelW[c] = Math.Max(labelW[c], rows[i].Label);
            valueW[c] = Math.Max(valueW[c], rows[i].Value);
        }
        double bodyW = (columns - 1) * ColumnGap;
        for (int c = 0; c < columns; c++) bodyW += lead + labelW[c] + ValueGap + valueW[c];

        snap ??= v => v;
        double w = snap(Math.Max(Math.Max(bodyW, headLead + headWidth) + padX * 2, compact ? 96 : 118));
        double h = snap(count > 0 ? top + Math.Min(count, perColumn) * rowH + foot : headBand);

        var places = new CardRowPlace[count];
        double x = padX;
        for (int c = 0; c < columns; c++)
        {
            // A single column's values line up with the card's right edge,
            // however wide the heading made it.
            double right = columns == 1 ? w - padX : x + lead + labelW[c] + ValueGap + valueW[c];
            for (int i = c * perColumn; i < Math.Min(count, (c + 1) * perColumn); i++)
                places[i] = new CardRowPlace(x, x + lead, right, top + (i - c * perColumn) * rowH);
            x += lead + labelW[c] + ValueGap + valueW[c] + ColumnGap;
        }
        return new CardLayout(w, h, padX, padX + headLead, headBand / 2, count > 0 ? headBand : null, rowH, places);
    }

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
