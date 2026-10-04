using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Zerg.Native;

/// <summary>
/// Zerg's icon in the taskbar's notification area, through Shell_NotifyIcon.
/// It says only what was done to it (picked, or asked for its menu); what
/// those do is the main window's business.
///
/// <para>The icon belongs to a window, which is told about the mouse and the
/// keyboard on it by a message. When Explorer restarts it forgets every icon
/// and says so to every top-level window, and the icon is put back.</para>
/// </summary>
sealed class TrayIcon : IDisposable
{
    const uint Id = 1;

    readonly nint hwnd;
    readonly string tip;
    readonly nint icon;
    readonly HwndSourceHook hook;
    readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");

    /// <summary>The icon is in the notification area. False when Explorer
    /// would not take it, and then nothing may be hidden behind it.</summary>
    public bool Shown { get; private set; }

    /// <summary>Clicked, or chosen with the keyboard.</summary>
    public event Action? Picked;

    /// <summary>Right-clicked, or asked for its menu with the keyboard.</summary>
    public event Action? MenuAsked;

    /// <param name="window">The window the icon reports to. It must have its handle.</param>
    public TrayIcon(Window window, string tip)
    {
        hwnd = new WindowInteropHelper(window).Handle;
        this.tip = tip;
        icon = OwnIcon();
        hook = OnMessage;
        HwndSource.FromHwnd(hwnd)?.AddHook(hook);
        Add();
    }

    void Add()
    {
        var data = Data(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        Shown = Shell_NotifyIcon(NIM_ADD, ref data);
        if (!Shown) return;
        // The current way of reporting: one message per click, with the
        // click's place, and a menu request that also comes from the keyboard.
        data.uVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIcon(NIM_SETVERSION, ref data);
    }

    Data32 Data(uint flags) => new()
    {
        cbSize = (uint)Marshal.SizeOf<Data32>(),
        hWnd = hwnd,
        uID = Id,
        uFlags = flags,
        uCallbackMessage = Callback,
        hIcon = icon,
        szTip = tip,
    };

    /// <summary>The icon the exe carries, at the size the notification area
    /// draws; Windows' plain application icon if it carries none.</summary>
    static nint OwnIcon()
    {
        var small = new nint[1];
        if (Environment.ProcessPath is { } exe && ExtractIconEx(exe, 0, null, small, 1) > 0 && small[0] != 0) return small[0];
        return LoadIcon(0, IDI_APPLICATION);
    }

    nint OnMessage(nint h, int msg, nint wp, nint lp, ref bool handled)
    {
        if ((uint)msg == taskbarCreated)
        {
            Add();
            return 0;
        }
        if (msg != Callback) return 0;
        handled = true;
        // What happened is the low word; the high word is which icon.
        switch ((int)(lp & 0xFFFF))
        {
            case NIN_SELECT or NIN_KEYSELECT: Picked?.Invoke(); break;
            case WM_CONTEXTMENU: MenuAsked?.Invoke(); break;
        }
        return 0;
    }

    /// <summary>Makes a window the one in front. Allowed in answer to a
    /// click on the icon, and what a menu opened from it needs: a menu that
    /// is not in front does not close when the player clicks elsewhere.</summary>
    public static void Front(nint window) => SetForegroundWindow(window);

    public void Dispose()
    {
        HwndSource.FromHwnd(hwnd)?.RemoveHook(hook);
        if (Shown)
        {
            var data = Data(0);
            Shell_NotifyIcon(NIM_DELETE, ref data);
            Shown = false;
        }
        if (icon != 0) DestroyIcon(icon);
    }

    // ------------------------------------------------------------- interop

    /// <summary>The message the icon reports with: one of the window's own.</summary>
    public const int Callback = 0x8000 + 1; // WM_APP + 1

    const uint NIM_ADD = 0, NIM_DELETE = 2, NIM_SETVERSION = 4;
    const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_SHOWTIP = 0x80;
    const uint NOTIFYICON_VERSION_4 = 4;
    const int WM_CONTEXTMENU = 0x7B, NIN_SELECT = 0x400, NIN_KEYSELECT = 0x401;
    const nint IDI_APPLICATION = 32512;

    /// <summary>NOTIFYICONDATAW, whole: Shell_NotifyIcon reads its size to
    /// know which fields are there.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct Data32
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool Shell_NotifyIcon(uint message, ref Data32 data);

    [DllImport("shell32.dll", EntryPoint = "ExtractIconExW", CharSet = CharSet.Unicode)]
    static extern uint ExtractIconEx(string file, int index, nint[]? large, nint[]? small, uint count);

    [DllImport("user32.dll", EntryPoint = "LoadIconW")]
    static extern nint LoadIcon(nint instance, nint name);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool DestroyIcon(nint icon);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode)]
    static extern uint RegisterWindowMessage(string name);
}
