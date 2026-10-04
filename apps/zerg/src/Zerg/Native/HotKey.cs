using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Zerg.Core;

namespace Zerg.Native;

/// <summary>
/// A key chord Windows watches for on Zerg's behalf, whichever application
/// has the keyboard: the game does, and Zerg's panels never take it.
///
/// <para>Windows gives a chord to one program at a time. If another already
/// has it, registering fails and there is no hot key: nothing here tries a
/// second chord, because a key the player was never told about is no use.</para>
/// </summary>
sealed class HotKey : IDisposable
{
    readonly nint hwnd;
    readonly int id;
    readonly Action pressed;
    readonly HwndSourceHook hook;

    HotKey(nint hwnd, int id, Action pressed)
    {
        this.hwnd = hwnd;
        this.id = id;
        this.pressed = pressed;
        hook = OnMessage;
        HwndSource.FromHwnd(hwnd)?.AddHook(hook);
    }

    /// <summary>
    /// Asks Windows for the chord, reported to <paramref name="window"/>,
    /// which must have its handle. Null when Windows refuses. Holding the
    /// keys down presses it once, not over and over.
    /// </summary>
    public static HotKey? Register(Window window, KeyChord chord, Action pressed, int id = 1)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        uint modifiers = (chord.Alt ? MOD_ALT : 0) | (chord.Ctrl ? MOD_CONTROL : 0) | (chord.Shift ? MOD_SHIFT : 0)
                       | (chord.Win ? MOD_WIN : 0) | MOD_NOREPEAT;
        return hwnd != 0 && RegisterHotKey(hwnd, id, modifiers, (uint)chord.Key) ? new HotKey(hwnd, id, pressed) : null;
    }

    nint OnMessage(nint h, int msg, nint wp, nint lp, ref bool handled)
    {
        if (msg != WM_HOTKEY || wp != id) return 0;
        handled = true;
        pressed();
        return 0;
    }

    public void Dispose()
    {
        UnregisterHotKey(hwnd, id);
        HwndSource.FromHwnd(hwnd)?.RemoveHook(hook);
    }

    // ------------------------------------------------------------- interop

    const int WM_HOTKEY = 0x0312;
    const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool UnregisterHotKey(nint hwnd, int id);
}
