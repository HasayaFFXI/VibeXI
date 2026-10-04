namespace Zerg.Core.Charts;

/// <summary>One bar of a horizontal bar chart, as the layout needs it.</summary>
public interface IBarRow
{
    string Label { get; }
    double Value { get; }
}

/// <summary>Where one bar goes. The band (<see cref="Y0"/> to <see cref="Y1"/>)
/// is the strip of the chart the pointer picks this row by.</summary>
public sealed record BarGeometry(string Label, double Cy, double BarX, double BarY, double BarW, double BarH,
                                 string ValueText, double ValueX, double Y0, double Y1);

/// <summary>
/// Where everything on a horizontal bar chart goes: one bar per row, its label
/// in a column on the left and its value beside the bar's end.
/// </summary>
public sealed class BarsLayout
{
    /// <summary>How round a bar's data end is.</summary>
    public const double Radius = 4;
    /// <summary>The widest a label may run, and the room kept for the value.</summary>
    public const double LabelWidth = 116, ValueWidth = 70;
    /// <summary>A bar is never thicker than this; the rest of its band is air.</summary>
    public const double MaxThickness = 24;

    public bool Empty { get; private set; }
    /// <summary>The labels' right edge.</summary>
    public double LabelRight { get; private set; }
    public List<BarGeometry> Rows { get; } = [];

    /// <summary>The height a bar chart of <paramref name="rows"/> rows wants.</summary>
    public static double HeightFor(int rows) => Math.Max(120, rows * 34 + 16);

    /// <param name="textWidth">The width of a string in the labels' type.</param>
    public static BarsLayout Compute(double width, double height, IReadOnlyList<IBarRow> rows,
                                     Func<string, double> textWidth, double labelWidth = LabelWidth)
    {
        var l = new BarsLayout { LabelRight = labelWidth };
        if (rows.Count == 0)
        {
            l.Empty = true;
            return l;
        }

        var left = labelWidth + 10;
        var right = width - ValueWidth;
        var w = Math.Max(10, right - left);

        double max = 0;
        foreach (var r in rows) max = Math.Max(max, r.Value);
        if (max <= 0) max = 1;

        var band = height / rows.Count;
        var barH = Math.Min(MaxThickness, Math.Max(8, band - 12));

        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var cy = band * i + band / 2;
            var bw = Math.Max(2, r.Value / max * w);
            l.Rows.Add(new BarGeometry(
                TextFit.Clip(r.Label, labelWidth, textWidth), cy,
                left, cy - barH / 2, bw, barH,
                Format.Compact(r.Value), left + bw + 8,
                band * i, band * (i + 1)));
        }
        return l;
    }

    /// <summary>The row whose band holds <paramref name="my"/>, or -1.</summary>
    public int RowAt(double my)
    {
        for (int k = 0; k < Rows.Count; k++)
            if (my >= Rows[k].Y0 && my < Rows[k].Y1) return k;
        return -1;
    }
}
