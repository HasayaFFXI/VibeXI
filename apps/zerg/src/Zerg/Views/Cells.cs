using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Zerg.Views;

/// <summary>
/// One row of a table: each child is a cell, laid out in the columns that
/// <see cref="Widths"/> names. A heading row and every data row given the same
/// widths line up, with none of the measuring across rows that a shared-size
/// grid does, so a few hundred rows stay cheap.
///
/// <para>A collapsed cell takes its column with it, which is how a table
/// drops a whole column: collapse that cell in the heading and in every row.</para>
/// </summary>
public sealed class Cells : Panel
{
    /// <summary>Columns, comma-separated: a number is that many pixels,
    /// <c>*</c> or <c>2*</c> is a share of what is left.</summary>
    public static readonly DependencyProperty WidthsProperty = DependencyProperty.Register(nameof(Widths), typeof(string),
        typeof(Cells), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure,
            (d, e) => ((Cells)d).columns = Parse((string)e.NewValue)));

    /// <summary>What a share is worth when the row is given no width to share out.</summary>
    const double LooseShare = 84;

    (double Size, bool Share)[] columns = [];

    public string Widths { get => (string)GetValue(WidthsProperty); set => SetValue(WidthsProperty, value); }

    static (double, bool)[] Parse(string? widths) =>
        (widths ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(w =>
            w.EndsWith('*')
                ? (w.Length == 1 ? 1 : double.Parse(w[..^1], CultureInfo.InvariantCulture), true)
                : (double.Parse(w, CultureInfo.InvariantCulture), false)).ToArray();

    double[] Resolve(double width)
    {
        var w = new double[InternalChildren.Count];
        double taken = 0, shares = 0;
        for (int i = 0; i < w.Length && i < columns.Length; i++)
        {
            if (InternalChildren[i].Visibility == Visibility.Collapsed) continue;
            if (columns[i].Share) shares += columns[i].Size;
            else taken += w[i] = columns[i].Size;
        }
        double one = double.IsInfinity(width) ? LooseShare : shares > 0 ? Math.Max(0, width - taken) / shares : 0;
        for (int i = 0; i < w.Length && i < columns.Length; i++)
            if (columns[i].Share && InternalChildren[i].Visibility != Visibility.Collapsed) w[i] = one * columns[i].Size;
        return w;
    }

    protected override Size MeasureOverride(Size available)
    {
        var w = Resolve(available.Width);
        double height = 0, width = 0;
        for (int i = 0; i < w.Length; i++)
        {
            var child = InternalChildren[i];
            child.Measure(new Size(w[i], available.Height));
            height = Math.Max(height, child.DesiredSize.Height);
            width += w[i];
        }
        return new Size(double.IsInfinity(available.Width) ? width : available.Width, height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var w = Resolve(final.Width);
        double x = 0;
        for (int i = 0; i < w.Length; i++)
        {
            InternalChildren[i].Arrange(new Rect(x, 0, w[i], final.Height));
            x += w[i];
        }
        return final;
    }
}
