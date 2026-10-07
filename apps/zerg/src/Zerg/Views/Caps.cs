using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Documents;
using System.Windows.Media;
using Zerg.Charts;

namespace Zerg.Views;

/// <summary>
/// A small label in capitals with room between the letters: "TOTAL DAMAGE",
/// "CHARACTERS 12/12", "WEAPONSKILL".
///
/// <para>WPF's <c>TextBlock</c> has no letter spacing, so this draws its one
/// line itself: the text is put in upper case, shaped by the text formatter
/// (<see cref="DrawnText"/>, so figures are tabular and a character the
/// face lacks comes from the font WPF falls back to), and each glyph is then
/// given <see cref="Tracking"/> more room. Nothing wraps and nothing is
/// trimmed: a label is a word or two.</para>
///
/// <para>The text is taken as written and only drawn in capitals. A script
/// or a screen reader is told the text as written ("Total damage"), which
/// is what it was told when the label was a <c>TextBlock</c>, and what a
/// reader says as words and not letter by letter.</para>
///
/// <para>The face, size, weight and ink are the inherited text properties,
/// so a style sets them as it would on a <c>TextBlock</c>.</para>
/// </summary>
public sealed class Caps : FrameworkElement
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string),
        typeof(Caps), new FrameworkPropertyMetadata("",
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Extra room after each glyph, in units (the design's "+0.7").</summary>
    public static readonly DependencyProperty TrackingProperty = DependencyProperty.Register(nameof(Tracking), typeof(double),
        typeof(Caps), new FrameworkPropertyMetadata(0.0,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    // The text properties every element inherits, as TextBlock has them: set
    // on a window, they reach this; a change measures and draws it again.
    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(typeof(Caps));
    public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(typeof(Caps));
    public static readonly DependencyProperty FontWeightProperty = TextElement.FontWeightProperty.AddOwner(typeof(Caps));
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(Caps));

    DrawnText? drawn;

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public double Tracking { get => (double)GetValue(TrackingProperty); set => SetValue(TrackingProperty, value); }
    public FontFamily FontFamily { get => (FontFamily)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public FontWeight FontWeight { get => (FontWeight)GetValue(FontWeightProperty); set => SetValue(FontWeightProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == TextProperty || e.Property == FontFamilyProperty || e.Property == FontSizeProperty ||
            e.Property == FontWeightProperty || e.Property == ForegroundProperty)
            Forget();
        // A reader that is listening hears the new text, as it would a TextBlock's.
        if (e.Property == TextProperty && UIElementAutomationPeer.FromElement(this) is { } peer)
            peer.RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, e.OldValue as string ?? "", e.NewValue as string ?? "");
    }

    void Forget()
    {
        drawn?.Dispose();
        drawn = null;
    }

    /// <summary>The line as it will be drawn, shaped once and kept until something it depends on changes.</summary>
    DrawnText? Line()
    {
        if (drawn != null) return drawn;
        var text = Text;
        if (string.IsNullOrEmpty(text)) return null;
        var face = new Typeface(FontFamily, FontStyles.Normal, FontWeight, FontStretches.Normal);
        return drawn = new DrawnText(text.ToUpperInvariant(), face, FontSize, Foreground ?? Brushes.Gray,
                                     VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        Line() is { } line ? new Size(line.WidthSpaced(Tracking), line.Height) : new Size(0, 0);

    protected override void OnRender(DrawingContext dc) => Line()?.DrawSpaced(dc, 0, 0, Tracking);

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        Forget();
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    /// <summary>What a <c>TextBlock</c> tells UI Automation: a piece of text, named by what it says.</summary>
    sealed class Peer(Caps owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
        protected override string GetClassNameCore() => nameof(Caps);

        protected override string GetNameCore()
        {
            // A name given in XAML outranks the text, as it does on a TextBlock.
            var given = base.GetNameCore();
            return string.IsNullOrEmpty(given) ? owner.Text ?? "" : given;
        }

        protected override bool IsControlElementCore() => true;
        protected override bool IsContentElementCore() => true;
    }
}
