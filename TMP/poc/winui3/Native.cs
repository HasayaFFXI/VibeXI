using System.Runtime.InteropServices;

namespace WinUiSpike;

static class Native
{
    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TRANSPARENT = 0x20, WS_EX_TOPMOST = 0x8, WS_EX_LAYERED = 0x80000,
                      WS_EX_NOREDIRECTIONBITMAP = 0x200000;
    public const uint LWA_ALPHA = 0x2;
    public const int WM_NCLBUTTONDOWN = 0xA1, HTCAPTION = 2, WM_HOTKEY = 0x312;
    public const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_NOREPEAT = 0x4000, VK_Z = 0x5A;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern nint GetWindowLongPtr(nint hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint value);
    [DllImport("user32.dll")]
    public static extern bool SetLayeredWindowAttributes(nint hWnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")]
    public static extern bool GetLayeredWindowAttributes(nint hWnd, out uint key, out byte alpha, out uint flags);
    [DllImport("user32.dll")]
    public static extern bool ReleaseCapture();
    [DllImport("user32.dll")]
    public static extern nint SendMessage(nint hWnd, int msg, nint wParam, nint lParam);
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(nint hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(nint hWnd, int id);
    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(nint hWnd);

    [StructLayout(LayoutKind.Sequential)]
    public struct MARGINS { public int Left, Right, Top, Bottom; }
    [DllImport("dwmapi.dll")]
    public static extern int DwmExtendFrameIntoClientArea(nint hWnd, ref MARGINS m);

    [StructLayout(LayoutKind.Sequential)]
    public struct DWM_BLURBEHIND { public uint dwFlags; public int fEnable; public nint hRgnBlur; public int fTransitionOnMaximized; }
    public const uint DWM_BB_ENABLE = 1, DWM_BB_BLURREGION = 2;
    [DllImport("dwmapi.dll")]
    public static extern int DwmEnableBlurBehindWindow(nint hWnd, ref DWM_BLURBEHIND bb);
    [DllImport("gdi32.dll")]
    public static extern nint CreateRectRgn(int l, int t, int r, int b);

    public delegate nint SubclassProc(nint hWnd, uint msg, nint wParam, nint lParam, nuint id, nuint data);
    [DllImport("comctl32.dll")]
    public static extern bool SetWindowSubclass(nint hWnd, SubclassProc proc, nuint id, nuint data);
    [DllImport("comctl32.dll")]
    public static extern bool RemoveWindowSubclass(nint hWnd, SubclassProc proc, nuint id);
    [DllImport("comctl32.dll")]
    public static extern nint DefSubclassProc(nint hWnd, uint msg, nint wParam, nint lParam);

    // ---- a Windows.System.DispatcherQueue for Windows.UI.Composition (backdrop brush)

    [StructLayout(LayoutKind.Sequential)]
    struct DispatcherQueueOptions { public int dwSize, threadType, apartmentType; }

    [DllImport("CoreMessaging.dll")]
    static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out nint controller);

    static nint queueController;

    public static void EnsureSystemDispatcherQueue()
    {
        if (Windows.System.DispatcherQueue.GetForCurrentThread() != null || queueController != 0) return;
        var o = new DispatcherQueueOptions
        {
            dwSize = Marshal.SizeOf<DispatcherQueueOptions>(),
            threadType = 2,     // DQTYPE_THREAD_CURRENT
            apartmentType = 2,  // DQTAT_COM_STA
        };
        Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(o, out queueController));
    }
}
