using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Zerg.Core.Layout;
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
///
/// <para><b>The bar is figures at rest</b>: the pair, the clock, the total
/// and, quiet at the right, the card's name. The opacity slider and Dock are
/// there only while the pointer is over the panel (<see cref="Chrome"/>).</para>
/// </summary>
public partial class PanelWindow : Window
{
    /// <summary>How far in from an edge a press resizes the panel, and how
    /// far along an edge a corner reaches, so it is not a one-pixel target.
    /// From the top a unit less: the pair begins 5 units down the bar, and
    /// a press on the top of Start is a press on Start.</summary>
    const double Grip = 6, TopGrip = 5, Corner = 16;

    /// <summary>The tools at their full length (the slider, what it stands
    /// at, Dock), and what is kept clear between them and whatever is left
    /// of them on the bar.</summary>
    const double ToolsWidth = 125, ToolsGap = 12;

    /// <summary>The tools with no slider at all: what it stands at, and Dock.</summary>
    const double ToolsLeast = 61;

    /// <summary>Where the title ends while clicks pass through: left of the lock.</summary>
    const double LockRoom = 22;

    readonly PanelInfo info;
    readonly MainViewModel model;
    readonly Settings settings;
    /// <summary>The panel is being moved or resized by the pointer.</summary>
    Overlay.Drag? drag;
    /// <summary>The pointer is over the panel.</summary>
    bool over;
    /// <summary>The tools are showing: what <see cref="Chrome"/> last made of
    /// the bar, so that it sets a brush only when that changes.</summary>
    bool shown;

    string PlacementKey => "panel:" + info.Key;

    /// <summary>The two panels that hold a cumulative chart, whose lines
    /// are drawn again at the draw frequency while a session counts.</summary>
    bool IsChart => info.Key is PanelSet.Line or PanelSet.HealLine;

    /// <summary>The two that hold a strip.</summary>
    bool IsStrip => info.Key is PanelSet.Bars or PanelSet.HealBars;

    public PanelWindow(PanelInfo info, MainViewModel model, Settings settings, Window main)
    {
        this.info = info;
        this.model = model;
        this.settings = settings;
        InitializeComponent();
        DataContext = model;
        Tools.DataContext = info;
        Title = info.Title + " · " + AppInfo.Name;

        // The bar shows the card's heading, since the card's own is not drawn
        // here; and after it, on a damage card, the targets and the damage
        // types its figures are isolated to (" · Kirin", " · Melee"), in the
        // accent: a panel is read over the game, where the main window's
        // Target and Type buttons cannot be seen.
        BarTitle.Inlines.Add(new Run(info.Title));
        if (!PanelSet.IsHealing(info.Key))
            foreach (var filter in new[] { nameof(MainViewModel.TargetFilter), nameof(MainViewModel.TypeFilter) })
            {
                var scope = new Run();
                scope.SetBinding(Run.TextProperty, new Binding(filter + "." + nameof(PickFilter.Scope)) { Mode = BindingMode.OneWay });
                scope.SetResourceReference(TextElement.ForegroundProperty, "AccentBrush");
                BarTitle.Inlines.Add(scope);
            }

        // The cumulative chart and the actions list take whatever height the
        // panel has; the strip is as tall as it is, and scrolls when the
        // panel is shorter.
        Body.Content = info.Key switch
        {
            PanelSet.Line => new LineCard(),
            PanelSet.Actions => new ActionsCard(),
            PanelSet.Bars => Scrolling(new BarsCard()),
            // The two side tables are one card, told which filter it lists.
            PanelSet.Types => new TallyCard { Filter = model.TypeFilter, Info = info },
            PanelSet.Targets => new TallyCard { Filter = model.TargetFilter, Info = info },
            PanelSet.HealLine => new HealLineCard(),
            PanelSet.HealActions => new HealActionsCard(),
            _ => Scrolling(new HealBarsCard()),
        };
        Body.Margin = Margins();
        Halo();

        // The bar's total is the total of the side of the fight the card
        // shows. Beside a heals table, the party's damage would be a figure
        // about something else.
        if (PanelSet.IsHealing(info.Key))
        {
            BarTotal.SetBinding(TextBlock.TextProperty, new Binding(nameof(MainViewModel.HealTotalText)));
            BarTotal.ToolTip = "Total healing this session";
        }

        var (width, height) = OpeningSize(main);
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
            // A heal panel starts a little right of its damage twin's place,
            // and the two side tables a little right of those.
            else if (WindowPlacement.CaptureFrame(main) is { } at)
            {
                int i = Array.IndexOf([PanelSet.Line, PanelSet.Bars, PanelSet.Actions,
                                       PanelSet.HealLine, PanelSet.HealBars, PanelSet.HealActions,
                                       PanelSet.Types, PanelSet.Targets], info.Key);
                int step = 36 * (i % 3), across = 48 * (i / 3);
                WindowPlacement.MoveFrame(this, at.X + 60 + step + across, at.Y + 140 + step);
            }
        };

        // What reaches the bar itself, and not a button or the slider on it, moves the panel.
        Bar.MouseLeftButtonDown += (_, e) =>
        {
            Begin(Hold.Move);
            e.Handled = true;
        };
        Frame.MouseEnter += (_, _) =>
        {
            over = true;
            Chrome();
        };
        Frame.MouseLeave += (_, _) =>
        {
            over = false;
            Chrome();
        };
        // The room the tools have is the bar's, less what stands at its
        // left: when either changes under the pointer (the panel is being
        // resized, the clock reaches an hour), the tools are fitted again.
        Line.SizeChanged += (_, _) => Chrome();
        Lead.SizeChanged += (_, _) => Chrome();

        info.PropertyChanged += OnPanelChanged;
        model.Panels.PropertyChanged += OnSetChanged;
        Tint();
        Chrome();
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

    /// <summary>
    /// How far the card stands from the frame. A strip has none at its
    /// sides (its rows keep 3 units of their own, where the owner's mark
    /// is drawn) and the foot the design draws; a table lies edge to edge,
    /// as it does in its pane, with its line of headings 26 units as the
    /// design has it there, and stops short of the frame's round corners;
    /// a chart keeps the margin it had.
    /// </summary>
    Thickness Margins() =>
        IsStrip ? new Thickness(0, 0, 0, PanelFit.StripFoot)
        : IsChart ? new Thickness(6, 4, 6, 6)
        : new Thickness(0, 2, 0, 6);

    /// <summary>
    /// The halo round the card's text (<c>PanelHalo</c> in <c>App.xaml</c>
    /// says what it is and what it may be set on): one effect on the whole
    /// card, in the six panels where nothing moves with the clock. A strip
    /// and a table are drawn again when an event arrives and at no other
    /// time, and their bars do not ease in a panel (<c>Views/Grow</c>), so
    /// the effect is worked out once per count. A cumulative chart's lines
    /// are drawn at the draw frequency: no effect lies over one, and the
    /// chart outlines its own labels.
    /// </summary>
    void Halo()
    {
        if (!IsChart) Body.Effect = (Effect)FindResource("PanelHalo");
    }

    // ------------------------------------------------------ move and resize
    //
    // By hand, through Overlay.Drag: handing the press to Windows would make
    // the panel the foreground window and take the keyboard from the game.

    /// <summary>Which edge or corner a point is on, if any.</summary>
    Hold EdgeAt(Point p)
    {
        double w = ActualWidth, h = ActualHeight;
        bool left = p.X < Grip, right = p.X >= w - Grip, top = p.Y < TopGrip, bottom = p.Y >= h - Grip;
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

    /// <summary>
    /// The edge a press or the pointer at <paramref name="e"/> has hold of.
    /// None over a scroll bar's thumb: a table lies edge to edge in its
    /// panel, so its scroll bar is inside the strip that sizes the panel,
    /// and the thumb is what was reached for.
    /// </summary>
    Hold EdgeUnder(MouseEventArgs e)
    {
        for (var d = e.OriginalSource as DependencyObject; d != null && d != this;
             d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is Thumb) return Hold.None;
        return EdgeAt(e.GetPosition(this));
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
        var hold = EdgeUnder(e);
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
            var hold = EdgeUnder(e);
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
    (double Width, double Height) OpeningSize(Window main)
    {
        // A strip is as tall as its rows, to the pixel, on the screen it
        // opens on, which is the main window's: every row is laid out on
        // whole pixels, and a panel a pixel short of its rows would open
        // with a scroll bar (Zerg.Core's PanelFit has the arithmetic).
        double scale = VisualTreeHelper.GetDpi(main).DpiScaleY;
        return info.Key switch
        {
            // A plot about 230 tall under the row with the grouping switch.
            PanelSet.Line or PanelSet.HealLine => (500, 300),
            // The design's width; the headings, then a row each, three rows at least.
            PanelSet.Bars => (440, PanelFit.StripHeight(model.Strip.Count, scale)),
            PanelSet.HealBars => (440, PanelFit.StripHeight(model.HealStrip.Count, scale)),
            // A side table: three columns, and room for eight rows under
            // the bar and the headings.
            PanelSet.Types or PanelSet.Targets => (360, 250),
            // Room for a few rows and the drill-down that opens under one.
            _ => (660, 520),
        };
    }

    /// <summary>The backdrop: black, as solid as the slider says. Only this is see-through.</summary>
    void Tint()
    {
        var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(info.Opacity * 2.55), 0, 0, 0));
        brush.Freeze();
        Frame.Background = brush;
    }

    /// <summary>
    /// What the bar shows beside its figures, which depends on where the
    /// pointer is. At rest: the card's name at the right, and nothing to
    /// press but the pair. With the pointer over the panel: the opacity
    /// slider and Dock at the right, the frame's edge a step stronger, and
    /// the name moved left of the tools and a step brighter while the panel
    /// is wide enough for both (<see cref="PanelFit.TitleStays"/>). In a
    /// panel too narrow for the tools beside the clock and the total, those
    /// two give the tools their place for as long as the pointer is there;
    /// the pair never moves and never goes.
    ///
    /// <para>The tools are switched, not faded, and while they are not
    /// shown they cannot be pressed: they stay in the tree at no opacity,
    /// so a script finds them by name, and are taken out of the pointer's
    /// way. While clicks pass through there are none
    /// (<see cref="Through"/>).</para>
    /// </summary>
    void Chrome()
    {
        bool through = model.Panels.ClickThrough, show = over && !through;
        if (show != shown)
        {
            shown = show;
            Tools.Opacity = show ? 1 : 0;
            Tools.IsHitTestVisible = show;
            Frame.SetResourceReference(Border.BorderBrushProperty, show ? "Line3Brush" : "Line2Brush");
            BarTitle.SetResourceReference(TextBlock.ForegroundProperty, show ? "Text2Brush" : "Text3Brush");
        }

        // The room right of the clock and the total, and right of the pair alone.
        double line = Line.ActualWidth, beside = line - Lead.ActualWidth, alone = line - Pair.ActualWidth;
        bool fits = !show || beside >= ToolsWidth + ToolsGap;
        Readouts.Visibility = fits ? Visibility.Visible : Visibility.Hidden;
        double tools = fits ? ToolsWidth : Math.Clamp(alone - ToolsGap, ToolsLeast, ToolsWidth);
        if (Tools.Width != tools) Tools.Width = tools;

        // The frame is the whole panel: as wide as the window.
        bool named = !show || (fits && PanelFit.TitleStays(Frame.ActualWidth));
        BarTitle.Visibility = named ? Visibility.Visible : Visibility.Hidden;
        var margin = new Thickness(ToolsGap, 0, through ? LockRoom : show ? ToolsWidth + ToolsGap : 0, 0);
        if (BarTitle.Margin != margin) BarTitle.Margin = margin;
    }

    /// <summary>
    /// Click-through on or off, for this panel as for every other. On, the
    /// panel is told nothing about the mouse, so the bar drops everything
    /// that is there to be pressed (Start and Pause, the slider, Dock) and
    /// keeps what is there to be read: the clock, the total and the title,
    /// with a lock at the bar's end.
    /// </summary>
    void Through()
    {
        bool on = model.Panels.ClickThrough;
        if (on)
        {
            // A drag under way has just lost the pointer for good, and so
            // has whatever the pointer was resting on.
            if (drag != null) ReleaseMouseCapture();
            ForceCursor = false;
            Cursor = null;
        }
        // Switched on, the pointer is no longer the panel's to know about;
        // switched off, it may be resting on the panel already.
        over = !on && Frame.IsMouseOver;
        Overlay.ClickThrough(this, on);
        Tools.Visibility = Pair.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        Locked.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        Chrome();
    }

    void OnPanelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PanelInfo.Opacity)) Tint();
    }

    void OnSetChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PanelSet.ClickThrough)) Through();
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
        model.Panels.PropertyChanged -= OnSetChanged;
        base.OnClosed(e);
    }
}
