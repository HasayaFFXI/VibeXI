using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace FluentSpike;

/// <summary>DWM attributes read back from Windows, monitors, and a screen grab
/// of our own window (BitBlt of the composed desktop, so Mica is in it).</summary>
static class Native
{
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    [DllImport("dwmapi.dll")]
    static extern int DwmGetWindowAttribute(nint hwnd, int attr, out int value, int size);

    [DllImport("dwmapi.dll")]
    static extern int DwmGetWindowAttribute(nint hwnd, int attr, out Rect value, int size);

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect { public int Left, Top, Right, Bottom; }

    public static string GetInt(nint hwnd, int attr)
    {
        int hr = DwmGetWindowAttribute(hwnd, attr, out int v, 4);
        return hr == 0 ? v.ToString() : $"hr 0x{hr:X8}";
    }

    public static int SetInt(nint hwnd, int attr, int value) =>
        DwmSetWindowAttribute(hwnd, attr, ref value, 4);

    public static Rect Frame(nint hwnd)
    {
        DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out Rect r, Marshal.SizeOf<Rect>());
        return r;
    }

    // ------------------------------------------------------------- monitors

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MonitorInfoEx
    {
        public int Size;
        public Rect Monitor, Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }

    delegate bool MonitorEnum(nint mon, nint hdc, ref Rect r, nint data);

    [DllImport("user32.dll")]
    static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnum cb, nint data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool GetMonitorInfo(nint mon, ref MonitorInfoEx info);

    [DllImport("shcore.dll")]
    static extern int GetDpiForMonitor(nint mon, int type, out uint x, out uint y);

    public record Monitor(nint Handle, Rect Bounds, Rect Work, uint Dpi, bool Primary, string Device);

    public static List<Monitor> Monitors()
    {
        var list = new List<Monitor>();
        EnumDisplayMonitors(0, 0, (nint m, nint _, ref Rect _, nint _) =>
        {
            var mi = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
            GetMonitorInfo(m, ref mi);
            GetDpiForMonitor(m, 0, out uint dx, out _);
            list.Add(new Monitor(m, mi.Monitor, mi.Work, dx, (mi.Flags & 1) != 0, mi.Device));
            return true;
        }, 0);
        // Primary first, then left to right: "--monitor 2" means the second screen.
        return list.OrderByDescending(m => m.Primary).ThenBy(m => m.Bounds.Left).ToList();
    }

    // -------------------------------------------------------------- capture

    [DllImport("user32.dll")] static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(nint hwnd, nint hdc);
    [DllImport("gdi32.dll")] static extern nint CreateCompatibleDC(nint hdc);
    [DllImport("gdi32.dll")] static extern nint CreateCompatibleBitmap(nint hdc, int w, int h);
    [DllImport("gdi32.dll")] static extern nint SelectObject(nint hdc, nint obj);
    [DllImport("gdi32.dll")] static extern bool BitBlt(nint dst, int x, int y, int w, int h, nint src, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(nint hdc);

    /// <summary>What is on screen inside the window's frame, in physical pixels
    /// (this process is per-monitor aware, so the rect and the desktop agree).</summary>
    public static void Snap(nint hwnd, string path)
    {
        var r = Frame(hwnd);
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        nint screen = GetDC(0), mem = CreateCompatibleDC(screen), bmp = CreateCompatibleBitmap(screen, w, h);
        nint old = SelectObject(mem, bmp);
        BitBlt(mem, 0, 0, w, h, screen, r.Left, r.Top, 0x00CC0020 /* SRCCOPY */);
        SelectObject(mem, old);
        try
        {
            var src = Imaging.CreateBitmapSourceFromHBitmap(bmp, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(src));
            using var f = File.Create(path);
            enc.Save(f);
        }
        finally
        {
            DeleteObject(bmp);
            DeleteDC(mem);
            ReleaseDC(0, screen);
        }
    }
}
