using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Documents;
using System.Windows.Media;
using Zerg.Charts;
using Zerg.Core;

namespace Zerg.Views;

/// <summary>
/// The session's state as a small outlined pill beside the clock: a mark
/// and a word, LIVE, ARMED, HELD, IDLE, or SAVED over a parse that is only
/// being looked at.
///
/// <para>The mark says it a second way, for an eye that does not tell green
/// from amber: a dot; a dot with a ring round it while armed; two upright
/// bars, the pause sign, while held.</para>
///
/// <para>One element that paints itself, and only when the state, the
/// theme or the screen's scale changes: it stands beside the clock, which is
/// rewritten at the draw frequency, and takes no part in that. Its three
/// colours (<see cref="Fill"/>, <see cref="Edge"/>, <see cref="Ink"/>) are
/// given by its style, per state (<c>StateTag</c> in <c>App.xaml</c>); the
/// face, size and weight of the word are the inherited text properties, and
/// <see cref="Tracking"/> is the room between its letters.</para>
///
/// <para>A script or a screen reader is told the state as a word ("Live"),
/// as a <c>TextBlock</c> would tell it, not the capitals drawn.</para>
/// </summary>
public sealed class StateTag : FrameworkElement
{
    /// <summary>The pill's height; its ends are half circles.</summary>
    const double Tall = 18;
    /// <summary>The middle of the mark, from the pill's left end.</summary>
    const double MarkAt = 10;
    /// <summary>Where the word begins, and the room after it.</summary>
    const double WordAt = 18, After = 9;
    /// <summary>How far under the pill's top the word's letters stand.</summary>
    const double WordBaseline = 12.4;

    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(nameof(State), typeof(SessionTag),
        typeof(StateTag), new FrameworkPropertyMetadata(SessionTag.Idle,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    static DependencyProperty Colour(string name) => DependencyProperty.Register(name, typeof(Brush), typeof(StateTag),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The pill's inside: the state's colour, faint.</summary>
    public static readonly DependencyProperty FillProperty = Colour(nameof(Fill));
    /// <summary>The pill's outline: the state's colour, half strength.</summary>
    public static readonly DependencyProperty EdgeProperty = Colour(nameof(Edge));
    /// <summary>The mark and the word: the state's colour.</summary>
    public static readonly DependencyProperty InkProperty = Colour(nameof(Ink));

    /// <summary>Extra room after each letter of the word, in units.</summary>
    public static readonly DependencyProperty TrackingProperty = DependencyProperty.Register(nameof(Tracking), typeof(double),
        typeof(StateTag), new FrameworkPropertyMetadata(0.0,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(typeof(StateTag));
    public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(typeof(StateTag));
    public static readonly DependencyProperty FontWeightProperty = TextElement.FontWeightProperty.AddOwner(typeof(StateTag));

    DrawnText? word;
    /// <summary>The ring round an armed tag's dot, kept with the ink it was made from.</summary>
    (Brush Ink, Pen Ring)? ring;

    public SessionTag State { get => (SessionTag)GetValue(StateProperty); set => SetValue(StateProperty, value); }
    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush? Edge { get => (Brush?)GetValue(EdgeProperty); set => SetValue(EdgeProperty, value); }
    public Brush? Ink { get => (Brush?)GetValue(InkProperty); set => SetValue(InkProperty, value); }
    public double Tracking { get => (double)GetValue(TrackingProperty); set => SetValue(TrackingProperty, value); }
    public FontFamily FontFamily { get => (FontFamily)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public FontWeight FontWeight { get => (FontWeight)GetValue(FontWeightProperty); set => SetValue(FontWeightProperty, value); }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == StateProperty || e.Property == InkProperty || e.Property == FontFamilyProperty ||
            e.Property == FontSizeProperty || e.Property == FontWeightProperty)
            Forget();
        // A reader that is listening hears the new state, as it would a TextBlock's new text.
        if (e.Property == StateProperty && UIElementAutomationPeer.FromElement(this) is { } peer)
            peer.RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, e.OldValue.ToString(), e.NewValue.ToString());
    }

    void Forget()
    {
        word?.Dispose();
        word = null;
    }

    /// <summary>The word as it will be drawn, shaped once and kept until
    /// something it depends on changes.</summary>
    DrawnText Word()
    {
        if (word != null) return word;
        var face = new Typeface(FontFamily, FontStyles.Normal, FontWeight, FontStretches.Normal);
        return word = new DrawnText(State.ToString().ToUpperInvariant(), face, FontSize, Ink ?? Brushes.Gray,
                                    VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }

    /// <summary>As wide as the word needs, on a whole number of pixels so
    /// both ends of the pill are sharp.</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double wide = WordAt + Word().WidthSpaced(Tracking) + After;
        return new Size(Math.Ceiling(wide * scale) / scale, Tall);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = RenderSize.Width, h = RenderSize.Height;
        if (w < 1 || h < 1) return;
        var ink = Ink ?? Brushes.Gray;

        // The outline is as thick as a control's edge beside it: one unit,
        // on whole pixels. Drawn inside the pill, so nothing is cut off.
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double line = Math.Max(1, Math.Round(scale)) / scale, half = line / 2;
        if (Fill is { } fill) dc.DrawRoundedRectangle(fill, null, new Rect(0, 0, w, h), h / 2, h / 2);
        if (Edge is { } edge)
        {
            var pen = new Pen(edge, line);
            pen.Freeze();
            dc.DrawRoundedRectangle(null, pen, new Rect(half, half, w - line, h - line), h / 2 - half, h / 2 - half);
        }

        double middle = h / 2;
        if (State == SessionTag.Held)
        {
            // The pause sign: two bars, 2 by 6, 2 apart.
            dc.DrawRectangle(ink, null, new Rect(MarkAt - 3, middle - 3, 2, 6));
            dc.DrawRectangle(ink, null, new Rect(MarkAt + 1, middle - 3, 2, 6));
        }
        else
        {
            if (State == SessionTag.Armed) dc.DrawEllipse(null, Ring(ink), new Point(MarkAt, middle), 6.5, 6.5);
            dc.DrawEllipse(ink, null, new Point(MarkAt, middle), 3, 3);
        }

        var text = Word();
        text.DrawSpaced(dc, WordAt, WordBaseline - text.Baseline, Tracking);
    }

    /// <summary>The ink at about a third of its strength, one unit wide.</summary>
    Pen Ring(Brush ink)
    {
        if (ring is { } kept && ReferenceEquals(kept.Ink, ink)) return kept.Ring;
        var faint = ink.Clone();
        faint.Opacity = 0.35;
        faint.Freeze();
        var pen = new Pen(faint, 1);
        pen.Freeze();
        ring = (ink, pen);
        return pen;
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        Forget();
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    /// <summary>What a <c>TextBlock</c> tells UI Automation: a piece of text, named by what it says.</summary>
    sealed class Peer(StateTag owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
        protected override string GetClassNameCore() => nameof(StateTag);

        protected override string GetNameCore()
        {
            // A name given in XAML outranks the word, as it does on a TextBlock.
            var given = base.GetNameCore();
            return string.IsNullOrEmpty(given) ? owner.State.ToString() : given;
        }

        // Whether it is a control and content is left to WPF: yes while it
        // can be seen, no while it cannot (its band is not the one on
        // screen), so a list of what is shown does not have it twice.
    }
}
