using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Zerg.Native;

/// <summary>
/// What is still Windows' about the main window, now that its title bar is
/// Zerg's own: the thin frame drawn round it, and where a maximized window
/// really is.
///
/// <para><b>The frame.</b> Windows 11 draws a one-pixel edge and round
/// corners on a window whose title bar it does not draw, in a colour of its
/// own choosing. <see cref="Edge"/> asks for Zerg's colour (the design's
/// window frame is the strongest rule, <c>Line3</c>) and says the corners
/// are to be round. Windows 10 has neither attribute: both calls fail there
/// and the window is square with no edge, which is accepted (the plan's
/// Q26).</para>
///
/// <para><b>Maximized.</b> Windows makes a maximized window larger than the
/// screen by the width of the sizing frame it would have had, and lets that
/// much hang off every side. With a title bar of its own the window's
/// content reaches its very edge, so that much of the content would be off
/// the screen. <see cref="Fit"/> measures it, from where the window and the
/// screen's work area actually are and not from what the frame is supposed
/// to measure, so it is right whatever the scale and whatever WPF itself
/// does about it.</para>
/// </summary>
static class WindowFrame
{
    /// <summary>The frame Windows draws round the window, in this colour,
    /// with round corners. Call once the window has a handle, and again when
    /// the theme changes.</summary>
    public static void Edge(Window window, Color colour)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0) return;
        // A COLORREF: 0x00BBGGRR.
        int rgb = colour.R | colour.G << 8 | colour.B << 16;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref rgb, sizeof(int));
        int round = DWMWCP_ROUND;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
    }

    /// <summary>
    /// How a maximized window lies on its screen, in the window's own units:
    /// how far each side of its content hangs over the screen's work area
    /// (what to keep clear of), and how far below the window's own top edge
    /// the screen begins (the window's top is above the screen by that
    /// much). All nothing for a window that is not maximized.
    /// </summary>
    public static (Thickness Over, double Top) Fit(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0 || !IsZoomed(hwnd)) return default;
        var origin = new Point32();
        var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        if (!GetClientRect(hwnd, out var client) || !ClientToScreen(hwnd, ref origin) || !GetWindowRect(hwnd, out var frame) ||
            !GetMonitorInfo(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST), ref info))
            return default;

        // In pixels, then in the units this window is laid out in.
        var dpi = VisualTreeHelper.GetDpi(window);
        var work = info.work;
        double left = Math.Max(0, work.Left - origin.X), top = Math.Max(0, work.Top - origin.Y);
        double right = Math.Max(0, origin.X + client.Right - work.Right), bottom = Math.Max(0, origin.Y + client.Bottom - work.Bottom);
        return (new Thickness(left / dpi.DpiScaleX, top / dpi.DpiScaleY, right / dpi.DpiScaleX, bottom / dpi.DpiScaleY),
                Math.Max(0, work.Top - frame.Top) / dpi.DpiScaleY);
    }

    // ------------------------------------------------------------- interop

    const int DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWA_BORDER_COLOR = 34;
    const int DWMWCP_ROUND = 2;
    const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct Point32 { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    struct MonitorInfo
    {
        public int cbSize;
        public Rect monitor;
        public Rect work;
        public uint flags;
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool IsZoomed(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetClientRect(nint hwnd, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetWindowRect(nint hwnd, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool ClientToScreen(nint hwnd, ref Point32 point);

    [DllImport("user32.dll")]
    static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}
