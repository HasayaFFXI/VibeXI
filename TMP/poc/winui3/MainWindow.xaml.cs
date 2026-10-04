using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using static WinUiSpike.Native;

namespace WinUiSpike;

/// <summary>
/// The OverlaySpike checks (apps/zerg/poc/SPIKE.md) in WinUI 3:
///   1. translucent window that keeps repainting  -- three opacity methods, switchable
///   2. always on top                             -- OverlappedPresenter.IsAlwaysOnTop
///   3. background vanishes, panels stay solid    -- TransparentBackdrop + transparent Root
///   4. click-through + Ctrl+Alt+Z                -- WS_EX_TRANSPARENT|WS_EX_LAYERED, RegisterHotKey
///   5. frameless, movable by a drag strip        -- SetBorderAndTitleBar(false,false) + HTCAPTION
/// The readout card reads the real state back from Win32 every 500 ms.
/// </summary>
public sealed partial class MainWindow : Window
{
    enum Method { XamlWhole, XamlTint, Win32Layered }

    const int HotkeyId = 0x5A47;
    static readonly Windows.UI.Color Page = Windows.UI.Color.FromArgb(255, 0x14, 0x14, 0x1A);

    readonly nint hwnd;
    readonly OverlappedPresenter presenter;
    readonly ObservableCollection<MeterRow> rows = new();
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly Random rng = new();
    readonly SubclassProc wndProc;      // kept in a field so the GC can't collect the thunk
    readonly MenuFlyout menu = new();
    readonly long startupMs;

    int dwmHr = -1;
    string dwmMode = "";
    int pct = 100;
    Method method = Method.XamlWhole;
    bool transparentBg, frameless, clickThrough, topmost = true, hotkeyOk, syncing;

    public MainWindow()
    {
        InitializeComponent();
        hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        presenter = (OverlappedPresenter)AppWindow.Presenter;

        // Always installed: with it, anything Root leaves transparent is see-through.
        if (!Environment.GetCommandLineArgs().Contains("--nobackdrop"))
            SystemBackdrop = new TransparentBackdrop();
        dwmMode = ArgValue("--dwm") ?? "blur";
        if (dwmMode == "extend")
        {
            var m = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            dwmHr = DwmExtendFrameIntoClientArea(hwnd, ref m);
        }
        else if (dwmMode == "blur")
        {
            // Classic per-pixel-alpha trick for a redirected window: blur-behind
            // with an empty region, so DWM honours alpha but blurs nothing.
            var bb = new DWM_BLURBEHIND { dwFlags = DWM_BB_ENABLE | DWM_BB_BLURREGION, fEnable = 1,
                                          hRgnBlur = CreateRectRgn(0, 0, -1, -1) };
            dwmHr = DwmEnableBlurBehindWindow(hwnd, ref bb);
        }
        AppWindow.Title = "Zerg overlay spike (WinUI 3)";

        foreach (var n in new[] { "Hasaya", "Kirin", "Paradox", "Zergling", "Overlord" })
            rows.Add(new MeterRow(n));
        Rows.ItemsSource = rows;

        BuildMenu();
        Grip.ContextFlyout = menu;
        MethodBox.SelectedIndex = 0;

        wndProc = WndProc;
        SetWindowSubclass(hwnd, wndProc, 1, 0);
        hotkeyOk = RegisterHotKey(hwnd, HotkeyId, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_Z);
        Closed += (_, _) => { UnregisterHotKey(hwnd, HotkeyId); RemoveWindowSubclass(hwnd, wndProc, 1); };

        ParseArgs();
        double s = GetDpiForWindow(hwnd) / 96.0;
        AppWindow.MoveAndResize(new RectInt32(argX, argY, (int)(460 * s), (int)(800 * s)));
        Apply();
        // Window.Activate() shows the window and can drop the topmost bit set before it:
        // re-apply once it is actually up.
        bool first = true;
        Activated += (_, _) => { if (first) { first = false; Apply(); Report(); } };

        var q = DispatcherQueue;
        var fast = q.CreateTimer(); fast.Interval = TimeSpan.FromMilliseconds(100); fast.Tick += (_, _) => Tick100(); fast.Start();
        var slow = q.CreateTimer(); slow.Interval = TimeSpan.FromMilliseconds(500); slow.Tick += (_, _) => Tick500(); slow.Start();
        startupMs = (long)(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;
    }

    // ------------------------------------------------------------- animation

    void Tick100()
    {
        var t = clock.Elapsed;
        Clock.Text = $"{(int)t.TotalMinutes:00}:{t.Seconds + t.Milliseconds / 1000.0:00.0}";
    }

    void Tick500()
    {
        for (int i = 0; i < rows.Count; i++) rows[i].Add((long)Math.Round(rng.NextDouble() * 400 * (1 - i * 0.15)));
        long max = rows.Max(r => r.Damage);
        foreach (var r in rows) r.Scale(max);
        Report();
    }

    // ------------------------------------------------------------- the knobs

    void Apply()
    {
        // 1. opacity / tint (XAML side)
        double o = pct / 100.0;
        Root.Opacity = method == Method.XamlWhole ? o : 1.0;
        var bg = transparentBg ? Colors.Transparent : Page;
        if (method == Method.XamlTint)
            bg = transparentBg ? Windows.UI.Color.FromArgb((byte)(o * 255), 0, 0, 0)
                               : Windows.UI.Color.FromArgb((byte)(o * 255), Page.R, Page.G, Page.B);
        Root.Background = new SolidColorBrush(bg);

        // 2-5. presenter
        presenter.IsAlwaysOnTop = topmost;
        presenter.SetBorderAndTitleBar(!frameless, !frameless);

        ApplyExStyle();
    }

    /// <summary>Win32 side: layered (for LWA alpha and/or click-through) and transparent.
    /// Re-run after any presenter change in case the style was rebuilt.</summary>
    void ApplyExStyle()
    {
        bool lwa = method == Method.Win32Layered && pct < 100;
        bool layered = lwa || clickThrough;
        long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        ex = layered ? ex | WS_EX_LAYERED : ex & ~WS_EX_LAYERED;
        ex = clickThrough ? ex | WS_EX_TRANSPARENT : ex & ~WS_EX_TRANSPARENT;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, (nint)ex);
        // A layered window draws nothing until its attributes are set.
        if (layered) SetLayeredWindowAttributes(hwnd, 0, (byte)(lwa ? pct * 255 / 100 : 255), LWA_ALPHA);
    }

    void SetOpacity(int v) { pct = Math.Clamp(v, 10, 100); Apply(); Report(); }
    void SetMethod(Method m) { method = m; Apply(); Report(); }
    void SetTransparent(bool on) { transparentBg = on; Apply(); Report(); }
    void SetFrameless(bool on) { frameless = on; Apply(); Report(); }
    void SetClickThrough(bool on) { clickThrough = on; Apply(); Report(); }
    void SetTopmost(bool on) { topmost = on; Apply(); Report(); }

    // ------------------------------------------------------------- UI events

    void OpacitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (OpacityVal is null) return;     // fires during InitializeComponent
        OpacityVal.Text = $"{(int)e.NewValue}%";
        if (!syncing) SetOpacity((int)e.NewValue);
    }

    void MethodBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!syncing && MethodBox.SelectedIndex >= 0) SetMethod((Method)MethodBox.SelectedIndex);
    }

    void Toggle_Click(object sender, RoutedEventArgs e)
    {
        var box = (CheckBox)sender;
        bool on = box.IsChecked == true;
        if (box == TransparentBox) SetTransparent(on);
        else if (box == FramelessBox) SetFrameless(on);
        else if (box == ClickThroughBox) SetClickThrough(on);
        else if (box == TopmostBox) SetTopmost(on);
    }

    void Grip_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(Grip).Properties.IsLeftButtonPressed) return;
        // Same as WinForms: hand the drag to Windows as a title-bar drag.
        ReleaseCapture();
        SendMessage(hwnd, WM_NCLBUTTONDOWN, HTCAPTION, 0);
    }

    void BuildMenu()
    {
        var op = new MenuFlyoutSubItem { Text = "Opacity" };
        foreach (var v in new[] { 100, 85, 70, 55, 40, 25 })
        {
            var item = new MenuFlyoutItem { Text = v + "%" };
            item.Click += (_, _) => SetOpacity(v);
            op.Items.Add(item);
        }
        menu.Items.Add(op);
        menu.Items.Add(new MenuFlyoutSeparator());

        ToggleMenuFlyoutItem Toggle(string text, Action click)
        {
            var t = new ToggleMenuFlyoutItem { Text = text };
            t.Click += (_, _) => click();
            menu.Items.Add(t);
            return t;
        }
        var tr = Toggle("Transparent background", () => SetTransparent(!transparentBg));
        var fr = Toggle("Frameless", () => SetFrameless(!frameless));
        var ct = Toggle("Click-through  (Ctrl+Alt+Z)", () => SetClickThrough(!clickThrough));
        var tp = Toggle("Always on top", () => SetTopmost(!topmost));
        menu.Items.Add(new MenuFlyoutSeparator());
        var exit = new MenuFlyoutItem { Text = "Exit" };
        exit.Click += (_, _) => Close();
        menu.Items.Add(exit);

        menu.Opening += (_, _) =>
        {
            tr.IsChecked = transparentBg; fr.IsChecked = frameless;
            ct.IsChecked = clickThrough; tp.IsChecked = topmost;
        };
    }

    // ------------------------------------------------------------- hotkey

    nint WndProc(nint h, uint msg, nint wParam, nint lParam, nuint id, nuint data)
    {
        if (msg == WM_HOTKEY && wParam == HotkeyId)
        {
            SetClickThrough(!clickThrough);
            return 0;
        }
        return DefSubclassProc(h, msg, wParam, lParam);
    }

    // ------------------------------------------------------------- readout

    /// <summary>What the window actually is, read back from Win32, and the
    /// controls re-synced from our state (the hotkey and menu change it too).</summary>
    void Report()
    {
        syncing = true;
        OpacitySlider.Value = pct;
        MethodBox.SelectedIndex = (int)method;
        TransparentBox.IsChecked = transparentBg;
        FramelessBox.IsChecked = frameless;
        ClickThroughBox.IsChecked = clickThrough;
        TopmostBox.IsChecked = topmost;
        syncing = false;

        long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        string lwa = GetLayeredWindowAttributes(hwnd, out _, out byte a, out uint fl) && (fl & LWA_ALPHA) != 0
            ? $"{a} ({a * 100 / 255}%)" : "none";
        string runtime;
        try { runtime = Microsoft.Windows.ApplicationModel.WindowsAppRuntime.ReleaseInfo.AsString; }
        catch (Exception x) { runtime = "ReleaseInfo threw " + x.GetType().Name; }
        try { runtime += ", runtime " + Microsoft.Windows.ApplicationModel.WindowsAppRuntime.RuntimeInfo.AsString; }
        catch { runtime += ", RuntimeInfo n/a (self-contained)"; }

        AppWindow.Title = $"Zerg spike (WinUI 3) - {pct}%" + (transparentBg ? " transparent" : "") +
                          (clickThrough ? " click-through" : "") + (topmost ? "" : " (not on top)");

        State.Text =
            $"Root.Opacity   {Root.Opacity:0.00}   method {method}\n" +
            $"LWA alpha      {lwa}\n" +
            $"ex style       0x{ex:X8}\n" +
            $"  layered      {(ex & WS_EX_LAYERED) != 0}\n" +
            $"  transparent  {(ex & WS_EX_TRANSPARENT) != 0}\n" +
            $"  topmost      {(ex & WS_EX_TOPMOST) != 0}   (presenter {presenter.IsAlwaysOnTop})\n" +
            $"  noredirect   {(ex & WS_EX_NOREDIRECTIONBITMAP) != 0}\n" +
            $"frame          border {presenter.HasBorder}, title bar {presenter.HasTitleBar}\n" +
            $"backdrop       {SystemBackdrop?.GetType().Name ?? "none"}, dwm {dwmMode} hr=0x{dwmHr:X}\n" +
            $"dpi            {GetDpiForWindow(hwnd)}   bounds {AppWindow.Position.X},{AppWindow.Position.Y} {AppWindow.Size.Width}x{AppWindow.Size.Height}\n" +
            $"hotkey         {(hotkeyOk ? "Ctrl+Alt+Z registered" : "NOT registered (taken?)")}\n" +
            $"WinAppSDK      {runtime}\n" +
            $"startup        {startupMs} ms to first frame setup";
    }

    // ------------------------------------------------------------- test args

    int argX = 120, argY = 120;

    /// <summary>--opacity N --method 0|1|2 --transparent --frameless --clickthrough
    /// --notopmost --pos X,Y : start in a given state, for scripted checks.</summary>
    static string? ArgValue(string name)
    {
        var a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }

    void ParseArgs()
    {
        var a = Environment.GetCommandLineArgs();
        for (int i = 1; i < a.Length; i++)
        {
            string next() => i + 1 < a.Length ? a[++i] : "";
            switch (a[i])
            {
                case "--opacity": pct = Math.Clamp(int.Parse(next()), 10, 100); break;
                case "--method": method = (Method)int.Parse(next()); break;
                case "--transparent": transparentBg = true; break;
                case "--frameless": frameless = true; break;
                case "--clickthrough": clickThrough = true; break;
                case "--notopmost": topmost = false; break;
                case "--pos":
                    var p = next().Split(',');
                    argX = int.Parse(p[0]); argY = int.Parse(p[1]);
                    break;
            }
        }
    }
}
