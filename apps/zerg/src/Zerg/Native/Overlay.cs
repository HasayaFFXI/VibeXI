using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Zerg.Native;

/// <summary>Which part of a panel's frame the pointer has hold of.</summary>
[Flags]
enum Hold { None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8, Move = 16 }

/// <summary>
/// What makes a borderless window behave as an overlay for the game: it stays
/// off the taskbar and out of Alt+Tab, and it never takes the keyboard from
/// the game, not when it is clicked and not when it is moved or resized.
///
/// <para><b>Moving and resizing are done here, not by Windows.</b> The usual
/// way to drag a borderless window is to hand the press to the system (answer
/// a hit test as the caption or an edge, or <c>DragMove</c>). The system's
/// drag loop makes the window the foreground window, no-activate style or
/// not, and the game would lose the keyboard every time a panel was nudged.
/// So a drag is followed by hand: the frame the window had when the button
/// went down, plus how far the pointer has gone since, set without activating.</para>
/// </summary>
static class Overlay
{
    /// <summary>Call from SourceInitialized, before the window is first shown.</summary>
    public static void Attach(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        // Tool window: no taskbar button, not in Alt+Tab. No-activate: a click
        // lands on the panel's controls and the game keeps the keyboard, so a
        // fight can be started from the panel without leaving it.
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, GetWindowLongPtr(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        HwndSource.FromHwnd(hwnd)?.AddHook((nint h, int msg, nint wp, nint lp, ref bool handled) =>
        {
            if (msg != WM_MOUSEACTIVATE) return 0;
            handled = true;
            return MA_NOACTIVATE;
        });
    }

    /// <summary>
    /// Lets every click, and the pointer itself, pass through the window to
    /// whatever is under it, or takes them back. The window is already
    /// layered (it is see-through per pixel), which is what the transparent
    /// style needs. While it is on the window is told nothing about the
    /// mouse, so nothing on it can switch it off: that has to come from
    /// somewhere else.
    /// </summary>
    public static void ClickThrough(Window window, bool on)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0) return;
        nint style = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, on ? style | WS_EX_TRANSPARENT : style & ~WS_EX_TRANSPARENT);
    }

    /// <summary>
    /// A drag in progress: begun where the pointer and the window were when
    /// the button went down. Everything is in screen pixels, so it is right
    /// on a monitor of any scale.
    /// </summary>
    public sealed class Drag
    {
        readonly nint hwnd;
        readonly Hold hold;
        readonly Point32 from;
        readonly Rect start;
        readonly int minWidth, minHeight;

        public Drag(Window window, Hold hold, int minWidth, int minHeight)
        {
            hwnd = new WindowInteropHelper(window).Handle;
            this.hold = hold;
            this.minWidth = minWidth;
            this.minHeight = minHeight;
            GetCursorPos(out from);
            GetWindowRect(hwnd, out start);
        }

        /// <summary>Puts the frame where the pointer has taken it.</summary>
        public void Follow()
        {
            if (!GetCursorPos(out var now)) return;
            int dx = now.X - from.X, dy = now.Y - from.Y;
            if (hold == Hold.Move)
            {
                // The size is left alone: crossing onto a monitor with another
                // scale resizes the window, and that size is the right one.
                SetWindowPos(hwnd, 0, start.Left + dx, start.Top + dy, 0, 0, SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOSIZE);
                return;
            }

            int left = start.Left, top = start.Top, right = start.Right, bottom = start.Bottom;
            // An edge stops at the smallest size and the opposite edge stays put.
            if (hold.HasFlag(Hold.Left)) left = Math.Min(left + dx, right - minWidth);
            if (hold.HasFlag(Hold.Right)) right = Math.Max(right + dx, left + minWidth);
            if (hold.HasFlag(Hold.Top)) top = Math.Min(top + dy, bottom - minHeight);
            if (hold.HasFlag(Hold.Bottom)) bottom = Math.Max(bottom + dy, top + minHeight);
            SetWindowPos(hwnd, 0, left, top, right - left, bottom - top, SWP_NOZORDER | SWP_NOACTIVATE);
        }
    }

    // ------------------------------------------------------------- interop

    const int GWL_EXSTYLE = -20;
    const nint WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TRANSPARENT = 0x20;
    const int WM_MOUSEACTIVATE = 0x21;
    const int MA_NOACTIVATE = 3;
    const uint SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct Point32 { public int X, Y; }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    static extern nint GetWindowLongPtr(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetCursorPos(out Point32 point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetWindowRect(nint hwnd, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
}
