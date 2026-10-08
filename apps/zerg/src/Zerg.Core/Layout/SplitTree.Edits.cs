namespace Zerg.Core.Layout;

/// <summary>A side of a pane, or of the whole section.</summary>
public enum PaneSide { Left, Right, Above, Below }

// Changing a tree. Every function here takes a tree and returns another:
// nothing is changed in place (the nodes are records and cannot be), so a
// drag can be shown as it would come out and then dropped, and an
// arrangement that was not wanted is simply not kept. A change that cannot
// be made (a pane the tree has not got, a path that leads nowhere) gives
// the tree back as it was, the very same object.
public static partial class SplitTree
{
    /// <summary>The share a pane takes of the section when it is put along
    /// one of the section's edges: a third.</summary>
    public const double EdgeShare = 1.0 / 3;

    // ------------------------------------------------------------- finding

    /// <summary>The pane with this key, or null.</summary>
    public static PaneLeaf? Leaf(SplitNode tree, string key) => tree switch
    {
        PaneLeaf leaf => leaf.Key == key ? leaf : null,
        PaneSplit split => Leaf(split.First, key) ?? Leaf(split.Second, key),
        _ => null,
    };

    /// <summary>The way down to a pane, as a <see cref="Divider.Path"/> is
    /// written ("01"), or null if the tree has no such pane. The path less
    /// its last step is the split the pane is a half of.</summary>
    public static string? PathTo(SplitNode tree, string key) => tree switch
    {
        PaneLeaf leaf => leaf.Key == key ? "" : null,
        PaneSplit split => PathTo(split.First, key) is { } first ? "0" + first
                         : PathTo(split.Second, key) is { } second ? "1" + second : null,
        _ => null,
    };

    /// <summary>Every node replaced by what <paramref name="change"/> makes
    /// of it, from the leaves up; a node it returns null for is taken out,
    /// and the split it was a half of becomes its other half.</summary>
    static SplitNode? Rebuild(SplitNode node, Func<SplitNode, SplitNode?> change)
    {
        if (node is PaneSplit split)
        {
            var first = Rebuild(split.First, change);
            var second = Rebuild(split.Second, change);
            if (first is null) return second;
            if (second is null) return first;
            if (!ReferenceEquals(first, split.First) || !ReferenceEquals(second, split.Second))
                node = split with { First = first, Second = second };
        }
        return change(node);
    }

    /// <summary>The node at the end of a path replaced.</summary>
    static SplitNode Replace(SplitNode node, string path, int at, Func<PaneSplit, SplitNode> change)
    {
        if (node is not PaneSplit split) return node;
        if (at == path.Length) return change(split);
        return path[at] switch
        {
            '0' => Same(split, Replace(split.First, path, at + 1, change), split.Second),
            '1' => Same(split, split.First, Replace(split.Second, path, at + 1, change)),
            _ => node,
        };

        static SplitNode Same(PaneSplit was, SplitNode first, SplitNode second) =>
            ReferenceEquals(first, was.First) && ReferenceEquals(second, was.Second) ? was : was with { First = first, Second = second };
    }

    /// <summary>A share as it is kept: between 0 and 1, to four places,
    /// which is how it is written to the settings (a ten-thousandth of a
    /// room is under a pixel on any screen). Kept so from the start, what
    /// is on screen is what a restart brings back, to the pixel.</summary>
    static double Share(double ratio) => Math.Round(Math.Clamp(ratio, 0, 1), 4);

    // -------------------------------------------------------------- a rule

    /// <summary>The same tree with one split's share set: the split a
    /// <see cref="Divider.Path"/> leads to. The share is kept between 0 and
    /// 1 and nothing more: keeping the panes to their least sizes is
    /// <see cref="Arrange"/>'s, which knows the room; <see cref="Drag"/>
    /// gives a share that needs no such correction.</summary>
    public static SplitNode SetRatio(SplitNode tree, string path, double ratio)
    {
        if (double.IsNaN(ratio)) return tree;
        ratio = Share(ratio);
        return Replace(tree, path, 0, split => split.Ratio == ratio ? split : split with { Ratio = ratio });
    }

    /// <summary>
    /// The share that puts a rule's leading edge (its left, or its top) at
    /// <paramref name="position"/>, in the coordinates the divider was
    /// arranged in. The position is first brought inside what the panes'
    /// least sizes allow (<see cref="Divider.Low"/>, <see cref="Divider.High"/>),
    /// so the share is the one <see cref="Arrange"/> will act on as it is.
    /// </summary>
    public static double RatioAt(Divider divider, double position)
    {
        bool columns = divider.Way == SplitWay.Columns;
        double start = columns ? divider.Room.X : divider.Room.Y;
        double extent = columns ? divider.Room.Width : divider.Room.Height;
        double rule = columns ? divider.Line.Width : divider.Line.Height;
        double room = extent - rule;
        if (double.IsNaN(position) || room <= 0) return 0.5;
        position = Math.Clamp(position, divider.Low, Math.Max(divider.Low, divider.High));
        return Share((position - start) / room);
    }

    /// <summary>The tree with a rule moved to a position, by as much as the
    /// panes' least sizes let it go. A rule that is held (a folded pane or
    /// a stand-in on one side, or no room to give) stays where it is.</summary>
    public static SplitNode Drag(SplitNode tree, Divider divider, double position) =>
        divider.Held ? tree : SetRatio(tree, divider.Path, RatioAt(divider, position));

    /// <summary>
    /// The share a split is set back to by a double-click on its rule: the
    /// installed tree's, where that tree has a split of the same way in the
    /// same place; a half otherwise (a split that a moved pane made has no
    /// installed share to go back to).
    /// </summary>
    public static double InstalledRatio(SplitNode tree, SplitNode installed, string path) =>
        At(tree, path) is { } here && At(installed, path) is { } there && here.Way == there.Way ? there.Ratio : 0.5;

    // -------------------------------------------------------------- a fold

    /// <summary>The tree with one pane folded to its heading, or opened again.</summary>
    public static SplitNode Fold(SplitNode tree, string key, bool folded) =>
        Rebuild(tree, node => node is PaneLeaf leaf && leaf.Key == key && leaf.Folded != folded ? leaf with { Folded = folded } : node)!;

    /// <summary>Folded if it was open, open if it was folded.</summary>
    public static SplitNode ToggleFold(SplitNode tree, string key) =>
        Leaf(tree, key) is { } leaf ? Fold(tree, key, !leaf.Folded) : tree;

    // -------------------------------------------------------------- a move

    /// <summary>The tree without one pane: the split it was a half of
    /// becomes its other half, which so takes the room of both. Null if the
    /// pane was the only one.</summary>
    public static SplitNode? Remove(SplitNode tree, string key) =>
        Rebuild(tree, node => node is PaneLeaf leaf && leaf.Key == key ? null : node);

    /// <summary>
    /// A pane moved beside another: taken out of where it was (its old
    /// neighbour takes its room), and the target's room split in two, half
    /// each, with the moved pane on the side said. It keeps its fold.
    /// </summary>
    public static SplitNode Move(SplitNode tree, string key, string target, PaneSide side)
    {
        if (key == target || Leaf(tree, key) is not { } moved || Leaf(tree, target) is null) return tree;
        if (Remove(tree, key) is not { } rest) return tree;
        return Rebuild(rest, node => node is PaneLeaf leaf && leaf.Key == target ? Beside(moved, leaf, side, 0.5) : node)!;
    }

    /// <summary>
    /// A pane moved to an edge of the section: it takes that whole side,
    /// the full height at the left or right, the full width at the top or
    /// bottom, a third of the section (<see cref="EdgeShare"/>); everything
    /// else keeps its arrangement in what is left.
    /// </summary>
    public static SplitNode MoveToEdge(SplitNode tree, string key, PaneSide side)
    {
        if (Leaf(tree, key) is not { } moved || Remove(tree, key) is not { } rest) return tree;
        return Beside(moved, rest, side, EdgeShare);
    }

    /// <summary>A split of a pane and something else, the pane on the side
    /// said with <paramref name="share"/> of the room.</summary>
    static PaneSplit Beside(PaneLeaf pane, SplitNode other, PaneSide side, double share) => side switch
    {
        PaneSide.Left => new PaneSplit(SplitWay.Columns, Share(share), pane, other),
        PaneSide.Right => new PaneSplit(SplitWay.Columns, Share(1 - share), other, pane),
        PaneSide.Above => new PaneSplit(SplitWay.Rows, Share(share), pane, other),
        _ => new PaneSplit(SplitWay.Rows, Share(1 - share), other, pane),
    };

    /// <summary>Two panes change places. Each keeps its own fold; every
    /// split and every share stays as it was.</summary>
    public static SplitNode Swap(SplitNode tree, string a, string b)
    {
        if (a == b || Leaf(tree, a) is not { } first || Leaf(tree, b) is not { } second) return tree;
        return Rebuild(tree, node => node is PaneLeaf leaf ? leaf.Key == a ? second : leaf.Key == b ? first : leaf : node)!;
    }

    /// <summary>
    /// The pane next to this one on a side, or null at an edge of the
    /// section. Worked out from the shares alone, as the panes stand with
    /// none folded and none floating: of the panes whose edge lies along
    /// this pane's edge on that side, the one that shares the most of it,
    /// and of two that share alike, the first in reading order.
    /// </summary>
    public static string? Neighbour(SplitNode tree, string key, PaneSide side)
    {
        var boxes = new Dictionary<string, Box>();
        var order = new List<string>();
        Unit(tree, new Box(0, 0, 1, 1));
        if (!boxes.TryGetValue(key, out var me)) return null;

        const double near = 1e-9;
        string? best = null;
        double most = near;
        foreach (string other in order)
        {
            if (other == key) continue;
            var b = boxes[other];
            bool touches = side switch
            {
                PaneSide.Left => Math.Abs(b.Right - me.X) < near,
                PaneSide.Right => Math.Abs(b.X - me.Right) < near,
                PaneSide.Above => Math.Abs(b.Bottom - me.Y) < near,
                _ => Math.Abs(b.Y - me.Bottom) < near,
            };
            if (!touches) continue;
            double shared = side is PaneSide.Left or PaneSide.Right
                ? Math.Min(b.Bottom, me.Bottom) - Math.Max(b.Y, me.Y)
                : Math.Min(b.Right, me.Right) - Math.Max(b.X, me.X);
            if (shared > most + near)
            {
                most = shared;
                best = other;
            }
        }
        return best;

        // The panes in a room one by one, by their shares and nothing else.
        void Unit(SplitNode node, Box box)
        {
            if (node is PaneLeaf leaf)
            {
                boxes[leaf.Key] = box;
                order.Add(leaf.Key);
            }
            else if (node is PaneSplit split)
            {
                double ratio = double.IsNaN(split.Ratio) ? 0.5 : Math.Clamp(split.Ratio, 0.001, 0.999);
                if (split.Way == SplitWay.Columns)
                {
                    Unit(split.First, new Box(box.X, box.Y, box.Width * ratio, box.Height));
                    Unit(split.Second, new Box(box.X + box.Width * ratio, box.Y, box.Width * (1 - ratio), box.Height));
                }
                else
                {
                    Unit(split.First, new Box(box.X, box.Y, box.Width, box.Height * ratio));
                    Unit(split.Second, new Box(box.X, box.Y + box.Height * ratio, box.Width, box.Height * (1 - ratio)));
                }
            }
        }
    }

    /// <summary>A pane moved one place to a side: it changes places with
    /// its neighbour there (<see cref="Neighbour"/>). At an edge of the
    /// section there is none, and nothing changes.</summary>
    public static SplitNode MoveBy(SplitNode tree, string key, PaneSide side) =>
        Neighbour(tree, key, side) is { } other ? Swap(tree, key, other) : tree;

    // ------------------------------------------------------------ a repair

    /// <summary>How deep a tree may be before it is taken for damaged: far
    /// deeper than any arrangement of a section's few panes.</summary>
    const int Deepest = 32;

    /// <summary>
    /// A tree made fit for this build, whatever it was read from. The panes
    /// this build has are the installed tree's: a pane the tree names that
    /// the build has not got is dropped (its neighbour takes its room); a
    /// pane named twice is kept where it is first met; a share that is not
    /// a number is a half, and one outside 0 to 1 is brought inside; and
    /// each pane of the build that the tree does not mention is put under
    /// everything else, the section's whole width, with an even share of
    /// its height, so that it is on screen and can be moved from there.
    /// Nothing left, or no tree at all, or one too deep to be anything but
    /// damage, is the installed tree. Never throws.
    /// </summary>
    public static SplitNode Repair(SplitNode? tree, SplitNode installed)
    {
        var known = new HashSet<string>(Keys(installed), StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        bool tooDeep = false;
        var kept = tree is null ? null : Clean(tree, 0);
        if (kept is null || tooDeep) return installed;

        foreach (string key in Keys(installed))
        {
            if (seen.Contains(key)) continue;
            int count = seen.Count + 1;
            seen.Add(key);
            kept = new PaneSplit(SplitWay.Rows, Share(1 - 1.0 / count), kept, new PaneLeaf(key));
        }
        // Said the same way as the installed tree, it is the installed tree.
        return kept == installed ? installed : kept;

        SplitNode? Clean(SplitNode node, int depth)
        {
            if (depth > Deepest)
            {
                tooDeep = true;
                return null;
            }
            switch (node)
            {
                case PaneLeaf leaf:
                    return leaf.Key is not null && known.Contains(leaf.Key) && seen.Add(leaf.Key) ? leaf : null;
                case PaneSplit split:
                    var first = split.First is null ? null : Clean(split.First, depth + 1);
                    var second = split.Second is null ? null : Clean(split.Second, depth + 1);
                    if (first is null) return second;
                    if (second is null) return first;
                    double ratio = double.IsNaN(split.Ratio) ? 0.5 : Math.Clamp(split.Ratio, 0, 1);
                    var way = split.Way == SplitWay.Rows ? SplitWay.Rows : SplitWay.Columns;
                    return ReferenceEquals(first, split.First) && ReferenceEquals(second, split.Second) && ratio == split.Ratio && way == split.Way
                        ? split
                        : new PaneSplit(way, ratio, first, second);
                default:
                    return null;
            }
        }
    }
}
