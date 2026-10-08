using System.Runtime.InteropServices;

namespace Zerg.Native;

/// <summary>
/// Which window has the keyboard, and handing it on.
///
/// <para>A floating panel is never meant to have it. A press on one does not
/// give it (<see cref="Overlay"/>). But when the window of Zerg's that had
/// the foreground goes away (the tray menu closing; something at a start)
/// Windows picks another of Zerg's windows to take it, and a panel, being
/// topmost, can be the one it picks, no-activate or not. The main window
/// looks for that at the two moments it has been seen, and hands the
/// foreground on. Nothing here is on a panel's own paths: a panel is asked
/// nothing and told nothing.</para>
/// </summary>
static class InFront
{
    /// <summary>The window in front, the one the keyboard goes to.</summary>
    public static nint Window => GetForegroundWindow();

    /// <summary>
    /// Gives the foreground to a window, if it still is one. Windows allows
    /// it only while Zerg has the foreground to give, which is the only time
    /// this is called: when one of Zerg's own panels has ended up with it.
    /// </summary>
    public static bool Give(nint window) => window != 0 && IsWindow(window) && SetForegroundWindow(window);

    [DllImport("user32.dll")]
    static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetForegroundWindow(nint hwnd);
}
