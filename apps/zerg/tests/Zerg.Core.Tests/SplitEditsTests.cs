using System.Text.Json;
using Zerg.Core.Layout;

namespace Zerg.Core.Tests;

/// <summary>Changing a section's arrangement (a rule moved, a pane folded,
/// moved or swapped), repairing one that was read, writing one and reading
/// it back, and where a carried pane can be put down.</summary>
public class SplitEditsTests
{
    static readonly PaneLimits Limits = new();
    const double W = 1440, H = 706;

    static PaneSplit Columns(double ratio, SplitNode a, SplitNode b) => new(SplitWay.Columns, ratio, a, b);
    static PaneSplit Rows(double ratio, SplitNode a, SplitNode b) => new(SplitWay.Rows, ratio, a, b);
    static PaneLeaf Pane(string key, bool folded = false) => new(key, folded);

    static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    static readonly SplitNode[] Installed = [PaneLayouts.Damage, PaneLayouts.Healing, PaneLayouts.Compare];

    /// <summary>
    /// Four panes in two columns: what the Damage section was installed
    /// with until it gained its two side tables (2026-10-09), and so what
    /// a settings file from before then was arranged from. Most tests here
    /// change this one: they are about the change, not about which panes a
    /// section has.
    /// </summary>
    static readonly SplitNode Four =
        Columns(0.653, Rows(0.474, Pane("bars"), Pane("actions")), Rows(0.42, Pane("line"), Pane("drill")));

    // ------------------------------------------------------------- finding

    [Fact]
    public void A_pane_is_found_by_its_key_and_by_its_path()
    {
        Assert.Equal(Pane("line"), SplitTree.Leaf(Four, "line"));
        Assert.Null(SplitTree.Leaf(Four, "hline"));
        Assert.Equal("00", SplitTree.PathTo(Four, "bars"));
        Assert.Equal("11", SplitTree.PathTo(Four, "drill"));
        Assert.Equal("111", SplitTree.PathTo(PaneLayouts.Compare, "ctargets"));
        Assert.Equal("", SplitTree.PathTo(Pane("only"), "only"));
        Assert.Null(SplitTree.PathTo(Four, "nobody"));
    }

    // -------------------------------------------------------------- a rule

    [Fact]
    public void A_share_is_set_on_the_split_its_path_leads_to()
    {
        var tree = SplitTree.SetRatio(Four, "1", 0.6);

        Assert.Equal(0.6, SplitTree.At(tree, "1")!.Ratio);
        // Nothing else moved, and the tree it was made from is as it was.
        Assert.Equal(0.653, SplitTree.At(tree, "")!.Ratio);
        Assert.Equal(0.474, SplitTree.At(tree, "0")!.Ratio);
        Assert.Equal(0.42, SplitTree.At(Four, "1")!.Ratio);
        // The half that was not touched is the same object: nothing is copied for nothing.
        Assert.Same(((PaneSplit)Four).First, ((PaneSplit)tree).First);
    }

    [Theory]
    [InlineData(-3, 0)]
    [InlineData(7, 1)]
    [InlineData(0.123456, 0.1235)]
    public void A_share_is_kept_between_nothing_and_everything_to_four_places(double asked, double kept)
    {
        Assert.Equal(kept, SplitTree.At(SplitTree.SetRatio(Four, "", asked), "")!.Ratio);
    }

    [Fact]
    public void A_change_that_cannot_be_made_gives_the_same_tree_back()
    {
        var tree = Four;

        Assert.Same(tree, SplitTree.SetRatio(tree, "", double.NaN));
        Assert.Same(tree, SplitTree.SetRatio(tree, "00", 0.5));      // a pane, not a split
        Assert.Same(tree, SplitTree.SetRatio(tree, "0101", 0.5));    // nowhere
        Assert.Same(tree, SplitTree.SetRatio(tree, "x", 0.5));
        Assert.Same(tree, SplitTree.SetRatio(tree, "", 0.653));      // as it is already
        Assert.Same(tree, SplitTree.Fold(tree, "nobody", true));
        Assert.Same(tree, SplitTree.Fold(tree, "bars", false));
        Assert.Same(tree, SplitTree.ToggleFold(tree, "nobody"));
        Assert.Same(tree, SplitTree.Move(tree, "bars", "bars", PaneSide.Left));
        Assert.Same(tree, SplitTree.Move(tree, "bars", "nobody", PaneSide.Left));
        Assert.Same(tree, SplitTree.Move(tree, "nobody", "bars", PaneSide.Left));
        Assert.Same(tree, SplitTree.MoveToEdge(tree, "nobody", PaneSide.Left));
        Assert.Same(tree, SplitTree.Swap(tree, "bars", "bars"));
        Assert.Same(tree, SplitTree.Swap(tree, "bars", "nobody"));
        Assert.Same(tree, SplitTree.MoveBy(tree, "bars", PaneSide.Left));   // the section's edge
        // The last pane cannot be taken out of its own section.
        SplitNode only = Pane("only");
        Assert.Same(only, SplitTree.MoveToEdge(only, "only", PaneSide.Left));
        Assert.Null(SplitTree.Remove(only, "only"));
    }

    [Fact]
    public void A_rule_dragged_puts_its_edge_under_the_pointer()
    {
        var a = SplitTree.Arrange(Four, W, H, Limits);
        var upright = a.Dividers.Single(d => d.Path == "");

        var tree = SplitTree.Drag(Four, upright, 700);
        var b = SplitTree.Arrange(tree, W, H, Limits);

        Assert.Equal(700, b.Panes["bars"].Width);
        Assert.Equal(700, b.Dividers.Single(d => d.Path == "").Line.X);
        Assert.Equal(739, b.Panes["line"].Width);
        // The rule lying in the right-hand column, which begins at 701 now.
        var lying = b.Dividers.Single(d => d.Path == "1");
        var down = SplitTree.Arrange(SplitTree.Drag(tree, lying, 400), W, H, Limits);
        Assert.Equal(400, down.Panes["line"].Height);
        Assert.Equal(new Box(701, 401, 739, 305), down.Panes["drill"]);
    }

    [Theory]
    [InlineData(-500, 320)]     // past the least width of the left-hand column
    [InlineData(100, 320)]
    [InlineData(5000, 1119)]    // and of the right-hand one: 1440 - 1 - 320
    public void A_rule_stops_where_a_pane_would_be_under_its_least_size(double asked, double comes)
    {
        var a = SplitTree.Arrange(Four, W, H, Limits);
        var tree = SplitTree.Drag(Four, a.Dividers.Single(d => d.Path == ""), asked);

        // The share kept is the one that is acted on: it needs no correcting.
        double ratio = SplitTree.At(tree, "")!.Ratio;
        Assert.Equal(comes, Math.Round(ratio * 1439), 0);
        var b = SplitTree.Arrange(tree, W, H, Limits);
        Assert.Equal(comes, b.Panes["bars"].Width);
        foreach (var box in b.Panes.Values)
        {
            Assert.True(box.Width >= 320, "narrower than the least");
            Assert.True(box.Height >= 132, "shorter than the least");
        }
    }

    [Fact]
    public void A_rule_that_is_held_is_not_dragged()
    {
        // The drill-down folded: the rule over it has nothing to give.
        var folded = new HashSet<string> { "drill" };
        var a = SplitTree.Arrange(Four, W, H, Limits, null, folded);
        var held = a.Dividers.Single(d => d.Path == "1");

        Assert.True(held.Held);
        Assert.Same(Four, SplitTree.Drag(Four, held, 200));
    }

    [Fact]
    public void A_rule_is_set_back_to_the_installed_share_where_the_installed_tree_has_that_split()
    {
        var dragged = SplitTree.SetRatio(Four, "0", 0.8);
        Assert.Equal(0.474, SplitTree.InstalledRatio(dragged, Four, "0"));
        Assert.Equal(0.653, SplitTree.InstalledRatio(dragged, Four, ""));

        // A split a moved pane made: a half. So for one of the other way in the same place.
        var moved = SplitTree.Move(Four, "line", "bars", PaneSide.Right);
        Assert.Equal(SplitWay.Columns, SplitTree.At(moved, "00")!.Way);
        Assert.Equal(0.5, SplitTree.InstalledRatio(moved, Four, "00"));
        var turned = SplitTree.MoveToEdge(Four, "line", PaneSide.Above);
        Assert.Equal(0.5, SplitTree.InstalledRatio(turned, Four, ""));
        Assert.Equal(0.5, SplitTree.InstalledRatio(dragged, Four, "0000"));
    }

    // -------------------------------------------------------------- a fold

    [Fact]
    public void A_pane_folds_and_opens_again()
    {
        var folded = SplitTree.ToggleFold(Four, "bars");

        Assert.True(SplitTree.Leaf(folded, "bars")!.Folded);
        Assert.False(SplitTree.Leaf(Four, "bars")!.Folded);
        var a = SplitTree.Arrange(folded, W, H, Limits);
        Assert.Equal(new Box(0, 0, 940, 30), a.Panes["bars"]);
        Assert.Equal(new Box(0, 31, 940, 675), a.Panes["actions"]);
        // Opened again it is the installed tree, share and all.
        Assert.Equal(Four, SplitTree.ToggleFold(folded, "bars"));
    }

    // -------------------------------------------------------------- a move

    [Fact]
    public void A_pane_taken_out_leaves_its_room_to_its_neighbour()
    {
        var rest = SplitTree.Remove(Four, "line")!;

        Assert.Equal(Columns(0.653, Rows(0.474, Pane("bars"), Pane("actions")), Pane("drill")), rest);
        Assert.Equal(new[] { "bars", "actions", "drill" }, SplitTree.Keys(rest));
        Assert.Equal(Four, SplitTree.Remove(Four, "nobody"));
    }

    [Theory]
    [InlineData(PaneSide.Left, SplitWay.Columns, true)]
    [InlineData(PaneSide.Right, SplitWay.Columns, false)]
    [InlineData(PaneSide.Above, SplitWay.Rows, true)]
    [InlineData(PaneSide.Below, SplitWay.Rows, false)]
    public void A_pane_moved_beside_another_halves_that_panes_room(PaneSide side, SplitWay way, bool first)
    {
        // The chart, put beside the Actions table.
        var tree = SplitTree.Move(Four, "line", "actions", side);

        // Where the chart was, the drill-down has the column.
        Assert.Equal(Pane("drill"), ((PaneSplit)tree).Second);
        var made = SplitTree.At(tree, "01")!;
        Assert.Equal(way, made.Way);
        Assert.Equal(0.5, made.Ratio);
        Assert.Equal(first ? Pane("line") : Pane("actions"), made.First);
        Assert.Equal(first ? Pane("actions") : Pane("line"), made.Second);
        // Every pane still there, once.
        Assert.Equal(new[] { "actions", "bars", "drill", "line" }, SplitTree.Keys(tree).Order());

        var before = SplitTree.Arrange(Four, W, H, Limits).Panes["actions"];
        var a = SplitTree.Arrange(tree, W, H, Limits);
        var room = Union(a.Panes["line"], a.Panes["actions"]);
        Assert.Equal(before, room);
    }

    static Box Union(Box a, Box b)
    {
        double x = Math.Min(a.X, b.X), y = Math.Min(a.Y, b.Y);
        return new Box(x, y, Math.Max(a.Right, b.Right) - x, Math.Max(a.Bottom, b.Bottom) - y);
    }

    [Fact]
    public void A_pane_moved_beside_its_own_neighbour_turns_their_split()
    {
        // The per-character table from over Actions to the right of it.
        var tree = SplitTree.Move(Four, "bars", "actions", PaneSide.Right);

        Assert.Equal(Columns(0.653, Columns(0.5, Pane("actions"), Pane("bars")),
                             Rows(0.42, Pane("line"), Pane("drill"))), tree);
    }

    [Fact]
    public void A_moved_pane_keeps_its_fold()
    {
        var tree = SplitTree.Move(SplitTree.Fold(Four, "line", true), "line", "bars", PaneSide.Below);

        Assert.True(SplitTree.Leaf(tree, "line")!.Folded);
    }

    [Theory]
    [InlineData(PaneSide.Left, 0, 0, 480, 706)]
    [InlineData(PaneSide.Right, 960, 0, 480, 706)]
    [InlineData(PaneSide.Above, 0, 0, 1440, 235)]
    [InlineData(PaneSide.Below, 0, 471, 1440, 235)]
    public void A_pane_moved_to_an_edge_takes_that_whole_side(PaneSide side, double x, double y, double width, double height)
    {
        var tree = SplitTree.MoveToEdge(Four, "line", side);

        var a = SplitTree.Arrange(tree, W, H, Limits);
        Assert.Equal(new Box(x, y, width, height), a.Panes["line"]);
        // What is left keeps its own arrangement: two tables in a column, the drill-down beside them.
        var rest = side is PaneSide.Left or PaneSide.Above ? ((PaneSplit)tree).Second : ((PaneSplit)tree).First;
        Assert.Equal(SplitTree.Remove(Four, "line"), rest);
        Assert.Equal(4, a.Panes.Count);
    }

    [Fact]
    public void Two_panes_change_places_and_nothing_else_moves()
    {
        // Sheet 10, B: the chart and Actions have changed places.
        var tree = SplitTree.Swap(Four, "line", "actions");

        Assert.Equal(Columns(0.653, Rows(0.474, Pane("bars"), Pane("line")),
                             Rows(0.42, Pane("actions"), Pane("drill"))), tree);
        var a = SplitTree.Arrange(tree, W, H, Limits);
        Assert.Equal(new Box(0, 335, 940, 371), a.Panes["line"]);
        Assert.Equal(new Box(941, 0, 499, 296), a.Panes["actions"]);
        // Twice is not at all.
        Assert.Equal(Four, SplitTree.Swap(tree, "actions", "line"));
    }

    [Fact]
    public void Each_of_two_swapped_panes_keeps_its_own_fold()
    {
        var tree = SplitTree.Swap(SplitTree.Fold(Four, "line", true), "line", "actions");

        Assert.Equal(Pane("line", folded: true), SplitTree.At(tree, "0")!.Second);
        Assert.Equal(Pane("actions"), SplitTree.At(tree, "1")!.First);
    }

    [Theory]
    [InlineData("bars", PaneSide.Right, "line")]
    [InlineData("bars", PaneSide.Below, "actions")]
    [InlineData("bars", PaneSide.Left, null)]
    [InlineData("bars", PaneSide.Above, null)]
    [InlineData("actions", PaneSide.Right, "drill")]
    [InlineData("actions", PaneSide.Above, "bars")]
    [InlineData("line", PaneSide.Left, "bars")]
    [InlineData("line", PaneSide.Below, "drill")]
    [InlineData("drill", PaneSide.Left, "actions")]
    [InlineData("drill", PaneSide.Above, "line")]
    [InlineData("drill", PaneSide.Right, null)]
    [InlineData("drill", PaneSide.Below, null)]
    [InlineData("nobody", PaneSide.Left, null)]
    public void The_neighbour_on_a_side_is_the_pane_that_shares_most_of_that_edge(string key, PaneSide side, string? neighbour)
    {
        Assert.Equal(neighbour, SplitTree.Neighbour(Four, key, side));
    }

    [Theory]
    [InlineData("cactors", PaneSide.Right, "ckinds")]   // the middle of three shares the most of its edge
    [InlineData("cpace", PaneSide.Left, "cactors")]
    [InlineData("ckinds", PaneSide.Left, "cactors")]
    [InlineData("ctargets", PaneSide.Left, "cactors")]
    [InlineData("cpace", PaneSide.Below, "ckinds")]
    [InlineData("ctargets", PaneSide.Above, "ckinds")]
    [InlineData("cpace", PaneSide.Above, null)]
    public void And_so_it_is_in_Compares_three_in_a_column(string key, PaneSide side, string? neighbour)
    {
        Assert.Equal(neighbour, SplitTree.Neighbour(PaneLayouts.Compare, key, side));
    }

    [Fact]
    public void A_fold_does_not_change_who_a_panes_neighbour_is()
    {
        // The menu's Move goes by where the panes belong, not by what is folded just now.
        var folded = SplitTree.Fold(Four, "line", true);
        Assert.Equal("line", SplitTree.Neighbour(folded, "bars", PaneSide.Right));
    }

    [Fact]
    public void Move_by_one_place_swaps_with_the_neighbour_there()
    {
        Assert.Equal(SplitTree.Swap(Four, "line", "bars"), SplitTree.MoveBy(Four, "line", PaneSide.Left));
        Assert.Equal(SplitTree.Swap(Four, "line", "drill"), SplitTree.MoveBy(Four, "line", PaneSide.Below));
        // And back again is the installed arrangement.
        Assert.Equal(Four,
            SplitTree.MoveBy(SplitTree.MoveBy(Four, "line", PaneSide.Left), "line", PaneSide.Right));
    }

    // -------------------------------------------------------- least sizes

    [Fact]
    public void However_the_panes_are_rearranged_none_is_under_its_least_size()
    {
        // Two hundred arrangements made by chance from each installed one,
        // each in the smallest room it says it needs and in a large one.
        var random = new Random(11);
        var sides = Enum.GetValues<PaneSide>();
        foreach (var installed in Installed)
        {
            var keys = SplitTree.Keys(installed);
            var tree = installed;
            for (int step = 0; step < 200; step++)
            {
                string a = keys[random.Next(keys.Count)], b = keys[random.Next(keys.Count)];
                var side = sides[random.Next(sides.Length)];
                tree = random.Next(6) switch
                {
                    0 => SplitTree.Move(tree, a, b, side),
                    1 => SplitTree.MoveToEdge(tree, a, side),
                    2 => SplitTree.Swap(tree, a, b),
                    3 => SplitTree.MoveBy(tree, a, side),
                    4 => SplitTree.ToggleFold(tree, a),
                    _ => SplitTree.SetRatio(tree, SplitTree.PathTo(tree, a)![..^1], random.NextDouble() * 1.4 - 0.2),
                };

                Assert.Equal(keys.Order(), SplitTree.Keys(tree).Order());
                var (w, h) = SplitTree.Least(tree, Limits);
                foreach (var (width, height) in new[] { (w, h), (w + 900, h + 500) })
                {
                    var arranged = SplitTree.Arrange(tree, width, height, Limits);
                    Assert.Equal(keys.Count, arranged.Panes.Count);
                    foreach (var (key, box) in arranged.Panes)
                    {
                        bool folded = SplitTree.Leaf(tree, key)!.Folded;
                        bool rail = arranged.Rails.Contains(key);
                        Assert.True(box.X >= 0 && box.Y >= 0 && box.Right <= width + 1e-6 && box.Bottom <= height + 1e-6, "outside the room");
                        if (rail) Assert.Equal(30, box.Width);
                        else Assert.True(box.Width >= 320, $"{key} is {box.Width} wide after step {step}");
                        if (folded && !rail) Assert.Equal(30, box.Height);
                        else Assert.True(box.Height >= 132, $"{key} is {box.Height} tall after step {step}");
                    }
                }
            }
        }
    }

    // ------------------------------------------------------------ a repair

    [Fact]
    public void A_tree_that_is_whole_is_left_as_it_is()
    {
        var moved = SplitTree.Swap(Four, "line", "actions");

        Assert.Same(moved, SplitTree.Repair(moved, Four));
        // Said the same way as the installed one, it is the installed one.
        Assert.Same(Four, SplitTree.Repair(SplitTree.Swap(moved, "line", "actions"), Four));
        Assert.Same(Four, SplitTree.Repair(null, Four));
    }

    [Fact]
    public void A_pane_this_build_has_not_got_is_dropped_and_its_neighbour_takes_its_room()
    {
        var saved = Columns(0.6, Rows(0.5, Pane("bars"), Pane("minimap")), Rows(0.3, Pane("line"), Rows(0.5, Pane("actions"), Pane("drill"))));

        Assert.Equal(Columns(0.6, Pane("bars"), Rows(0.3, Pane("line"), Rows(0.5, Pane("actions"), Pane("drill")))),
                     SplitTree.Repair(saved, Four));
    }

    [Fact]
    public void A_pane_the_tree_does_not_mention_is_put_beside_the_pane_it_is_installed_beside()
    {
        // Written by a build that had no drill-down pane, say. As
        // installed the drill-down is under the chart, with 0.42 of their
        // room to the chart: so it is here, wherever the chart was put.
        var saved = Columns(0.5, Pane("bars"), Rows(0.5, Pane("line"), Pane("actions")));

        var tree = SplitTree.Repair(saved, Four);

        Assert.Equal(Columns(0.5, Pane("bars"), Rows(0.5, Rows(0.42, Pane("line"), Pane("drill")), Pane("actions"))), tree);
        // Everything the player did is as it was: only the chart's room is shared.
        var before = SplitTree.Arrange(saved, W, H, Limits);
        var after = SplitTree.Arrange(tree, W, H, Limits);
        Assert.Equal(before.Panes["bars"], after.Panes["bars"]);
        Assert.Equal(before.Panes["actions"].Width, after.Panes["actions"].Width);
        Assert.Equal(before.Panes["line"].X, after.Panes["drill"].X);
        Assert.Equal(after.Panes["line"].Bottom + 1, after.Panes["drill"].Y);
    }

    [Fact]
    public void A_pane_that_comes_first_as_installed_is_put_back_first()
    {
        // The per-character table left out: installed, it is over Actions.
        var saved = Columns(0.3, Rows(0.5, Pane("line"), Pane("drill")), Pane("actions"));

        Assert.Equal(Columns(0.3, Rows(0.5, Pane("line"), Pane("drill")), Rows(0.474, Pane("bars"), Pane("actions"))),
                     SplitTree.Repair(saved, Four));
    }

    [Fact]
    public void A_pane_whose_own_neighbour_is_missing_too_is_put_beside_the_nearest_piece_that_is_there()
    {
        // Only the two tables: the chart's own neighbour, the drill-down,
        // is not there, so the chart goes beside what its column is
        // installed beside (the tables, whole, with the installed share),
        // and the drill-down then under the chart.
        var saved = Rows(0.3, Pane("actions"), Pane("bars"));

        Assert.Equal(Columns(0.653, saved, Rows(0.42, Pane("line"), Pane("drill"))), SplitTree.Repair(saved, Four));

        // Wherever its own neighbour was put, that is where it goes: the
        // chart over the drill-down, though the drill-down is over Actions.
        var apart = Columns(0.5, Pane("bars"), Rows(0.5, Pane("drill"), Pane("actions")));
        Assert.Equal(Columns(0.5, Pane("bars"), Rows(0.5, Rows(0.42, Pane("line"), Pane("drill")), Pane("actions"))),
                     SplitTree.Repair(apart, Four));
    }

    [Fact]
    public void Three_panes_left_out_come_back_as_the_piece_they_are_installed_as()
    {
        // The Damage section's chart between the tables, and neither side
        // table nor the drill-down. As installed those three are under the
        // chart: By damage type goes there, By target under it, and the
        // drill-down under the two of them together, not under the last.
        var saved = Columns(0.5, Pane("bars"), Rows(0.5, Pane("line"), Pane("actions")));

        Assert.Equal(
            Columns(0.5, Pane("bars"),
                Rows(0.5, Rows(0.36, Pane("line"), Rows(0.5, Rows(0.5, Pane("types"), Pane("targets")), Pane("drill"))),
                     Pane("actions"))),
            SplitTree.Repair(saved, PaneLayouts.Damage));
    }

    // ------------------------------- a tree from before the two side tables

    [Fact]
    public void An_arrangement_saved_before_the_side_tables_gains_them_where_they_are_installed()
    {
        // The four panes as they were installed, with two shares a player
        // had set: the new installed shape, and those shares still theirs.
        var saved = SplitTree.SetRatio(SplitTree.SetRatio(Four, "", 0.6), "1", 0.3);

        var tree = SplitTree.Repair(saved, PaneLayouts.Damage);

        Assert.Equal(
            Columns(0.6, Rows(0.474, Pane("bars"), Pane("actions")),
                Rows(0.3, Pane("line"), Rows(0.5, Rows(0.5, Pane("types"), Pane("targets")), Pane("drill")))),
            tree);
        // Arranged as installed but for the shares, it has the installed order.
        Assert.Equal(SplitTree.Keys(PaneLayouts.Damage), SplitTree.Keys(tree));
        // And the installed four, said outright, is the installed six.
        Assert.Same(PaneLayouts.Damage,
                    SplitTree.Repair(SplitTree.SetRatio(Four, "1", 0.36), PaneLayouts.Damage));
    }

    [Fact]
    public void An_arrangement_a_player_made_keeps_everything_and_gains_the_side_tables_by_the_drill_down()
    {
        // The chart over the per-character table at the left, Actions over
        // the drill-down at the right: an arrangement as a settings file
        // had it on 2026-10-09, before the build with the side tables ran.
        var saved = Columns(0.652, Rows(0.474, Pane("line"), Pane("bars")), Rows(0.42, Pane("actions"), Pane("drill")));

        var tree = SplitTree.Repair(saved, PaneLayouts.Damage);

        Assert.Equal(
            Columns(0.652, Rows(0.474, Pane("line"), Pane("bars")),
                Rows(0.42, Pane("actions"), Rows(0.5, Rows(0.5, Pane("types"), Pane("targets")), Pane("drill")))),
            tree);
        // Nothing that was there has moved or changed size but the
        // drill-down, which the tables stand over in what was its room;
        // every pane has its least size or more, and nothing is lost.
        var before = SplitTree.Arrange(saved, 1700, 870, Limits);
        var after = SplitTree.Arrange(tree, 1700, 870, Limits);
        foreach (string key in new[] { "line", "bars", "actions" }) Assert.Equal(before.Panes[key], after.Panes[key]);
        Assert.Equal(6, after.Panes.Count);
        Assert.Equal(before.Panes["drill"].Y, after.Panes["types"].Y);
        Assert.Equal(before.Panes["drill"].Bottom, after.Panes["drill"].Bottom);
        foreach (string key in new[] { "types", "targets", "drill" })
        {
            Assert.Equal(before.Panes["drill"].X, after.Panes[key].X);
            Assert.Equal(before.Panes["drill"].Width, after.Panes[key].Width);
            Assert.True(after.Panes[key].Width >= 320 && after.Panes[key].Height >= 132, key);
        }
        // While the drill-down has nothing picked it is its heading, where
        // it was, and Actions keeps the share it had been given.
        var folded = new HashSet<string> { "drill" };
        Assert.Equal(SplitTree.Arrange(saved, 1700, 870, Limits, null, folded).Panes["drill"],
                     SplitTree.Arrange(tree, 1700, 870, Limits, null, folded).Panes["drill"]);
        // Repaired again, it is the same tree: a repair is made once.
        Assert.Same(tree, SplitTree.Repair(tree, PaneLayouts.Damage));
    }

    [Fact]
    public void A_saved_damage_arrangement_from_before_the_side_tables_is_read_with_them()
    {
        var layouts = Json(
            """{"Damage":{"split":"columns","share":0.652,"first":{"split":"rows","share":0.474,"first":{"pane":"line"},"second":{"pane":"bars"}},"second":{"split":"rows","share":0.42,"first":{"pane":"actions"},"second":{"pane":"drill"}}}}""");

        var tree = PaneLayouts.Read(layouts, "Damage");

        Assert.Equal(new[] { "line", "bars", "actions", "types", "targets", "drill" }, SplitTree.Keys(tree));
        Assert.Equal(0.652, SplitTree.At(tree, "")!.Ratio);
        Assert.Equal(0.42, SplitTree.At(tree, "1")!.Ratio);
        // Written again it has all six, and reads back as it is.
        var written = PaneLayouts.Write(tree, PaneLayouts.Healing, PaneLayouts.Compare)!.Value;
        Assert.Equal(tree, PaneLayouts.Read(written, "Damage"));
    }

    [Fact]
    public void One_side_table_left_out_goes_beside_the_other()
    {
        // By target dragged to the left edge, and By damage type missing
        // (a file edited by hand): the one goes over the other, as installed.
        var saved = SplitTree.Remove(SplitTree.MoveToEdge(PaneLayouts.Damage, "targets", PaneSide.Left), "types")!;

        var tree = SplitTree.Repair(saved, PaneLayouts.Damage);

        Assert.Equal(Rows(0.5, Pane("types"), Pane("targets")), SplitTree.At(tree, "0"));
        Assert.Equal(6, SplitTree.Keys(tree).Count);
    }

    [Fact]
    public void A_pane_named_twice_is_kept_where_it_is_first_met()
    {
        var saved = Columns(0.5, Rows(0.5, Pane("bars"), Pane("line")), Rows(0.5, Pane("line", folded: true), Rows(0.5, Pane("actions"), Pane("drill"))));

        Assert.Equal(Columns(0.5, Rows(0.5, Pane("bars"), Pane("line")), Rows(0.5, Pane("actions"), Pane("drill"))),
                     SplitTree.Repair(saved, Four));
    }

    [Fact]
    public void A_tree_of_another_sections_panes_is_the_installed_one()
    {
        Assert.Same(Four, SplitTree.Repair(PaneLayouts.Healing, Four));
        Assert.Same(PaneLayouts.Compare, SplitTree.Repair(Pane("bars"), PaneLayouts.Compare));
    }

    [Fact]
    public void A_share_that_is_not_one_is_made_one()
    {
        var saved = Columns(double.NaN, Rows(-4, Pane("bars"), Pane("actions")), Rows(9, Pane("line"), Pane("drill")));

        var tree = SplitTree.Repair(saved, Four);

        Assert.Equal(Columns(0.5, Rows(0, Pane("bars"), Pane("actions")), Rows(1, Pane("line"), Pane("drill"))), tree);
        // And whatever the shares say, the panes keep their least sizes.
        foreach (var box in SplitTree.Arrange(tree, W, H, Limits).Panes.Values)
        {
            Assert.True(box.Width >= 320);
            Assert.True(box.Height >= 132);
        }
    }

    [Fact]
    public void A_tree_too_deep_to_be_anything_but_damage_is_the_installed_one()
    {
        SplitNode deep = Pane("bars");
        for (int i = 0; i < 200; i++) deep = Rows(0.5, deep, Pane("x" + i));

        Assert.Same(Four, SplitTree.Repair(deep, Four));
    }

    // ------------------------------------------------------------ written

    [Fact]
    public void A_tree_is_written_as_shares_of_the_room()
    {
        var tree = SplitTree.Fold(PaneLayouts.Compare, "ckinds", true);

        Assert.Equal(
            """{"split":"columns","share":0.653,"first":{"pane":"cactors"},"second":{"split":"rows","share":0.3136,"first":""" +
            """{"pane":"cpace"},"second":{"split":"rows","share":0.6145,"first":{"pane":"ckinds","folded":true},"second":""" +
            """{"pane":"ctargets"}}}}""",
            SplitTree.ToJson(tree).ToJsonString());
    }

    [Fact]
    public void What_is_written_is_read_back_the_same()
    {
        foreach (var installed in Installed)
        {
            var keys = SplitTree.Keys(installed);
            var changed = SplitTree.Fold(SplitTree.MoveToEdge(SplitTree.SetRatio(installed, "", 0.41237), keys[1], PaneSide.Above), keys[0], true);
            foreach (var tree in new[] { installed, changed })
                Assert.Equal(tree, SplitTree.FromJson(Json(SplitTree.ToJson(tree).ToJsonString())));
        }
    }

    [Theory]
    [InlineData("5")]
    [InlineData("\"bars\"")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"pane":7}""")]
    [InlineData("""{"pane":""}""")]
    [InlineData("""{"split":"rows"}""")]
    [InlineData("""{"split":"rows","first":3,"second":[1,2]}""")]
    public void What_is_not_a_tree_reads_as_none(string json)
    {
        Assert.Null(SplitTree.FromJson(Json(json)));
    }

    [Fact]
    public void A_damaged_tree_is_read_as_far_as_it_can_be()
    {
        // The way is misspelt (side by side, then), the share is in quotes,
        // one half is nonsense and one pane says "folded" in a way that is not yes.
        var read = SplitTree.FromJson(Json(
            """{"split":"sideways","share":"0.25","first":{"pane":"bars","folded":"yes"},"second":{"split":"ROWS","share":"lots","first":{"pane":"line"},"second":{"split":"rows","first":12,"second":{"pane":"drill","folded":true}}}}"""));

        Assert.Equal(Columns(0.25, Pane("bars"), Rows(0.5, Pane("line"), Pane("drill", folded: true))), read);
    }

    [Fact]
    public void A_settings_file_from_before_arrangements_were_saved_gives_the_installed_ones()
    {
        Assert.Same(PaneLayouts.Damage, PaneLayouts.Read(null, "Damage"));
        Assert.Same(PaneLayouts.Healing, PaneLayouts.Read(null, "Healing"));
        Assert.Same(PaneLayouts.Compare, PaneLayouts.Read(null, "Compare"));
        // And so does one that mentions only another section.
        var one = Json("""{"Healing":{"split":"rows","share":0.5,"first":{"pane":"hbars"},"second":{"pane":"hline"}}}""");
        Assert.Same(PaneLayouts.Damage, PaneLayouts.Read(one, "Damage"));
        Assert.Throws<ArgumentException>(() => PaneLayouts.Read(null, "Settings"));
    }

    [Theory]
    [InlineData("7")]
    [InlineData("\"wide\"")]
    [InlineData("[1,2,3]")]
    [InlineData("""{"Damage":null}""")]
    [InlineData("""{"Damage":"as installed"}""")]
    [InlineData("""{"Damage":{"split":"columns","share":0.5}}""")]
    [InlineData("""{"Damage":{"pane":"hbars"}}""")]
    [InlineData("""{"damage":{"pane":"bars"}}""")]
    public void Damage_under_layouts_gives_the_installed_arrangement(string layouts)
    {
        Assert.Same(PaneLayouts.Damage, PaneLayouts.Read(Json(layouts), "Damage"));
    }

    [Fact]
    public void A_saved_arrangement_is_read_and_repaired()
    {
        // Healing, with its chart moved under the table, a pane this build
        // has not got, and its Heals table left out.
        var layouts = Json(
            """{"Healing":{"split":"columns","share":0.7,"first":{"split":"rows","share":0.4,"first":{"pane":"hbars"},"second":{"pane":"hline","folded":true}},"second":{"split":"rows","share":0.5,"first":{"pane":"minimap"},"second":{"pane":"hdrill"}}}}""");

        var tree = PaneLayouts.Read(layouts, "Healing");

        // The Heals table is put back under the per-character table, where
        // it is installed, with the share it is installed with.
        Assert.Equal(Columns(0.7, Rows(0.4, Rows(0.318, Pane("hbars"), Pane("hactions")), Pane("hline", folded: true)), Pane("hdrill")), tree);
        Assert.Same(PaneLayouts.Compare, PaneLayouts.Read(layouts, "Compare"));
    }

    [Fact]
    public void Only_what_is_not_as_installed_is_written()
    {
        Assert.Null(PaneLayouts.Write(PaneLayouts.Damage, PaneLayouts.Healing, PaneLayouts.Compare));

        var healing = SplitTree.Swap(PaneLayouts.Healing, "hline", "hactions");
        var written = PaneLayouts.Write(PaneLayouts.Damage, healing, PaneLayouts.Compare)!.Value;

        Assert.Equal(new[] { "Healing" }, written.EnumerateObject().Select(p => p.Name));
        Assert.Equal(healing, PaneLayouts.Read(written, "Healing"));
        Assert.Same(PaneLayouts.Damage, PaneLayouts.Read(written, "Damage"));

        var all = PaneLayouts.Write(SplitTree.ToggleFold(PaneLayouts.Damage, "bars"), healing,
                                    SplitTree.SetRatio(PaneLayouts.Compare, "", 0.5))!.Value;
        Assert.Equal(new[] { "Damage", "Healing", "Compare" }, all.EnumerateObject().Select(p => p.Name));
        Assert.True(SplitTree.Leaf(PaneLayouts.Read(all, "Damage"), "bars")!.Folded);
        Assert.Equal(0.5, SplitTree.At(PaneLayouts.Read(all, "Compare"), "")!.Ratio);
    }

    [Fact]
    public void Each_section_has_an_installed_arrangement_by_its_name()
    {
        Assert.Equal(new[] { "Damage", "Healing", "Compare" }, PaneLayouts.Sections);
        Assert.Same(PaneLayouts.Damage, PaneLayouts.Installed("Damage"));
        Assert.Same(PaneLayouts.Healing, PaneLayouts.Installed("Healing"));
        Assert.Same(PaneLayouts.Compare, PaneLayouts.Installed("Compare"));
        Assert.Null(PaneLayouts.Installed("View"));
        Assert.Null(PaneLayouts.Installed(null));
    }

    // ------------------------------------------- where a pane can be put down

    [Fact]
    public void A_pane_has_five_places_as_the_sheet_draws_them()
    {
        // Sheet 10, A: the Actions pane, 940 by 371 at 0,505 of the window
        // (0,335 of the section); its heading is 30.
        var zones = PaneDrops.Zones(new Box(0, 335, 940, 371), 30);

        Assert.Equal(5, zones.Count);
        Near(new Box(3.5, 368.5, 933, 81.66), zones[0].Box);       // above
        Near(new Box(3.5, 620.84, 933, 81.66), zones[1].Box);      // below
        Near(new Box(3.5, 457.16, 218.6, 156.68), zones[2].Box);   // left of
        Near(new Box(717.9, 457.16, 218.6, 156.68), zones[3].Box); // right of
        Near(new Box(229.1, 457.16, 481.8, 156.68), zones[4].Box); // the middle
        Assert.Equal(new[] { PaneSide.Above, PaneSide.Below, PaneSide.Left, PaneSide.Right },
                     zones.Take(4).Select(z => z.Side));
        Assert.All(zones.Take(4), z => Assert.Equal(DropKind.Beside, z.Kind));
        Assert.Equal(DropKind.Swap, zones[4].Kind);
    }

    static void Near(Box expected, Box actual)
    {
        Assert.Equal(expected.X, actual.X, 0);
        Assert.Equal(expected.Y, actual.Y, 0);
        Assert.Equal(expected.Width, actual.Width, 0);
        Assert.Equal(expected.Height, actual.Height, 0);
    }

    [Fact]
    public void A_pane_folded_to_its_heading_has_three_places_across_it()
    {
        var zones = PaneDrops.Zones(new Box(941, 676, 499, 30), 30);

        Assert.Equal(new[] { (DropKind.Beside, PaneSide.Above), (DropKind.Beside, PaneSide.Below), (DropKind.Swap, PaneSide.Left) },
                     zones.Select(z => (z.Kind, z.Side)));
        // Above at the left, the middle between, below at the right; none outside the pane.
        Assert.True(zones[0].Box.Right < zones[2].Box.X && zones[2].Box.Right < zones[1].Box.X);
        Assert.All(zones, z => Assert.True(z.Box.X >= 941 && z.Box.Right <= 1440 && z.Box.Y >= 676 && z.Box.Bottom <= 706));
        // A stand-in, 46 tall: the same three.
        Assert.Equal(3, PaneDrops.Zones(new Box(0, 0, 940, 46), 30).Count);
    }

    [Fact]
    public void A_rail_has_three_places_down_it()
    {
        var zones = PaneDrops.Zones(new Box(0, 0, 30, 706), 30);

        Assert.Equal(new[] { (DropKind.Beside, PaneSide.Left), (DropKind.Beside, PaneSide.Right), (DropKind.Swap, PaneSide.Left) },
                     zones.Select(z => (z.Kind, z.Side)));
        Assert.True(zones[0].Box.Bottom < zones[2].Box.Y && zones[2].Box.Bottom < zones[1].Box.Y);
    }

    [Theory]
    // Over Actions (0,335 940 by 371), carrying the chart.
    [InlineData(470, 520, DropKind.Swap, "actions", PaneSide.Left)]
    [InlineData(470, 400, DropKind.Beside, "actions", PaneSide.Above)]
    [InlineData(470, 350, DropKind.Beside, "actions", PaneSide.Above)]   // its heading counts as above
    [InlineData(470, 660, DropKind.Beside, "actions", PaneSide.Below)]
    [InlineData(100, 520, DropKind.Beside, "actions", PaneSide.Left)]
    [InlineData(850, 520, DropKind.Beside, "actions", PaneSide.Right)]
    [InlineData(225, 520, DropKind.Beside, "actions", PaneSide.Left)]    // in the gap: the nearer place
    // Over the per-character table and the drill-down.
    [InlineData(470, 180, DropKind.Swap, "bars", PaneSide.Left)]
    [InlineData(1200, 500, DropKind.Swap, "drill", PaneSide.Left)]
    // The section's edges, which lie over the panes along them.
    [InlineData(4, 300, DropKind.Edge, null, PaneSide.Left)]
    [InlineData(1436, 300, DropKind.Edge, null, PaneSide.Right)]
    [InlineData(700, 3, DropKind.Edge, null, PaneSide.Above)]
    [InlineData(700, 700, DropKind.Edge, null, PaneSide.Below)]
    [InlineData(1200, 12, DropKind.Edge, null, PaneSide.Above)]          // even over its own place
    // Its own place, a rule, and outside the section: it goes back.
    [InlineData(1200, 150, DropKind.None, null, PaneSide.Left)]
    [InlineData(940.5, 300, DropKind.None, null, PaneSide.Left)]
    [InlineData(-20, 300, DropKind.None, null, PaneSide.Left)]
    [InlineData(700, 900, DropKind.None, null, PaneSide.Left)]
    [InlineData(double.NaN, 300, DropKind.None, null, PaneSide.Left)]
    public void Letting_go_at_a_point_is_one_of_those_places_or_nothing(double x, double y, DropKind kind, string? target, PaneSide side)
    {
        var arranged = SplitTree.Arrange(Four, W, H, Limits);

        var drop = PaneDrops.At(arranged, "line", x, y, W, H, 30);

        Assert.Equal(kind, drop.Kind);
        Assert.Equal(target, drop.Target);
        if (kind is DropKind.Beside or DropKind.Edge) Assert.Equal(side, drop.Side);
    }

    [Fact]
    public void An_edge_lights_the_third_of_the_section_the_pane_would_take()
    {
        Near(new Box(0, 0, 480, 706), PaneDrops.EdgeLanding(PaneSide.Left, W, H));
        Near(new Box(960, 0, 480, 706), PaneDrops.EdgeLanding(PaneSide.Right, W, H));
        Near(new Box(0, 0, 1440, 235.33), PaneDrops.EdgeLanding(PaneSide.Above, W, H));
        Near(new Box(0, 470.67, 1440, 235.33), PaneDrops.EdgeLanding(PaneSide.Below, W, H));
    }

    [Fact]
    public void Letting_go_makes_the_tree_that_goes_with_the_place()
    {
        var tree = Four;
        var arranged = SplitTree.Arrange(tree, W, H, Limits);
        SplitNode Dropped(double x, double y) => PaneDrops.Apply(tree, "line", PaneDrops.At(arranged, "line", x, y, W, H, 30));

        Assert.Equal(SplitTree.Swap(tree, "line", "actions"), Dropped(470, 520));
        Assert.Equal(SplitTree.Move(tree, "line", "actions", PaneSide.Above), Dropped(470, 400));
        Assert.Equal(SplitTree.Move(tree, "line", "bars", PaneSide.Right), Dropped(850, 180));
        Assert.Equal(SplitTree.MoveToEdge(tree, "line", PaneSide.Below), Dropped(700, 700));
        // Put down on its own place, or let go outside: the same tree, the same object.
        Assert.Same(tree, Dropped(1200, 150));
        Assert.Same(tree, Dropped(-20, 300));
    }
}
