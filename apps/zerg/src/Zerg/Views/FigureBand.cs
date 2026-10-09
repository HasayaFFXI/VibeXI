using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Zerg.Core.Charts;

namespace Zerg.Views;

/// <summary>
/// The band of figures under the command bar: its children in equal cells,
/// five in a row, with a short upright rule between two neighbours. (And,
/// with <see cref="Six"/>, the Compare section's band of six.)
///
/// <para>A band too narrow for five in a row stands them in threes, then in
/// twos (<see cref="BandMarks.Columns"/>), as rows of the same height with a
/// rule between two rows. Each cell is told whether it is wide enough for
/// the mark beside its figure (<see cref="RoomyProperty"/>, which the cell's
/// contents inherit): under 240 units the mark is left out.</para>
///
/// <para>The rules are painted by the panel, once per layout: they are not
/// elements, and nothing about them changes with the figures.</para>
/// </summary>
public sealed class FigureBand : Panel
{
    /// <summary>How far short of a row's top and bottom the rule between two cells stops.</summary>
    const double Inset = 10;

    /// <summary>A row's height: the band's, while all five figures are in one.</summary>
    public static readonly DependencyProperty RowHeightProperty = DependencyProperty.Register(nameof(RowHeight), typeof(double),
        typeof(FigureBand), new FrameworkPropertyMetadata(64.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>What the rules are drawn in.</summary>
    public static readonly DependencyProperty RuleProperty = DependencyProperty.Register(nameof(Rule), typeof(Brush),
        typeof(FigureBand), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>On a cell and everything in it: the cell has room for a
    /// mark beside its figure. The band sets it.</summary>
    public static readonly DependencyProperty RoomyProperty = DependencyProperty.RegisterAttached("Roomy", typeof(bool),
        typeof(FigureBand), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>The band is Compare's, of six figures for two runs each:
    /// six in a row, then threes, then twos (<see cref="BandMarks.PairColumns"/>).</summary>
    public static readonly DependencyProperty SixProperty = DependencyProperty.Register(nameof(Six), typeof(bool),
        typeof(FigureBand), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static bool GetRoomy(DependencyObject d) => (bool)d.GetValue(RoomyProperty);
    static void SetRoomy(DependencyObject d, bool value) => d.SetValue(RoomyProperty, value);

    public double RowHeight { get => (double)GetValue(RowHeightProperty); set => SetValue(RowHeightProperty, value); }
    public Brush? Rule { get => (Brush?)GetValue(RuleProperty); set => SetValue(RuleProperty, value); }
    public bool Six { get => (bool)GetValue(SixProperty); set => SetValue(SixProperty, value); }

    /// <summary>How many cells stand in a row of a band this wide.</summary>
    int Across(double width) => Six ? BandMarks.PairColumns(width) : BandMarks.Columns(width);

    /// <summary>How the cells were last laid out, for the rules.</summary>
    (int Columns, int Cells, double Cell) drawn;

    /// <summary>The children that take a cell, in order.</summary>
    List<UIElement> Cells()
    {
        var cells = new List<UIElement>(InternalChildren.Count);
        foreach (UIElement child in InternalChildren)
            if (child.Visibility != Visibility.Collapsed) cells.Add(child);
        return cells;
    }

    protected override Size MeasureOverride(Size available)
    {
        var cells = Cells();
        // Asked how wide it would like to be: five across, each as wide as it likes.
        bool free = double.IsInfinity(available.Width);
        int columns = free ? Math.Max(1, cells.Count) : Across(available.Width);
        double cell = free ? double.PositiveInfinity : available.Width / columns;

        double widest = 0;
        foreach (var child in cells)
        {
            // Before it is measured: what is in the cell may leave its mark out.
            SetRoomy(child, free || BandMarks.RoomForMark(cell));
            child.Measure(new Size(cell, RowHeight));
            widest = Math.Max(widest, child.DesiredSize.Width);
        }
        int rows = (cells.Count + columns - 1) / columns;
        return new Size(free ? widest * columns : available.Width, rows * RowHeight);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var cells = Cells();
        int columns = Across(final.Width);
        double cell = final.Width / columns;
        for (int i = 0; i < cells.Count; i++)
            cells[i].Arrange(new Rect(i % columns * cell, i / columns * RowHeight, cell, RowHeight));

        var now = (columns, cells.Count, cell);
        if (now != drawn)
        {
            drawn = now;
            InvalidateVisual();
        }
        return final;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (Rule is not { } rule || drawn.Columns == 0) return;

        // One unit, on whole pixels, as a control's edge is.
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double Snap(double x) => Math.Round(x * scale) / scale;
        double line = Math.Max(1, Math.Round(scale)) / scale;

        var (columns, count, cell) = drawn;
        for (int i = 0; i < count; i++)
        {
            int column = i % columns, row = i / columns;
            // To the left of every cell but the first of its row.
            if (column > 0)
                dc.DrawRectangle(rule, null, new Rect(Snap(column * cell), Snap(row * RowHeight + Inset), line,
                                                      Snap(RowHeight - 2 * Inset)));
            // Above every row but the first, the whole width.
            if (column == 0 && row > 0)
                dc.DrawRectangle(rule, null, new Rect(0, Snap(row * RowHeight) - line, RenderSize.Width, line));
        }
    }
}
