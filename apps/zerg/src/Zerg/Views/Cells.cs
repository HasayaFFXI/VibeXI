using System.Windows;
using System.Windows.Controls;
using Zerg.Core.Layout;

namespace Zerg.Views;

/// <summary>
/// One row of a table: each child is a cell, laid out in the columns that
/// <see cref="Widths"/> names. A heading row and every data row given the same
/// widths line up, with none of the measuring across rows that a shared-size
/// grid does, so a few hundred rows stay cheap.
///
/// <para>How wide each column comes out is worked out in
/// <c>Zerg.Core/Layout/Columns</c>, where it is tested: a number is that
/// many units, <c>*</c> or <c>2*</c> is a share of what is left, and
/// <c>278*150</c> a share that is never under 150.</para>
///
/// <para>A collapsed cell takes its column with it, which is how a table
/// drops a whole column: collapse that cell in the heading and in every row.
/// A column with no cell at all (a row with fewer children than columns)
/// still takes its room: the blank column at the end of a table needs no
/// element to hold it open.</para>
/// </summary>
public sealed class Cells : Panel
{
    /// <summary>Columns, comma-separated: a number is that many units,
    /// <c>*</c> or <c>2*</c> is a share of what is left, and a second
    /// number after the star is the least that share may be.</summary>
    public static readonly DependencyProperty WidthsProperty = DependencyProperty.Register(nameof(Widths), typeof(string),
        typeof(Cells), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure,
            (d, e) => ((Cells)d).columns = Columns.Parse((string)e.NewValue)));

    Column[] columns = [];

    public string Widths { get => (string)GetValue(WidthsProperty); set => SetValue(WidthsProperty, value); }

    /// <summary>The width of each column, in a row this wide.</summary>
    double[] Resolve(double width)
    {
        var w = new double[columns.Length];
        Span<bool> present = columns.Length <= 64 ? stackalloc bool[columns.Length] : new bool[columns.Length];
        for (int i = 0; i < present.Length; i++)
            present[i] = i >= InternalChildren.Count || InternalChildren[i].Visibility != Visibility.Collapsed;
        Columns.Resolve(columns, present, width, w);
        return w;
    }

    protected override Size MeasureOverride(Size available)
    {
        var w = Resolve(available.Width);
        double height = 0, width = 0;
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            child.Measure(new Size(i < w.Length ? w[i] : 0, available.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }
        foreach (var column in w) width += column;
        return new Size(double.IsInfinity(available.Width) ? width : available.Width, height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var w = Resolve(final.Width);
        double x = 0;
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            double wide = i < w.Length ? w[i] : 0;
            InternalChildren[i].Arrange(new Rect(x, 0, wide, final.Height));
            x += wide;
        }
        return final;
    }
}
