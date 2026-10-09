using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Zerg.Views;

// The marks drawn inside a table row: the low accuracy mark behind a rate,
// the small bar beside a share, an action's least, average and greatest hit
// on one line, and how far one hit fell from the average; and, in a row of
// the Compare section, which run each of its two lines is.
//
// Rows are the hot path: an alliance with every heading open is a few
// hundred of them. So each mark is one element that paints itself, with no
// panel and no shape inside it; every brush it paints with is frozen and
// shared between all the marks there are; none has an effect; and none is
// painted on the draw beat. A mark asks to be painted when a count gives
// it a new number, when a setting it reads flips, or when its colours
// change with the theme, and at no other time: the DPS beside it is
// rewritten thirty times a second and does not touch it. How long each mark
// is, is worked out in Zerg.Core (RowMarks, LowMark), where it is tested.

/// <summary>What the marks share: a colour at a strength as a frozen brush
/// made once, and whole pixels.</summary>
static class MarkInk
{
    static readonly Dictionary<(Color, int), SolidColorBrush> Brushes = [];
    static readonly Dictionary<(Color, int, double), Pen> Pens = [];

    /// <summary>A brush's colour at a strength, 0 to 1, in whole percent.
    /// Null for nothing to draw with.</summary>
    public static Brush? At(Brush? brush, double strength)
    {
        if (brush is not SolidColorBrush solid) return strength >= 1 ? brush : null;
        int percent = (int)Math.Round(Math.Clamp(strength * solid.Opacity, 0, 1) * 100);
        if (percent <= 0) return null;
        if (Brushes.TryGetValue((solid.Color, percent), out var made)) return made;
        made = new SolidColorBrush(solid.Color) { Opacity = percent / 100.0 };
        made.Freeze();
        return Brushes[(solid.Color, percent)] = made;
    }

    public static Pen? PenAt(Brush? brush, double strength, double thickness)
    {
        if (At(brush, strength) is not SolidColorBrush ink) return null;
        int percent = (int)Math.Round(ink.Opacity * 100);
        if (Pens.TryGetValue((ink.Color, percent, thickness), out var pen)) return pen;
        pen = new Pen(ink, thickness);
        pen.Freeze();
        return Pens[(ink.Color, percent, thickness)] = pen;
    }

    /// <summary>A length in units, on whole pixels of the screen the element is on.</summary>
    public static double Snap(double units, double scale) => Math.Round(units * scale) / scale;

    /// <summary>One unit as whole pixels: one at 100%, two at 150%.</summary>
    public static double Hair(double scale) => Math.Max(1, Math.Round(scale)) / scale;
}

/// <summary>
/// The low accuracy mark, behind the figure it holds: a rate under the
/// threshold is marked in red, more strongly the lower it falls. Just under,
/// a thin rule under the figure; further down the cell is cut out of its
/// row: filled with the pane's own surface, washed red and outlined red, so
/// it reads the same on a plain row and on any shade
/// (<c>Zerg.Core/LowMark</c> grades it).
///
/// <para>The figure is the element's one child, as in a <c>Border</c>; the
/// mark is painted under it, against the cell's right-hand edge, where the
/// figure ends. The threshold is the card's
/// (<see cref="Shading.LowAccuracyProperty"/>, inherited).</para>
/// </summary>
public sealed class LowMark : Decorator
{
    /// <summary>The cut-out: 46 by 18, 3 from the row's top, its
    /// right-hand edge 4 from the cell's (the figure ends 8 from it).</summary>
    const double CutWidth = 46, CutHeight = 18, CutTop = 3, CutRight = 4, CutRadius = 2;

    /// <summary>The rule: 36 by 2, 3 under the figure.</summary>
    const double RuleWidth = 36, RuleHeight = 2, RuleTop = 19, RuleRight = 9, RuleRadius = 1;

    /// <summary>The rate, 0 to 1; null where there is nothing to measure.</summary>
    public static readonly DependencyProperty RateProperty = DependencyProperty.Register(nameof(Rate), typeof(double?),
        typeof(LowMark), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty InkProperty = DependencyProperty.Register(nameof(Ink), typeof(Brush),
        typeof(LowMark), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SurfaceProperty = DependencyProperty.Register(nameof(Surface), typeof(Brush),
        typeof(LowMark), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public LowMark()
    {
        SetResourceReference(InkProperty, "CritBrush");
        SetResourceReference(SurfaceProperty, "Bg1Brush");
    }

    public double? Rate { get => (double?)GetValue(RateProperty); set => SetValue(RateProperty, value); }
    /// <summary>The red of the mark.</summary>
    public Brush? Ink { get => (Brush?)GetValue(InkProperty); set => SetValue(InkProperty, value); }
    /// <summary>The pane's own surface, which cuts the cell out of its row.</summary>
    public Brush? Surface { get => (Brush?)GetValue(SurfaceProperty); set => SetValue(SurfaceProperty, value); }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == Shading.LowAccuracyProperty) InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var look = Core.LowMark.Of(Rate, Shading.GetLowAccuracy(this));
        if (look.None) return;
        double w = RenderSize.Width, scale = VisualTreeHelper.GetDpi(this).DpiScaleX;

        if (look.CutOut)
        {
            double left = MarkInk.Snap(Math.Max(0, w - CutRight - CutWidth), scale), top = MarkInk.Snap(CutTop, scale);
            var box = new Rect(left, top, MarkInk.Snap(Math.Min(CutWidth, w - CutRight), scale), MarkInk.Snap(CutHeight, scale));
            if (box.Width > 0)
            {
                if (MarkInk.At(Surface, look.Surface) is { } surface) dc.DrawRoundedRectangle(surface, null, box, CutRadius, CutRadius);
                if (MarkInk.At(Ink, look.Wash) is { } wash) dc.DrawRoundedRectangle(wash, null, box, CutRadius, CutRadius);
                double hair = MarkInk.Hair(scale);
                if (MarkInk.PenAt(Ink, look.Outline, hair) is { } outline && box.Width > hair && box.Height > hair)
                    dc.DrawRoundedRectangle(null, outline, new Rect(box.X + hair / 2, box.Y + hair / 2, box.Width - hair, box.Height - hair),
                                            CutRadius, CutRadius);
            }
        }
        if (MarkInk.At(Ink, look.Rule) is { } rule)
        {
            double left = MarkInk.Snap(Math.Max(0, w - RuleRight - RuleWidth), scale);
            dc.DrawRoundedRectangle(rule, null, new Rect(left, MarkInk.Snap(RuleTop, scale), MarkInk.Snap(RuleWidth, scale),
                                                         MarkInk.Snap(RuleHeight, scale)), RuleRadius, RuleRadius);
        }
    }
}

/// <summary>Which of the two shading settings a <see cref="SmallBar"/> answers to.</summary>
public enum BarFor
{
    /// <summary>A character's row or heading: drawn while "shade characters" is off.</summary>
    Character,
    /// <summary>A row under a character: drawn while "shade actions and heals" is off.</summary>
    Action,
}

/// <summary>
/// The small bar beside a share's figure: a track, filled to the share,
/// painted under the figure it holds, at the cell's left. A share is drawn
/// exactly once: where the row itself is shaded to it, the row is the bar
/// and this paints nothing. Which setting decides that is
/// <see cref="For"/>; the settings are the card's (<see cref="Shading"/>,
/// inherited), and the bar paints itself again when one flips.
/// </summary>
public sealed class SmallBar : Decorator
{
    const double Thick = 6, Radius = 1.5;

    /// <summary>How much of the bar is filled, 0 to 1.</summary>
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double),
        typeof(SmallBar), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(Brush),
        typeof(SmallBar), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(nameof(Track), typeof(Brush),
        typeof(SmallBar), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>How long the bar is, in units: 48 beside a character's share, 42 beside an action's.</summary>
    public static readonly DependencyProperty LengthProperty = DependencyProperty.Register(nameof(Length), typeof(double),
        typeof(SmallBar), new FrameworkPropertyMetadata(48.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>How far in from the cell's left the bar begins.</summary>
    public static readonly DependencyProperty InsetProperty = DependencyProperty.Register(nameof(Inset), typeof(double),
        typeof(SmallBar), new FrameworkPropertyMetadata(10.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForProperty = DependencyProperty.Register(nameof(For), typeof(BarFor),
        typeof(SmallBar), new FrameworkPropertyMetadata(BarFor.Character, FrameworkPropertyMetadataOptions.AffectsRender));

    public SmallBar() => SetResourceReference(TrackProperty, "Bg3Brush");

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush? Track { get => (Brush?)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    public double Length { get => (double)GetValue(LengthProperty); set => SetValue(LengthProperty, value); }
    public double Inset { get => (double)GetValue(InsetProperty); set => SetValue(InsetProperty, value); }
    public BarFor For { get => (BarFor)GetValue(ForProperty); set => SetValue(ForProperty, value); }

    /// <summary>The row is not shaded to this share, so the bar is what draws it.</summary>
    bool Shown => For == BarFor.Character ? !Shading.GetCharacters(this) : !Shading.GetActions(this);

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        // Only the setting this bar answers to.
        if (e.Property == (For == BarFor.Character ? Shading.CharactersProperty : Shading.ActionsProperty)) InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (!Shown) return;
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double x = MarkInk.Snap(Inset, scale), thick = MarkInk.Snap(Thick, scale);
        double y = MarkInk.Snap((RenderSize.Height - Thick) / 2, scale);
        double length = Math.Min(Length, RenderSize.Width - Inset);
        if (length <= 0) return;
        if (Track is { } track) dc.DrawRoundedRectangle(track, null, new Rect(x, y, length, thick), Radius, Radius);
        double filled = length * Math.Clamp(double.IsNaN(Value) ? 0 : Value, 0, 1);
        if (filled > 0 && Fill is { } fill)
            dc.DrawRoundedRectangle(fill, null, new Rect(x, y, Math.Max(filled, Math.Min(length, 2 * Radius)), thick), Radius, Radius);
    }
}

/// <summary>
/// An action's least hit, average and greatest hit on one line: a baseline
/// the cell's width, a bar from the least to the greatest, and a tick at
/// the average, all to one scale, the character's biggest hit of any
/// action, so the lines under one character can be compared.
/// </summary>
public sealed class Spread : FrameworkElement
{
    /// <summary>The line begins 16 in from the cell's left and ends 12
    /// from its right: 132 long in a cell 160 wide.</summary>
    const double Left = 16, Right = 12;
    const double BarThick = 4, BarRadius = 2, TickWide = 2, TickTall = 10, TickRadius = 1;

    public static readonly DependencyProperty MinProperty = DependencyProperty.Register(nameof(Min), typeof(double),
        typeof(Spread), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty AvgProperty = DependencyProperty.Register(nameof(Avg), typeof(double),
        typeof(Spread), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty MaxProperty = DependencyProperty.Register(nameof(Max), typeof(double),
        typeof(Spread), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LineProperty = DependencyProperty.Register(nameof(Line), typeof(Brush),
        typeof(Spread), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty BarProperty = DependencyProperty.Register(nameof(Bar), typeof(Brush),
        typeof(Spread), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TickProperty = DependencyProperty.Register(nameof(Tick), typeof(Brush),
        typeof(Spread), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Spread()
    {
        SetResourceReference(LineProperty, "Line2Brush");
        SetResourceReference(BarProperty, "Text3Brush");
        SetResourceReference(TickProperty, "Text1Brush");
    }

    /// <summary>The least hit, the average and the greatest, each out of
    /// the character's biggest hit, 0 to 1.</summary>
    public double Min { get => (double)GetValue(MinProperty); set => SetValue(MinProperty, value); }
    public double Avg { get => (double)GetValue(AvgProperty); set => SetValue(AvgProperty, value); }
    public double Max { get => (double)GetValue(MaxProperty); set => SetValue(MaxProperty, value); }
    public Brush? Line { get => (Brush?)GetValue(LineProperty); set => SetValue(LineProperty, value); }
    public Brush? Bar { get => (Brush?)GetValue(BarProperty); set => SetValue(BarProperty, value); }
    public Brush? Tick { get => (Brush?)GetValue(TickProperty); set => SetValue(TickProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double x0 = MarkInk.Snap(Left, scale), length = RenderSize.Width - Left - Right;
        if (length < 8) return;
        double hair = MarkInk.Hair(scale);
        // The line's middle, on a pixel boundary when the line is an even
        // number of pixels thick and on a pixel's middle when it is odd.
        double mid = MarkInk.Snap(RenderSize.Height / 2 - hair / 2, scale) + hair / 2;
        if (Line is { } line) dc.DrawRectangle(line, null, new Rect(x0, mid - hair / 2, length, hair));

        double max = Math.Clamp(double.IsNaN(Max) ? 0 : Max, 0, 1);
        if (max <= 0) return;
        double min = Math.Clamp(double.IsNaN(Min) ? 0 : Min, 0, max), avg = Math.Clamp(double.IsNaN(Avg) ? 0 : Avg, min, max);
        double thick = MarkInk.Snap(BarThick, scale), tall = MarkInk.Snap(TickTall, scale);
        if (Bar is { } bar)
        {
            // An action that always hits for the same is a dot, not nothing.
            double from = x0 + length * min, wide = Math.Max(BarThick, length * (max - min));
            from = Math.Min(from, x0 + length - wide);
            dc.DrawRoundedRectangle(bar, null, new Rect(MarkInk.Snap(from, scale), mid - thick / 2, wide, thick), BarRadius, BarRadius);
        }
        if (Tick is { } tick)
            dc.DrawRoundedRectangle(tick, null, new Rect(MarkInk.Snap(x0 + length * avg - TickWide / 2, scale), mid - tall / 2,
                                                         MarkInk.Snap(TickWide, scale), tall), TickRadius, TickRadius);
    }
}

/// <summary>
/// How far one hit fell from the average: an upright tick, and a short bar
/// to its right in green for a hit over the average or to its left in red
/// for one under it, as long as the hit's distance out of the furthest any
/// hit in the list fell.
/// </summary>
public sealed class Deviation : FrameworkElement
{
    /// <summary>The longest a bar is, either side of the tick.</summary>
    const double Reach = 36;
    const double TickTall = 12, BarThick = 5, BarRadius = 1, Strength = 0.85;

    /// <summary>Over the average up to 1, under it down to -1.</summary>
    public static readonly DependencyProperty OffsetProperty = DependencyProperty.Register(nameof(Offset), typeof(double),
        typeof(Deviation), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TickProperty = DependencyProperty.Register(nameof(Tick), typeof(Brush),
        typeof(Deviation), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty OverProperty = DependencyProperty.Register(nameof(Over), typeof(Brush),
        typeof(Deviation), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty UnderProperty = DependencyProperty.Register(nameof(Under), typeof(Brush),
        typeof(Deviation), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Deviation()
    {
        SetResourceReference(TickProperty, "Line3Brush");
        SetResourceReference(OverProperty, "LiveBrush");
        SetResourceReference(UnderProperty, "CritBrush");
    }

    public double Offset { get => (double)GetValue(OffsetProperty); set => SetValue(OffsetProperty, value); }
    public Brush? Tick { get => (Brush?)GetValue(TickProperty); set => SetValue(TickProperty, value); }
    public Brush? Over { get => (Brush?)GetValue(OverProperty); set => SetValue(OverProperty, value); }
    public Brush? Under { get => (Brush?)GetValue(UnderProperty); set => SetValue(UnderProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        double w = RenderSize.Width, h = RenderSize.Height, scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (w < 8 || h < 4) return;
        double hair = MarkInk.Hair(scale), x = MarkInk.Snap(w / 2, scale);
        double tall = MarkInk.Snap(Math.Min(TickTall, h), scale), thick = MarkInk.Snap(BarThick, scale);
        double mid = MarkInk.Snap(h / 2, scale);
        if (Tick is { } tick) dc.DrawRectangle(tick, null, new Rect(x, mid - tall / 2, hair, tall));

        double offset = double.IsNaN(Offset) ? 0 : Math.Clamp(Offset, -1, 1);
        double length = Math.Abs(offset) * Math.Min(Reach, w / 2 - hair);
        if (length <= 0) return;
        var top = MarkInk.Snap(mid - BarThick / 2, scale);
        if (offset > 0 && MarkInk.At(Over, Strength) is { } over)
            dc.DrawRoundedRectangle(over, null, new Rect(x + hair, top, length, thick), BarRadius, BarRadius);
        else if (offset < 0 && MarkInk.At(Under, Strength) is { } under)
            dc.DrawRoundedRectangle(under, null, new Rect(x - length, top, length, thick), BarRadius, BarRadius);
    }
}

/// <summary>
/// Which run each of a Compare row's two lines is, said once for the whole
/// row: two short marks at the cell's left, run A's colour beside the upper
/// line and run B's beside the lower, painted under whatever the cell
/// holds (the job in each run, A over B). It is what a tick before every
/// figure of every cell used to say.
///
/// <para>The two lines are 14 units apart about the row's middle, as a
/// pair of figures' are (<see cref="RunPair"/>). The colours are the runs'
/// own brushes (<c>RunABrush</c>, <c>RunBBrush</c>: the user's, or the
/// installed pair), and the marks are painted again when one changes and
/// at no other time.</para>
/// </summary>
public sealed class RunMarks : Decorator
{
    /// <summary>A mark: 6 by 2, 8 in from the cell's left; the two lines' middles 7 either side of the row's.</summary>
    const double Wide = 6, Thick = 2, Inset = 8, Apart = 7, Radius = 1;

    public static readonly DependencyProperty AProperty = DependencyProperty.Register(nameof(A), typeof(Brush),
        typeof(RunMarks), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BProperty = DependencyProperty.Register(nameof(B), typeof(Brush),
        typeof(RunMarks), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public RunMarks()
    {
        SetResourceReference(AProperty, "RunABrush");
        SetResourceReference(BProperty, "RunBBrush");
    }

    /// <summary>Run A's colour.</summary>
    public Brush? A { get => (Brush?)GetValue(AProperty); set => SetValue(AProperty, value); }
    /// <summary>Run B's colour.</summary>
    public Brush? B { get => (Brush?)GetValue(BProperty); set => SetValue(BProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        double w = RenderSize.Width, h = RenderSize.Height, scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (w < Inset + Wide || h < 2 * Apart + Thick) return;
        double x = MarkInk.Snap(Inset, scale), wide = MarkInk.Snap(Wide, scale), thick = MarkInk.Snap(Thick, scale);
        if (A is { } a) dc.DrawRoundedRectangle(a, null, new Rect(x, MarkInk.Snap(h / 2 - Apart - Thick / 2, scale), wide, thick), Radius, Radius);
        if (B is { } b) dc.DrawRoundedRectangle(b, null, new Rect(x, MarkInk.Snap(h / 2 + Apart - Thick / 2, scale), wide, thick), Radius, Radius);
    }
}
