using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Zerg.Views;

/// <summary>
/// Lets the mouse wheel through a scrolling area that has nowhere left to go.
/// A scroll viewer swallows the wheel even when it cannot scroll that way, so
/// a table inside the page would stop the page under the pointer. With
/// <c>WheelChain.On="True"</c> it hands the wheel to whatever it sits in once
/// it is at its end, or when it only scrolls sideways.
/// </summary>
public static class WheelChain
{
    public static readonly DependencyProperty OnProperty = DependencyProperty.RegisterAttached("On", typeof(bool),
        typeof(WheelChain), new PropertyMetadata(false, (d, e) =>
        {
            if (d is not UIElement el) return;
            if ((bool)e.NewValue) el.PreviewMouseWheel += OnWheel;
            else el.PreviewMouseWheel -= OnWheel;
        }));

    public static bool GetOn(DependencyObject d) => (bool)d.GetValue(OnProperty);
    public static void SetOn(DependencyObject d, bool value) => d.SetValue(OnProperty, value);

    static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || FindViewer((DependencyObject)sender) is not { } viewer) return;
        bool stuck = viewer.ScrollableHeight <= 0
                     || (e.Delta > 0 && viewer.VerticalOffset <= 0)
                     || (e.Delta < 0 && viewer.VerticalOffset >= viewer.ScrollableHeight);
        if (!stuck || System.Windows.Media.VisualTreeHelper.GetParent(viewer) is not UIElement parent) return;

        e.Handled = true;
        parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = sender,
        });
    }

    /// <summary>The element itself, or the scroll viewer inside its template
    /// (a virtualised list keeps its own).</summary>
    static ScrollViewer? FindViewer(DependencyObject d)
    {
        if (d is ScrollViewer s) return s;
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i++)
            if (FindViewer(System.Windows.Media.VisualTreeHelper.GetChild(d, i)) is { } inner) return inner;
        return null;
    }
}
