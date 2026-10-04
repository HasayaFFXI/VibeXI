using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace WpfOverlayPoc;

public sealed class Settings
{
    public int OpacityPct = 100;
    public bool Transparent, Frameless, ClickThrough, Topmost = true;
}

/// <summary>
/// The controller. Owns the settings, the (re)creatable window and the global
/// hotkey, and reads the window's real state back from Win32 for the readout.
///
/// Two window shapes, because WPF only does per-pixel alpha with
/// AllowsTransparency=true, which requires WindowStyle=None and can't be
/// changed once the window is shown:
///   frameless: AllowsTransparency, WPF itself makes the window WS_EX_LAYERED
///              and pushes per-pixel alpha with UpdateLayeredWindow. Opacity is
///              Window.Opacity. Transparent background = Background=Transparent.
///   framed:    an ordinary window. Opacity / click-through add WS_EX_LAYERED
///              ourselves with SetLayeredWindowAttributes(LWA_ALPHA).
/// </summary>
public partial class App : Application
{
    const int HotkeyId = 0x5A47;

    readonly Settings s = new();
    OverlayView view = null!;
    Window? win;
    HwndSource? hotkeySink;
    bool hotkeyOk, swapping;
    char hotkeyKey = 'Z';
    string hotkeyError = "";
    double startupMs = -1;
    TimeSpan lastCpu;
    DateTime lastSample = DateTime.UtcNow;
    double cpuPct;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ParseArgs(e.Args);
        view = new OverlayView(this);
        RegisterHotkey();

        // The readout re-reads Win32 twice a second: Windows, not our flags, is the truth.
        new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) => Report(), Dispatcher);

        ShowWindow(null);
    }

    void ParseArgs(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
            switch (args[i])
            {
                case "--opacity" when i + 1 < args.Length: s.OpacityPct = Math.Clamp(int.Parse(args[++i]), 10, 100); break;
                case "--transparent": s.Transparent = s.Frameless = true; break;
                case "--frameless": s.Frameless = true; break;
                case "--click-through": s.ClickThrough = true; break;
                case "--not-topmost": s.Topmost = false; break;
                // Another overlay (the Electron spike, say) may already own Ctrl+Alt+Z.
                case "--hotkey" when i + 1 < args.Length: hotkeyKey = char.ToUpperInvariant(args[++i][0]); break;
            }
    }

    // ------------------------------------------------------------- the window

    void ShowWindow(Win32Rect? at)
    {
        var w = new Window
        {
            Title = "Zerg WPF overlay spike",
            Width = 440,
            SizeToContent = SizeToContent.Height,
            Topmost = s.Topmost,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 120,
            Top = 120,
        };
        if (s.Frameless)
        {
            w.WindowStyle = WindowStyle.None;
            w.AllowsTransparency = true;
            w.ResizeMode = ResizeMode.NoResize;
            w.Opacity = s.OpacityPct / 100.0;
        }
        else
        {
            w.WindowStyle = WindowStyle.SingleBorderWindow;
            w.ResizeMode = ResizeMode.CanResize;
        }
        ApplyBackground(w);

        w.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            // Keep the physical position across a recreate. WPF's Left/Top are
            // DIPs and get muddled when the window is on a monitor whose scale
            // differs from the primary's.
            if (at is { } r) Win32Pos.Move(hwnd, r.Left, r.Top);
            HwndSource.FromHwnd(hwnd).AddHook((nint h, int m, nint wp, nint lp, ref bool handled) => StyleHook(w, m, wp, lp, ref handled));
            ApplyExStyle(w);
        };
        w.ContentRendered += (_, _) =>
        {
            if (startupMs < 0) startupMs = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;
            Report();
        };
        w.Closed += (_, _) => { if (!swapping) Shutdown(); };
        w.Content = view;
        win = w;
        w.Show();
    }

    /// <summary>AllowsTransparency is fixed once a window is shown, so
    /// toggling the frame means a new window with the same view in it.</summary>
    void Recreate()
    {
        if (win is null) return;
        var old = win;
        var rect = Win32Pos.Get(new WindowInteropHelper(old).Handle);
        swapping = true;
        old.Content = null;
        old.Close();
        swapping = false;
        ShowWindow(rect);
    }

    void ApplyBackground(Window w)
    {
        var bg = (Brush)Resources["Bg"];
        w.Background = s.Frameless && s.Transparent ? Brushes.Transparent : bg;
    }

    /// <summary>The ex-style bits we insist on (set, clear) for the current settings.</summary>
    (long set, long clear) WantedBits(Window w)
    {
        long set = 0, clear = 0;
        if (s.ClickThrough) set |= Win32.WS_EX_TRANSPARENT; else clear |= Win32.WS_EX_TRANSPARENT;
        // With AllowsTransparency WPF owns WS_EX_LAYERED (UpdateLayeredWindow).
        // Framed: Window.Opacity does nothing without AllowsTransparency, and
        // click-through needs a layered window, so we layer it ourselves.
        if (!w.AllowsTransparency)
        {
            if (s.ClickThrough || s.OpacityPct < 100) set |= Win32.WS_EX_LAYERED; else clear |= Win32.WS_EX_LAYERED;
        }
        return (set, clear);
    }

    void ApplyExStyle(Window w)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == 0) return;
        var (set, clear) = WantedBits(w);
        Win32.SetExStyle(hwnd, (Win32.ExStyle(hwnd) | set) & ~clear);
        if ((set & Win32.WS_EX_LAYERED) != 0)
            Win32.SetLayeredWindowAttributes(hwnd, 0, (byte)Math.Round(s.OpacityPct * 2.55), Win32.LWA_ALPHA);
    }

    /// <summary>
    /// WPF keeps its own copy of the window's ex style and writes it back
    /// whenever it touches the window (Show, Topmost...), and its message hook
    /// strips a WS_EX_LAYERED it didn't ask for. So a plain SetWindowLongPtr
    /// doesn't stick: we rewrite every WM_STYLECHANGING for GWL_EXSTYLE to
    /// carry our bits, and mark it handled so WPF's hook doesn't undo it.
    /// </summary>
    nint StyleHook(Window w, int msg, nint wParam, nint lParam, ref bool handled)
    {
        const int WM_STYLECHANGING = 0x007C;
        if (msg != WM_STYLECHANGING || wParam != Win32.GWL_EXSTYLE) return 0;
        var ss = System.Runtime.InteropServices.Marshal.PtrToStructure<Win32.StyleStruct>(lParam);
        var (set, clear) = WantedBits(w);
        ss.New = (uint)((ss.New | set) & ~clear);
        System.Runtime.InteropServices.Marshal.StructureToPtr(ss, lParam, false);
        handled = true;
        return 0;
    }

    // ------------------------------------------------------------- the knobs

    public void SetOpacity(int pct)
    {
        s.OpacityPct = Math.Clamp(pct, 10, 100);
        if (win is null) return;
        if (win.AllowsTransparency) win.Opacity = s.OpacityPct / 100.0;
        else ApplyExStyle(win);
        Report();
    }

    public void SetTransparent(bool on)
    {
        s.Transparent = on;
        if (on && !s.Frameless) { s.Frameless = true; Recreate(); }
        else if (win is not null) ApplyBackground(win);
        Report();
    }

    public void SetFrameless(bool on)
    {
        if (s.Frameless == on) return;
        s.Frameless = on;
        if (!on) s.Transparent = false;   // per-pixel alpha needs AllowsTransparency, which needs no frame
        Recreate();
        Report();
    }

    public void SetClickThrough(bool on)
    {
        s.ClickThrough = on;
        if (win is not null) ApplyExStyle(win);
        Report();
    }

    public void SetTopmost(bool on)
    {
        s.Topmost = on;
        if (win is not null) win.Topmost = on;
        Report();
    }

    // ----------------------------------------------------- hotkey (the way out)

    /// <summary>On a message-only window of its own, so it survives the main
    /// window being recreated.</summary>
    void RegisterHotkey()
    {
        hotkeySink = new HwndSource(new HwndSourceParameters("WpfOverlayPocHotkey")
        {
            ParentWindow = Win32.HWND_MESSAGE,
            WindowStyle = 0,
        });
        hotkeySink.AddHook((nint hwnd, int msg, nint wParam, nint lParam, ref bool handled) =>
        {
            if (msg == Win32.WM_HOTKEY && wParam == HotkeyId)
            {
                SetClickThrough(!s.ClickThrough);
                handled = true;
            }
            return 0;
        });
        hotkeyOk = Win32.RegisterHotKey(hotkeySink.Handle, HotkeyId,
            Win32.MOD_CONTROL | Win32.MOD_ALT | Win32.MOD_NOREPEAT, hotkeyKey);
        if (!hotkeyOk) hotkeyError = $" (error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()})";
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (hotkeySink is not null)
        {
            Win32.UnregisterHotKey(hotkeySink.Handle, HotkeyId);
            hotkeySink.Dispose();
        }
        base.OnExit(e);
    }

    // ---------------------------------------------------------------- readout

    void Report()
    {
        if (win is null) return;
        var hwnd = new WindowInteropHelper(win).Handle;
        if (hwnd == 0) return;

        var now = DateTime.UtcNow;
        double dt = (now - lastSample).TotalSeconds;
        if (dt >= 0.4)
        {
            var proc = Process.GetCurrentProcess();
            var cpu = proc.TotalProcessorTime;
            cpuPct = (cpu - lastCpu).TotalSeconds / dt / Environment.ProcessorCount * 100;
            lastCpu = cpu;
            lastSample = now;
        }

        long ex = Win32.ExStyle(hwnd);
        bool layered = (ex & Win32.WS_EX_LAYERED) != 0;
        string alpha = !layered ? "not layered (opaque)"
            : Win32.GetLayeredWindowAttributes(hwnd, out _, out byte a, out uint flags) && (flags & Win32.LWA_ALPHA) != 0
                ? $"LWA_ALPHA {a}/255 ({a / 2.55:0}%)"
                : "per-pixel (UpdateLayeredWindow)";
        uint dpi = Win32.GetDpiForWindow(hwnd);
        var wpfDpi = VisualTreeHelper.GetDpi(win);
        string awareness = Win32.GetAwarenessFromDpiAwarenessContext(Win32.GetWindowDpiAwarenessContext(hwnd)) switch
        {
            0 => "unaware", 1 => "system", 2 => "per-monitor", var x => x.ToString(),
        };
        var r = Win32Pos.Get(hwnd);

        string text =
            $"window         {(win.AllowsTransparency ? "AllowsTransparency, WindowStyle=None" : "framed, opaque surface")}\n" +
            $"Window.Opacity {win.Opacity:0.00}   bg {(win.Background == Brushes.Transparent ? "Transparent" : "solid")}\n" +
            $"ex style       0x{ex:X8}\n" +
            $"  layered      {layered}\n" +
            $"  transparent  {(ex & Win32.WS_EX_TRANSPARENT) != 0}  (click-through)\n" +
            $"  topmost      {(ex & Win32.WS_EX_TOPMOST) != 0}\n" +
            $"alpha          {alpha}\n" +
            $"dpi            {dpi} ({dpi / 96.0:P0}), WPF scale {wpfDpi.DpiScaleX:0.##}, {awareness}\n" +
            $"rect (px)      {r.Left},{r.Top} {r.Right - r.Left}x{r.Bottom - r.Top}\n" +
            $"hotkey         Ctrl+Alt+{hotkeyKey} {(hotkeyOk ? "registered" : "NOT registered" + hotkeyError)}\n" +
            $"render         tier {RenderCapability.Tier >> 16} ({RenderOptions.ProcessRenderMode}), cpu {cpuPct:0.0}%\n" +
            $"memory         {Environment.WorkingSet / 1048576} MB working set\n" +
            $"startup        {(startupMs < 0 ? "-" : $"{startupMs:0} ms to first frame")}";
        view.Sync(s, text);
    }
}
