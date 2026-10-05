using System.Windows;
using System.Windows.Media;
using Zerg.Core.Charts;

namespace Zerg.Charts;

/// <summary>
/// How one action's hits, or one heal's casts, are spread in two runs: in
/// every bin a column for A beside one for B, each run in its own colour,
/// with each run's mean marked by a rule.
/// </summary>
public sealed class PairedHistogramChart : Chart
{
    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(nameof(Model),
        typeof(PairedHistogramModel), typeof(PairedHistogramChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillAProperty = DependencyProperty.Register(nameof(FillA), typeof(Brush),
        typeof(PairedHistogramChart), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillBProperty = DependencyProperty.Register(nameof(FillB), typeof(Brush),
        typeof(PairedHistogramChart), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    readonly DrawingVisual columns = new();
    PairedHistogramLayout? layout;

    public PairedHistogramChart()
    {
        Layers(columns);
        // The runs' colours are the theme's, as on everything else of a run.
        SetResourceReference(FillAProperty, "RunABrush");
        SetResourceReference(FillBProperty, "RunBBrush");
    }

    public PairedHistogramModel? Model
    {
        get => (PairedHistogramModel?)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    /// <summary>Run A's columns.</summary>
    public Brush FillA { get => (Brush)GetValue(FillAProperty); set => SetValue(FillAProperty, value); }
    /// <summary>Run B's columns.</summary>
    public Brush FillB { get => (Brush)GetValue(FillBProperty); set => SetValue(FillBProperty, value); }

    Brush FillOf(int run) => run == 0 ? FillA : FillB;

    protected override void Paint(double w, double h)
    {
        layout = null;
        using var dc = columns.RenderOpen();
        if (w < 1 || h < 1) return;
        var m = Model;
        var l = layout = PairedHistogramLayout.Compute(w, h, m?.Bins, m?.AvgA, m?.AvgB, m?.CountA ?? 0, m?.CountB ?? 0,
                                                       s => Text(s, 11, true, Ink2).Width, m?.Unit ?? "hit");
        if (l.Empty)
        {
            DrawEmpty(dc, w, h, "No " + (m?.Unit ?? "hit") + "s in either run");
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

        var foot = SnapEdge(plot.Bottom);
        foreach (var c in l.Columns)
        {
            double left = SnapEdge(c.X), right = Math.Max(left + 1 / Scale, SnapEdge(c.X + c.W));
            dc.DrawGeometry(FillOf(c.Run), null, RoundedTop(left, c.Y, right - left, foot - c.Y, c.Radius));
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

        // The rules are in the text's ink, not the runs': a rule in a run's
        // colour is lost against that run's own columns. The label says whose
        // it is, and the swatch ahead of it repeats that.
        foreach (var mean in l.Means)
        {
            var rule = mean.Rule;
            var x = SnapLine(rule.X);
            dc.DrawLine(Hairline(Ink2), new Point(x, rule.Top), new Point(x, rule.Bottom));
            var text = Text(rule.Label, 11, true, Ink2);
            const double swatch = 8;
            double left = rule.Align == LabelAlign.Right
                ? rule.LabelX - text.Width - PairedHistogramLayout.SwatchRoom
                : rule.LabelX;
            double top = rule.Top - text.Height + 2;
            dc.DrawRoundedRectangle(FillOf(mean.Run), null,
                new Rect(SnapEdge(left), SnapEdge(top + (text.Height - swatch) / 2), swatch, swatch), 2, 2);
            Draw(dc, text, left + PairedHistogramLayout.SwatchRoom, top);
        }
    }

    protected override Readout? ReadoutAt(Point p)
    {
        if (layout?.Hover(p.X, p.Y) is not { } h) return null;
        return new Readout(h.Bin, h.Head, null,
                           h.Rows.Select(r => new ReadoutRow(r.Label, r.Value, r.Series >= 0 ? FillOf(r.Series) : null)).ToList(),
                           p.X, p.Y);
    }
}
