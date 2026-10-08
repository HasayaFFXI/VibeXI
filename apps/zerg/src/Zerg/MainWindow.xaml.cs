using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;
using Zerg.Core;
using Zerg.Native;
using Zerg.Views;

namespace Zerg;

public partial class MainWindow : Window
{
    const string PlacementKey = "main";

    readonly MainViewModel model;
    readonly Settings settings;
    readonly Metronome draw;
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

        // Before the saved placement is asked for below: at the moment the
        // window gets its handle, it is first given its title bar and then
        // put where it was, in that order.
        barHeight = (double)FindResource("TitleBarHeight");
        chrome = TakeTitleBar();

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

        // Everything the clock moves is redrawn on one beat, as often as the
        // Settings page says: faster than a dispatcher timer can keep time,
        // and never per frame. These monitors refresh at 120 Hz, and per-frame
        // work would keep WPF rendering at that.
        draw = new Metronome(model.DrawFrequency, model.Tick, Dispatcher);
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.DrawFrequency)) draw.PerSecond = model.DrawFrequency;
        };
        IsVisibleChanged += (_, _) => Look();

        // A panel is a window, and this is where windows are made; the view
        // model only says which cards are out.
        model.Panels.Opener = panel => new PanelWindow(panel, model, settings, this);
        // The same goes for a dialog.
        model.Compare.Picker = model.Picker = () => ParseDialog.Open(this);
        model.Saver = name => ParseDialog.Save(this, name);
        model.FolderPicker = PickFolder;
        model.Asker = (headline, content) => TaskDialog.Ask(headline, content, TaskDialog.Icon.Warning, this);
        model.Binder = Bind;

        // A drill-down that has just opened is brought into view, where it
        // may be out of view: in a narrow window, whose panes stand in one
        // column on a page that scrolls. In a wider one the drill-down's
        // pane is always on screen, and picking an action moves nothing.
        // Once it has been laid out: the pane is folded until then. Not
        // while the table it belongs to floats: the drill-down is in that
        // panel, and there is nothing here to scroll to.
        model.DrillOpened += () =>
        {
            if (SplitPanel.GetStacked(Page) && !model.Panels[PanelSet.Actions].IsOut)
                Dispatcher.BeginInvoke(Drill.BringIntoView, DispatcherPriority.Loaded);
        };
        model.HealDrillOpened += () =>
        {
            if (SplitPanel.GetStacked(Page) && !model.Panels[PanelSet.HealActions].IsOut)
                Dispatcher.BeginInvoke(HealDrill.BringIntoView, DispatcherPriority.Loaded);
        };

        // The panes' arrangement is the view model's to keep. A section's
        // panel says what a player asked for (a rule dragged, a pane
        // folded or put somewhere else) and what it has to say on the
        // status line meanwhile; both are handed on from here, whichever of
        // the three panels spoke.
        AddHandler(SplitPanel.RearrangedEvent, new EventHandler<RearrangedEventArgs>((_, e) =>
        {
            model.Rearrange(e.Section, e.Tree, e.What);
            e.Handled = true;
        }));
        AddHandler(SplitPanel.NotedEvent, new EventHandler<NotedEventArgs>((_, e) => model.LayoutNote = e.Note));

        menu = new TrayMenu(model, ComeForward, ShowSettings, Close);
        // Once the menu has gone and Windows has chosen who is in front.
        menu.Closed += (_, _) => Dispatcher.BeginInvoke(() => HandOn(beforeMenu, "the tray menu closed"), DispatcherPriority.ApplicationIdle);
        SourceInitialized += (_, _) => Attach();
    }

    // ---------------------------------------------------------- the title bar
    //
    // The window's top 36 units are Zerg's own (the first row of
    // MainWindow.xaml). WPF's WindowChrome makes the whole window the
    // client area and answers Windows' question "what is under the pointer"
    // for it: the bar's empty parts are the caption, so Windows itself still
    // drags the window by them, maximizes on a double-click, snaps it to a
    // screen edge and opens the window's menu on a right-click or Alt+Space;
    // and a strip along each edge sizes the window. Placement is untouched:
    // WindowPlacement still reads and sets the same rectangle, and
    // minimizing still goes through OnStateChanged.

    /// <summary>How wide the strip along each edge is that sizes the window,
    /// in units. Inside the window: with the whole window Zerg's to draw,
    /// Windows keeps no sizing frame outside it.</summary>
    const double SizingEdge = 6;

    readonly WindowChrome chrome;
    /// <summary>The title bar's height (TitleBarHeight in App.xaml).</summary>
    readonly double barHeight;
    /// <summary>A change to the caption's height is waiting its turn.</summary>
    bool fitting;

    /// <summary>
    /// Hands the window's top to the title bar. Call from the constructor,
    /// before the window has a handle.
    /// </summary>
    WindowChrome TakeTitleBar()
    {
        var made = new WindowChrome
        {
            // Counted from under the top sizing strip: together they are the bar.
            CaptionHeight = barHeight - SizingEdge,
            ResizeBorderThickness = new Thickness(SizingEdge),
            // Zerg draws its own three buttons (and so gets no Snap Layouts
            // flyout over Maximize; dragging to an edge and Win+arrows still snap).
            UseAeroCaptionButtons = false,
            // Not nothing: with any of Windows' frame extended into the
            // window, Windows 11 goes on giving it round corners, its edge
            // and its shadow. One unit along the bottom, under the status
            // line, where Zerg paints over it.
            GlassFrameThickness = new Thickness(0, 0, 0, 1),
            CornerRadius = default,
        };
        WindowChrome.SetWindowChrome(this, made);

        SourceInitialized += (_, _) =>
        {
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(Watch);
            Edge();
            Fit();
        };
        AppTheme.Changed += Edge;
        DpiChanged += (_, _) => Fit();
        return made;
    }

    /// <summary>The edge Windows draws round the window, in the theme's
    /// strongest rule: the design's window frame.</summary>
    void Edge() => WindowFrame.Edge(this, AppTheme.Token("Line3", AppTheme.IsDark));

    /// <summary>Every move and resize of the window passes here, maximizing
    /// and restoring included, after Windows has carried it out.</summary>
    nint Watch(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        const int WM_WINDOWPOSCHANGED = 0x0047;
        if (message == WM_WINDOWPOSCHANGED) Fit();
        return 0;
    }

    /// <summary>
    /// Keeps everything on the screen while the window is maximized.
    /// Windows makes a maximized window larger than the screen and hangs
    /// its edges over; the content keeps clear of that by a margin, measured
    /// each time (WindowFrame.Fit). The window's own top edge is then above
    /// the screen, so the caption is that much taller, measured from it, to
    /// reach the bottom of the bar and no further.
    /// </summary>
    void Fit()
    {
        var (over, top) = WindowFrame.Fit(this);
        if (Root.Margin != over) Root.Margin = over;

        if (fitting || Math.Abs(chrome.CaptionHeight - (barHeight - SizingEdge + top)) < 0.01) return;
        // Not from inside Windows' own message about the move: a change to
        // the chrome makes WPF set the window's frame again, there and then.
        // Measured again when its turn comes, in case the window has moved on.
        fitting = true;
        Dispatcher.BeginInvoke(() =>
        {
            fitting = false;
            chrome.CaptionHeight = barHeight - SizingEdge + WindowFrame.Fit(this).Top;
        });
    }

    void OnMinimize(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    void OnMaximize(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }

    void OnClose(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    /// <summary>
    /// The layout button's menu, hung under the button with its right-hand
    /// edge under the button's: the button is at the end of the command
    /// bar, and a menu that began at its left would run off the window.
    /// The menu's own window is larger than the menu by the room its
    /// shadow needs (14 units at the sides, 6 above), which is taken off.
    /// </summary>
    void OnLayout(object sender, RoutedEventArgs e)
    {
        var layout = (System.Windows.Controls.ContextMenu)LayoutButton.FindResource("LayoutMenu");
        layout.DataContext = model;
        layout.PlacementTarget = LayoutButton;
        layout.Placement = PlacementMode.Custom;
        layout.CustomPopupPlacementCallback = (popup, target, _) =>
        {
            double scale = System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX;
            return [new CustomPopupPlacement(new Point(target.Width - popup.Width + 14 * scale, target.Height - 2 * scale),
                                             PopupPrimaryAxis.Vertical)];
        };
        layout.IsOpen = true;
    }

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

        model.BindHotKey();
    }

    /// <summary>
    /// Asks Windows for a chord in place of the one held, and says whether
    /// it was given. Null only lets go of the one held, which the Settings
    /// page does while it reads a new chord off the keyboard.
    /// </summary>
    bool Bind(KeyChord? chord)
    {
        hotKey?.Dispose();
        hotKey = chord is null ? null
            : HotKey.Register(this, chord, () => model.Panels.ToggleClickThroughCommand.Execute(null));
        return hotKey != null;
    }

    /// <summary>The folder the player picked, or null if they cancelled. The
    /// dialog opens at the one in use, when it is there to open at.</summary>
    string? PickFolder(string from)
    {
        var dialog = new OpenFolderDialog { Title = "The folder VibeXI writes its event files to" };
        if (Directory.Exists(from)) dialog.InitialDirectory = from;
        return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
    }

    /// <summary>The Settings page, with the window brought forward to show it.</summary>
    void ShowSettings()
    {
        ComeForward();
        model.IsSettings = true;
    }

    /// <summary>The panels that were out when Zerg was last closed. Once this
    /// window is on screen. A start leaves this window in front, as a
    /// start with no panel to bring back does; if one of the panels has
    /// been given the foreground instead, it is handed here.</summary>
    public void Reopen()
    {
        model.Panels.Reopen();
        Dispatcher.BeginInvoke(() => HandOn(0, "the start"), DispatcherPriority.ApplicationIdle);
    }

    /// <summary>What was in front when the tray menu was last asked for.</summary>
    nint beforeMenu;

    /// <summary>
    /// A panel must not be left holding the foreground. When the window of
    /// Zerg's that had it goes away, Windows hands it to another of Zerg's,
    /// and may pick a panel (seen after the tray menu closed with Zerg in
    /// front, and after a start with the game in front). If that is how
    /// things stand now, the foreground goes to <paramref name="to"/> (what
    /// had it before), or failing that to this window while it can be seen.
    /// Otherwise nothing is done: whoever has the foreground keeps it.
    /// </summary>
    void HandOn(nint to, string after)
    {
        if (!model.Panels.Holds(InFront.Window)) return;
        nint me = new WindowInteropHelper(this).Handle;
        bool back = to != 0 && to != me && !model.Panels.Holds(to) && InFront.Give(to);
        bool here = !back && IsVisible && WindowState != WindowState.Minimized && InFront.Give(me);
        Log.Write($"a panel had the foreground after {after}: " +
                  (back ? "handed back to what had it before" : here ? "handed to the main window" : "left there, with nothing to hand it to"));
    }

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
        // What the keyboard is with now (after a click on the icon, the
        // taskbar): if a panel is left with it once the menu has gone, it
        // goes back there (HandOn).
        beforeMenu = InFront.Window;
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
        Look();
        base.OnStateChanged(e);
    }

    /// <summary>Tells the view model whether this window can be seen, so the
    /// beat does not rewrite figures nobody is looking at. Back in view, they
    /// are brought up to date at once and not at the next beat.</summary>
    void Look()
    {
        bool seen = IsVisible && WindowState != WindowState.Minimized;
        if (seen == model.Seen) return;
        model.Seen = seen;
        if (seen) model.Tick();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        draw.Dispose();
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
