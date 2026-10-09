using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Documents;
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
/// What the charts share: the inks, drawn text, crisp hairlines, the empty
/// state and the hover card.
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
///
/// <para><b>To UI Automation a chart is one picture</b>: its name (given
/// in XAML: "Cumulative damage chart") and one line about what it shows
/// now (<see cref="Summary"/>, as its help text), with nothing inside it.
/// The line is made when a reader asks for it and at no other time: no
/// count and no beat does any work for it, and nothing is said when it
/// changes. What the chart draws is in the tables beside it, which a
/// reader can go through cell by cell.</para>
/// </summary>
public abstract class Chart : FrameworkElement
{
    /// <summary>The sizes of a chart's type: the labels along an axis, and
    /// everything else (a line's name at its end, the hover card).</summary>
    protected const double AxisSize = 10, LabelSize = 10.5;

    static readonly FontFamily Fallback = new("Segoe UI Variable Text, Segoe UI");
    static readonly Dictionary<Color, SolidColorBrush> Solids = [];

    /// <summary>
    /// The hover card's shadow: black, soft, falling 4 units below the card.
    /// It is built of plain rings, each a unit wider than the one inside it
    /// and each a little fainter, not of an effect: an effect is worked out
    /// again on every frame that draws it, and with the pointer resting on a
    /// live chart that is the draw frequency. Outermost first.
    /// </summary>
    static readonly SolidColorBrush[] Shade = [.. new[] { 0.007, 0.014, 0.020, 0.025, 0.029, 0.035, 0.039, 0.045, 0.051 }
        .Select(a =>
        {
            var b = new SolidColorBrush(Colors.Black) { Opacity = a };
            b.Freeze();
            return b;
        })];
    const double ShadeDrop = 4;

    /// <summary>
    /// The outline round a label while <see cref="Halo"/> is on: how deep it
    /// is, in units (the design's halo is 2.4 across, half of it outside
    /// the glyph), and the ink of each of the copies it is made of. A pixel
    /// beside a stem lies under about three of the eight copies of a ring,
    /// which at this strength comes to about two thirds; where the outline
    /// is two rings deep (150% and up) each copy is fainter.
    /// </summary>
    const double HaloDepth = 1.2;
    static readonly SolidColorBrush HaloInk = Black(0.30), HaloInkDeep = Black(0.20);
    static readonly (int X, int Y)[] Around = [(-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (1, -1), (-1, 1), (1, 1)];

    static SolidColorBrush Black(double strength)
    {
        var b = new SolidColorBrush(Colors.Black) { Opacity = strength };
        b.Freeze();
        return b;
    }

    /// <summary>Drawn text is kept between redraws; past this many it is thrown away and rebuilt.</summary>
    const int TextLimit = 2000;

    readonly List<Visual> layers = [];
    readonly DrawingVisual shade = new(), card = new();
    readonly Dictionary<(string, double, bool, Brush), DrawnText> texts = [];
    readonly Dictionary<(Brush, int), Pen> pens = [];
    Typeface? regular, semiBold;
    Point? pointer;
    object? shownKey;
    Size cardSize, shadeSize;

    protected Chart()
    {
        AddVisualChild(shade);
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
        // The face is the application's, as every other piece of text has it.
        SetResourceReference(FontFamilyProperty, "TextFont");
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

    /// <summary>An ink: a brush whose change draws the chart again from nothing.</summary>
    protected static DependencyProperty RegisterInk(string name, Type owner) => DependencyProperty.Register(name, typeof(Brush), owner,
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((Chart)d).Forget()));

    public static readonly DependencyProperty InkProperty = RegisterInk(nameof(Ink), typeof(Chart));
    public static readonly DependencyProperty Ink2Property = RegisterInk(nameof(Ink2), typeof(Chart));
    public static readonly DependencyProperty MutedProperty = RegisterInk(nameof(Muted), typeof(Chart));
    public static readonly DependencyProperty GridInkProperty = RegisterInk(nameof(GridInk), typeof(Chart));
    public static readonly DependencyProperty AxisInkProperty = RegisterInk(nameof(AxisInk), typeof(Chart));
    public static readonly DependencyProperty SurfaceProperty = RegisterInk(nameof(Surface), typeof(Chart));
    public static readonly DependencyProperty CardFillProperty = RegisterInk(nameof(CardFill), typeof(Chart));
    public static readonly DependencyProperty CardStrokeProperty = RegisterInk(nameof(CardStroke), typeof(Chart));

    /// <summary>The face every label is drawn in: the application's
    /// <c>TextFont</c>, unless something nearer says otherwise.</summary>
    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(typeof(Chart),
        new FrameworkPropertyMetadata(Fallback, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((Chart)d).Forget()));

    public static readonly DependencyProperty CompactProperty = DependencyProperty.Register(nameof(Compact), typeof(bool),
        typeof(Chart), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty EmptyTextProperty = DependencyProperty.Register(nameof(EmptyText), typeof(string),
        typeof(Chart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>A change draws the chart again from nothing, a layer that
    /// stands between counts included: what was drawn without the outline
    /// has to be drawn with it.</summary>
    public static readonly DependencyProperty HaloProperty = DependencyProperty.Register(nameof(Halo), typeof(bool),
        typeof(Chart), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((Chart)d).Forget()));

    protected Brush Get(DependencyProperty p) => (Brush?)GetValue(p) ?? Brushes.Gray;

    /// <summary>Figures and headings.</summary>
    public Brush Ink { get => Get(InkProperty); set => SetValue(InkProperty, value); }
    /// <summary>Names and labels beside the marks.</summary>
    public Brush Ink2 { get => Get(Ink2Property); set => SetValue(Ink2Property, value); }
    /// <summary>Axis labels, the empty state, the crosshair, and a line that stands for a group.</summary>
    public Brush Muted { get => Get(MutedProperty); set => SetValue(MutedProperty, value); }
    public Brush GridInk { get => Get(GridInkProperty); set => SetValue(GridInkProperty, value); }
    /// <summary>The baseline and the ticks under it.</summary>
    public Brush AxisInk { get => Get(AxisInkProperty); set => SetValue(AxisInkProperty, value); }
    /// <summary>The ring round a marker: the colour of what the chart sits on,
    /// solid, so a marker stays legible over the lines under it.</summary>
    public Brush Surface { get => Get(SurfaceProperty); set => SetValue(SurfaceProperty, value); }
    /// <summary>The hover card. Opaque: labels showing through it read as noise.</summary>
    public Brush CardFill { get => Get(CardFillProperty); set => SetValue(CardFillProperty, value); }
    /// <summary>The hover card's edge, and the rule under its heading.</summary>
    public Brush CardStroke { get => Get(CardStrokeProperty); set => SetValue(CardStrokeProperty, value); }

    public FontFamily FontFamily { get => (FontFamily)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }

    /// <summary>For a chart in a small window floating over the game: tighter
    /// margins and a denser hover card.</summary>
    public bool Compact { get => (bool)GetValue(CompactProperty); set => SetValue(CompactProperty, value); }

    /// <summary>What to say when there is nothing to draw; each chart has its own default.</summary>
    public string? EmptyText { get => (string?)GetValue(EmptyTextProperty); set => SetValue(EmptyTextProperty, value); }

    /// <summary>
    /// For a chart drawn straight onto a floating panel's see-through
    /// backdrop, with the game behind it: a label drawn with
    /// <see cref="Label"/> gets a dark outline, so it holds over a bright
    /// sky. A panel's other text has its halo from an effect; over a chart
    /// whose lines are drawn at the draw frequency an effect would be
    /// worked out again on every one of those draws, so that chart paints
    /// its own, on the layers that stand between counts.
    /// </summary>
    public bool Halo { get => (bool)GetValue(HaloProperty); set => SetValue(HaloProperty, value); }

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

    protected override int VisualChildrenCount => layers.Count + 2;
    protected override Visual GetVisualChild(int index) =>
        index < layers.Count ? layers[index] : index == layers.Count ? shade : card;

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

    /// <summary>
    /// How many times the inks, the face or the DPI have changed. A chart
    /// that leaves a layer standing from one draw to the next (the
    /// cumulative chart, between two counts) keeps the number it drew the
    /// layer at, and draws it again when this has moved on.
    /// </summary>
    protected int Epoch { get; private set; }

    void Forget()
    {
        ForgetText();
        pens.Clear();
        regular = semiBold = null;
        Epoch++;
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

    Typeface Face(bool bold) => bold
        ? semiBold ??= new Typeface(FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal)
        : regular ??= new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private protected DrawnText Text(string text, double size, bool bold, Brush brush)
    {
        var key = (text, size, bold, brush);
        if (texts.TryGetValue(key, out var t)) return t;
        return texts[key] = new DrawnText(text, Face(bold), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }

    /// <summary>Draws text with its corner on a device pixel, where it is sharpest.</summary>
    private protected void Draw(DrawingContext dc, DrawnText text, double x, double y) => text.Draw(dc, SnapEdge(x), SnapEdge(y));

    /// <summary>
    /// Draws text as <see cref="Draw"/> does, and while <see cref="Halo"/>
    /// is on with a dark outline round it: its glyphs in black, a pixel out
    /// in each of eight directions (two pixels deep from 150%), under the
    /// text itself. Nine or seventeen lines of glyphs where there was one:
    /// for a layer that is drawn when its data changes, never for one that
    /// is drawn at the draw frequency.
    /// </summary>
    private protected void Label(DrawingContext dc, DrawnText text, double x, double y)
    {
        double left = SnapEdge(x), top = SnapEdge(y);
        if (Halo)
        {
            double scale = Scale;
            int rings = Math.Max(1, (int)Math.Round(HaloDepth * scale));
            var ink = rings == 1 ? HaloInk : HaloInkDeep;
            for (int ring = 1; ring <= rings; ring++)
                foreach (var (dx, dy) in Around)
                    text.DrawGlyphs(dc, ink, left + dx * ring / scale, top + dy * ring / scale);
        }
        text.Draw(dc, left, top);
    }

    protected Pen CachedPen(Brush brush, int kind, Func<Pen> make)
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

    /// <summary>The 1.5 px ring round every marker.</summary>
    protected Pen Ring => CachedPen(Surface, 1, () => new Pen(Surface, 1.5));

    /// <summary>A line with round joins, 1.5 px, or 2 px for the one that
    /// leads; dashed for a line that stands for a group.</summary>
    protected Pen LinePen(Brush brush, bool dashed, bool lead = false) =>
        CachedPen(brush, (dashed ? 3 : 2) + (lead ? 2 : 0), () => new Pen(brush, lead ? 2 : 1.5)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            DashCap = PenLineCap.Round,
            // In units of the thickness: about 5 px on, 4 px off.
            DashStyle = dashed ? new DashStyle([3.3, 2.7], 0) : DashStyles.Solid,
        });

    /// <summary>A hairline in two units on, two off: the crosshair.</summary>
    protected Pen DashedHairline(Brush brush) => CachedPen(brush, 6, () =>
    {
        var hair = Hair;
        return new Pen(brush, hair) { DashStyle = new DashStyle([2 / hair, 2 / hair], 0) };
    });

    /// <summary>A hairline in a brush at part strength: the rule at a live chart's edge.</summary>
    protected Pen FaintHairline(Brush brush, double strength) => CachedPen(brush, 7, () =>
    {
        var faint = brush.Clone();
        faint.Opacity *= strength;
        faint.Freeze();
        return new Pen(faint, Hair);
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

    /// <summary>A small square that says whose something is, its middle at <paramref name="midY"/>.</summary>
    protected void DrawSwatch(DrawingContext dc, Brush fill, double x, double midY) =>
        dc.DrawRoundedRectangle(fill, null,
            new Rect(SnapEdge(x), SnapEdge(midY - HoverCard.Swatch / 2), HoverCard.Swatch, HoverCard.Swatch),
            HoverCard.SwatchRadius, HoverCard.SwatchRadius);

    protected void DrawEmpty(DrawingContext dc, double w, double h, string fallback)
    {
        var t = Text(string.IsNullOrEmpty(EmptyText) ? fallback : EmptyText, 12, false, Muted);
        Label(dc, t, (w - t.Width) / 2, (h - t.Height) / 2);
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
            // Only when a card is up. A chart redrawn with the pointer
            // elsewhere (every step of a live chart's edge) has nothing to
            // take down, and an empty layer drawn again is still a layer
            // drawn.
            if (Readout != null || shownKey != null)
            {
                Clear(card);
                Clear(shade);
                shadeSize = default;
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
            DrawShade(cardSize);
            shownKey = r.Key;
            OnHover(r);
        }
        Readout = r;
        var (left, top) = HoverCard.Place(r.X, r.Y, cardSize.Width, cardSize.Height, RenderSize.Width, RenderSize.Height);
        card.Offset = new Vector(SnapEdge(left), SnapEdge(top));
        shade.Offset = new Vector(card.Offset.X, card.Offset.Y + ShadeDrop);
    }

    /// <summary>The shadow under a card of this size. Drawn when the size
    /// changes and otherwise only moved with the card.</summary>
    void DrawShade(Size size)
    {
        if (size == shadeSize) return;
        shadeSize = size;
        using var dc = shade.RenderOpen();
        for (int i = 0; i < Shade.Length; i++)
        {
            double grow = Shade.Length - i, r = HoverCard.Radius + grow;
            dc.DrawRoundedRectangle(Shade[i], null, new Rect(-grow, -grow, size.Width + 2 * grow, size.Height + 2 * grow), r, r);
        }
    }

    /// <summary>
    /// Draws the card at the layer's origin and says how big it came out.
    /// Where everything in it goes is <see cref="HoverCard.Arrange"/>'s to
    /// say: a heading ruled off from the rows, and the rows in columns when
    /// there are more than the chart is tall.
    /// </summary>
    Size DrawCard(Readout r)
    {
        double size = Compact ? 10 : LabelSize;
        var head = Text(r.Head, size, true, Ink);
        var labels = r.Rows.Select(x => Text(x.Label, size, false, Ink2)).ToList();
        var values = r.Rows.Select(x => Text(x.Value, size, true, Ink)).ToList();
        var widths = new (double, double)[labels.Count];
        for (int i = 0; i < widths.Length; i++) widths[i] = (labels[i].Width, values[i].Width);

        var l = HoverCard.Arrange(head.Width, r.HeadSwatch != null, widths, r.Rows.Any(x => x.Swatch != null),
                                  RenderSize.Height, Compact, SnapEdge);

        using var dc = card.RenderOpen();
        var hair = Hair;
        dc.DrawRoundedRectangle(CardFill, Hairline(CardStroke), new Rect(hair / 2, hair / 2, l.Width - hair, l.Height - hair),
                                HoverCard.Radius, HoverCard.Radius);
        if (r.HeadSwatch != null) DrawSwatch(dc, r.HeadSwatch, l.HeadSwatchX, l.HeadMid);
        Draw(dc, head, l.HeadX, l.HeadMid - head.Height / 2);
        if (l.RuleY is { } rule)
        {
            var y = SnapLine(rule);
            dc.DrawLine(Hairline(CardStroke), new Point(hair, y), new Point(l.Width - hair, y));
        }

        for (int i = 0; i < l.Rows.Count; i++)
        {
            var at = l.Rows[i];
            double mid = at.Top + l.RowHeight / 2;
            if (r.Rows[i].Swatch is { } ink) DrawSwatch(dc, ink, at.SwatchX, mid);
            Draw(dc, labels[i], at.LabelX, mid - labels[i].Height / 2);
            Draw(dc, values[i], at.ValueRight - values[i].Width, mid - values[i].Height / 2);
        }
        return new Size(l.Width, l.Height);
    }

    // ------------------------------------------------------- to a screen reader

    /// <summary>One line about what the chart shows now: "3 lines over
    /// 00:24. Highest: Hasaya, 73,210". Empty when it shows nothing.
    /// Asked for by a screen reader, never by a draw.</summary>
    protected abstract string Summary();

    /// <summary>What the chart reads when it has nothing to draw.</summary>
    private protected string Nothing(string fallback) => string.IsNullOrEmpty(EmptyText) ? fallback : EmptyText;

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    /// <summary>A picture with a name and a line of help, and nothing in it.</summary>
    sealed class Peer(Chart owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;
        protected override string GetClassNameCore() => owner.GetType().Name;

        /// <summary>Its layers are drawings, not elements: there is
        /// nothing to look through, on any layout.</summary>
        protected override List<AutomationPeer>? GetChildrenCore() => null;

        protected override string GetHelpTextCore()
        {
            // Help given in XAML outranks the line.
            var given = base.GetHelpTextCore();
            return string.IsNullOrEmpty(given) ? owner.Summary() : given;
        }

        // Whether it is a control is left to WPF: yes while it can be seen,
        // no while it cannot (the chart of the section that is not on
        // screen, of a folded pane, of a card that is floating).
    }
}
