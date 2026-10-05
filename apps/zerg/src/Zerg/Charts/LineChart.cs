using System.Windows;
using System.Windows.Media;
using Zerg.Core;
using Zerg.Core.Charts;

namespace Zerg.Charts;

/// <summary>
/// The cumulative chart: one line per series on a shared time grid, with end
/// markers, end labels and a crosshair that reads every line at one instant.
///
/// <para>Three layers. The lines are the expensive part, so they are kept as
/// a bitmap between redraws and the crosshair moves over that. The furniture
/// (grid, axes, labels, markers) is drawn over them. Both are redrawn when the
/// data changes and when the live edge steps; the crosshair, on a layer of its
/// own, only when the pointer reaches another grid time.</para>
/// </summary>
public sealed class LineChart : Chart
{
    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(nameof(Model), typeof(LineModel),
        typeof(LineChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender,
            // New data states its own edge; one stepped to earlier belongs to the old data.
            (d, _) => ((LineChart)d).edge = double.NaN));

    public static readonly DependencyProperty EndLabelsProperty = DependencyProperty.Register(nameof(EndLabels), typeof(bool),
        typeof(LineChart), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty EdgeProperty = DependencyProperty.Register(nameof(Edge), typeof(double),
        typeof(LineChart), new FrameworkPropertyMetadata(double.NaN, (d, e) =>
        {
            // Not AffectsRender: that redraws a chart nobody can see (its
            // section is off screen, the window is in the tray), and at the
            // draw frequency that cost as much as drawing the one on screen.
            // A chart is drawn again when it comes back into view.
            var chart = (LineChart)d;
            chart.edge = (double)e.NewValue;
            if (chart.IsVisible) chart.InvalidateVisual();
        }));

    readonly DrawingVisual lines = new() { CacheMode = new BitmapCache { SnapsToDevicePixels = true } };
    readonly DrawingVisual furniture = new(), crosshair = new();
    double edge = double.NaN;
    LineLayout? layout;
    LineHover? hover;

    public LineChart() => Layers(lines, furniture, crosshair);

    public LineModel? Model { get => (LineModel?)GetValue(ModelProperty); set => SetValue(ModelProperty, value); }

    /// <summary>Print every line's name and total at its end, in place of a
    /// legend the window has no room for.</summary>
    public bool EndLabels { get => (bool)GetValue(EndLabelsProperty); set => SetValue(EndLabelsProperty, value); }

    /// <summary>
    /// The live edge: the session clock, in milliseconds. Setting it moves a
    /// live model's last grid time there and redraws, so the lines run flat
    /// through a quiet stretch instead of standing still and then lurching.
    /// Step it at the draw frequency (<see cref="DrawRate"/>), never per
    /// frame. It is ignored for a model that is not live, and forgotten when
    /// the model changes.
    /// </summary>
    public double Edge { get => (double)GetValue(EdgeProperty); set => SetValue(EdgeProperty, value); }

    Brush InkOf(LineSeries s) => s.Group ? Muted : Solid(s.Color);

    protected override void Paint(double w, double h)
    {
        var m = Model;
        layout = null;
        if (m == null || w < 1 || h < 1)
        {
            Clear(lines);
            Clear(furniture);
            return;
        }

        // The model is the caller's and may be drawn by another chart too, so
        // the edge goes into a copy of its grid.
        var times = m.Times;
        if (m.Live && times.Length > 0 && !double.IsNaN(edge) && edge != times[^1]
            && (times.Length < 2 || edge >= times[^2]))
        {
            times = (double[])times.Clone();
            times[^1] = edge;
        }

        var l = layout = LineLayout.Compute(w, h, times, m.Series, Compact, EndLabels, s => Text(s, 11, true, Ink2).Width);
        if (l.Empty)
        {
            Clear(lines);
            using var e = furniture.RenderOpen();
            DrawEmpty(e, w, h, "No damage yet");
            return;
        }
        var plot = l.Plot;

        using (var dc = lines.RenderOpen())
        {
            foreach (var s in m.Series)
            {
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    bool pen = false;
                    int n = Math.Min(times.Length, s.Values.Length);
                    for (int j = 0; j < n; j++)
                    {
                        if (!double.IsFinite(s.Values[j])) { pen = false; continue; }
                        var p = new Point(l.Px(times[j]), l.Py(s.Values[j]));
                        if (pen) ctx.LineTo(p, true, true); else ctx.BeginFigure(p, false, false);
                        pen = true;
                    }
                }
                g.Freeze();
                dc.DrawGeometry(null, LinePen(InkOf(s), s.Group), g);
            }
        }

        using (var dc = furniture.RenderOpen())
        {
            var grid = Hairline(GridInk);
            foreach (var t in l.YTicks)
            {
                var y = SnapLine(t.Pos);
                dc.DrawLine(grid, new Point(plot.X, y), new Point(plot.Right, y));
                var text = Text(t.Label, 11, false, Muted);
                Draw(dc, text, plot.X - LineLayout.LabelGap - text.Width, y - text.Height / 2);
            }
            foreach (var t in l.XTicks)
            {
                var text = Text(t.Label, 11, false, Muted);
                Draw(dc, text, t.Pos - text.Width / 2, plot.Bottom + LineLayout.LabelGap - 2);
            }
            var floor = SnapLine(l.AxisY);
            dc.DrawLine(Hairline(AxisInk), new Point(plot.X, floor), new Point(plot.Right, floor));

            foreach (var mark in l.Markers)
                dc.DrawEllipse(InkOf(m.Series[mark.Series]), Ring, new Point(mark.X, mark.Y),
                               LineLayout.MarkerRadius, LineLayout.MarkerRadius);

            foreach (var label in l.Labels)
            {
                var text = Text(label.Text, 11, true, Ink2);
                var x = label.X;
                if (label.Swatch)
                {
                    // A swatch, because a label nudged clear of its neighbours
                    // is no longer level with its own dot.
                    dc.DrawRectangle(InkOf(m.Series[label.Series]), null,
                        new Rect(SnapEdge(x), SnapEdge(Js.Round(label.Y) - LineLayout.SwatchHeight / 2),
                                 LineLayout.SwatchWidth, LineLayout.SwatchHeight));
                    x += LineLayout.SwatchToText;
                }
                Draw(dc, text, x, label.Y - text.Height / 2);
            }
        }
    }

    protected override Readout? ReadoutAt(Point p)
    {
        hover = layout?.Hover(p.X, p.Y);
        if (hover == null || Model is not { } m) return null;
        return new Readout(hover.Index, hover.Head, null,
            hover.Rows.Select(r => new ReadoutRow(r.Label, r.Value, InkOf(m.Series[r.Series]))).ToList(),
            hover.AnchorX, p.Y);
    }

    protected override void OnHover(Readout? readout)
    {
        using var dc = crosshair.RenderOpen();
        if (readout == null || hover == null || layout == null || Model is not { } m) return;
        var plot = layout.Plot;
        var x = SnapLine(hover.X);
        dc.DrawLine(Hairline(AxisInk), new Point(x, plot.Y), new Point(x, plot.Bottom));
        foreach (var d in hover.Dots)
            dc.DrawEllipse(InkOf(m.Series[d.Series]), Ring, new Point(x, d.Y), LineLayout.MarkerRadius, LineLayout.MarkerRadius);
    }
}
