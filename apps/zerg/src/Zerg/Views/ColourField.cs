using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;
using System.Windows.Media;
using Zerg.Core;

namespace Zerg.Views;

// The two parts of the colour picker that are pointed at: the square
// (ColourField: across it the saturation, up it the brightness) and the
// strip beside it (HueStrip). Each is one element that paints itself and
// follows the pointer while the button is held, wherever the pointer goes;
// each takes the keyboard, with a focus ring, and moves by the arrow keys;
// and each has an automation peer, so a screen reader can say where it
// stands and a script can set it without the pointer.
//
// Neither knows what a colour is for. ColourPicker holds the colour and
// tells them where to stand; they tell it (Moved) when a person moved them.

/// <summary>What the two share: the marker's inks, and a press that follows the pointer.</summary>
static class ColourMarks
{
    /// <summary>The marker: white, with a thin dark line round it, so it
    /// shows on any colour.</summary>
    public static readonly Pen White = Frozen(new Pen(Brushes.White, 1.5));
    public static readonly Pen Dark = Frozen(new Pen(Frozen(new SolidColorBrush(Colors.Black) { Opacity = 0.5 }), 1));

    public static T Frozen<T>(T made) where T : Freezable
    {
        made.Freeze();
        return made;
    }

    /// <summary>How far one press of an arrow key goes: a hundredth, or
    /// with Shift held a tenth.</summary>
    public static double Step => Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.1 : 0.01;
}

/// <summary>
/// The picker's square: every colour of one hue. Saturation runs from none
/// at its left edge to full at its right, brightness from full at its top
/// to none at its bottom, and a ring marks the colour picked.
///
/// <para>Pressed, it takes the colour under the pointer and follows the
/// pointer until the button is let go, outside the square as well (the
/// marker stops at the edge). With the keyboard: Left and Right change the
/// saturation, Up and Down the brightness, by a hundredth, or a tenth with
/// Shift; Home and End go to the left and right edges, Page Up and Page
/// Down a tenth up and down.</para>
///
/// <para>To UI Automation it is a custom control with a value: the
/// saturation and the brightness in whole percent ("81 91"), which can be
/// set in the same form.</para>
/// </summary>
public sealed class ColourField : FrameworkElement
{
    const double Radius = 3;

    /// <summary>The hue the square is of, in degrees.</summary>
    public static readonly DependencyProperty HueProperty = DependencyProperty.Register(nameof(Hue), typeof(double),
        typeof(ColourField), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Where the marker stands across the square, 0 to 1.</summary>
    public static readonly DependencyProperty SaturationProperty = DependencyProperty.Register(nameof(Saturation), typeof(double),
        typeof(ColourField), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Where it stands up the square, 0 (the bottom) to 1.</summary>
    public static readonly DependencyProperty BrightnessProperty = DependencyProperty.Register(nameof(Brightness), typeof(double),
        typeof(ColourField), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The line round the square.</summary>
    public static readonly DependencyProperty EdgeProperty = DependencyProperty.Register(nameof(Edge), typeof(Brush),
        typeof(ColourField), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>White at the left fading to nothing at the right, and
    /// nothing at the top deepening to black at the bottom: laid over the
    /// hue, one after the other, they are every colour of it.</summary>
    static readonly Brush Pale = ColourMarks.Frozen(new LinearGradientBrush(Colors.White, Color.FromArgb(0, 255, 255, 255), 0));
    static readonly Brush Deep = ColourMarks.Frozen(new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Colors.Black, 90));

    bool following;

    public ColourField()
    {
        Focusable = true;
        Cursor = Cursors.Cross;
        SetResourceReference(EdgeProperty, "Line2Brush");
    }

    public double Hue { get => (double)GetValue(HueProperty); set => SetValue(HueProperty, value); }
    public double Saturation { get => (double)GetValue(SaturationProperty); set => SetValue(SaturationProperty, value); }
    public double Brightness { get => (double)GetValue(BrightnessProperty); set => SetValue(BrightnessProperty, value); }
    public Brush? Edge { get => (Brush?)GetValue(EdgeProperty); set => SetValue(EdgeProperty, value); }

    /// <summary>A person moved the marker: with the pointer, with the
    /// keyboard, or through UI Automation. Not raised when the picker sets
    /// where it stands.</summary>
    public event EventHandler? Moved;

    /// <summary>Puts the marker somewhere, as a person would.</summary>
    internal void Move(double saturation, double brightness)
    {
        var to = Hsv.At(0, saturation, brightness);
        if (to.S == Saturation && to.V == Brightness) return;
        SetCurrentValue(SaturationProperty, to.S);
        SetCurrentValue(BrightnessProperty, to.V);
        Moved?.Invoke(this, EventArgs.Empty);
    }

    void Follow(Point at)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        Move(at.X / ActualWidth, 1 - at.Y / ActualHeight);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        // Without the capture a drag would stop at the square's edge: no drag then.
        following = CaptureMouse();
        Follow(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (following) Follow(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!following) return;
        following = false;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e) => following = false;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        double step = ColourMarks.Step, s = Saturation, v = Brightness;
        switch (e.Key)
        {
            case Key.Left: s -= step; break;
            case Key.Right: s += step; break;
            case Key.Up: v += step; break;
            case Key.Down: v -= step; break;
            case Key.Home: s = 0; break;
            case Key.End: s = 1; break;
            case Key.PageUp: v += 0.1; break;
            case Key.PageDown: v -= 0.1; break;
            default: return;
        }
        Move(s, v);
        e.Handled = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = RenderSize.Width, h = RenderSize.Height;
        if (w <= 0 || h <= 0) return;
        var box = new Rect(0, 0, w, h);
        var (r, g, b) = Hsv.Pure(Hue);
        dc.DrawRoundedRectangle(ColourMarks.Frozen(new SolidColorBrush(Color.FromRgb(r, g, b))), null, box, Radius, Radius);
        dc.DrawRoundedRectangle(Pale, null, box, Radius, Radius);
        dc.DrawRoundedRectangle(Deep, null, box, Radius, Radius);
        if (Edge is { } edge)
        {
            double hair = MarkInk.Hair(VisualTreeHelper.GetDpi(this).DpiScaleX);
            if (MarkInk.PenAt(edge, 1, hair) is { } line && w > hair && h > hair)
                dc.DrawRoundedRectangle(null, line, new Rect(hair / 2, hair / 2, w - hair, h - hair), Radius, Radius);
        }
        var at = new Point(Math.Clamp(Saturation, 0, 1) * w, (1 - Math.Clamp(Brightness, 0, 1)) * h);
        dc.DrawEllipse(null, ColourMarks.Dark, at, 6.5, 6.5);
        dc.DrawEllipse(null, ColourMarks.White, at, 5, 5);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    sealed class Peer(ColourField owner) : FrameworkElementAutomationPeer(owner), IValueProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Custom;
        protected override string GetLocalizedControlTypeCore() => "colour field";
        protected override string GetClassNameCore() => nameof(ColourField);
        public override object? GetPattern(PatternInterface pattern) => pattern == PatternInterface.Value ? this : base.GetPattern(pattern);

        public bool IsReadOnly => !owner.IsEnabled;

        /// <summary>The saturation, then the brightness, in whole percent.</summary>
        public string Value => string.Create(CultureInfo.InvariantCulture,
            $"{Math.Round(owner.Saturation * 100):0} {Math.Round(owner.Brightness * 100):0}");

        public void SetValue(string? value)
        {
            var parts = (value ?? "").Split([' ', ',', ';', '%'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 ||
                !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double s) ||
                !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                throw new ArgumentException("Two percentages: the saturation, then the brightness (\"81 91\").", nameof(value));
            owner.Move(s / 100, v / 100);
        }
    }
}

/// <summary>
/// The picker's strip: every hue, red at the top round to red again at the
/// bottom, with a bar across it at the hue picked.
///
/// <para>Pressed, it takes the hue under the pointer and follows the
/// pointer until the button is let go. With the keyboard: Up and Left go a
/// degree back, Down and Right a degree on, ten with Shift; Page Up and
/// Page Down thirty; Home and End the two ends.</para>
///
/// <para>To UI Automation it is a slider from 0 to 360.</para>
/// </summary>
public sealed class HueStrip : FrameworkElement
{
    /// <summary>The strip is drawn this far in from the element's sides,
    /// which leaves the bar's ends, and the pointer, some room.</summary>
    const double Inset = 3;
    const double Radius = 2, BarTall = 5;

    /// <summary>The hue the bar stands at, 0 to 360. Both ends are red:
    /// the far one is kept as 360 here, so a bar dragged to the bottom
    /// stays at the bottom.</summary>
    public static readonly DependencyProperty HueProperty = DependencyProperty.Register(nameof(Hue), typeof(double),
        typeof(HueStrip), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    static readonly Brush Hues = MakeHues();

    static Brush MakeHues()
    {
        var stops = new GradientStopCollection();
        for (int i = 0; i <= 6; i++)
        {
            var (r, g, b) = Hsv.Pure(i * 60);
            stops.Add(new GradientStop(Color.FromRgb(r, g, b), i / 6.0));
        }
        return ColourMarks.Frozen(new LinearGradientBrush(stops, 90));
    }

    bool following;

    public HueStrip()
    {
        Focusable = true;
        Cursor = Cursors.Hand;
    }

    public double Hue { get => (double)GetValue(HueProperty); set => SetValue(HueProperty, value); }

    /// <summary>A person moved the bar. Not raised when the picker sets where it stands.</summary>
    public event EventHandler? Moved;

    /// <summary>Puts the bar somewhere, as a person would.</summary>
    internal void Move(double hue)
    {
        hue = double.IsNaN(hue) ? 0 : Math.Clamp(hue, 0, 360);
        if (hue == Hue) return;
        SetCurrentValue(HueProperty, hue);
        Moved?.Invoke(this, EventArgs.Empty);
    }

    void Follow(Point at)
    {
        if (ActualHeight > 0) Move(at.Y / ActualHeight * 360);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        following = CaptureMouse();
        Follow(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (following) Follow(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!following) return;
        following = false;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e) => following = false;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        double step = ColourMarks.Step * 100, hue = Hue;
        switch (e.Key)
        {
            case Key.Up or Key.Left: hue -= step; break;
            case Key.Down or Key.Right: hue += step; break;
            case Key.PageUp: hue -= 30; break;
            case Key.PageDown: hue += 30; break;
            case Key.Home: hue = 0; break;
            case Key.End: hue = 360; break;
            default: return;
        }
        Move(hue);
        e.Handled = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = RenderSize.Width, h = RenderSize.Height;
        if (w <= 2 * Inset || h <= 0) return;
        // The whole element sees the pointer, not only the strip.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        dc.DrawRoundedRectangle(Hues, null, new Rect(Inset, 0, w - 2 * Inset, h), Radius, Radius);
        double y = Math.Clamp(Hue, 0, 360) / 360 * h;
        dc.DrawRoundedRectangle(null, ColourMarks.Dark, new Rect(0.5, y - BarTall / 2, w - 1, BarTall), 1.5, 1.5);
        dc.DrawRoundedRectangle(null, ColourMarks.White, new Rect(1.25, y - BarTall / 2 + 0.75, w - 2.5, BarTall - 1.5), 1, 1);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    sealed class Peer(HueStrip owner) : FrameworkElementAutomationPeer(owner), IRangeValueProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;
        protected override string GetClassNameCore() => nameof(HueStrip);
        public override object? GetPattern(PatternInterface pattern) => pattern == PatternInterface.RangeValue ? this : base.GetPattern(pattern);

        public bool IsReadOnly => !owner.IsEnabled;
        public double Minimum => 0;
        public double Maximum => 360;
        public double SmallChange => 1;
        public double LargeChange => 30;
        public double Value => owner.Hue;
        public void SetValue(double value) => owner.Move(value);
    }
}
