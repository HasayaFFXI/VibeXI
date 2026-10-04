using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Zerg.Core;
using Zerg.Core.Charts;

namespace Zerg.Charts;

/// <summary>
/// Horizontal bars: one per row, in the row's own colour, the label on the
/// left and the value beside the bar's end. The colour is who the row is, not
/// where it ranks, so filtering never repaints the rows that stay.
///
/// <para>The caller sets the height: <see cref="BarsLayout.HeightFor"/>.</para>
///
/// <para>When the rows change, each bar grows or shrinks from where it was
/// over a quarter of a second, so a poll's new totals arrive as movement
/// instead of a jump. Those are a handful of rectangles; the cumulative
/// chart's lines are never redrawn per frame.</para>
/// </summary>
public sealed class BarChart : Chart
{
    public static readonly DependencyProperty RowsProperty = DependencyProperty.Register(nameof(Rows),
        typeof(IReadOnlyList<BarRow>), typeof(BarChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((BarChart)d).RowsChanged()));

    public static readonly DependencyProperty LabelWidthProperty = DependencyProperty.Register(nameof(LabelWidth), typeof(double),
        typeof(BarChart), new FrameworkPropertyMetadata(BarsLayout.LabelWidth, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>How far the bars have moved from their old widths to their new ones, 0 to 1.</summary>
    static readonly DependencyProperty ProgressProperty = DependencyProperty.Register("Progress", typeof(double),
        typeof(BarChart), new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    static readonly Duration Move = TimeSpan.FromMilliseconds(250);

    readonly DrawingVisual bars = new();
    Dictionary<string, double> from = [], shown = [];
    BarsLayout? layout;

    public BarChart() => Layers(bars);

    public IReadOnlyList<BarRow>? Rows
    {
        get => (IReadOnlyList<BarRow>?)GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    /// <summary>How wide the label column is; a longer label is cut with an ellipsis.</summary>
    public double LabelWidth { get => (double)GetValue(LabelWidthProperty); set => SetValue(LabelWidthProperty, value); }

    void RowsChanged()
    {
        from = shown;
        // The first rows simply appear; so does everything when nobody can
        // see it, or when Windows has animations switched off.
        if (from.Count > 0 && IsVisible && SystemParameters.ClientAreaAnimation)
        {
            var move = new DoubleAnimation(0, 1, Move) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            Timeline.SetDesiredFrameRate(move, 60);
            BeginAnimation(ProgressProperty, move);
        }
        else BeginAnimation(ProgressProperty, null);
    }

    protected override void Paint(double w, double h)
    {
        var rows = Rows;
        layout = null;
        using var dc = bars.RenderOpen();
        var now = new Dictionary<string, double>();
        try
        {
            if (rows == null || w < 1 || h < 1) return;
            var l = layout = BarsLayout.Compute(w, h, rows, s => Text(s, 12, false, Ink2).Width, LabelWidth);
            if (l.Empty)
            {
                DrawEmpty(dc, w, h, "No data");
                return;
            }

            double progress = (double)GetValue(ProgressProperty);
            for (int i = 0; i < l.Rows.Count; i++)
            {
                var b = l.Rows[i];
                var row = rows[i];
                if (double.IsNaN(b.BarW)) continue;

                double bw = b.BarW;
                if (progress < 1)
                {
                    // A row that wasn't there grows from the smallest bar.
                    double start = from.TryGetValue(row.Key ?? row.Label, out var f) ? f : 2;
                    bw = start + (bw - start) * progress;
                }
                now[row.Key ?? row.Label] = bw;

                var label = Text(b.Label, 12, false, Ink2);
                Draw(dc, label, l.LabelRight - label.Width, b.Cy - label.Height / 2);

                double top = SnapEdge(b.BarY), bottom = SnapEdge(b.BarY + b.BarH);
                dc.DrawGeometry(Solid(row.Color), null, RoundedEnd(b.BarX, top, bw, bottom - top));

                var value = Text(b.ValueText, 12, true, Ink);
                Draw(dc, value, b.BarX + bw + 8, b.Cy - value.Height / 2);
            }
        }
        finally
        {
            // What is on screen now: where the next change starts from. Kept
            // through an animation's frames, which all start from `from`.
            shown = now;
        }
    }

    /// <summary>A bar: square where it starts, rounded at its data end.</summary>
    static StreamGeometry RoundedEnd(double x, double y, double w, double h)
    {
        double r = Math.Max(0, Math.Min(BarsLayout.Radius, Math.Min(w, h / 2)));
        var size = new Size(r, r);
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(x, y), true, true);
            ctx.LineTo(new Point(x + w - r, y), false, false);
            ctx.ArcTo(new Point(x + w, y + r), size, 0, false, SweepDirection.Clockwise, false, false);
            ctx.LineTo(new Point(x + w, y + h - r), false, false);
            ctx.ArcTo(new Point(x + w - r, y + h), size, 0, false, SweepDirection.Clockwise, false, false);
            ctx.LineTo(new Point(x, y + h), false, false);
        }
        g.Freeze();
        return g;
    }

    protected override Readout? ReadoutAt(Point p)
    {
        if (layout == null || Rows is not { } rows) return null;
        // The whole band is the row's, so there is no dead space between bars.
        int k = layout.RowAt(p.Y);
        if (k < 0 || k >= rows.Count) return null;
        var row = rows[k];
        var lines = row.Hover ?? [new CardRow("Damage", Format.Int(row.Value))];
        return new Readout(k, row.Label, Solid(row.Color),
                           lines.Select(x => new ReadoutRow(x.Label, x.Value)).ToList(), p.X, p.Y);
    }
}
