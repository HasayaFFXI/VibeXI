using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Zerg.Native;
using Zerg.Views;

namespace Zerg;

/// <summary>
/// A floating panel: one card in a borderless, always-on-top window that the
/// game shows through.
///
/// <para>The card inside is a second instance of the control the main window
/// docks, bound to the same view model, so it shows the same figures at the
/// same moment and nothing has to be pushed to it. While the panel is open
/// the main window collapses its own copy and shows a stand-in.</para>
/// </summary>
public partial class PanelWindow : Window
{
    /// <summary>Faded until the pointer is over the panel.</summary>
    const double Resting = 0.45;

    /// <summary>The bar, the frame and the margins round the card: what a
    /// panel adds to its card's height.</summary>
    const double Chrome = 32 + 2 + 4 + 8;

    /// <summary>How far in from an edge a press resizes the panel, and how
    /// far along an edge a corner reaches, so it is not a one-pixel target.</summary>
    const double Grip = 6, Corner = 16;

    readonly PanelInfo info;
    readonly MainViewModel model;
    readonly Settings settings;
    /// <summary>The panel is being moved or resized by the pointer.</summary>
    Overlay.Drag? drag;

    string PlacementKey => "panel:" + info.Key;

    public PanelWindow(PanelInfo info, MainViewModel model, Settings settings, Window main)
    {
        this.info = info;
        this.model = model;
        this.settings = settings;
        InitializeComponent();
        DataContext = model;
        Tools.DataContext = info;
        Title = info.Title + " · " + AppInfo.Name;

        // The cumulative chart and the actions list take whatever height the
        // panel has; the strip and the drill-down are as tall as they are,
        // and scroll when the panel is shorter.
        Body.Content = info.Key switch
        {
            PanelSet.Line => new LineCard(),
            PanelSet.Actions => new ActionsCard(),
            PanelSet.Bars => Scrolling(new BarsCard()),
            PanelSet.Drill => Scrolling(new DrillCard()),
            PanelSet.HealLine => new HealLineCard(),
            PanelSet.HealActions => new HealActionsCard(),
            PanelSet.HealBars => Scrolling(new HealBarsCard()),
            _ => Scrolling(new HealDrillCard()),
        };

        // The bar's total is the total of the side of the fight the card
        // shows. Beside a heals table, the party's damage would be a figure
        // about something else.
        if (PanelSet.IsHealing(info.Key))
        {
            BarTotal.SetBinding(TextBlock.TextProperty, new Binding(nameof(MainViewModel.HealTotalText)));
            BarTotal.ToolTip = "Total healing this session";
        }

        var (width, height) = OpeningSize();
        Width = width;
        Height = height;

        settings.Windows.TryGetValue(PlacementKey, out var saved);
        SourceInitialized += (_, _) =>
        {
            Overlay.Attach(this);
            // Now there is a handle to set it on: a panel opened while clicks
            // pass through the others is one of them from its first frame.
            Through();
            if (WindowPlacement.IsUsableFrame(saved)) WindowPlacement.ApplyFrame(this, saved!);
            // The first time, near the main window's corner, each panel a
            // step further in so several opened at once do not stack exactly.
            // A heal panel starts a little right of its damage twin's place.
            else if (WindowPlacement.CaptureFrame(main) is { } at)
            {
                int i = Array.IndexOf([PanelSet.Line, PanelSet.Bars, PanelSet.Actions, PanelSet.Drill,
                                       PanelSet.HealLine, PanelSet.HealBars, PanelSet.HealActions, PanelSet.HealDrill], info.Key);
                int step = 36 * (i % 4), across = 48 * (i / 4);
                WindowPlacement.MoveFrame(this, at.X + 60 + step + across, at.Y + 140 + step);
            }
        };

        // What reaches the bar itself, and not a button or the slider on it, moves the panel.
        Bar.MouseLeftButtonDown += (_, e) =>
        {
            Begin(Hold.Move);
            e.Handled = true;
        };
        Frame.MouseEnter += (_, _) => Tools.Opacity = BarTitle.Opacity = 1;
        Frame.MouseLeave += (_, _) => Tools.Opacity = BarTitle.Opacity = Resting;

        info.PropertyChanged += OnPanelChanged;
        model.PropertyChanged += OnModelChanged;
        model.Panels.PropertyChanged += OnSetChanged;
        Tint();
        Describe();
    }

    static ScrollViewer Scrolling(FrameworkElement card)
    {
        var viewer = new ScrollViewer
        {
            Content = card,
            Focusable = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        // The scroll bar is drawn over the content. The card makes room for
        // it only while there is one, so a panel that fits loses no width.
        viewer.ScrollChanged += (_, _) =>
        {
            double room = viewer.ComputedVerticalScrollBarVisibility == Visibility.Visible ? 14 : 0;
            if (card.Margin.Right != room) card.Margin = new Thickness(0, 0, room, 0);
        };
        return viewer;
    }

    // ------------------------------------------------------ move and resize
    //
    // By hand, through Overlay.Drag: handing the press to Windows would make
    // the panel the foreground window and take the keyboard from the game.

    /// <summary>Which edge or corner a point is on, if any.</summary>
    Hold EdgeAt(Point p)
    {
        double w = ActualWidth, h = ActualHeight;
        bool left = p.X < Grip, right = p.X >= w - Grip, top = p.Y < Grip, bottom = p.Y >= h - Grip;
        if (!(left || right || top || bottom)) return Hold.None;

        // On one edge and within a corner's reach of another, it is the corner.
        bool onSide = left || right, onEnd = top || bottom;
        var hold = Hold.None;
        if (left || (onEnd && p.X < Corner)) hold |= Hold.Left;
        else if (right || (onEnd && p.X >= w - Corner)) hold |= Hold.Right;
        if (top || (onSide && p.Y < Corner)) hold |= Hold.Top;
        else if (bottom || (onSide && p.Y >= h - Corner)) hold |= Hold.Bottom;
        return hold;
    }

    void Begin(Hold hold)
    {
        var scale = VisualTreeHelper.GetDpi(this);
        drag = new Overlay.Drag(this, hold, (int)Math.Ceiling(MinWidth * scale.DpiScaleX),
                                (int)Math.Ceiling(MinHeight * scale.DpiScaleY));
        // Without the capture nothing would say when the button came up, and
        // the panel would follow the pointer for good.
        if (!CaptureMouse()) drag = null;
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var hold = EdgeAt(e.GetPosition(this));
        if (hold != Hold.None)
        {
            Begin(hold);
            e.Handled = true;
        }
        base.OnPreviewMouseLeftButtonDown(e);
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        if (drag != null) drag.Follow();
        // Not while something else is being dragged (the slider's thumb, say):
        // the pointer crossing an edge then is not a reach for the edge.
        else if (Mouse.Captured == null)
        {
            var hold = EdgeAt(e.GetPosition(this));
            // Over whatever is underneath: a chart has a cursor of its own.
            ForceCursor = hold != Hold.None;
            Cursor = hold switch
            {
                Hold.Left or Hold.Right => Cursors.SizeWE,
                Hold.Top or Hold.Bottom => Cursors.SizeNS,
                Hold.Left | Hold.Top or Hold.Right | Hold.Bottom => Cursors.SizeNWSE,
                Hold.Left | Hold.Bottom or Hold.Right | Hold.Top => Cursors.SizeNESW,
                _ => null,
            };
        }
        base.OnPreviewMouseMove(e);
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (drag != null)
        {
            drag = null;
            ReleaseMouseCapture();
        }
        base.OnPreviewMouseLeftButtonUp(e);
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        drag = null;
        base.OnLostMouseCapture(e);
    }

    /// <summary>
    /// What a panel opens at when it has no saved frame. Each is sized for
    /// its floating form, not for the docked card, which is as wide as the
    /// main window and, for an alliance, taller than the screen.
    /// </summary>
    (double Width, double Height) OpeningSize() => info.Key switch
    {
        // A plot about 230 tall under the row with the grouping switch.
        PanelSet.Line or PanelSet.HealLine => (500, 304),
        // The strip's heading, then a row each (20 and a gap of 1), three
        // rows at least. Half a unit over per row, and a little in all: a row
        // is rounded up to whole pixels on a scaled monitor, and a panel one
        // pixel short of its rows would open with a scroll bar.
        PanelSet.Bars => (500, StripHeight(model.Strip.Count)),
        PanelSet.HealBars => (500, StripHeight(model.HealStrip.Count)),
        PanelSet.Actions or PanelSet.HealActions => (660, 420),
        _ => (600, 520),
    };

    static double StripHeight(int rows) => Chrome + 16 + 4 + 21.5 * Math.Max(rows, 3);

    /// <summary>The backdrop: black, as solid as the slider says. Only this is see-through.</summary>
    void Tint()
    {
        var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(info.Opacity * 2.55), 0, 0, 0));
        brush.Freeze();
        Frame.Background = brush;
    }

    /// <summary>
    /// The bar shows the card's heading, since the card's own is not drawn
    /// here. A drill-down's is the action it is open on, and with none
    /// picked the card hides itself, so the panel says why it is empty.
    /// </summary>
    void Describe()
    {
        var (drill, open, title) = info.Key switch
        {
            PanelSet.Drill => (true, model.DrillOpen, model.DrillTitle),
            PanelSet.HealDrill => (true, model.HealDrillOpen, model.HealDrillTitle),
            _ => (false, false, ""),
        };
        BarTitle.Text = drill && open ? title : info.Title;
        Nothing.Text = info.Key == PanelSet.HealDrill
            ? "Nothing to show yet. Select a heal in the Healing section to drill into it here."
            : "Nothing to show yet. Select an action in the main window to drill into it here.";
        Nothing.Visibility = drill && !open ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Click-through on or off, for this panel as for every other. On, the
    /// panel is told nothing about the mouse, so the bar drops everything
    /// that is there to be pressed (Start and Pause, the slider, Dock) and
    /// keeps what is there to be read: the title, the clock and the total,
    /// with a lock where the tools were.
    /// </summary>
    void Through()
    {
        bool on = model.Panels.ClickThrough;
        if (on)
        {
            // A drag under way has just lost the pointer for good, and so
            // has whatever the pointer was resting on.
            if (drag != null) ReleaseMouseCapture();
            Tools.Opacity = BarTitle.Opacity = Resting;
            ForceCursor = false;
            Cursor = null;
        }
        Overlay.ClickThrough(this, on);
        Tools.Visibility = Pair.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        Locked.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    }

    void OnPanelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PanelInfo.Opacity)) Tint();
    }

    void OnSetChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PanelSet.ClickThrough)) Through();
    }

    void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.DrillOpen) or nameof(MainViewModel.DrillTitle)
                           or nameof(MainViewModel.HealDrillOpen) or nameof(MainViewModel.HealDrillTitle)) Describe();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (WindowPlacement.CaptureFrame(this) is { } frame)
        {
            settings.Windows[PlacementKey] = frame;
            settings.Save();
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        // The view model outlives the panel and would keep it alive.
        info.PropertyChanged -= OnPanelChanged;
        model.PropertyChanged -= OnModelChanged;
        model.Panels.PropertyChanged -= OnSetChanged;
        base.OnClosed(e);
    }
}
