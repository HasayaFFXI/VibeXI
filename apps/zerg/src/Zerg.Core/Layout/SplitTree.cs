namespace Zerg.Core.Layout;

/// <summary>Which way a split divides its room.</summary>
public enum SplitWay
{
    /// <summary>Side by side: the first on the left, the rule upright between them.</summary>
    Columns,
    /// <summary>One over the other: the first on top, the rule lying between them.</summary>
    Rows,
}

/// <summary>
/// A section's panes as a tree of splits and nothing else: a node is one
/// pane (<see cref="PaneLeaf"/>) or a room cut in two (<see cref="PaneSplit"/>),
/// each half a node again. Records, so two trees that say the same thing
/// are equal, and a changed arrangement is a new tree.
/// </summary>
public abstract record SplitNode;

/// <summary>One pane, by its key ("bars", "drill", ...).</summary>
/// <param name="Key">Which pane. The host arranges the child that carries this key.</param>
/// <param name="Folded">The pane is folded to its heading: it takes the
/// heading's height and its neighbour takes the rest. This is the fold a
/// person asks for and the one that is remembered; a pane that is folded
/// for a reason of its own (a drill-down with nothing picked) is named in
/// <c>folded</c> instead, and a card that is floating is given its
/// stand-in's height in <c>fixedHeights</c>; the tree is not touched by
/// either.</param>
public sealed record PaneLeaf(string Key, bool Folded = false) : SplitNode;

/// <summary>A room cut in two.</summary>
/// <param name="Way">Side by side, or one over the other.</param>
/// <param name="Ratio">The first half's share of the room, 0 to 1. The
/// room is what the split has along its way, less the rule between the
/// halves: 940 of 1439 where a 1440-wide body is cut at 940 and ruled.
/// A share and not a size, so the arrangement holds when the window is
/// resized.</param>
public sealed record PaneSplit(SplitWay Way, double Ratio, SplitNode First, SplitNode Second) : SplitNode;

/// <summary>A rectangle in the host's own coordinates.</summary>
public readonly record struct Box(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
}

/// <summary>
/// The sizes an arrangement keeps to.
/// </summary>
/// <param name="MinWidth">The narrowest a pane is made.</param>
/// <param name="MinHeight">The shortest a pane is made: a heading, column
/// headings and three rows.</param>
/// <param name="Heading">How tall a folded pane is: its heading.</param>
/// <param name="Rule">How thick the rule between two halves is. It lies
/// between them and takes this much of the room.</param>
/// <param name="Grain">What every edge is rounded to: one unit unless
/// said, which is a whole pixel on a screen at 100%. On a scaled screen,
/// the size of one device pixel in units (two thirds at 150%). Zero leaves
/// the edges where the shares put them. The room is taken to begin on a
/// whole pixel.</param>
public sealed record PaneLimits(double MinWidth = 320, double MinHeight = 132, double Heading = 30, double Rule = 1,
                                double Grain = 1);

/// <summary>
/// The rule between the two halves of one split: where it is drawn now, and
/// what moving it would be allowed to do.
/// </summary>
/// <param name="Path">Which split of the tree: the way down to it from the
/// root, "0" for a first half and "1" for a second ("" is the root, "01"
/// the second half of the root's first half). <see cref="SplitTree.At"/>
/// finds the split again.</param>
/// <param name="Way">The split's way: <see cref="SplitWay.Columns"/> is an
/// upright rule that moves sideways.</param>
/// <param name="Line">The rule itself, as a rectangle <see cref="PaneLimits.Rule"/> thick.</param>
/// <param name="Room">The whole room the split cuts in two.</param>
/// <param name="Low">The least the rule's leading edge (its left, or its
/// top) may be: nearer the room's start, the first half would be under its
/// least size.</param>
/// <param name="High">The most it may be, for the second half's sake.</param>
/// <param name="Held">The rule cannot be moved: one half has a height of
/// its own (folded, or a stand-in), or the room is too small for both
/// halves' least sizes and there is nothing to give.</param>
public sealed record Divider(string Path, SplitWay Way, Box Line, Box Room, double Low, double High, bool Held);

/// <summary>Where everything goes: a rectangle per pane, by key, and the rule of every split.</summary>
/// <param name="Rails">The panes that are folded sideways: a folded pane
/// that is one half of a split side by side, alone in its column, is a
/// rail as wide as a heading is tall, with its title reading down.</param>
public sealed record Arrangement(IReadOnlyDictionary<string, Box> Panes, IReadOnlyList<Divider> Dividers,
                                 IReadOnlySet<string> Rails);

/// <summary>
/// Arranges a tree of panes in a room. Numbers in, rectangles out: nothing
/// here draws, and nothing here knows what a pane holds. <c>Views/SplitPanel</c>
/// puts its children where this says.
///
/// <para>Four rules. A split gives its first half <see cref="PaneSplit.Ratio"/>
/// of the room and the second half the rest, <b>except</b> that neither is
/// made smaller than its least size (<see cref="Least"/>): the ratio gives
/// way, and comes back when the room does. A pane with a height of its own
/// (folded to its heading, or the short stand-in of a card that is
/// floating) takes exactly that, and the other half of its split takes
/// what is left; where both halves have one, they stand at the top and the
/// rest of the room is empty. <b>A folded pane that is one half of a split
/// side by side</b> has nobody over or under it to give its room to: it
/// folds sideways, to a rail as wide as a heading is tall, and the other
/// half takes the rest of the width. And in a room too small for both least
/// sizes, each half gets its share of too little: nothing is ever given a
/// negative size, and nothing throws.</para>
///
/// <para>Arranging is in this file. Changing a tree (a new ratio, a fold,
/// a pane moved beside another) is in <c>SplitTree.Edits.cs</c>; writing
/// one to a settings file and reading it back, in <c>SplitTree.Json.cs</c>.</para>
/// </summary>
public static partial class SplitTree
{
    /// <summary>Every pane of a tree in reading order: a first half before
    /// its second, so down the left-hand column and then down the right.
    /// The order the panes stand in when a narrow window puts them in one
    /// column.</summary>
    public static IReadOnlyList<string> Keys(SplitNode tree)
    {
        var keys = new List<string>();
        Walk(tree);
        return keys;

        void Walk(SplitNode node)
        {
            if (node is PaneLeaf leaf) keys.Add(leaf.Key);
            else if (node is PaneSplit split)
            {
                Walk(split.First);
                Walk(split.Second);
            }
        }
    }

    /// <summary>The split a <see cref="Divider.Path"/> leads to, or null if
    /// the path leads nowhere in this tree.</summary>
    public static PaneSplit? At(SplitNode tree, string path)
    {
        var node = tree;
        foreach (char step in path)
        {
            if (node is not PaneSplit split) return null;
            node = step == '0' ? split.First : step == '1' ? split.Second : null;
            if (node is null) return null;
        }
        return node as PaneSplit;
    }

    /// <summary>
    /// The smallest room a tree can be arranged in with every pane at its
    /// least size or more. A host given less either squeezes the panes
    /// (<see cref="Arrange"/> does, by their shares of what is needed) or
    /// asks for this much and scrolls.
    /// </summary>
    public static (double Width, double Height) Least(SplitNode tree, PaneLimits limits,
                                                      IReadOnlyDictionary<string, double>? fixedHeights = null,
                                                      IReadOnlySet<string>? folded = null)
    {
        switch (tree)
        {
            case PaneLeaf leaf:
                return (limits.MinWidth, Fixed(leaf, limits, fixedHeights, folded) ?? limits.MinHeight);
            case PaneSplit split:
                var (w1, h1) = Least(split.First, limits, fixedHeights, folded);
                var (w2, h2) = Least(split.Second, limits, fixedHeights, folded);
                if (split.Way == SplitWay.Rows) return (Math.Max(w1, w2), h1 + limits.Rule + h2);
                // A rail is a heading wide, and as tall as any pane has to
                // be: its title reads down it.
                if (IsRail(split.First, fixedHeights, folded)) (w1, h1) = (limits.Heading, limits.MinHeight);
                if (IsRail(split.Second, fixedHeights, folded)) (w2, h2) = (limits.Heading, limits.MinHeight);
                return (w1 + limits.Rule + w2, Math.Max(h1, h2));
            default:
                return (0, 0);
        }
    }

    /// <summary>
    /// Where each pane goes in a room <paramref name="width"/> by
    /// <paramref name="height"/>, and the rule of each split.
    /// </summary>
    /// <param name="fixedHeights">The panes that have a height of their own
    /// just now, by key: the stand-in's, for a card that is floating. It
    /// outranks a fold, and such a pane is never a rail.</param>
    /// <param name="folded">The panes that are folded for a reason of
    /// their own just now, by key (a drill-down with nothing picked). They
    /// are arranged as the panes the tree says are folded are, which need
    /// no entry here.</param>
    public static Arrangement Arrange(SplitNode tree, double width, double height, PaneLimits limits,
                                      IReadOnlyDictionary<string, double>? fixedHeights = null,
                                      IReadOnlySet<string>? folded = null)
    {
        var panes = new Dictionary<string, Box>();
        var dividers = new List<Divider>();
        var rails = new HashSet<string>();
        Place(tree, new Box(0, 0, Whole(width), Whole(height)), "");
        return new Arrangement(panes, dividers, rails);

        void Place(SplitNode node, Box box, string path)
        {
            if (node is PaneLeaf leaf)
            {
                panes[leaf.Key] = box;
                return;
            }
            if (node is not PaneSplit split) return;

            bool columns = split.Way == SplitWay.Columns;
            double rule = Math.Min(limits.Rule, columns ? box.Width : box.Height);
            double room = (columns ? box.Width : box.Height) - rule;

            var (w1, h1) = Least(split.First, limits, fixedHeights, folded);
            var (w2, h2) = Least(split.Second, limits, fixedHeights, folded);
            // A height of its own counts only where the halves are one over
            // the other: side by side, a short pane is still as tall as the
            // room. What a folded pane has side by side is a width of its
            // own: it is a rail.
            double? own1, own2;
            if (columns)
            {
                own1 = IsRail(split.First, fixedHeights, folded) ? limits.Heading : null;
                own2 = IsRail(split.Second, fixedHeights, folded) ? limits.Heading : null;
                if (own1 != null)
                {
                    w1 = limits.Heading;
                    rails.Add(((PaneLeaf)split.First).Key);
                }
                if (own2 != null)
                {
                    w2 = limits.Heading;
                    rails.Add(((PaneLeaf)split.Second).Key);
                }
            }
            else
            {
                own1 = FixedHeight(split.First, limits, fixedHeights, folded);
                own2 = FixedHeight(split.Second, limits, fixedHeights, folded);
            }
            double least1 = columns ? w1 : h1, least2 = columns ? w2 : h2;

            double first, second, low, high;
            bool held = true;
            if (own1 is { } a && own2 is { } b)
            {
                // Both at the top (two rails: at the left); whatever is left
                // under them (beside them) is empty.
                first = Math.Min(a, room);
                second = Math.Min(b, room - first);
                low = high = first;
            }
            else if (own1 is { } top)
            {
                first = Math.Min(top, room);
                second = room - first;
                low = high = first;
            }
            else if (own2 is { } bottom)
            {
                second = Math.Min(bottom, room);
                first = room - second;
                low = high = first;
            }
            else if (room >= least1 + least2)
            {
                low = Snap(least1, limits.Grain);
                high = Math.Max(low, Snap(room - least2, limits.Grain));
                double ratio = double.IsNaN(split.Ratio) ? 0.5 : Math.Clamp(split.Ratio, 0, 1);
                first = Math.Clamp(Snap(ratio * room, limits.Grain), low, high);
                second = room - first;
                held = high <= low;
            }
            else
            {
                // Not room for both: each its share of what there is.
                first = least1 + least2 > 0 ? Snap(room * least1 / (least1 + least2), limits.Grain) : 0;
                first = Math.Clamp(first, 0, room);
                second = room - first;
                low = high = first;
            }

            Box one, line, two;
            if (columns)
            {
                one = new Box(box.X, box.Y, first, box.Height);
                line = new Box(box.X + first, box.Y, rule, box.Height);
                two = new Box(box.X + first + rule, box.Y, second, box.Height);
                low += box.X;
                high += box.X;
            }
            else
            {
                one = new Box(box.X, box.Y, box.Width, first);
                line = new Box(box.X, box.Y + first, box.Width, rule);
                two = new Box(box.X, box.Y + first + rule, box.Width, second);
                low += box.Y;
                high += box.Y;
            }
            dividers.Add(new Divider(path, split.Way, line, box, low, high, held));
            Place(split.First, one, path + "0");
            Place(split.Second, two, path + "1");
        }
    }

    /// <summary>A size that can be arranged in: none is nothing, and so is less than none.</summary>
    static double Whole(double size) => double.IsNaN(size) || double.IsInfinity(size) || size < 0 ? 0 : size;

    static double Snap(double value, double grain) => grain > 0 ? Math.Round(value / grain) * grain : value;

    /// <summary>Whether a pane has been given a height of its own (a stand-in's).</summary>
    static bool Given(PaneLeaf leaf, IReadOnlyDictionary<string, double>? fixedHeights, out double height)
    {
        height = 0;
        if (fixedHeights == null || !fixedHeights.TryGetValue(leaf.Key, out double given) || double.IsNaN(given)) return false;
        height = Math.Max(0, given);
        return true;
    }

    /// <summary>Folded: by the tree, or for a reason of the pane's own.</summary>
    static bool IsFolded(PaneLeaf leaf, IReadOnlySet<string>? folded) =>
        leaf.Folded || (folded != null && folded.Contains(leaf.Key));

    /// <summary>A pane's own height, if it has one just now.</summary>
    static double? Fixed(PaneLeaf leaf, PaneLimits limits, IReadOnlyDictionary<string, double>? fixedHeights,
                         IReadOnlySet<string>? folded) =>
        Given(leaf, fixedHeights, out double given) ? given : IsFolded(leaf, folded) ? limits.Heading : null;

    /// <summary>A half of a split side by side that is a folded pane, and
    /// so a rail. Not a stand-in: that needs its width.</summary>
    static bool IsRail(SplitNode half, IReadOnlyDictionary<string, double>? fixedHeights, IReadOnlySet<string>? folded) =>
        half is PaneLeaf leaf && IsFolded(leaf, folded) && !Given(leaf, fixedHeights, out _);

    /// <summary>A node's own height: a pane's, or a split's whose every
    /// pane has one (two such panes over each other are as tall as both and
    /// their rule; side by side, as tall as the taller, unless one of them
    /// is a rail, which is as tall as its room).</summary>
    static double? FixedHeight(SplitNode node, PaneLimits limits, IReadOnlyDictionary<string, double>? fixedHeights,
                               IReadOnlySet<string>? folded)
    {
        switch (node)
        {
            case PaneLeaf leaf:
                return Fixed(leaf, limits, fixedHeights, folded);
            case PaneSplit split:
                if (split.Way == SplitWay.Columns &&
                    (IsRail(split.First, fixedHeights, folded) || IsRail(split.Second, fixedHeights, folded))) return null;
                if (FixedHeight(split.First, limits, fixedHeights, folded) is not { } a ||
                    FixedHeight(split.Second, limits, fixedHeights, folded) is not { } b) return null;
                return split.Way == SplitWay.Rows ? a + limits.Rule + b : Math.Max(a, b);
            default:
                return null;
        }
    }
}

/// <summary>
/// The panes Zerg has, and the arrangement each section is installed with:
/// the one the design's sheets draw. It is what a section starts from, what
/// it is set back to, and what a saved arrangement is repaired against (the
/// panes this build has are the installed tree's). Reading the saved ones
/// and writing them is in <c>SplitTree.Json.cs</c>.
/// </summary>
public static partial class PaneLayouts
{
    // The Damage section's four. The first three are also the keys of the
    // cards that can float (PanelSet); a drill-down cannot, and goes where
    // its table goes.
    public const string Bars = "bars", Actions = "actions", Line = "line", Drill = "drill";
    /// <summary>The Healing section's four, each the twin of the one above it.</summary>
    public const string HealBars = "hbars", HealActions = "hactions", HealLine = "hline", HealDrill = "hdrill";

    /// <summary>
    /// Damage, as sheet 01 draws it in a body 1440 by 706: the tables in a
    /// column 940 wide, the per-character table 334 tall over Actions; the
    /// chart 296 tall over the drill-down in the 499 that are left.
    /// </summary>
    public static SplitNode Damage { get; } =
        new PaneSplit(SplitWay.Columns, 0.653,
            new PaneSplit(SplitWay.Rows, 0.474, new PaneLeaf(Bars), new PaneLeaf(Actions)),
            new PaneSplit(SplitWay.Rows, 0.42, new PaneLeaf(Line), new PaneLeaf(Drill)));

    /// <summary>
    /// Healing, as sheet 02 draws it: the same columns; the per-character
    /// table, which has eight columns and few rows, 224 tall. The sheet
    /// draws the chart floating, so its share is Damage's.
    /// </summary>
    public static SplitNode Healing { get; } =
        new PaneSplit(SplitWay.Columns, 0.653,
            new PaneSplit(SplitWay.Rows, 0.318, new PaneLeaf(HealBars), new PaneLeaf(HealActions)),
            new PaneSplit(SplitWay.Rows, 0.42, new PaneLeaf(HealLine), new PaneLeaf(HealDrill)));

    /// <summary>The Compare section's four: who (by character or by job;
    /// comparing healing, by healer), both runs on one clock, what kind (by
    /// damage type; by heal), and at whom (by target). None can float.</summary>
    public const string CompareActors = "cactors", ComparePace = "cpace", CompareKinds = "ckinds",
                        CompareTargets = "ctargets";

    /// <summary>
    /// Compare, as sheet 03 draws it under its bands, in a body 1440 by
    /// 728: the table of characters alone in a column 940 wide; in the 499
    /// that are left, the chart 228 tall, then By damage type 306 tall over
    /// By target. One arrangement serves its Damage and its Healing mode.
    /// </summary>
    public static SplitNode Compare { get; } =
        new PaneSplit(SplitWay.Columns, 0.653,
            new PaneLeaf(CompareActors),
            new PaneSplit(SplitWay.Rows, 0.3136, new PaneLeaf(ComparePace),
                new PaneSplit(SplitWay.Rows, 0.6145, new PaneLeaf(CompareKinds), new PaneLeaf(CompareTargets))));
}
