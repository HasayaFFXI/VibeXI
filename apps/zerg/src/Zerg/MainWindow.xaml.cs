using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using Zerg.Core;
using Zerg.Native;

namespace Zerg;

public partial class MainWindow : Window
{
    const string PlacementKey = "main";

    readonly MainViewModel model;
    readonly Settings settings;
    readonly DispatcherTimer clock;
    readonly TrayMenu menu;
    TrayIcon? tray;
    HotKey? hotKey;
    /// <summary>What the window was before it was minimized, to go back to.</summary>
    WindowState restoreTo = WindowState.Normal;

    public MainWindow(MainViewModel model, Settings settings)
    {
        this.model = model;
        this.settings = settings;
        InitializeComponent();
        DataContext = model;

        settings.Windows.TryGetValue(PlacementKey, out var saved);
        if (WindowPlacement.IsUsable(saved))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            SourceInitialized += (_, _) => WindowPlacement.Apply(this, saved!);
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        // The same 4x a second as the poll, never per frame: these monitors
        // refresh at 120 Hz, and per-frame work would keep WPF rendering at that.
        clock = new DispatcherTimer(EventFeed.Interval, DispatcherPriority.Normal, (_, _) => model.Tick(), Dispatcher);

        // A panel is a window, and this is where windows are made; the view
        // model only says which cards are out.
        model.Panels.Opener = panel => new PanelWindow(panel, model, settings, this);
        // The same goes for a dialog.
        model.Compare.Picker = model.Picker = () => ParseDialog.Open(this);
        model.Saver = name => ParseDialog.Save(this, name);

        // Once it has been laid out: the card is not there to scroll to until then.
        model.DrillOpened += () => Dispatcher.BeginInvoke(Drill.BringIntoView, DispatcherPriority.Loaded);
        model.HealDrillOpened += () => Dispatcher.BeginInvoke(HealDrill.BringIntoView, DispatcherPriority.Loaded);
        Body.SizeChanged += (_, _) => FitTiles();

        menu = new TrayMenu(model, ComeForward, Close);
        SourceInitialized += (_, _) => Attach();
    }

    /// <summary>Five figures in a row need the width for it; a narrower window
    /// stacks them in threes, then twos. Both sections' rows, so switching
    /// section never shows one laid out for another width.</summary>
    void FitTiles() =>
        Tiles.Columns = HealTiles.Columns = Body.ActualWidth >= 1040 ? 5 : Body.ActualWidth >= 640 ? 3 : 2;

    void OnCharactersToggle(object sender, RoutedEventArgs e) => model.CharactersOpen = !model.CharactersOpen;

    // ------------------------------------------------- tray icon and hot key
    //
    // Both belong to this window's handle: it is there from before the window
    // is first shown until Zerg closes, hidden or not.

    void Attach()
    {
        tray = new TrayIcon(this, AppInfo.Name);
        tray.Picked += ComeForward;
        tray.MenuAsked += ShowMenu;
        if (!tray.Shown) Log.Write("tray icon: not shown; minimizing will not hide Zerg");

        // Settled before anything is asked of Windows: a setting that is no
        // chord is the default chord, not no hot key.
        var chord = KeyChord.OrDefault(settings.ClickThroughKey);
        if (KeyChord.Parse(settings.ClickThroughKey) is null)
            Log.Write($"settings: clickThroughKey \"{settings.ClickThroughKey}\" is not a key chord; using {chord}");
        hotKey = HotKey.Register(this, chord, () => model.Panels.ToggleClickThroughCommand.Execute(null));
        model.Panels.HotKey = hotKey != null ? chord.ToString() : null;
        Log.Write(hotKey != null ? $"hot key {chord}: click-through panels"
                                 : $"hot key {chord}: taken by another program; click-through is on the tray menu");
    }

    /// <summary>The panels that were out when Zerg was last closed. Once this window is on screen.</summary>
    public void Reopen() => model.Panels.Reopen();

    /// <summary>
    /// In front of everything, from wherever it was: behind other windows,
    /// minimized, or hidden in the tray. For a click on the tray icon and for
    /// a second launch, which is a player looking for the window.
    /// </summary>
    public void ComeForward()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = restoreTo;
        Activate();
    }

    /// <summary>
    /// The tray icon's menu, at the pointer. The menu's own window is made
    /// the foreground window, which Windows allows in answer to a click on
    /// the icon: a menu opened by a program that is not in front otherwise
    /// stays up when the player clicks somewhere else.
    /// </summary>
    void ShowMenu()
    {
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
        if (PresentationSource.FromVisual(menu) is HwndSource popup) TrayIcon.Front(popup.Handle);
    }

    /// <summary>Minimizing puts Zerg away in the tray: off the taskbar, with
    /// the panels still up and the session still measured.</summary>
    protected override void OnStateChanged(EventArgs e)
    {
        if (WindowState != WindowState.Minimized) restoreTo = WindowState;
        // Only behind an icon that is there to bring it back.
        else if (tray is { Shown: true })
        {
            Hide();
            Log.Write("minimized to the tray");
        }
        base.OnStateChanged(e);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        clock.Stop();
        // First, so each panel saves its frame as it closes, into the same
        // settings this window's placement is about to be written to.
        model.Panels.Leave();
        if (WindowPlacement.Capture(this) is { } placement)
        {
            settings.Windows[PlacementKey] = placement;
            settings.Save();
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        hotKey?.Dispose();
        tray?.Dispose();
        base.OnClosed(e);
    }
}
