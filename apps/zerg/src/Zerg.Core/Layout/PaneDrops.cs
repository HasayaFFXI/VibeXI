namespace Zerg.Core.Layout;

/// <summary>What putting a carried pane down somewhere would do.</summary>
public enum DropKind
{
    /// <summary>Nothing: the pane goes back where it was.</summary>
    None,
    /// <summary>It goes beside another pane, on one of its four sides, and
    /// the two share that pane's room.</summary>
    Beside,
    /// <summary>It changes places with another pane.</summary>
    Swap,
    /// <summary>It takes a whole side of the section.</summary>
    Edge,
}

/// <summary>One place a carried pane can be put down on another pane.</summary>
/// <param name="Kind"><see cref="DropKind.Beside"/> or <see cref="DropKind.Swap"/>.</param>
/// <param name="Side">Which side, for <see cref="DropKind.Beside"/>.</param>
/// <param name="Box">Where the place is drawn.</param>
public sealed record DropZone(DropKind Kind, PaneSide Side, Box Box);

/// <summary>Where a carried pane is just now, as what would happen if it were let go.</summary>
/// <param name="Kind">What would happen.</param>
/// <param name="Target">The pane it is over, for <see cref="DropKind.Beside"/> and <see cref="DropKind.Swap"/>.</param>
/// <param name="Side">The side of that pane, or of the section.</param>
/// <param name="Box">The place to light: a zone of the target, or the part
/// of the section the pane would take at an edge.</param>
public sealed record PaneDrop(DropKind Kind, string? Target, PaneSide Side, Box Box)
{
    public static PaneDrop Nowhere { get; } = new(DropKind.None, null, PaneSide.Left, default);
}

/// <summary>
/// Where a pane that is being carried by its heading can be put down, as
/// the design's sheet 10 draws it. On another pane there are five places:
/// above it, below it, left of it and right of it, each of which splits
/// that pane's room between the two, and its middle, which makes the two
/// change places. Along each edge of the section there is a sixth kind:
/// put down there, the pane takes that whole side. Over its own place, or
/// anywhere else, it goes back.
///
/// <para>Numbers in, a description out. The host draws the places
/// (<see cref="Zones"/>), asks where the pointer is (<see cref="At"/>) and
/// makes the tree that goes with the answer (<see cref="Apply"/>).</para>
/// </summary>
public static class PaneDrops
{
    /// <summary>How wide the band drawn along each edge of the section is.</summary>
    public const double EdgeBand = 5;

    /// <summary>How near an edge of the section the pointer has to be for
    /// that edge to be the place: more than the band that is drawn, which
    /// would be a hard thing to hit.</summary>
    public const double EdgeReach = 14;

    /// <summary>The room kept clear inside a pane's edge, and between two
    /// of its places.</summary>
    public const double Inset = 3.5, Gap = 7;

    /// <summary>A pane (under its heading) shorter than this, or narrower,
    /// has three places and not five: see <see cref="Zones"/>.</summary>
    public const double Small = 90;

    /// <summary>The share of a pane's inner width that "left of" and
    /// "right of" each take, and of its inner height that "above" and
    /// "below" each take. The middle has the rest.</summary>
    public const double SideShare = 0.238, EndShare = 0.255;

    /// <summary>
    /// The places on one pane, to draw: above, below, left of, right of and
    /// the middle, in that order, in the room under its heading.
    ///
    /// <para>A pane too short for that (folded to its heading, a stand-in)
    /// has three places side by side across the whole of it: above, the
    /// middle, below. One too narrow (a rail) has three down it: left of,
    /// the middle, right of. Splitting such a pane the other way would give
    /// two panes nobody could use.</para>
    /// </summary>
    public static IReadOnlyList<DropZone> Zones(Box pane, double heading)
    {
        bool narrow = pane.Width < Small;
        bool flat = !narrow && pane.Height - heading < Small;
        if (flat || narrow)
        {
            // Along the long way: a third each, less the gaps.
            double x = pane.X + Inset, y = pane.Y + Inset;
            double w = Math.Max(0, pane.Width - 2 * Inset), h = Math.Max(0, pane.Height - 2 * Inset);
            if (flat)
            {
                double each = Math.Max(0, (w - 2 * Gap) / 3);
                return
                [
                    new DropZone(DropKind.Beside, PaneSide.Above, new Box(x, y, each, h)),
                    new DropZone(DropKind.Beside, PaneSide.Below, new Box(x + 2 * (each + Gap), y, each, h)),
                    new DropZone(DropKind.Swap, PaneSide.Left, new Box(x + each + Gap, y, each, h)),
                ];
            }
            double part = Math.Max(0, (h - 2 * Gap) / 3);
            return
            [
                new DropZone(DropKind.Beside, PaneSide.Left, new Box(x, y, w, part)),
                new DropZone(DropKind.Beside, PaneSide.Right, new Box(x, y + 2 * (part + Gap), w, part)),
                new DropZone(DropKind.Swap, PaneSide.Left, new Box(x, y + part + Gap, w, part)),
            ];
        }

        double left = pane.X + Inset, top = pane.Y + heading + Inset;
        double width = pane.Width - 2 * Inset, height = pane.Height - heading - 2 * Inset;
        double end = (height - 2 * Gap) * EndShare, side = (width - 2 * Gap) * SideShare;
        double middleTop = top + end + Gap, middleHeight = height - 2 * (end + Gap);
        return
        [
            new DropZone(DropKind.Beside, PaneSide.Above, new Box(left, top, width, end)),
            new DropZone(DropKind.Beside, PaneSide.Below, new Box(left, middleTop + middleHeight + Gap, width, end)),
            new DropZone(DropKind.Beside, PaneSide.Left, new Box(left, middleTop, side, middleHeight)),
            new DropZone(DropKind.Beside, PaneSide.Right, new Box(left + width - side, middleTop, side, middleHeight)),
            new DropZone(DropKind.Swap, PaneSide.Left, new Box(left + side + Gap, middleTop, width - 2 * (side + Gap), middleHeight)),
        ];
    }

    /// <summary>The part of the section a pane would take at an edge: a
    /// third of it, along that whole side.</summary>
    public static Box EdgeLanding(PaneSide side, double width, double height) => side switch
    {
        PaneSide.Left => new Box(0, 0, width * SplitTree.EdgeShare, height),
        PaneSide.Right => new Box(width - width * SplitTree.EdgeShare, 0, width * SplitTree.EdgeShare, height),
        PaneSide.Above => new Box(0, 0, width, height * SplitTree.EdgeShare),
        _ => new Box(0, height - height * SplitTree.EdgeShare, width, height * SplitTree.EdgeShare),
    };

    /// <summary>
    /// What letting go at a point would do, for a pane carried over an
    /// arrangement in a room <paramref name="width"/> by <paramref name="height"/>.
    ///
    /// <para>Within <see cref="EdgeReach"/> of an edge of the room, that
    /// edge (the nearest, in a corner). Over another pane, whichever of its
    /// places the point is in or nearest: a pane's places leave no part of
    /// it that means nothing, and its heading counts as "above". Over the
    /// carried pane's own place, on a rule, or outside the room: nothing.</para>
    /// </summary>
    public static PaneDrop At(Arrangement arranged, string carried, double x, double y, double width, double height,
                              double heading)
    {
        if (double.IsNaN(x) || double.IsNaN(y) || x < 0 || y < 0 || x > width || y > height) return PaneDrop.Nowhere;

        // An edge of the section first: it lies over the panes along it.
        (double Far, PaneSide Side)[] edges =
            [(x, PaneSide.Left), (width - x, PaneSide.Right), (y, PaneSide.Above), (height - y, PaneSide.Below)];
        var nearest = edges.MinBy(e => e.Far);
        if (nearest.Far <= EdgeReach && arranged.Panes.Count > 1)
            return new PaneDrop(DropKind.Edge, null, nearest.Side, EdgeLanding(nearest.Side, width, height));

        foreach (var (key, box) in arranged.Panes)
        {
            if (x < box.X || x >= box.Right || y < box.Y || y >= box.Bottom) continue;
            if (key == carried) return PaneDrop.Nowhere;
            var zones = Zones(box, heading);
            var zone = zones.MinBy(z => Away(z.Box, x, y))!;
            // The four round the middle win a tie with it only by being
            // listed first; a point inside a zone is at no distance from it.
            return new PaneDrop(zone.Kind, key, zone.Side, zone.Box);
        }
        return PaneDrop.Nowhere;
    }

    /// <summary>How far a point is from a box: nothing, inside it.</summary>
    static double Away(Box box, double x, double y)
    {
        double dx = Math.Max(Math.Max(box.X - x, 0), x - box.Right);
        double dy = Math.Max(Math.Max(box.Y - y, 0), y - box.Bottom);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>The tree that letting go there makes. <see cref="DropKind.None"/>
    /// is the tree as it was, the same object.</summary>
    public static SplitNode Apply(SplitNode tree, string carried, PaneDrop drop) => drop.Kind switch
    {
        DropKind.Beside when drop.Target is { } target => SplitTree.Move(tree, carried, target, drop.Side),
        DropKind.Swap when drop.Target is { } target => SplitTree.Swap(tree, carried, target),
        DropKind.Edge => SplitTree.MoveToEdge(tree, carried, drop.Side),
        _ => tree,
    };
}
