using System.Windows;
using System.Windows.Media;
using Zerg.Core.Charts;

namespace Zerg.Charts;

/// <summary>
/// How one character's hits are spread: a column per bin, all in one colour,
/// with the mean marked by a rule.
/// </summary>
public sealed class HistogramChart : Chart
{
    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(nameof(Model), typeof(HistogramModel),
        typeof(HistogramChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(Color),
        typeof(HistogramChart), new FrameworkPropertyMetadata(Colors.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    readonly DrawingVisual columns = new();
    HistogramLayout? layout;

    public HistogramChart() => Layers(columns);

    public HistogramModel? Model { get => (HistogramModel?)GetValue(ModelProperty); set => SetValue(ModelProperty, value); }

    /// <summary>The columns' colour: the character's.</summary>
    public Color Fill { get => (Color)GetValue(FillProperty); set => SetValue(FillProperty, value); }

    protected override void Paint(double w, double h)
    {
        layout = null;
        using var dc = columns.RenderOpen();
        if (w < 1 || h < 1) return;
        var m = Model;
        var l = layout = HistogramLayout.Compute(w, h, m?.Bins, m?.Avg ?? 0, m?.Count ?? 0);
        if (l.Empty)
        {
            DrawEmpty(dc, w, h, "No hits recorded");
            return;
        }
        var plot = l.Plot;

        var grid = Hairline(GridInk);
        foreach (var t in l.YTicks)
        {
            var y = SnapLine(t.Pos);
            dc.DrawLine(grid, new Point(plot.X, y), new Point(plot.Right, y));
            var text = Text(t.Label, 11, false, Muted);
            Draw(dc, text, plot.X - HistogramLayout.YLabelGap - text.Width, y - text.Height / 2);
        }

        var fill = Solid(Fill);
        var foot = SnapEdge(plot.Bottom);
        foreach (var c in l.Columns)
        {
            double left = SnapEdge(c.X), right = SnapEdge(c.X + c.W);
            dc.DrawGeometry(fill, null, RoundedTop(left, c.Y, right - left, foot - c.Y, c.Radius));
        }

        var baseline = SnapLine(l.BaselineY);
        dc.DrawLine(Hairline(AxisInk), new Point(plot.X, baseline), new Point(plot.Right, baseline));

        foreach (var x in l.XLabels)
        {
            var text = Text(x.Text, 11, false, Muted);
            double left = x.Align switch
            {
                LabelAlign.Left => x.X,
                LabelAlign.Right => x.X - text.Width,
                _ => x.X - text.Width / 2,
            };
            Draw(dc, text, left, plot.Bottom + HistogramLayout.XLabelGap - 2);
        }

        if (l.Mean is { } mean)
        {
            var x = SnapLine(mean.X);
            dc.DrawLine(Hairline(Ink2), new Point(x, mean.Top), new Point(x, mean.Bottom));
            var text = Text(mean.Label, 11, true, Ink2);
            Draw(dc, text, mean.Align == LabelAlign.Right ? mean.LabelX - text.Width : mean.LabelX,
                 mean.Top - text.Height + 2);
        }
    }

    protected override Readout? ReadoutAt(Point p)
    {
        if (layout?.Hover(p.X, p.Y) is not { } h) return null;
        return new Readout(h.Bin, h.Head, null, h.Rows.Select(r => new ReadoutRow(r.Label, r.Value)).ToList(), p.X, p.Y);
    }
}
