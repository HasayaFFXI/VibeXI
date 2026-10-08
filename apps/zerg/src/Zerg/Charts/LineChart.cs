using System.Windows;
using System.Windows.Media;
using Zerg.Core;
using Zerg.Core.Charts;

namespace Zerg.Charts;

/// <summary>
/// The cumulative chart: one line per series on a shared time grid, with end
/// markers, end labels and a crosshair that reads every line at one instant.
///
/// <para>Five layers, in two kinds. Two <b>stand</b> between counts: under the
/// lines, the value axis (gridlines, their labels, the baseline); over them,
/// what is at the lines' ends (the rule that says the session is counting,
/// the markers, the names and the lines back to their markers). They are
/// drawn when the data, the size, the inks or the DPI change, and not when
/// the live edge steps: none of them depends on the time at the edge. On a
/// floating panel their text has a dark outline (<see cref="Chart.Halo"/>),
/// which costs nothing between counts for that reason. Two
/// <b>move</b> with the edge, at the draw frequency: the lines themselves,
/// kept as a bitmap between redraws, and the time axis's labels. The
/// crosshair is a layer of its own, drawn when the pointer reaches another
/// grid time.</para>
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

    public static readonly DependencyProperty LiveInkProperty = RegisterInk(nameof(LiveInk), typeof(LineChart));
    public static readonly DependencyProperty LeaderInkProperty = RegisterInk(nameof(LeaderInk), typeof(LineChart));

    /// <summary>How strong the rule at a counting session's edge is drawn.</summary>
    const double LiveStrength = 0.55;

    readonly DrawingVisual back = new(), axis = new();
    readonly DrawingVisual lines = new() { CacheMode = new BitmapCache { SnapsToDevicePixels = true } };
    readonly DrawingVisual front = new(), crosshair = new();
    double edge = double.NaN;
    LineLayout? layout;
    LineHover? hover;

    // What the standing layers were last drawn for. While all of it holds, a
    // redraw is the edge stepping, and they are left alone.
    LineModel? drawnModel;
    double drawnW, drawnH;
    bool drawnCompact, drawnEndLabels;
    string? drawnEmpty;
    int drawnEpoch = -1;

    // The model's grid with the edge in its last place: one copy for as long
    // as the model lasts, written to on every step.
    double[]? stepped;
    LineModel? steppedOf;

    public LineChart()
    {
        Layers(back, axis, lines, front, crosshair);
        SetResourceReference(LiveInkProperty, "LiveBrush");
        SetResourceReference(LeaderInkProperty, "Line3Brush");
    }

    public LineModel? Model { get => (LineModel?)GetValue(ModelProperty); set => SetValue(ModelProperty, value); }

    /// <summary>Name every line at its end, in the margin right of the plot,
    /// in place of a legend; and draw the line that leads stronger than the
    /// pack.</summary>
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

    /// <summary>The rule at the right-hand edge while the session is counting.</summary>
    public Brush LiveInk { get => Get(LiveInkProperty); set => SetValue(LiveInkProperty, value); }
    /// <summary>The thin line from a marker to a name that was moved clear of its neighbours.</summary>
    public Brush LeaderInk { get => Get(LeaderInkProperty); set => SetValue(LeaderInkProperty, value); }

    Brush InkOf(LineSeries s) => s.Group ? Muted : Solid(s.Color);

    protected override void Paint(double w, double h)
    {
        var m = Model;
        if (m == null || w < 1 || h < 1)
        {
            layout = null;
            drawnModel = null;
            Clear(back);
            Clear(axis);
            Clear(lines);
            Clear(front);
            return;
        }

        var times = Grid(m);
        bool compact = Compact, endLabels = EndLabels;
        var empty = EmptyText;

        if (layout != null && ReferenceEquals(m, drawnModel) && w == drawnW && h == drawnH && compact == drawnCompact
            && endLabels == drawnEndLabels && empty == drawnEmpty && Epoch == drawnEpoch)
        {
            // The edge stepped, and nothing else: the time axis is worked
            // out again and the two layers that follow it are drawn.
            if (layout.Empty) return;
            layout.Retime(times);
        }
        else
        {
            // The model is the caller's and may be drawn by another chart too,
            // so the edge is in a copy of its grid (Grid), never in the model.
            layout = LineLayout.Compute(w, h, times, m.Series, compact, endLabels,
                                        (s, bold) => Text(s, LabelSize, bold, bold ? Ink : Ink2).Width);
            (drawnModel, drawnW, drawnH, drawnCompact, drawnEndLabels, drawnEmpty, drawnEpoch) =
                (m, w, h, compact, endLabels, empty, Epoch);
            if (layout.Empty)
            {
                Clear(axis);
                Clear(lines);
                Clear(front);
                using var e = back.RenderOpen();
                DrawEmpty(e, w, h, "No damage yet");
                return;
            }
            DrawBack(layout);
            DrawFront(layout, m);
        }
        DrawAxis(layout);
        DrawLines(layout, m, times);
    }

    /// <summary>The model's grid, with its last time at the edge when the
    /// model is live and the edge has been stepped to somewhere it can be.</summary>
    double[] Grid(LineModel m)
    {
        var times = m.Times;
        if (!m.Live || times.Length == 0 || double.IsNaN(edge) || edge == times[^1]
            || (times.Length >= 2 && edge < times[^2]))
            return times;
        if (stepped == null || !ReferenceEquals(steppedOf, m) || stepped.Length != times.Length)
        {
            stepped = (double[])times.Clone();
            steppedOf = m;
        }
        stepped[^1] = edge;
        return stepped;
    }

    /// <summary>Under the lines: the value axis. It stands between counts.</summary>
    void DrawBack(LineLayout l)
    {
        var plot = l.Plot;
        using var dc = back.RenderOpen();
        var grid = Hairline(GridInk);
        foreach (var t in l.YTicks)
        {
            var y = SnapLine(t.Pos);
            dc.DrawLine(grid, new Point(plot.X, y), new Point(plot.Right, y));
            var text = Text(t.Label, AxisSize, false, Muted);
            Label(dc, text, plot.X - LineLayout.YLabelGap - text.Width, y - text.Height / 2);
        }
        var floor = SnapLine(l.AxisY);
        dc.DrawLine(Hairline(AxisInk), new Point(plot.X, floor), new Point(plot.Right, floor));
    }

    /// <summary>The time axis: a tick and a label per round time. It moves
    /// with the edge, at the draw frequency, so its labels are drawn plain
    /// (Draw, not Label): on a floating panel they are the one piece of the
    /// chart's text without an outline, and the one piece drawn on a beat.</summary>
    void DrawAxis(LineLayout l)
    {
        var plot = l.Plot;
        using var dc = axis.RenderOpen();
        var pen = Hairline(AxisInk);
        var floor = SnapLine(l.AxisY);
        foreach (var t in l.XTicks)
        {
            var x = SnapLine(t.Pos);
            dc.DrawLine(pen, new Point(x, floor), new Point(x, floor + LineLayout.TickLength));
            // The first begins at the plot's edge instead of hanging out
            // under the value axis's labels.
            var text = Text(t.Label, AxisSize, false, Muted);
            Draw(dc, text, Math.Max(plot.X, t.Pos - text.Width / 2), plot.Bottom + LineLayout.XLabelGap);
        }
    }

    /// <summary>The lines. They move with the edge.</summary>
    void DrawLines(LineLayout l, LineModel m, double[] times)
    {
        using var dc = lines.RenderOpen();
        for (int i = 0; i < m.Series.Count; i++)
        {
            var s = m.Series[i];
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
            dc.DrawGeometry(null, LinePen(InkOf(s), s.Group, lead: i == l.Leader), g);
        }
    }

    /// <summary>Over the lines: what is at their ends. It stands between counts.</summary>
    void DrawFront(LineLayout l, LineModel m)
    {
        var plot = l.Plot;
        using var dc = front.RenderOpen();

        // The edge of a session that is counting.
        if (m.Running)
        {
            var x = SnapLine(plot.Right);
            dc.DrawLine(FaintHairline(LiveInk, LiveStrength), new Point(x, plot.Y), new Point(x, plot.Bottom));
        }

        foreach (var mark in l.Markers)
            dc.DrawEllipse(InkOf(m.Series[mark.Series]), Ring, new Point(mark.X, mark.Y),
                           LineLayout.MarkerRadius, LineLayout.MarkerRadius);

        var leader = Hairline(LeaderInk);
        foreach (var label in l.Labels)
        {
            if (label.Elbow is { } e)
            {
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(new Point(e.X0, e.Y0), false, false);
                    ctx.LineTo(new Point(e.X1, e.Y1), true, true);
                    ctx.LineTo(new Point(e.X2, e.Y1), true, true);
                }
                g.Freeze();
                dc.DrawGeometry(null, leader, g);
            }

            // The leader's name in the strongest ink; a total, where there
            // is one after the name, in the quietest.
            var text = Text(label.Text, LabelSize, label.Lead, label.Lead ? Ink : Ink2);
            Label(dc, text, label.X, label.Y - text.Height / 2);
            if (label.Total.Length > 0)
            {
                var total = Text(label.Total, LabelSize, false, Muted);
                Label(dc, total, label.TotalRight - total.Width, label.Y - total.Height / 2);
            }
        }
    }

    protected override string Summary()
    {
        if (Model is not { Times.Length: > 0, Series.Count: > 0 } m) return Nothing("No data");
        // The line that ends highest, and where it ends.
        LineSeries? top = null;
        double most = double.NegativeInfinity;
        foreach (var line in m.Series)
        {
            double last = double.NaN;
            for (int i = line.Values.Length - 1; i >= 0 && !double.IsFinite(last); i--) last = line.Values[i];
            if (double.IsFinite(last) && last > most) (top, most) = (line, last);
        }
        string lines = m.Series.Count == 1 ? "1 line" : m.Series.Count + " lines";
        string over = lines + " over " + Format.Clock(m.Times[^1]);
        return top is null ? over : $"{over}. Highest: {top.Name}, {Format.Int(most)}";
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
        dc.DrawLine(DashedHairline(Muted), new Point(x, plot.Y), new Point(x, plot.Bottom));
        foreach (var d in hover.Dots)
            dc.DrawEllipse(InkOf(m.Series[d.Series]), Ring, new Point(x, d.Y), LineLayout.MarkerRadius, LineLayout.MarkerRadius);
    }
}
