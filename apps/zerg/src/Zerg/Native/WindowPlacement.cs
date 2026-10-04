using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Zerg.Native;

/// <summary>
/// Puts a window back where it was, through Get/SetWindowPlacement, which
/// report the restored rectangle even while the window is maximized or
/// minimized, in physical pixels on whichever monitor it was on.
/// </summary>
static class WindowPlacement
{
    /// <summary>Whether a saved placement can be used: there is one, and it is
    /// still on a screen. Monitors get unplugged, and a window restored off
    /// every screen is a lost window.</summary>
    public static bool IsUsable(Placement? saved) =>
        saved is not null && OnScreen(ToRect(saved));

    /// <summary>
    /// Moves the window to the saved restored rectangle, maximized over it if
    /// it was. Call from SourceInitialized: the window has a handle but has not
    /// been shown, so it appears in the right place instead of jumping there.
    /// </summary>
    public static void Apply(Window window, Placement saved)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var wp = new Placement32 { length = Marshal.SizeOf<Placement32>(), showCmd = SW_HIDE, normal = ToRect(saved) };
        SetWindowPlacement(hwnd, ref wp);
        // Twice on purpose. A move onto a monitor with a different scale makes
        // WPF resize the window for the new DPI; the second call lands the
        // exact size on a window that is already on that monitor.
        SetWindowPlacement(hwnd, ref wp);
        // Only now, with the window on the right monitor: WPF shows it
        // maximized on whichever monitor it is on. Set before the handle
        // exists, WPF creates it already maximized on the default monitor,
        // and it stays there.
        if (saved.Maximized) window.WindowState = WindowState.Maximized;
    }

    // ------------------------------------------------------ floating panels
    //
    // A panel is never maximized or minimized, so it needs none of what
    // Get/SetWindowPlacement are for, and it is a tool window, whose placement
    // rectangle is in a different coordinate space from an ordinary window's.
    // Its frame is read and set directly, in screen pixels.

    /// <summary>Whether a panel's saved frame is still on a screen.</summary>
    public static bool IsUsableFrame(Placement? saved) =>
        saved is { Width: > 0, Height: > 0 } && FrameOnScreen(ToRect(saved));

    /// <summary>Moves a panel to a saved frame. Call from SourceInitialized.</summary>
    public static void ApplyFrame(Window window, Placement saved)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        // Twice, for the same reason as Apply: the first move onto a monitor
        // with a different scale makes WPF resize the window for it.
        for (int i = 0; i < 2; i++)
            SetWindowPos(hwnd, 0, saved.X, saved.Y, saved.Width, saved.Height, SWP_NOZORDER | SWP_NOACTIVATE);
    }

    /// <summary>Moves a panel without resizing it, to a point in screen pixels.</summary>
    public static void MoveFrame(Window window, int x, int y) =>
        SetWindowPos(new WindowInteropHelper(window).Handle, 0, x, y, 0, 0, SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOSIZE);

    /// <summary>A window's frame in screen pixels, or null before it has a handle.</summary>
    public static Placement? Frame(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0 || !GetWindowRect(hwnd, out var r)) return null;
        return new Placement(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, false);
    }

    /// <summary>Where a panel is now, or null if that is nowhere worth remembering.</summary>
    public static Placement? CaptureFrame(Window window) =>
        Frame(window) is { } f && FrameOnScreen(ToRect(f)) ? f : null;

    static Rect ToRect(Placement p) =>
        new() { Left = p.X, Top = p.Y, Right = p.X + p.Width, Bottom = p.Y + p.Height };

    /// <summary>Where the window is now, or null if that is nowhere useful.
    /// Windows parks a window hidden for "show desktop" at about (-32000,
    /// -32000); saving that would replace the last good placement with one that
    /// can never be used.</summary>
    public static Placement? Capture(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0) return null;
        var wp = new Placement32 { length = Marshal.SizeOf<Placement32>() };
        if (!GetWindowPlacement(hwnd, ref wp) || !OnScreen(wp.normal)) return null;
        bool maximized = wp.showCmd == SW_SHOWMAXIMIZED ||
                         (wp.showCmd == SW_SHOWMINIMIZED && (wp.flags & WPF_RESTORETOMAXIMIZED) != 0);
        var r = wp.normal;
        return new Placement(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, maximized);
    }

    /// <summary>
    /// Whether enough of the rectangle to grab and drag (120 × 40 pixels) is
    /// inside the work area of the monitor it is mostly on.
    ///
    /// The placement rectangle is in workspace coordinates: screen coordinates
    /// shifted by where the primary monitor's work area starts, which differs
    /// only when the taskbar is docked at the top or left. (A WS_EX_TOOLWINDOW
    /// window's placement is in plain screen coordinates instead.)
    /// </summary>
    static bool OnScreen(Rect workspace)
    {
        var primary = MonitorFromPoint(new Point32(), MONITOR_DEFAULTTOPRIMARY);
        var pi = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(primary, ref pi)) return false;
        int dx = pi.work.Left - pi.monitor.Left, dy = pi.work.Top - pi.monitor.Top;
        return FrameOnScreen(new Rect { Left = workspace.Left + dx, Top = workspace.Top + dy, Right = workspace.Right + dx, Bottom = workspace.Bottom + dy });
    }

    /// <summary>The same test for a rectangle already in screen pixels.</summary>
    static bool FrameOnScreen(Rect screen)
    {
        var mon = MonitorFromRect(ref screen, MONITOR_DEFAULTTONULL);
        if (mon == 0) return false;
        var mi = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(mon, ref mi)) return false;
        int w = Math.Min(screen.Right, mi.work.Right) - Math.Max(screen.Left, mi.work.Left);
        int h = Math.Min(screen.Bottom, mi.work.Bottom) - Math.Max(screen.Top, mi.work.Top);
        return w >= 120 && h >= 40;
    }

    // ------------------------------------------------------------- interop

    const int SW_HIDE = 0, SW_SHOWMINIMIZED = 2, SW_SHOWMAXIMIZED = 3;
    const int WPF_RESTORETOMAXIMIZED = 2;
    const uint MONITOR_DEFAULTTONULL = 0, MONITOR_DEFAULTTOPRIMARY = 1;
    const uint SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct Point32 { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    struct Placement32
    {
        public int length;
        public int flags;
        public int showCmd;
        public Point32 minPosition;
        public Point32 maxPosition;
        public Rect normal;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MonitorInfo
    {
        public int cbSize;
        public Rect monitor;
        public Rect work;
        public uint flags;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetWindowPlacement(nint hwnd, ref Placement32 placement);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetWindowPlacement(nint hwnd, ref Placement32 placement);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetWindowRect(nint hwnd, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    static extern nint MonitorFromRect(ref Rect rect, uint flags);

    [DllImport("user32.dll")]
    static extern nint MonitorFromPoint(Point32 pt, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}
