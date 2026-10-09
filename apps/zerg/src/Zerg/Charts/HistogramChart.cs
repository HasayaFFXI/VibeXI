using System.Windows;
using System.Windows.Media;
using Zerg.Core;
using Zerg.Core.Charts;

namespace Zerg.Charts;

/// <summary>
/// How one character's hits are spread: a column per bin, all in one colour,
/// over a band for the middle half of them, with the mean marked by a rule
/// and the median by a tick on the baseline.
/// </summary>
public sealed class HistogramChart : Chart
{
    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(nameof(Model), typeof(HistogramModel),
        typeof(HistogramChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(Color),
        typeof(HistogramChart), new FrameworkPropertyMetadata(Colors.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BandInkProperty = RegisterInk(nameof(BandInk), typeof(HistogramChart));

    readonly DrawingVisual columns = new();
    HistogramLayout? layout;

    public HistogramChart()
    {
        Layers(columns);
        SetResourceReference(BandInkProperty, "HistogramBandBrush");
    }

    public HistogramModel? Model { get => (HistogramModel?)GetValue(ModelProperty); set => SetValue(ModelProperty, value); }

    /// <summary>The columns' colour: the character's.</summary>
    public Color Fill { get => (Color)GetValue(FillProperty); set => SetValue(FillProperty, value); }

    /// <summary>The band behind the columns: where the middle half of the values fell.</summary>
    public Brush BandInk { get => Get(BandInkProperty); set => SetValue(BandInkProperty, value); }

    protected override void Paint(double w, double h)
    {
        layout = null;
        using var dc = columns.RenderOpen();
        if (w < 1 || h < 1) return;
        var m = Model;
        var l = layout = HistogramLayout.Compute(w, h, m?.Bins, m?.Avg ?? 0, m?.Count ?? 0, m?.Q1, m?.Q3, m?.Median,
                                                 (s, bold) => Text(s, AxisSize, bold, bold ? Ink2 : Muted).Width);
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
            var text = Text(t.Label, AxisSize, false, Muted);
            Draw(dc, text, plot.X - HistogramLayout.YLabelGap - text.Width, y - text.Height / 2);
        }

        var foot = SnapEdge(plot.Bottom);
        if (l.Band is { } band)
        {
            double left = SnapEdge(band.X), top = SnapEdge(band.Y);
            dc.DrawRectangle(BandInk, null, new Rect(left, top, Math.Max(0, SnapEdge(band.Right) - left), Math.Max(0, foot - top)));
        }

        var fill = Solid(Fill);
        foreach (var c in l.Columns)
        {
            double left = SnapEdge(c.X), right = Math.Max(left + 1 / Scale, SnapEdge(c.X + c.W));
            dc.DrawGeometry(fill, null, RoundedTop(left, c.Y, right - left, foot - c.Y, c.Radius));
        }

        var baseline = SnapLine(l.BaselineY);
        dc.DrawLine(Hairline(AxisInk), new Point(plot.X, baseline), new Point(plot.Right, baseline));

        foreach (var x in l.XLabels)
        {
            var text = Text(x.Text, AxisSize, false, Muted);
            double left = x.Align switch
            {
                LabelAlign.Left => x.X,
                LabelAlign.Right => x.X - text.Width,
                _ => x.X - text.Width / 2,
            };
            Draw(dc, text, left, plot.Bottom + HistogramLayout.XLabelGap);
        }

        if (l.Mean is { } mean)
        {
            var x = SnapLine(mean.X);
            dc.DrawLine(Hairline(Ink2), new Point(x, mean.Top), new Point(x, mean.Bottom));
            var text = Text(mean.Label, AxisSize, true, Ink2);
            Draw(dc, text, mean.Align == LabelAlign.Right ? mean.LabelX - text.Width : mean.LabelX,
                 plot.Y - text.Height);
        }

        // The median: a short stroke across the baseline, a pixel and a
        // half wide and on whole pixels, in the strongest ink.
        if (l.Median is { } median)
        {
            double wide = Math.Max(1, Math.Round(1.5 * Scale)) / Scale;
            dc.DrawRectangle(Ink, null, new Rect(SnapEdge(median.X - wide / 2), SnapEdge(median.Top),
                                                 wide, SnapEdge(median.Bottom) - SnapEdge(median.Top)));
        }
    }

    protected override string Summary()
    {
        if (Model is not { Count: > 0, Bins.Count: > 0 } m) return Nothing("No hits recorded");
        string values = m.Count == 1 ? "1 value" : Format.Int(m.Count) + " values";
        string line = $"{values} from {Format.Int(m.Bins[0].Lo)} to {Format.Int(m.Bins[^1].Hi)}, average {Format.Int(m.Avg)}";
        return m.Median is { } median ? line + ", median " + Format.Int(median) : line;
    }

    protected override Readout? ReadoutAt(Point p)
    {
        if (layout?.Hover(p.X, p.Y) is not { } h) return null;
        return new Readout(h.Bin, h.Head, null, h.Rows.Select(r => new ReadoutRow(r.Label, r.Value)).ToList(), p.X, p.Y);
    }
}
