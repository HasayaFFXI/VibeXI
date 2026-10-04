using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Zerg.Views;

/// <summary>
/// A bar whose length is a share of the room it is given:
/// <c>Grow.Share="{Binding Fraction}"</c> on an element stretched across that
/// room. A new share is eased to over 250 ms, so a bar moves between counts
/// instead of jumping. The length is a scale on the drawn element, not its
/// width, so no layout runs per frame.
/// </summary>
public static class Grow
{
    public static readonly DependencyProperty ShareProperty = DependencyProperty.RegisterAttached("Share", typeof(double),
        typeof(Grow), new PropertyMetadata(0.0, OnShare));

    public static double GetShare(DependencyObject d) => (double)d.GetValue(ShareProperty);
    public static void SetShare(DependencyObject d, double value) => d.SetValue(ShareProperty, value);

    static void OnShare(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement el) return;
        double to = Math.Clamp((double)e.NewValue, 0, 1);
        // A template's transform is frozen and shared; each bar needs its own.
        if (el.RenderTransform is not ScaleTransform scale || scale.IsFrozen)
            el.RenderTransform = scale = new ScaleTransform(to, 1);

        // A bar that is not on screen yet starts at its length: only a
        // change someone can see is worth animating. And not at all when
        // Windows has animations switched off.
        if (!el.IsVisible || !SystemParameters.ClientAreaAnimation)
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
