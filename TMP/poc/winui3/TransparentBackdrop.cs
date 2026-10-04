using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace WinUiSpike;

/// <summary>
/// A SystemBackdrop that paints nothing: a fully transparent Windows.UI.Composition
/// colour brush. WinUI 3 windows are DirectComposition windows with no
/// redirection bitmap, so whatever the XAML tree leaves transparent shows the
/// desktop through -- real per-pixel alpha. This is the same trick as WinUIEx's
/// TransparentTintBackdrop; Windows App SDK has no built-in transparent backdrop.
/// </summary>
sealed class TransparentBackdrop : SystemBackdrop
{
    Windows.UI.Composition.Compositor? compositor;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(target, xamlRoot);
        // The *system* compositor needs a Windows.System.DispatcherQueue on this thread.
        Native.EnsureSystemDispatcherQueue();
        compositor ??= new Windows.UI.Composition.Compositor();
        target.SystemBackdrop = compositor.CreateColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        target.SystemBackdrop = null;
        base.OnTargetDisconnected(target);
    }
}
