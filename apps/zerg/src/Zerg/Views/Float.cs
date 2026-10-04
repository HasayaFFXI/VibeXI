using System.Windows;

namespace Zerg.Views;

/// <summary>
/// Says a card is in a floating panel, not in the main window.
///
/// <para>A panel sets <c>Float.On="True"</c> on what holds its card, and
/// everything inside inherits it. A card is the same control in both places,
/// bound to the same view model; the places it differs are style triggers on
/// this one flag (a card gives up its frame and heading, the cumulative chart
/// labels its lines, the per-character card becomes a strip). Nothing in the
/// view model knows which window a card is in.</para>
/// </summary>
public static class Float
{
    public static readonly DependencyProperty OnProperty = DependencyProperty.RegisterAttached("On", typeof(bool),
        typeof(Float), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static bool GetOn(DependencyObject d) => (bool)d.GetValue(OnProperty);
    public static void SetOn(DependencyObject d, bool value) => d.SetValue(OnProperty, value);
}
