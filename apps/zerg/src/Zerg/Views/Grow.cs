using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Zerg.Views;

/// <summary>
/// A bar whose length is a share of the room it is given:
/// <c>Grow.Share="{Binding Fraction}"</c> on an element stretched across that
/// room. A new share is eased to over 250 ms, so a bar moves between counts
/// instead of jumping (in the main window; in a floating panel it goes
/// there at once). The length is a scale on the drawn element, not its
/// width, so no layout runs per frame.
/// </summary>
public static class Grow
{
    // Unset is "not a number", not 0: a bar whose first share is exactly 0
    // (a healer whose pet did all the healing) must still be told so, or it
    // is never scaled at all and lies the whole length of its row.
    public static readonly DependencyProperty ShareProperty = DependencyProperty.RegisterAttached("Share", typeof(double),
        typeof(Grow), new PropertyMetadata(double.NaN, OnShare));

    public static double GetShare(DependencyObject d) => (double)d.GetValue(ShareProperty);
    public static void SetShare(DependencyObject d, double value) => d.SetValue(ShareProperty, value);

    static void OnShare(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement el) return;
        double share = (double)e.NewValue;
        double to = double.IsNaN(share) ? 0 : Math.Clamp(share, 0, 1);
        // A template's transform is frozen and shared; each bar needs its own.
        if (el.RenderTransform is not ScaleTransform scale || scale.IsFrozen)
            el.RenderTransform = scale = new ScaleTransform(to, 1);

        // A bar that is not on screen yet starts at its length: only a
        // change someone can see is worth animating. And not at all when
        // Windows has animations switched off. Nor in a floating panel,
        // where a bar goes to its new length at once: a panel's text has
        // its halo from an effect on the whole card, and a bar eased under
        // it would have that worked out again on every frame of the ease,
        // over a game, several times a second in a fight.
        if (!el.IsVisible || !SystemParameters.ClientAreaAnimation || Float.GetOn(el))
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.ScaleX = to;
            return;
        }
        var ease = new DoubleAnimation(to, TimeSpan.FromMilliseconds(250)) { EasingFunction = new QuadraticEase() };
        // These monitors refresh at 120 Hz; a bar does not need that.
        Timeline.SetDesiredFrameRate(ease, 60);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, ease);
    }
}
