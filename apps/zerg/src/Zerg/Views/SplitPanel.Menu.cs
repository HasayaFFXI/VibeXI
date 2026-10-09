using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Zerg.Core.Layout;

namespace Zerg.Views;

// A heading's own menu: everything a pane's heading can do with the
// pointer, without one. Fold (or Unfold), Pop out where the card can float
// (Bring back, on a stand-in), Move by one place to a side, and the
// section's arrangement set back. It opens from the grip (a press, or
// Enter or Space with the keyboard on it), from a right-click anywhere on
// a heading, and from the menu key or Shift+F10 with the keyboard on one of
// a heading's controls.
//
// It is made each time it is asked for, and belongs to the main window. A
// floating panel has no such thing: a card there has no heading, and a
// menu would take the keyboard from the game.
public partial class SplitPanel
{
    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        if (e.Handled || move != null || hold != null) return;
        var (child, key, handle) = Whose(e.OriginalSource);
        if (child is null || key is null || !handle) return;
        e.Handled = true;
        OpenMenu(key, child, null);
    }

    // Shift+F10 asks on its way down. The menu key asks on its way up, as
    // it does everywhere in Windows: a menu that is open when that key
    // comes up shuts itself, so one opened as the key went down was gone
    // before it was seen (found with the real key: Shift+F10 opened the
    // menu and the menu key opened nothing).
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        bool asked = (e.SystemKey == Key.F10 || e.Key == Key.F10) && Keyboard.Modifiers == ModifierKeys.Shift;
        if (asked) AskMenu(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (!e.Handled && e.Key == Key.Apps) AskMenu(e);
    }

    void AskMenu(KeyEventArgs e)
    {
        if (move != null || hold != null) return;
        var (child, key, handle) = Whose(e.OriginalSource);
        if (child is null || key is null || !handle) return;
        e.Handled = true;
        OpenMenu(key, child, e.OriginalSource as UIElement);
    }

    /// <summary>What stands in for a card that is floating, if this pane's is.</summary>
    static Away? AwayIn(DependencyObject child)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(child); i++)
            if (VisualTreeHelper.GetChild(child, i) is Away { IsVisible: true } away) return away;
        return null;
    }

    /// <summary>
    /// The menu of one pane's heading.
    /// </summary>
    /// <param name="at">What it opens under: the grip, or whichever of the
    /// heading's controls had the keyboard. Null opens it at the pointer.</param>
    void OpenMenu(string key, UIElement child, UIElement? at)
    {
        if (Tree is not { } tree || SplitTree.Leaf(tree, key) is not { } leaf) return;
        var menu = new ContextMenu();
        var away = AwayIn(child);
        var pane = PaneIn(child);

        if (away != null)
        {
            // The card is out: what is here is its place, and the way back.
            var back = new MenuItem { Header = "Bring back" };
            back.Click += (_, _) => (away.DataContext as PanelInfo)?.DockCommand.Execute(null);
            menu.Items.Add(back);
        }
        else
        {
            // A pane that has folded itself (a drill-down with nothing
            // picked) has nothing to open onto.
            var fold = new MenuItem { Header = leaf.Folded ? "Unfold" : "Fold", IsEnabled = pane is not { IsFolded: true } };
            fold.Click += (_, _) => Fold(key);
            menu.Items.Add(fold);
            if (pane?.PopOutCommand is { } popOut)
            {
                var glyph = new TextBlock { Text = ((char)0xE8A7).ToString(), FontSize = 11, TextAlignment = TextAlignment.Center };
                glyph.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
                var item = new MenuItem { Header = "Pop out", Icon = glyph };
                item.Click += (_, _) =>
                {
                    if (popOut.CanExecute(null)) popOut.Execute(null);
                };
                menu.Items.Add(item);
            }
        }

        // One place to a side: the pane changes places with its neighbour
        // there. At an edge of the section there is none.
        var moving = new MenuItem { Header = "Move", IsEnabled = CanMove };
        foreach (var (side, word) in new[] { (PaneSide.Left, "Left"), (PaneSide.Right, "Right"), (PaneSide.Above, "Up"), (PaneSide.Below, "Down") })
        {
            string? other = SplitTree.Neighbour(tree, key, side);
            var to = new MenuItem { Header = word, IsEnabled = other != null };
            if (other != null)
                to.Click += (_, _) =>
                {
                    if (Tree is { } now && CanMove)
                        Commit(SplitTree.MoveBy(now, key, side), $"{TitleOf(key)} moved {word.ToLowerInvariant()}, changing places with {TitleOf(other)}");
                };
            moving.Items.Add(to);
        }
        menu.Items.Add(moving);

        menu.Items.Add(new Separator());
        var reset = new MenuItem { Header = $"Reset {Section} layout", IsEnabled = PaneLayouts.Installed(Section) is { } installed && installed != tree };
        reset.Click += (_, _) =>
        {
            if (PaneLayouts.Installed(Section) is { } to) Commit(to, "set back to the installed arrangement");
        };
        menu.Items.Add(reset);

        menu.PlacementTarget = at ?? child;
        menu.Placement = at != null ? PlacementMode.Bottom : PlacementMode.MousePoint;
        menu.IsOpen = true;
    }
}
