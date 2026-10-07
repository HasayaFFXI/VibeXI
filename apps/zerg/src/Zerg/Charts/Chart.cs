using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Zerg.Core.Charts;

namespace Zerg.Charts;

/// <summary>One line of a hover card as it is shown, with the swatch that leads it.</summary>
public sealed record ReadoutRow(string Label, string Value, Brush? Swatch = null);

/// <summary>
/// What a chart's hover card says, and the point it is anchored to.
/// <see cref="Key"/> names the thing under the pointer (a grid time, a bar, a
/// bin): the card is redrawn only when it changes.
/// </summary>
public sealed record Readout(object Key, string Head, Brush? HeadSwatch, IReadOnlyList<ReadoutRow> Rows, double X, double Y);

/// <summary>
/// What the three charts share: the inks, drawn text, crisp hairlines, the
/// empty state and the hover card.
///
/// <para>A chart is a stack of drawing layers. Its own layers are redrawn when
/// its data, size, inks or DPI change (WPF calls <see cref="OnRender"/>, which
/// calls <see cref="Paint"/>); the hover card is a layer on top that is redrawn
/// only when the thing under the pointer changes, and otherwise just moved.
/// Nothing here redraws per frame: these monitors refresh at 120 Hz, and a
/// chart redrawn at that rate costs a whole core.</para>
///
/// <para>Where things go is decided in <c>Zerg.Core.Charts</c>; the charts
/// only paint it.</para>
/// </summary>
public abstract class Chart : FrameworkElement
{
    static readonly FontFamily Family = new("Segoe UI Variable Text, Segoe UI");
    static readonly Typeface Regular = new(Family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    static readonly Typeface SemiBold = new(Family, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    static readonly Dictionary<Color, SolidColorBrush> Solids = [];

    /// <summary>Drawn text is kept between redraws; past this many it is thrown away and rebuilt.</summary>
    const int TextLimit = 2000;

    readonly List<Visual> layers = [];
    readonly DrawingVisual card = new();
    readonly Dictionary<(string, double, bool, Brush), DrawnText> texts = [];
    readonly Dictionary<(Brush, int), Pen> pens = [];
    Point? pointer;
    object? shownKey;
    Size cardSize;

    protected Chart()
    {
        AddVisualChild(card);
        // The inks follow the theme, and are a floating panel's own inside
        // one (Themes/Panel.xaml). A drawn layer can't restyle itself, so
        // each is a property, and a change to one redraws the chart.
        SetResourceReference(InkProperty, "Text1Brush");
        SetResourceReference(Ink2Property, "Text2Brush");
        SetResourceReference(MutedProperty, "Text3Brush");
        SetResourceReference(GridInkProperty, "LineBrush");
        SetResourceReference(AxisInkProperty, "Line2Brush");
        SetResourceReference(SurfaceProperty, "ChartSurfaceBrush");
        SetResourceReference(CardFillProperty, "HoverCardBrush");
        SetResourceReference(CardStrokeProperty, "Line3Brush");
        UseLayoutRounding = true;
        Cursor = Cursors.Cross;
        // A chart that is not on screen may have skipped a redraw (the live
        // edge's): it is drawn as it stands when it comes back.
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue) InvalidateVisual();
        };
    }

    // ----------------------------------------------------------- properties

    static DependencyProperty RegisterInk(string name) => DependencyProperty.Register(name, typeof(Brush), typeof(Chart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((Chart)d).Forget()));

    public static readonly DependencyProperty InkProperty = RegisterInk(nameof(Ink));
    public static readonly DependencyProperty Ink2Property = RegisterInk(nameof(Ink2));
    public static readonly DependencyProperty MutedProperty = RegisterInk(nameof(Muted));
    public static readonly DependencyProperty GridInkProperty = RegisterInk(nameof(GridInk));
    public static readonly DependencyProperty AxisInkProperty = RegisterInk(nameof(AxisInk));
    public static readonly DependencyProperty SurfaceProperty = RegisterInk(nameof(Surface));
    public static readonly DependencyProperty CardFillProperty = RegisterInk(nameof(CardFill));
    public static readonly DependencyProperty CardStrokeProperty = RegisterInk(nameof(CardStroke));

    public static readonly DependencyProperty CompactProperty = DependencyProperty.Register(nameof(Compact), typeof(bool),
        typeof(Chart), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty EmptyTextProperty = DependencyProperty.Register(nameof(EmptyText), typeof(string),
        typeof(Chart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    Brush Get(DependencyProperty p) => (Brush?)GetValue(p) ?? Brushes.Gray;

    /// <summary>Figures and headings.</summary>
    public Brush Ink { get => Get(InkProperty); set => SetValue(InkProperty, value); }
    /// <summary>Names and labels beside the marks.</summary>
    public Brush Ink2 { get => Get(Ink2Property); set => SetValue(Ink2Property, value); }
    /// <summary>Axis labels, the empty state, and a line that stands for a group.</summary>
    public Brush Muted { get => Get(MutedProperty); set => SetValue(MutedProperty, value); }
    public Brush GridInk { get => Get(GridInkProperty); set => SetValue(GridInkProperty, value); }
    /// <summary>The baseline and the crosshair.</summary>
    public Brush AxisInk { get => Get(AxisInkProperty); set => SetValue(AxisInkProperty, value); }
    /// <summary>The ring round a marker: the colour of what the chart sits on,
    /// solid, so a marker stays legible over the lines under it.</summary>
    public Brush Surface { get => Get(SurfaceProperty); set => SetValue(SurfaceProperty, value); }
    /// <summary>The hover card. Opaque: labels showing through it read as noise.</summary>
    public Brush CardFill { get => Get(CardFillProperty); set => SetValue(CardFillProperty, value); }
    public Brush CardStroke { get => Get(CardStrokeProperty); set => SetValue(CardStrokeProperty, value); }

    /// <summary>For a chart in a small window floating over the game: tighter
    /// margins and a denser hover card.</summary>
    public bool Compact { get => (bool)GetValue(CompactProperty); set => SetValue(CompactProperty, value); }

    /// <summary>What to say when there is nothing to draw; each chart has its own default.</summary>
    public string? EmptyText { get => (string?)GetValue(EmptyTextProperty); set => SetValue(EmptyTextProperty, value); }

    // --------------------------------------------------------------- layers

    /// <summary>Adds the chart's own layers, bottom first. The hover card stays on top.</summary>
    protected void Layers(params Visual[] visuals)
    {
        foreach (var v in visuals)
        {
            layers.Add(v);
            AddVisualChild(v);
        }
    }

    protected override int VisualChildrenCount => layers.Count + 1;
    protected override Visual GetVisualChild(int index) => index < layers.Count ? layers[index] : card;

    protected static void Clear(DrawingVisual v)
    {
        using var dc = v.RenderOpen();
    }

    protected sealed override void OnRender(DrawingContext dc)
    {
        // A layer is only hit where it has ink. This makes the whole chart
        // see the pointer, so hover doesn't cut out between the lines.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (texts.Count > TextLimit) ForgetText();
        Paint(RenderSize.Width, RenderSize.Height);
        UpdateHover(redraw: true);
    }

    /// <summary>Redraws the chart's own layers. A size under one pixel (a
    /// collapsed card, a first layout pass) must draw nothing.</summary>
    protected abstract void Paint(double w, double h);

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        Forget();
        InvalidateVisual();
    }

    // ---------------------------------------------------------------- tools

    void Forget()
    {
        ForgetText();
        pens.Clear();
    }

    void ForgetText()
    {
        foreach (var t in texts.Values) t.Dispose();
        texts.Clear();
    }

    protected double Scale => VisualTreeHelper.GetDpi(this).DpiScaleX;

    protected static SolidColorBrush Solid(Color c)
    {
        if (Solids.TryGetValue(c, out var b)) return b;
        b = new SolidColorBrush(c);
        b.Freeze();
        return Solids[c] = b;
    }

    private protected DrawnText Text(string text, double size, bool bold, Brush brush)
    {
        var key = (text, size, bold, brush);
        if (texts.TryGetValue(key, out var t)) return t;
        return texts[key] = new DrawnText(text, bold ? SemiBold : Regular, size, brush,
                                          VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }

    /// <summary>Draws text with its corner on a device pixel, where it is sharpest.</summary>
    private protected void Draw(DrawingContext dc, DrawnText text, double x, double y) => text.Draw(dc, SnapEdge(x), SnapEdge(y));

    Pen CachedPen(Brush brush, int kind, Func<Pen> make)
    {
        if (pens.TryGetValue((brush, kind), out var p)) return p;
        p = make();
        if (p.CanFreeze) p.Freeze();
        return pens[(brush, kind)] = p;
    }

    /// <summary>How thick a hairline is: one device pixel, two from 200 %. A
    /// line one unit thick is a blurred pixel and a half at 150 %.</summary>
    protected double Hair => Math.Max(1, Math.Floor(Scale)) / Scale;

    protected Pen Hairline(Brush brush) => CachedPen(brush, 0, () => new Pen(brush, Hair));

    /// <summary>The 2 px ring round every marker.</summary>
    protected Pen Ring => CachedPen(Surface, 1, () => new Pen(Surface, 2));

    /// <summary>A 2 px line with round joins; dashed for a line that stands for a group.</summary>
    protected Pen LinePen(Brush brush, bool dashed) => CachedPen(brush, dashed ? 3 : 2, () => new Pen(brush, 2)
    {
        LineJoin = PenLineJoin.Round,
        StartLineCap = PenLineCap.Round,
        EndLineCap = PenLineCap.Round,
        DashCap = PenLineCap.Round,
        // In units of the thickness: 5 px on, 4 px off.
        DashStyle = dashed ? new DashStyle([2.5, 2], 0) : DashStyles.Solid,
    });

    /// <summary>Where the centre of a hairline goes so that it fills whole device pixels.</summary>
    protected double SnapLine(double v)
    {
        var s = Scale;
        return (Math.Floor(v * s) + Math.Max(1, Math.Floor(s)) / 2) / s;
    }

    /// <summary>The nearest device-pixel boundary.</summary>
    protected double SnapEdge(double v)
    {
        var s = Scale;
        return Math.Round(v * s) / s;
    }

    /// <summary>A histogram's column: square at the foot, rounded at the top.</summary>
    protected static StreamGeometry RoundedTop(double x, double y, double w, double h, double radius)
    {
        double r = Math.Max(0, Math.Min(radius, Math.Min(w / 2, h / 2)));
        var size = new Size(r, r);
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(x, y + h), true, true);
            ctx.LineTo(new Point(x, y + r), false, false);
            ctx.ArcTo(new Point(x + r, y), size, 0, false, SweepDirection.Clockwise, false, false);
            ctx.LineTo(new Point(x + w - r, y), false, false);
            ctx.ArcTo(new Point(x + w, y + r), size, 0, false, SweepDirection.Clockwise, false, false);
            ctx.LineTo(new Point(x + w, y + h), false, false);
        }
        g.Freeze();
        return g;
    }

    protected void DrawEmpty(DrawingContext dc, double w, double h, string fallback)
    {
        var t = Text(string.IsNullOrEmpty(EmptyText) ? fallback : EmptyText, 13, false, Muted);
        Draw(dc, t, (w - t.Width) / 2, (h - t.Height) / 2);
    }

    // ---------------------------------------------------------------- hover

    /// <summary>What the pointer reads at <paramref name="p"/>, or null over nothing.</summary>
    protected abstract Readout? ReadoutAt(Point p);

    /// <summary>The thing under the pointer changed (or the chart was redrawn under it).</summary>
    protected virtual void OnHover(Readout? readout) { }

    /// <summary>What the hover card is showing now, or null.</summary>
    public Readout? Readout { get; private set; }

    protected override void OnMouseMove(MouseEventArgs e) => HoverAt(e.GetPosition(this));
    protected override void OnMouseLeave(MouseEventArgs e) => HoverAt(null);

    /// <summary>Puts the pointer at <paramref name="p"/> (null: nowhere), as the mouse does.</summary>
    public void HoverAt(Point? p)
    {
        pointer = p;
        UpdateHover(redraw: false);
    }

    void UpdateHover(bool redraw)
    {
        var r = pointer is { } p && RenderSize.Width >= 1 && RenderSize.Height >= 1 ? ReadoutAt(p) : null;
        if (r == null)
        {
            if (Readout != null || redraw)
            {
                Clear(card);
                Readout = null;
                shownKey = null;
                OnHover(null);
            }
            return;
        }
        if (redraw || !Equals(r.Key, shownKey))
        {
            if (!redraw && texts.Count > TextLimit) ForgetText();
            cardSize = DrawCard(r);
            shownKey = r.Key;
            OnHover(r);
        }
        Readout = r;
        var (left, top) = HoverCard.Place(r.X, r.Y, cardSize.Width, cardSize.Height, RenderSize.Width, RenderSize.Height);
        card.Offset = new Vector(SnapEdge(left), SnapEdge(top));
    }

    /// <summary>
    /// Draws the card at the layer's origin and says how big it came out. A
    /// card with more rows than its chart is tall runs them in columns: in a
    /// small floating window an alliance's eighteen rows would otherwise be
    /// cut off.
    /// </summary>
    Size DrawCard(Readout r)
    {
        bool dense = Compact;
        double size = dense ? 11 : 12, rowH = dense ? 14 : 18, padX = dense ? 8 : 10, padY = dense ? 5 : 7;
        const double valueGap = 14, columnGap = 18, swatch = 8;

        var head = Text(r.Head, size, true, Ink);
        var labels = r.Rows.Select(x => Text(x.Label, size, false, Ink2)).ToList();
        var values = r.Rows.Select(x => Text(x.Value, size, true, Ink)).ToList();
        double lead = r.Rows.Any(x => x.Swatch != null) ? swatch + 6 : 0;
        double headLead = r.HeadSwatch != null ? swatch + 6 : 0;
        double headH = r.Rows.Count > 0 ? head.Height + (dense ? 1 : 3) : head.Height;

        int count = r.Rows.Count;
        double room = RenderSize.Height - 8 - padY * 2 - headH;
        int perColumn = Math.Max(1, (int)Math.Floor(room / rowH));
        int columns = Math.Clamp((int)Math.Ceiling(count / (double)perColumn), 1, 3);
        perColumn = Math.Max(1, (int)Math.Ceiling(count / (double)columns));

        var labelW = new double[columns];
        var valueW = new double[columns];
        for (int i = 0; i < count; i++)
        {
            int c = i / perColumn;
            labelW[c] = Math.Max(labelW[c], labels[i].Width);
            valueW[c] = Math.Max(valueW[c], values[i].Width);
        }
        double bodyW = (columns - 1) * columnGap;
        for (int c = 0; c < columns; c++) bodyW += lead + labelW[c] + valueGap + valueW[c];

        double w = SnapEdge(Math.Max(Math.Max(bodyW, headLead + head.Width) + padX * 2, dense ? 96 : 118));
        double h = SnapEdge(padY * 2 + headH + Math.Min(count, perColumn) * rowH);

        using var dc = card.RenderOpen();
        var hair = Hair;
        dc.DrawRoundedRectangle(CardFill, Hairline(CardStroke), new Rect(hair / 2, hair / 2, w - hair, h - hair), 6, 6);
        if (r.HeadSwatch != null)
            dc.DrawEllipse(r.HeadSwatch, null, new Point(padX + swatch / 2, padY + head.Height / 2), swatch / 2, swatch / 2);
        Draw(dc, head, padX + headLead, padY);

        double x = padX;
        for (int c = 0; c < columns; c++)
        {
            // A single column's values line up with the card's right edge,
            // however wide the heading made it.
            double right = columns == 1 ? w - padX : x + lead + labelW[c] + valueGap + valueW[c];
            for (int i = c * perColumn; i < Math.Min(count, (c + 1) * perColumn); i++)
            {
                double y = padY + headH + (i - c * perColumn) * rowH;
                if (r.Rows[i].Swatch is { } ink)
                    dc.DrawEllipse(ink, null, new Point(x + swatch / 2, y + rowH / 2), swatch / 2, swatch / 2);
                Draw(dc, labels[i], x + lead, y + (rowH - labels[i].Height) / 2);
                Draw(dc, values[i], right - values[i].Width, y + (rowH - values[i].Height) / 2);
            }
            x += lead + labelW[c] + valueGap + valueW[c] + columnGap;
        }
        return new Size(w, h);
    }
}
