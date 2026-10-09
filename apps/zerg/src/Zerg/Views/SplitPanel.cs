using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Zerg.Core.Layout;

namespace Zerg.Views;

/// <summary>What a <see cref="SplitPanel"/> says when a player has made
/// its panes another arrangement.</summary>
public sealed class RearrangedEventArgs(RoutedEvent routed, object source, string section, SplitNode tree, string what)
    : RoutedEventArgs(routed, source)
{
    /// <summary>Whose panes: "Damage", "Healing" or "Compare".</summary>
    public string Section { get; } = section;
    /// <summary>The arrangement asked for.</summary>
    public SplitNode Tree { get; } = tree;
    /// <summary>What was done, in a few words, for the log.</summary>
    public string What { get; } = what;
}

/// <summary>What a <see cref="SplitPanel"/> has to say on the status line
/// while its arrangement is in someone's hand; empty when it is not.</summary>
public sealed class NotedEventArgs(RoutedEvent routed, object source, string note) : RoutedEventArgs(routed, source)
{
    public string Note { get; } = note;
}

/// <summary>
/// A section's panes, arranged as a tree of splits says
/// (<see cref="Tree"/>; <see cref="SplitTree"/> does the arithmetic). Each
/// child carries the key of the pane it is (<see cref="KeyProperty"/>) and
/// is put in that pane's rectangle; the rule of each split is painted
/// between them.
///
/// <para><b>Children are never taken out and put back.</b> A new
/// arrangement only changes the rectangles they are arranged in, so a
/// chart is not built again, a list keeps its rows and where it was
/// scrolled to, and what UI Automation was told about them stays true.
/// That is why this is a panel of its own and not grids inside grids. It
/// holds for everything a player can do here: a rule dragged, a pane
/// folded, a pane carried somewhere else.</para>
///
/// <para><b>A pane with a size of its own.</b> A child says so:
/// <see cref="FixedProperty"/> is a height (the short stand-in of a card
/// that is floating), and <see cref="SelfFoldedProperty"/> says it is
/// folded for a reason of its own (a drill-down with nothing picked). A
/// pane the tree itself says is folded is told
/// (<see cref="FoldedProperty"/>, which everything inside the child
/// inherits), and a <see cref="Pane"/> in it folds. Folded, a pane is as
/// tall as a heading and its neighbour in the column takes the rest; alone
/// in its column, it folds sideways to a rail (<see cref="RailProperty"/>).</para>
///
/// <para><b>The arrangement is the player's.</b> Over each rule lies a
/// thumb (<see cref="DividerThumb"/>; <c>SplitPanel.Dividers.cs</c>): it
/// is dragged, set back by a double-click, and moved with the arrow keys.
/// A pane's heading is the handle it is carried by
/// (<see cref="HandleProperty"/>; <c>PaneDrag.cs</c>), the mark in the
/// heading folds it, and the grip opens the heading's menu
/// (<c>SplitPanel.Menu.cs</c>). The thumbs and what is drawn over the panes
/// while one is carried (<see cref="LayoutOverlay"/>) are visual children
/// of the panel that are not in <see cref="Panel.Children"/>. Whatever a
/// player does, the panel works out the tree that makes
/// (<c>Zerg.Core/Layout</c>) and says so (<see cref="RearrangedEvent"/>):
/// whoever holds the tree puts the new one there, and the panel follows
/// its binding. While a rule is in the hand the panel arranges by a tree
/// of its own (<see cref="preview"/>) and says nothing until it is let go.
/// <see cref="LockedProperty"/> stops every drag.</para>
///
/// <para><b>The Tab key, and a screen reader, go through the panes in the
/// order they stand in</b>, not the order they are written in: the tree's
/// reading order (down the first column, then down the next), which is
/// also the order of the one column of a narrow window. A child is still
/// never moved in <see cref="Panel.Children"/>: each is given its place in
/// that order as its <see cref="Panel.ZIndexProperty"/>, which is the
/// order WPF hands a panel's children out in to whatever walks them (the
/// Tab key, UI Automation, painting; the panes do not overlap, so what
/// is painted is the same). The rules' thumbs come after all the panes.</para>
///
/// <para><b>Stacked</b> (<see cref="StackedProperty"/>, set from above by
/// <see cref="PageStack"/> in a window too narrow for the panes side by
/// side), the tree is not used: the panes stand in one column in reading
/// order, each as tall as it asks to be, and the page scrolls. Nothing is
/// dragged there; a pane still folds. The tree is untouched and comes back
/// with the width.</para>
///
/// <para>Given less height than its panes' least sizes need, the panel
/// says what they need (<see cref="PageStack.NeedsProperty"/>), and the
/// page it is in gives it that and scrolls; it says how wide they need to
/// be too (<see cref="PageStack.NeedsWidthProperty"/>), which is how the
/// page knows a window is too narrow for an arrangement of three columns
/// that is wide enough for the installed two.</para>
///
/// <para>The Damage and Healing sections' panels have their panes written
/// into them in the main window. The Compare section's is a class of its
/// own (<see cref="ComparePanes"/>), with its four panes in its own file.</para>
/// </summary>
public partial class SplitPanel : Panel
{
    /// <summary>On a child: which pane of the tree it is.</summary>
    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached("Key", typeof(string),
        typeof(SplitPanel), new FrameworkPropertyMetadata(null, OnChildChanged));

    /// <summary>On a child: the height it has of its own just now, in
    /// units; not a number (the default) while it takes its share.</summary>
    public static readonly DependencyProperty FixedProperty = DependencyProperty.RegisterAttached("Fixed", typeof(double),
        typeof(SplitPanel), new FrameworkPropertyMetadata(double.NaN, OnChildChanged));

    /// <summary>On a child: the pane has folded itself (a drill-down with
    /// nothing picked), and is arranged as one the tree says is folded.</summary>
    public static readonly DependencyProperty SelfFoldedProperty = DependencyProperty.RegisterAttached("SelfFolded", typeof(bool),
        typeof(SplitPanel), new FrameworkPropertyMetadata(false, OnChildChanged));

    /// <summary>On a child and everything in it: the tree says this pane
    /// is folded. The panel sets it.</summary>
    public static readonly DependencyProperty FoldedProperty = DependencyProperty.RegisterAttached("Folded", typeof(bool),
        typeof(SplitPanel), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>On a child and everything in it: the pane is folded
    /// sideways, to a rail a heading wide with its title reading down. The
    /// panel sets it.</summary>
    public static readonly DependencyProperty RailProperty = DependencyProperty.RegisterAttached("Rail", typeof(bool),
        typeof(SplitPanel), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>On a child and everything in it: what the pane is called
    /// wherever a name that does not change is wanted (a rule's name, the
    /// fold mark's, "Cumulative damage is being moved"). A drill-down's
    /// heading reads the action it is open on; its name is "Drill-down".
    /// Without one, the title of the <see cref="Pane"/> the child is.</summary>
    public static readonly DependencyProperty TitleProperty = DependencyProperty.RegisterAttached("Title", typeof(string),
        typeof(SplitPanel), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>On the part of a pane it is carried by: its heading, or
    /// the whole of a stand-in. A press there that travels becomes a move.</summary>
    public static readonly DependencyProperty HandleProperty = DependencyProperty.RegisterAttached("Handle", typeof(bool),
        typeof(SplitPanel), new FrameworkPropertyMetadata(false));

    /// <summary>On the page and everything in it: the window is too narrow
    /// for two columns, and panes stand one under another at their own
    /// heights. A card asks this to know whether it fills a pane or is as
    /// tall as it likes. <see cref="PageStack"/> sets it.</summary>
    public static readonly DependencyProperty StackedProperty = DependencyProperty.RegisterAttached("Stacked", typeof(bool),
        typeof(SplitPanel), new FrameworkPropertyMetadata(false,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>On the page and everything in it: the arrangement is
    /// locked. No rule has a thumb, no heading starts a move, and a
    /// heading's grip is not drawn. The main window binds it.</summary>
    public static readonly DependencyProperty LockedProperty = DependencyProperty.RegisterAttached("Locked", typeof(bool),
        typeof(SplitPanel), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits, (d, _) =>
        {
            if (d is SplitPanel panel) panel.InvalidateMeasure();
        }));

    public static readonly DependencyProperty TreeProperty = DependencyProperty.Register(nameof(Tree), typeof(SplitNode),
        typeof(SplitPanel), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure,
            (d, _) => ((SplitPanel)d).OnTreeChanged()));

    /// <summary>Whose panes these are, as the arrangement is kept:
    /// "Damage", "Healing" or "Compare".</summary>
    public static readonly DependencyProperty SectionProperty = DependencyProperty.Register(nameof(Section), typeof(string),
        typeof(SplitPanel), new FrameworkPropertyMetadata(""));

    /// <summary>What the rules are drawn in.</summary>
    public static readonly DependencyProperty RuleProperty = DependencyProperty.Register(nameof(Rule), typeof(Brush),
        typeof(SplitPanel), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MinPaneWidthProperty = DependencyProperty.Register(nameof(MinPaneWidth),
        typeof(double), typeof(SplitPanel), new FrameworkPropertyMetadata(320.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MinPaneHeightProperty = DependencyProperty.Register(nameof(MinPaneHeight),
        typeof(double), typeof(SplitPanel), new FrameworkPropertyMetadata(132.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty HeadingHeightProperty = DependencyProperty.Register(nameof(HeadingHeight),
        typeof(double), typeof(SplitPanel), new FrameworkPropertyMetadata(30.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>A player has arranged the panes another way. Whoever holds
    /// the tree puts <see cref="RearrangedEventArgs.Tree"/> there and marks
    /// the event handled; left unhandled, the panel keeps the tree itself.</summary>
    public static readonly RoutedEvent RearrangedEvent = EventManager.RegisterRoutedEvent("Rearranged", RoutingStrategy.Bubble,
        typeof(EventHandler<RearrangedEventArgs>), typeof(SplitPanel));

    /// <summary>Something for the status line, or nothing any more.</summary>
    public static readonly RoutedEvent NotedEvent = EventManager.RegisterRoutedEvent("Noted", RoutingStrategy.Bubble,
        typeof(EventHandler<NotedEventArgs>), typeof(SplitPanel));

    public static string? GetKey(DependencyObject d) => (string?)d.GetValue(KeyProperty);
    public static void SetKey(DependencyObject d, string? value) => d.SetValue(KeyProperty, value);
    public static double GetFixed(DependencyObject d) => (double)d.GetValue(FixedProperty);
    public static void SetFixed(DependencyObject d, double value) => d.SetValue(FixedProperty, value);
    public static bool GetSelfFolded(DependencyObject d) => (bool)d.GetValue(SelfFoldedProperty);
    public static void SetSelfFolded(DependencyObject d, bool value) => d.SetValue(SelfFoldedProperty, value);
    public static bool GetFolded(DependencyObject d) => (bool)d.GetValue(FoldedProperty);
    static void SetFolded(DependencyObject d, bool value) => d.SetValue(FoldedProperty, value);
    public static bool GetRail(DependencyObject d) => (bool)d.GetValue(RailProperty);
    static void SetRail(DependencyObject d, bool value) => d.SetValue(RailProperty, value);
    public static string? GetTitle(DependencyObject d) => (string?)d.GetValue(TitleProperty);
    public static void SetTitle(DependencyObject d, string? value) => d.SetValue(TitleProperty, value);
    public static bool GetHandle(DependencyObject d) => (bool)d.GetValue(HandleProperty);
    public static void SetHandle(DependencyObject d, bool value) => d.SetValue(HandleProperty, value);
    public static bool GetStacked(DependencyObject d) => (bool)d.GetValue(StackedProperty);
    public static void SetStacked(DependencyObject d, bool value) => d.SetValue(StackedProperty, value);
    public static bool GetLocked(DependencyObject d) => (bool)d.GetValue(LockedProperty);
    public static void SetLocked(DependencyObject d, bool value) => d.SetValue(LockedProperty, value);

    /// <summary>The arrangement. Without one, the children stand in a column.</summary>
    public SplitNode? Tree { get => (SplitNode?)GetValue(TreeProperty); set => SetValue(TreeProperty, value); }
    public string Section { get => (string)GetValue(SectionProperty); set => SetValue(SectionProperty, value); }
    public Brush? Rule { get => (Brush?)GetValue(RuleProperty); set => SetValue(RuleProperty, value); }
    /// <summary>The narrowest and the shortest a pane is made (PaneMinWidth and PaneMinHeight in App.xaml).</summary>
    public double MinPaneWidth { get => (double)GetValue(MinPaneWidthProperty); set => SetValue(MinPaneWidthProperty, value); }
    public double MinPaneHeight { get => (double)GetValue(MinPaneHeightProperty); set => SetValue(MinPaneHeightProperty, value); }
    /// <summary>How tall a pane the tree says is folded is: a pane's heading.</summary>
    public double HeadingHeight { get => (double)GetValue(HeadingHeightProperty); set => SetValue(HeadingHeightProperty, value); }

    /// <summary>The rule of every split as last arranged, with how far each
    /// may be moved: empty while the panes are stacked.</summary>
    public IReadOnlyList<Divider> Dividers { get; private set; } = [];

    /// <summary>Where every pane was last put: empty while the panes are stacked.</summary>
    Arrangement? arranged;

    /// <summary>The rules as last arranged, to paint.</summary>
    List<Rect> lines = [];

    /// <summary>
    /// The arrangement as it would be if the rule in the hand were let go
    /// now. The panel arranges by it while there is one, so sizes follow
    /// the pointer; the tree itself is not touched until the rule is let
    /// go, and Esc only has to drop this.
    /// </summary>
    SplitNode? preview;

    /// <summary>The tree the panes are arranged by just now.</summary>
    SplitNode? Shown => preview ?? Tree;

    /// <summary>What is drawn over the panes while the arrangement is in
    /// someone's hand. The last visual child: over the panes and the thumbs.</summary>
    readonly LayoutOverlay overlay = new();

    public SplitPanel()
    {
        AddVisualChild(overlay);
        Listen();
    }

    static void OnChildChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Visual child && VisualTreeHelper.GetParent(child) is SplitPanel panel) panel.InvalidateMeasure();
    }

    /// <summary>The tree was replaced from outside: by what this panel
    /// asked for, by a reset, or by another section's panel that shows the
    /// same arrangement. Whatever was in the hand is let go of.</summary>
    void OnTreeChanged()
    {
        preview = null;
    }

    // ------------------------------------------- the children that are not panes

    /// <summary>The thumbs in the order of their rules, then the overlay.</summary>
    IEnumerable<Visual> Extras()
    {
        foreach (var thumb in thumbs) yield return thumb;
        yield return overlay;
    }

    protected override int VisualChildrenCount => base.VisualChildrenCount + thumbs.Count + 1;

    protected override Visual GetVisualChild(int index)
    {
        int panes = base.VisualChildrenCount;
        if (index < panes) return base.GetVisualChild(index);
        index -= panes;
        return index < thumbs.Count ? thumbs[index] : overlay;
    }

    // ------------------------------------------------------------ measuring

    /// <summary>One device pixel in units, and the rule's thickness: one
    /// unit on whole pixels, as every other rule in the window is (a
    /// pixel at 100%, two at 150%).</summary>
    (double Pixel, double Line) Pixels()
    {
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        return (1 / scale, Math.Max(1, Math.Round(scale)) / scale);
    }

    PaneLimits Limits()
    {
        var (pixel, line) = Pixels();
        return new PaneLimits(MinPaneWidth, MinPaneHeight, HeadingHeight, line, pixel);
    }

    /// <summary>The children that are panes of the tree, by key; the
    /// heights those of them have of their own; and those that have folded
    /// themselves.</summary>
    (Dictionary<string, UIElement> Panes, Dictionary<string, double> Own, HashSet<string> Folded) Keyed()
    {
        var panes = new Dictionary<string, UIElement>();
        var own = new Dictionary<string, double>();
        var folded = new HashSet<string>();
        foreach (UIElement child in InternalChildren)
        {
            if (GetKey(child) is not { } key || child.Visibility == Visibility.Collapsed) continue;
            panes[key] = child;
            double height = GetFixed(child);
            if (!double.IsNaN(height)) own[key] = height;
            if (GetSelfFolded(child)) folded.Add(key);
        }
        return (panes, own, folded);
    }

    /// <summary>The child that is a pane of the tree, or null.</summary>
    UIElement? ChildOf(string key)
    {
        foreach (UIElement child in InternalChildren)
            if (GetKey(child) == key && child.Visibility != Visibility.Collapsed) return child;
        return null;
    }

    /// <summary>The children in the order they stand in one column: the
    /// tree's panes in reading order, then any child the tree does not name.</summary>
    List<UIElement> Column()
    {
        var (panes, _, _) = Keyed();
        var column = new List<UIElement>();
        if (Shown is { } tree)
        {
            foreach (string key in SplitTree.Keys(tree))
                if (panes.Remove(key, out var child)) column.Add(child);
        }
        foreach (UIElement child in InternalChildren)
            if (child.Visibility != Visibility.Collapsed && !column.Contains(child)) column.Add(child);
        return column;
    }

    bool InColumn => Shown is null || GetStacked(this);

    /// <summary>
    /// Each pane's place in the order they stand in, said as its z-index:
    /// the tree's reading order, then any child the tree does not name, as
    /// written. Set only where it differs, so an arrangement that has not
    /// changed costs a comparison per pane.
    /// </summary>
    void Order(SplitNode tree)
    {
        var keys = SplitTree.Keys(tree);
        int after = keys.Count;
        foreach (UIElement child in InternalChildren)
        {
            int at = -1;
            if (GetKey(child) is { } key)
                for (int i = 0; i < keys.Count && at < 0; i++)
                    if (keys[i] == key) at = i;
            int place = at >= 0 ? at : after++;
            if (GetZIndex(child) != place) SetZIndex(child, place);
        }
    }

    protected override Size MeasureOverride(Size available)
    {
        // What the tree says is folded, said to the panes before they are
        // measured; and where each stands, to the Tab key and a screen reader.
        if (Shown is { } folds)
        {
            Tell(folds);
            Order(folds);
        }

        if (InColumn)
        {
            double line = Pixels().Line, total = 0, widest = 0;
            var column = Column();
            foreach (var child in column)
            {
                if (GetRail(child)) SetRail(child, false);
                child.Measure(new Size(available.Width, double.PositiveInfinity));
                total += child.DesiredSize.Height;
                widest = Math.Max(widest, child.DesiredSize.Width);
            }
            total += line * Math.Max(0, column.Count - 1);
            PageStack.SetNeeds(this, 0);
            // How wide the panes would have to be side by side is said
            // all the same: it is what the page asks before it stacks them.
            if (Shown is { } stacked)
            {
                var (_, heights, selves) = Keyed();
                PageStack.SetNeedsWidth(this, SplitTree.Least(stacked, Limits(), heights, selves).Width);
            }
            else PageStack.SetNeedsWidth(this, 0);
            Thumbs([], new Size());
            overlay.Measure(new Size(double.IsInfinity(available.Width) ? widest : available.Width, total));
            return new Size(double.IsInfinity(available.Width) ? widest : available.Width, total);
        }

        var tree = Shown!;
        var limits = Limits();
        var (_, own, folded) = Keyed();
        var least = SplitTree.Least(tree, limits, own, folded);
        // Asked how large it would like to be, the least it can be; given
        // less than that, the least all the same. WPF cuts what an element
        // asks for down to what it was offered, so the page is told apart
        // from that how much is needed, offers it, and scrolls.
        PageStack.SetNeeds(this, least.Height);
        PageStack.SetNeedsWidth(this, least.Width);
        double width = double.IsInfinity(available.Width) ? least.Width : available.Width;
        double height = double.IsInfinity(available.Height) ? least.Height : Math.Max(available.Height, least.Height);

        var put = SplitTree.Arrange(tree, width, height, limits, own, folded);
        foreach (UIElement child in InternalChildren)
        {
            if (GetKey(child) is { } key && put.Panes.TryGetValue(key, out var box))
            {
                // Said before it is measured: a rail measures as one.
                bool rail = put.Rails.Contains(key);
                if (GetRail(child) != rail) SetRail(child, rail);
                child.Measure(new Size(box.Width, box.Height));
            }
            else
                child.Measure(new Size(0, 0));
        }
        Thumbs(put.Dividers, new Size(width, height));
        overlay.Measure(new Size(width, height));
        return new Size(width, height);
    }

    void Tell(SplitNode node)
    {
        if (node is PaneLeaf leaf)
        {
            foreach (UIElement child in InternalChildren)
                if (GetKey(child) == leaf.Key && GetFolded(child) != leaf.Folded) SetFolded(child, leaf.Folded);
        }
        else if (node is PaneSplit split)
        {
            Tell(split.First);
            Tell(split.Second);
        }
    }

    protected override Size ArrangeOverride(Size final)
    {
        var drawn = new List<Rect>();
        if (InColumn)
        {
            double line = Pixels().Line, y = 0;
            var column = Column();
            for (int i = 0; i < column.Count; i++)
            {
                if (i > 0)
                {
                    drawn.Add(new Rect(0, y, final.Width, line));
                    y += line;
                }
                double height = column[i].DesiredSize.Height;
                column[i].Arrange(new Rect(0, y, final.Width, height));
                y += height;
            }
            Dividers = [];
            arranged = null;
            Place([]);
        }
        else
        {
            var (_, own, folded) = Keyed();
            var put = SplitTree.Arrange(Shown!, final.Width, final.Height, Limits(), own, folded);
            foreach (UIElement child in InternalChildren)
            {
                if (GetKey(child) is { } key && put.Panes.TryGetValue(key, out var box))
                    child.Arrange(new Rect(box.X, box.Y, box.Width, box.Height));
                else
                    child.Arrange(new Rect(0, 0, 0, 0));
            }
            foreach (var divider in put.Dividers)
                drawn.Add(new Rect(divider.Line.X, divider.Line.Y, divider.Line.Width, divider.Line.Height));
            Dividers = put.Dividers;
            arranged = put;
            Place(put.Dividers);
        }
        overlay.Arrange(new Rect(final));

        if (!drawn.SequenceEqual(lines))
        {
            lines = drawn;
            InvalidateVisual();
        }
        return final;
    }

    protected override void OnRender(DrawingContext dc)
    {
        // The panel's own surface first: where two short panes leave a
        // column empty, it shows.
        base.OnRender(dc);
        if (Rule is not { } rule) return;
        foreach (var line in lines) dc.DrawRectangle(rule, null, line);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    // ------------------------------------------------------- saying what was done

    /// <summary>What a pane is called: the name its child was given, or the
    /// title of the pane the child is.</summary>
    string TitleOf(string key)
    {
        if (ChildOf(key) is not { } child) return key;
        if (GetTitle(child) is { Length: > 0 } title) return title;
        return PaneIn(child)?.Title is { Length: > 0 } own ? own : key;
    }

    /// <summary>The pane a child is, or holds.</summary>
    static Pane? PaneIn(DependencyObject child)
    {
        if (child is Pane pane) return pane;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(child); i++)
            if (PaneIn(VisualTreeHelper.GetChild(child, i)) is { } inner) return inner;
        return null;
    }

    /// <summary>
    /// The panes are to be arranged by another tree. The tree's holder is
    /// told, and the panel then follows its binding; with nobody listening
    /// (a panel standing alone), it keeps the tree itself.
    /// </summary>
    void Commit(SplitNode tree, string what)
    {
        preview = null;
        InvalidateMeasure();
        if (Tree is not { } now || tree == now) return;
        var args = new RearrangedEventArgs(RearrangedEvent, this, Section, tree, what);
        RaiseEvent(args);
        if (!args.Handled) SetCurrentValue(TreeProperty, tree);
    }

    string noted = "";

    /// <summary>Something for the status line, or "" to take it back.</summary>
    void Note(string note)
    {
        if (note == noted) return;
        noted = note;
        RaiseEvent(new NotedEventArgs(NotedEvent, this, note));
    }
}
