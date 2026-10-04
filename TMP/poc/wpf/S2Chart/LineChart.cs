using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace ChartSpike;

public enum Strategy
{
    /// <summary>Every frame rebuilds every series geometry, as chart.js does.</summary>
    Rebuild,
    /// <summary>Series geometry is built once per data change, in data units, and
    /// mapped to pixels by one shared transform; a frame only moves the transform,
    /// the flat live tails and the furniture.</summary>
    Retained,
}

/// <summary>
/// chart.js's cumulative line, on DrawingVisuals. Three layers:
///   series   the lines (the expensive part)
///   frame    grid, axes, labels, end markers, end labels: whatever moves with the edge
///   overlay  crosshair and hover card, redrawn on pointer move only
/// </summary>
public sealed class LineChart : FrameworkElement
{
    readonly DrawingVisual series = new(), frame = new(), overlay = new();
    readonly VisualCollection children;
    readonly Dictionary<(string, double, bool, Color), FormattedText> textCache = new();
    readonly Dictionary<(Color, bool), Pen> pens = new();
    readonly Dictionary<Color, Pen> hairlines = new();
    static readonly Dictionary<Color, SolidColorBrush> brushes = new();

    LineModel? model;
    Strategy strategy;
    public bool Compact, EndLabels;
    public bool Dark = true;

    /// <summary>Keep the series layer as a bitmap, so a crosshair moving over it
    /// composites the cached lines instead of re-tessellating them.</summary>
    public bool CacheSeries
    {
        set => series.CacheMode = value ? new BitmapCache { SnapsToDevicePixels = true } : null;
    }
    public readonly Stopwatch DrawTime = new();
    public int Draws;

    // Retained strategy: geometry in data units (seconds, damage) under one transform.
    readonly MatrixTransform toPixels = new();
    readonly List<LineGeometry> tails = new();
    double yTop = 1;
    Rect plot;
    double? hoverX;

    public LineChart()
    {
        children = new VisualCollection(this) { series, frame, overlay };
        SizeChanged += (_, _) => Redraw(full: true);
    }

    protected override int VisualChildrenCount => children.Count;
    protected override Visual GetVisualChild(int i) => children[i];

    public void SetModel(LineModel m, Strategy s)
    {
        model = m;
        strategy = s;
        Redraw(full: true);
    }

    /// <summary>The live edge has moved: the session clock, in ms since zero.</summary>
    public void SetEdge(double ms)
    {
        if (model is null || !model.Live) return;
        model.Times[^1] = ms;
        Redraw(full: strategy == Strategy.Rebuild);
        if (hoverX is { } x) DrawOverlay(x);
    }

    public void Hover(double? x)
    {
        hoverX = x;
        if (x is null) { using var _ = overlay.RenderOpen(); return; }
        DrawOverlay(x.Value);
    }

    // ---------------------------------------------------------------- theme

    Color Ink => Dark ? Color.FromRgb(0xE8, 0xE6, 0xDF) : Color.FromRgb(0x1B, 0x1B, 0x1F);
    Color Ink2 => Dark ? Color.FromRgb(0xA8, 0xAC, 0xB8) : Color.FromRgb(0x55, 0x58, 0x63);
    Color Muted => Dark ? Color.FromRgb(0x70, 0x74, 0x84) : Color.FromRgb(0x8A, 0x8D, 0x96);
    Color Grid => Dark ? Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x14, 0, 0, 0);
    Color Axis => Dark ? Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x38, 0, 0, 0);
    Color Surface => Dark ? Color.FromRgb(0x2B, 0x2B, 0x2B) : Color.FromRgb(0xFB, 0xFB, 0xFB);

    /// <summary>The 2px surface ring round every marker, so overlaps stay legible.</summary>
    Pen Ring
    {
        get
        {
            if (ring?.Brush is SolidColorBrush b && b.Color == Surface) return ring;
            ring = new Pen(Brush(Surface), 2);
            ring.Freeze();
            return ring;
        }
    }
    Pen? ring;

    Color InkOf(Series s) => s.Group ? Muted : s.Color;

    static SolidColorBrush Brush(Color c)
    {
        if (brushes.TryGetValue(c, out var b)) return b;
        b = new SolidColorBrush(c);
        b.Freeze();
        return brushes[c] = b;
    }

    Pen LinePen(Color c, bool dashed)
    {
        if (pens.TryGetValue((c, dashed), out var p)) return p;
        p = new Pen(Brush(c), 2) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (dashed) p.DashStyle = new DashStyle([2.5, 2], 0);   // 5px on, 4px off at 2px thickness
        p.Freeze();
        return pens[(c, dashed)] = p;
    }

    Pen Hairline(Color c)
    {
        if (hairlines.TryGetValue(c, out var p)) return p;
        p = new Pen(Brush(c), 1);
        p.Freeze();
        return hairlines[c] = p;
    }

    FormattedText Text(string s, double size, bool bold, Color c)
    {
        var key = (s, size, bold, c);
        if (textCache.TryGetValue(key, out var ft)) return ft;
        if (textCache.Count > 4000) textCache.Clear();
        ft = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            bold ? SemiBold : Regular, size, Brush(c), VisualTreeHelper.GetDpi(this).PixelsPerDip);
        return textCache[key] = ft;
    }

    static readonly Typeface Regular = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    static readonly Typeface SemiBold = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    // ----------------------------------------------------------------- draw

    string LabelText(Series s) => ClipText(s.Name, 104) + " " + Fmt.Compact(LastValue(s.Values));

    string ClipText(string text, double maxW)
    {
        if (Text(text, 11, true, Ink2).WidthIncludingTrailingWhitespace <= maxW) return text;
        var t = text;
        while (t.Length > 1 && Text(t + "…", 11, true, Ink2).WidthIncludingTrailingWhitespace > maxW) t = t[..^1];
        return t + "…";
    }

    void Redraw(bool full)
    {
        if (model is null || ActualWidth < 1) return;
        DrawTime.Start();
        var m = model;
        double w = ActualWidth, h = ActualHeight;
        var pad = Compact ? new Thickness(46, 10, 12, 24) : new Thickness(68, 18, 76, 34);
        if (EndLabels && m.Series.Count > 0)
        {
            double widest = m.Series.Max(s => Text(LabelText(s), 11, true, Ink2).WidthIncludingTrailingWhitespace);
            pad.Right = Math.Ceiling(widest) + 34;
        }
        plot = new Rect(pad.Left, pad.Top, Math.Max(10, w - pad.Left - pad.Right), Math.Max(10, h - pad.Top - pad.Bottom));
        var times = m.Times;
        double t0 = times[0], t1 = times[^1];
        if (t1 == t0) t1 = t0 + 1000;

        if (full)
        {
            double vmax = 0;
            foreach (var s in m.Series) foreach (var v in s.Values) if (v > vmax) vmax = v;
            if (vmax <= 0) vmax = 1;
            var yt = Fmt.NiceTicks(0, vmax, plot.Height < 200 ? 3 : 5);
            yTop = Math.Max(vmax, yt[^1]);
        }
        double Px(double t) => plot.X + (t - t0) / (t1 - t0) * plot.Width;
        double Py(double v) => plot.Y + plot.Height - v / yTop * plot.Height;

        // ---- series
        if (strategy == Strategy.Rebuild)
        {
            using var dc = series.RenderOpen();
            foreach (var s in m.Series)
            {
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    bool pen = false;
                    for (int j = 0; j < times.Length; j++)
                    {
                        if (!double.IsFinite(s.Values[j])) { pen = false; continue; }
                        var p = new Point(Px(times[j]), Py(s.Values[j]));
                        if (!pen) ctx.BeginFigure(p, false, false); else ctx.LineTo(p, true, true);
                        pen = true;
                    }
                }
                g.Freeze();
                dc.DrawGeometry(null, LinePen(InkOf(s), s.Group), g);
            }
        }
        else
        {
            // Data units: x in ms since zero, y in damage. Rebuilt only on a data change.
            if (full)
            {
                tails.Clear();
                using var dc = series.RenderOpen();
                foreach (var s in m.Series)
                {
                    var g = new StreamGeometry { Transform = toPixels };
                    using (var ctx = g.Open())
                    {
                        bool pen = false;
                        for (int j = 0; j < times.Length - 1; j++)   // the live sample is the tail
                        {
                            if (!double.IsFinite(s.Values[j])) { pen = false; continue; }
                            var p = new Point(times[j], s.Values[j]);
                            if (!pen) ctx.BeginFigure(p, false, false); else ctx.LineTo(p, true, true);
                            pen = true;
                        }
                    }
                    var tail = new LineGeometry { Transform = toPixels };
                    tails.Add(tail);
                    var pen2 = LinePen(InkOf(s), s.Group);
                    dc.DrawGeometry(null, pen2, g);
                    dc.DrawGeometry(null, pen2, tail);
                }
            }
            double sx = plot.Width / (t1 - t0), sy = -plot.Height / yTop;
            toPixels.Matrix = new Matrix(sx, 0, 0, sy, plot.X - t0 * sx, plot.Y + plot.Height);
            int li = times.Length - 2;
            for (int k = 0; k < m.Series.Count; k++)
            {
                double v = m.Series[k].Values[^1];
                tails[k].StartPoint = new Point(times[li], v);
                tails[k].EndPoint = new Point(times[^1], v);
            }
        }

        // ---- frame: grid, axes, labels, markers
        using (var dc = frame.RenderOpen())
        {
            var yTicks = Fmt.NiceTicks(0, yTop, plot.Height < 200 ? 3 : 5);
            foreach (var v in yTicks)
            {
                double yy = Math.Round(Py(v)) + 0.5;
                dc.DrawLine(Hairline(Grid), new Point(plot.X, yy), new Point(plot.Right, yy));
                var ft = Text(Fmt.Compact(v), 11, false, Muted);
                dc.DrawText(ft, new Point(plot.X - 10 - ft.Width, yy - ft.Height / 2));
            }
            foreach (var t in Fmt.TimeTicks(t0, t1, Math.Max(3, (int)(plot.Width / 90))))
            {
                double xx = Math.Round(Px(t)) + 0.5;
                var ft = Text(Fmt.Elapsed(t), 11, false, Muted);
                dc.DrawText(ft, new Point(xx - ft.Width / 2, plot.Bottom + 10));
            }
            double ay = Math.Round(plot.Bottom) + 0.5;
            dc.DrawLine(Hairline(Axis), new Point(plot.X, ay), new Point(plot.Right, ay));

            var ring = Ring;
            foreach (var s in m.Series)
            {
                int li = LastIndex(s.Values);
                if (li < 0) continue;
                dc.DrawEllipse(Brush(InkOf(s)), ring, new Point(Px(times[li]), Py(s.Values[li])), 4.5, 4.5);
            }

            if (EndLabels)
            {
                // Every leader printed: collisions nudged apart (13px down, then
                // settled back up from the floor), never dropped.
                const double gap = 13;
                var tags = m.Series.Select(s => (y: Py(LastValue(s.Values)), text: LabelText(s), color: InkOf(s)))
                    .OrderBy(t => t.y).ToArray();
                for (int i = 1; i < tags.Length; i++) tags[i].y = Math.Max(tags[i].y, tags[i - 1].y + gap);
                if (tags.Length > 0 && tags[^1].y > plot.Bottom)
                {
                    tags[^1].y = plot.Bottom;
                    for (int i = tags.Length - 2; i >= 0; i--) tags[i].y = Math.Min(tags[i].y, tags[i + 1].y - gap);
                }
                double tx = plot.Right + 11;
                foreach (var tag in tags)
                {
                    dc.DrawRectangle(Brush(tag.color), null, new Rect(tx, Math.Round(tag.y) - 1.5, 10, 3));
                    var ft = Text(tag.text, 11, true, Ink2);
                    dc.DrawText(ft, new Point(tx + 15, tag.y - ft.Height / 2));
                }
            }
            else if (m.Series.Count <= 4)
            {
                var labels = m.Series.Select(s => (y: Py(LastValue(s.Values)), text: Fmt.Compact(LastValue(s.Values)),
                    x: Px(times[Math.Max(0, LastIndex(s.Values))]))).OrderBy(l => l.y).ToArray();
                bool collide = false;
                for (int i = 1; i < labels.Length; i++) if (labels[i].y - labels[i - 1].y < 14) { collide = true; break; }
                if (!collide)
                    foreach (var l in labels)
                    {
                        var ft = Text(l.text, 11, true, Ink2);
                        dc.DrawText(ft, new Point(l.x + 11, l.y - ft.Height / 2));
                    }
            }
        }
        DrawTime.Stop();
        Draws++;
    }

    void DrawOverlay(double mx)
    {
        if (model is null) return;
        DrawTime.Start();
        var m = model;
        var times = m.Times;
        double t0 = times[0], t1 = times[^1];
        double Px(double t) => plot.X + (t - t0) / (t1 - t0) * plot.Width;
        double Py(double v) => plot.Y + plot.Height - v / yTop * plot.Height;

        double frac = (mx - plot.X) / plot.Width;
        int idx = Math.Clamp((int)Math.Round(frac * (times.Length - 1)), 0, times.Length - 1);
        double X = Math.Round(Px(times[idx])) + 0.5;

        using var dc = overlay.RenderOpen();
        dc.DrawLine(Hairline(Axis), new Point(X, plot.Y), new Point(X, plot.Bottom));
        var ring = Ring;
        foreach (var s in m.Series)
            if (double.IsFinite(s.Values[idx]))
                dc.DrawEllipse(Brush(InkOf(s)), ring, new Point(X, Py(s.Values[idx])), 4.5, 4.5);

        // The hover card: time, then every series at that instant, largest first.
        var rows = m.Series.Where(s => double.IsFinite(s.Values[idx])).OrderByDescending(s => s.Values[idx]).ToList();
        var head = Text(Fmt.Elapsed(times[idx]), 12, true, Ink);
        var names = rows.Select(s => Text(s.Name, 11, false, Ink2)).ToList();
        var vals = rows.Select(s => Text(Fmt.Int(s.Values[idx]), 11, true, Ink)).ToList();
        double nameW = names.Count > 0 ? names.Max(n => n.Width) : 0, valW = vals.Count > 0 ? vals.Max(v => v.Width) : 0;
        double rowH = 16, cw = Math.Max(head.Width, 14 + nameW + 16 + valW) + 20, ch = 28 + rows.Count * rowH + 6;
        double left = X + 14;
        if (left + cw > ActualWidth - 4) left = X - cw - 14;
        double top = Math.Clamp(plot.Y, 4, Math.Max(4, ActualHeight - ch - 4));
        var card = new Rect(left, top, cw, ch);
        dc.DrawRoundedRectangle(Brush(Dark ? Color.FromArgb(0xF2, 0x2C, 0x2C, 0x2C) : Color.FromArgb(0xF7, 0xFF, 0xFF, 0xFF)),
            Hairline(Axis), card, 6, 6);
        dc.DrawText(head, new Point(left + 10, top + 6));
        for (int i = 0; i < rows.Count; i++)
        {
            double y = top + 28 + i * rowH;
            dc.DrawRectangle(Brush(InkOf(rows[i])), null, new Rect(left + 10, y + 4, 8, 8));
            dc.DrawText(names[i], new Point(left + 24, y));
            dc.DrawText(vals[i], new Point(left + cw - 10 - vals[i].Width, y));
        }
        DrawTime.Stop();
    }

    static int LastIndex(double[] v)
    {
        for (int i = v.Length - 1; i >= 0; i--) if (double.IsFinite(v[i])) return i;
        return -1;
    }

    static double LastValue(double[] v) { int i = LastIndex(v); return i < 0 ? 0 : v[i]; }
}
