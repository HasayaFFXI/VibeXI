using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Zerg.Core.Layout;

namespace Zerg.Views;

/// <summary>
/// The grip at the left of a pane's heading, and of a stand-in: six dots.
/// It is what says "this can be moved", and it is the one part of a
/// heading that takes the keyboard for the heading's own sake.
///
/// <para>A button that does not keep a press to itself. Dragged, it moves
/// the pane, as the rest of the heading does: the press goes on up to the
/// <see cref="SplitPanel"/>, which decides between a drag and a click.
/// Pressed and let go (or Enter or Space with the keyboard on it, or
/// invoked by a script), it opens the heading's menu: fold, pop out, move
/// by one place, set the arrangement back. So everything a drag does can
/// be done from here without one.</para>
///
/// <para>The look is the <c>PaneGrip</c> style in <c>Themes/Controls.xaml</c>.</para>
/// </summary>
public sealed class PaneGrip : Button
{
    // Not ButtonBase's: that takes the mouse and marks the press handled,
    // and the heading would never hear of a drag that began on its grip.
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) { }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { }
}

// A pane carried by its heading: the press that becomes a move, where the
// pane can be put down, and what putting it down does. Also the other
// things a heading asks of the panel: the fold mark, and the grip's menu
// (SplitPanel.Menu.cs).
public partial class SplitPanel
{
    /// <summary>How far a press has to travel before it is a move, in units.</summary>
    const double Slack = 4;

    /// <summary>A press on a heading that has not travelled yet.</summary>
    sealed record Press(string Key, UIElement Child, Point At, PaneGrip? Grip);

    /// <summary>A pane in the hand.</summary>
    sealed record Move(string Key, string Title, Arrangement Panes, PaneDrop Drop);

    Press? press;
    Move? move;

    void Listen()
    {
        // A click that reaches the panel from a heading's own controls.
        AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnHeadingClick));
        Focusable = false;
    }

    /// <summary>The child that is a pane of the tree and holds this element,
    /// with its key; and whether the element is on the part the pane is
    /// carried by.</summary>
    (UIElement? Child, string? Key, bool Handle) Whose(object? source)
    {
        bool handle = false;
        for (var at = source as DependencyObject; at != null; at = Parent(at))
        {
            if (at is UIElement element && ReferenceEquals(VisualTreeHelper.GetParent(element), this))
                return GetKey(element) is { } key ? (element, key, handle) : (null, null, false);
            if (GetHandle(at)) handle = true;
        }
        return (null, null, false);

        static DependencyObject? Parent(DependencyObject d) =>
            d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
    }

    /// <summary>Panes can be carried just now: not locked, not stacked, and
    /// more than one of them.</summary>
    bool CanMove => !GetLocked(this) && !InColumn && arranged is { Panes.Count: > 1 };

    // ---------------------------------------------------- the heading's controls

    void OnHeadingClick(object sender, RoutedEventArgs e)
    {
        var (child, key, _) = Whose(e.OriginalSource);
        if (child is null || key is null) return;
        if (e.OriginalSource is PaneGrip grip)
        {
            e.Handled = true;
            OpenMenu(key, child, grip);
        }
        else if (e.OriginalSource is Button { Name: "Fold", TemplatedParent: Pane })
        {
            e.Handled = true;
            Fold(key);
        }
    }

    /// <summary>The pane folded if it was open, opened if it was folded.</summary>
    void Fold(string key)
    {
        if (Tree is not { } tree || SplitTree.Leaf(tree, key) is not { } leaf) return;
        Commit(SplitTree.Fold(tree, key, !leaf.Folded), TitleOf(key) + (leaf.Folded ? " opened" : " folded"));
    }

    // -------------------------------------------------------------- the press

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.Handled || move != null || hold != null) return;
        var (child, key, handle) = Whose(e.OriginalSource);
        if (child is null || key is null || !handle) return;
        var grip = Up<PaneGrip>(e.OriginalSource as DependencyObject, child);
        // A heading that cannot be moved just now still has a grip to press.
        if (!CanMove && grip is null) return;
        if (!CaptureMouse()) return;
        press = new Press(key, child, e.GetPosition(this), grip);
        e.Handled = true;
    }

    static T? Up<T>(DependencyObject? from, DependencyObject until) where T : class
    {
        for (var at = from; at != null && !ReferenceEquals(at, until); at = at is Visual ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
            if (at is T found) return found;
        return null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (move is null && press is { } pressed)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                PutDown(cancel: true);
                return;
            }
            var by = e.GetPosition(this) - pressed.At;
            if (Math.Abs(by.X) <= Slack && Math.Abs(by.Y) <= Slack) return;
            if (!CanMove) return;
            Lift(pressed, e.GetPosition(this));
        }
        if (move != null) Carry(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (press is null) return;
        e.Handled = true;
        if (move != null)
        {
            Carry(e.GetPosition(this));
            PutDown(cancel: false);
            return;
        }
        // Pressed and let go where it was: on the grip, that is a click.
        var pressed = press;
        PutDown(cancel: true);
        if (pressed.Grip is { } grip) OpenMenu(pressed.Key, pressed.Child, grip);
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        // Taken away (another window came forward, a dialog opened): put back.
        if (press != null) PutDown(cancel: true);
    }

    // --------------------------------------------------------------- the move

    /// <summary>The press has travelled: the pane is in the hand.</summary>
    void Lift(Press pressed, Point pointer)
    {
        if (arranged is not { } panes || !panes.Panes.TryGetValue(pressed.Key, out var box)) return;
        string title = TitleOf(pressed.Key);
        move = new Move(pressed.Key, title, panes, PaneDrop.Nowhere);
        Cursor = Cursors.SizeAll;
        ForceCursor = true;
        WatchEsc(true);
        Note($"Moving {title} · Esc cancels");
        overlay.Carry(new Carried(title, new Rect(box.X, box.Y, box.Width, box.Height), Sketch(pressed.Child)), pointer);
    }

    /// <summary>The pane is somewhere else: which place is under it.</summary>
    void Carry(Point pointer)
    {
        if (move is not { } moving) return;
        var drop = PaneDrops.At(moving.Panes, moving.Key, pointer.X, pointer.Y, ActualWidth, ActualHeight, HeadingHeight);
        Under? under = null;
        if (drop.Target is { } target && moving.Panes.Panes.TryGetValue(target, out var box))
            under = new Under(TitleOf(target), new Rect(box.X, box.Y, box.Width, box.Height), PaneDrops.Zones(box, HeadingHeight));
        move = moving with { Drop = drop };
        overlay.Over(under, drop, pointer);
    }

    /// <summary>The pane is put down where it is, or put back.</summary>
    void PutDown(bool cancel)
    {
        var moving = move;
        bool pressed = press != null;
        press = null;
        move = null;
        if (pressed && IsMouseCaptured) ReleaseMouseCapture();
        if (moving is null) return;
        ClearValue(CursorProperty);
        ClearValue(ForceCursorProperty);
        WatchEsc(false);
        overlay.Clear();
        Note("");
        if (cancel || moving.Drop.Kind == DropKind.None || Tree is not { } tree) return;
        string where = moving.Drop.Kind switch
        {
            DropKind.Swap => "changed places with " + TitleOf(moving.Drop.Target!),
            DropKind.Beside => "put " + Words(moving.Drop.Side) + " " + TitleOf(moving.Drop.Target!),
            _ => "put along the section's " + moving.Drop.Side switch
            {
                PaneSide.Above => "top",
                PaneSide.Below => "bottom",
                PaneSide.Left => "left",
                _ => "right",
            },
        };
        Commit(PaneDrops.Apply(tree, moving.Key, moving.Drop), moving.Title + " " + where);
    }

    static string Words(PaneSide side) => side switch
    {
        PaneSide.Above => "above",
        PaneSide.Below => "below",
        PaneSide.Left => "left of",
        _ => "right of",
    };

    /// <summary>
    /// A picture of a pane as it stands, for the card that is carried: the
    /// top of what is under its heading, in the card's proportions. Taken
    /// once, as the pane is picked up, so a chart that goes on drawing
    /// itself is not drawn a second time for as long as the pane is in the
    /// hand.
    ///
    /// <para>The pane is drawn where it stands and the picture cut out of
    /// that: a bitmap draws an element at its own place in its parent, not
    /// at the bitmap's corner. (A VisualBrush with a viewbox said in the
    /// pane's own units, which would need no cutting, drew nothing: off
    /// screen, 0 pixels of 34,596.)</para>
    /// </summary>
    ImageSource? Sketch(UIElement child)
    {
        const double width = 248, height = 62;
        var size = child.RenderSize;
        if (size.Width < 1 || size.Height < 1) return null;
        double heading = size.Height > HeadingHeight + 20 ? HeadingHeight : 0;
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var at = VisualTreeHelper.GetOffset(child);
        int wide = (int)Math.Ceiling((at.X + size.Width) * scale), tall = (int)Math.Ceiling((at.Y + size.Height) * scale);
        if (wide < 1 || tall < 1 || at.X < 0 || at.Y < 0) return null;
        var whole = new RenderTargetBitmap(wide, tall, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        whole.Render(child);

        // As much of it, from under the heading down, as the card has room for.
        double take = Math.Min(size.Height - heading, size.Width * height / width);
        int x = (int)Math.Round(at.X * scale), y = (int)Math.Round((at.Y + heading) * scale);
        int w = Math.Min((int)Math.Floor(size.Width * scale), wide - x), h = Math.Min((int)Math.Floor(take * scale), tall - y);
        if (w < 1 || h < 1) return null;
        var cut = new CroppedBitmap(whole, new Int32Rect(x, y, w, h));
        cut.Freeze();
        return cut;
    }

    // ----------------------------------------------------------------- Esc

    UIElement? watched;

    /// <summary>
    /// While something is in the hand, Esc puts it back, wherever in the
    /// window the keyboard is: the key is listened for at the window's
    /// root, for as long as the hand is full and no longer.
    /// </summary>
    void WatchEsc(bool on)
    {
        if (watched != null)
        {
            watched.RemoveHandler(Keyboard.PreviewKeyDownEvent, (KeyEventHandler)OnEsc);
            watched = null;
        }
        if (!on || PresentationSource.FromVisual(this)?.RootVisual is not UIElement root) return;
        watched = root;
        root.AddHandler(Keyboard.PreviewKeyDownEvent, (KeyEventHandler)OnEsc, handledEventsToo: true);
    }

    void OnEsc(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (Cancel()) e.Handled = true;
    }

    /// <summary>Whatever is in the hand is put back: a pane to its place, a
    /// rule to where it was. Says whether anything was.</summary>
    internal bool Cancel()
    {
        if (move != null || press != null)
        {
            PutDown(cancel: true);
            return true;
        }
        if (hold is { } held)
        {
            foreach (var thumb in thumbs)
                if (thumb.Path == held.Path && thumb.IsDragging)
                {
                    // Its DragCompleted, cancelled, drops what was shown.
                    thumb.CancelDrag();
                    return true;
                }
        }
        return false;
    }
}
