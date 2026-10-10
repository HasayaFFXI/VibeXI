using Zerg.Core.Layout;

namespace Zerg.Core.Tests;

public class SplitTreeTests
{
    /// <summary>The design's sizes: a pane no smaller than 320 by 132, a
    /// 30-unit heading, a rule of one unit.</summary>
    static readonly PaneLimits Limits = new();

    /// <summary>The body of the design's sheets: 1440 wide, 706 tall.</summary>
    const double W = 1440, H = 706;

    static PaneSplit Columns(double ratio, SplitNode a, SplitNode b) => new(SplitWay.Columns, ratio, a, b);
    static PaneSplit Rows(double ratio, SplitNode a, SplitNode b) => new(SplitWay.Rows, ratio, a, b);
    static PaneLeaf Pane(string key, bool folded = false) => new(key, folded);

    static Dictionary<string, double> Own(params (string Key, double Height)[] heights) =>
        heights.ToDictionary(h => h.Key, h => h.Height);

    /// <summary>
    /// Four panes in two columns, as sheet 01 draws the Damage section:
    /// what that section was installed with until it gained its two side
    /// tables, and still the shape of the Healing section's. What most of
    /// these tests arrange: they are about the arithmetic, not about which
    /// panes a section has.
    /// </summary>
    static readonly SplitNode Four =
        Columns(0.653, Rows(0.474, Pane("bars"), Pane("actions")), Rows(0.42, Pane("line"), Pane("drill")));

    // ------------------------------------------------- the installed layouts

    [Fact]
    public void Four_panes_are_arranged_as_the_sheet_draws_them()
    {
        var a = SplitTree.Arrange(Four, W, H, Limits);

        // Sheet 01: rules at x 940.5, y 504.5 and y 466.5 in a window whose
        // body begins at y 170.
        Assert.Equal(new Box(0, 0, 940, 334), a.Panes["bars"]);
        Assert.Equal(new Box(0, 335, 940, 371), a.Panes["actions"]);
        Assert.Equal(new Box(941, 0, 499, 296), a.Panes["line"]);
        Assert.Equal(new Box(941, 297, 499, 409), a.Panes["drill"]);
        Assert.Equal(4, a.Panes.Count);
    }

    [Fact]
    public void Damage_keeps_the_sheets_tables_and_has_four_panes_down_its_second_column()
    {
        var a = SplitTree.Arrange(PaneLayouts.Damage, W, H, Limits);

        // The left-hand column is sheet 01's, to the unit.
        Assert.Equal(new Box(0, 0, 940, 334), a.Panes["bars"]);
        Assert.Equal(new Box(0, 335, 940, 371), a.Panes["actions"]);
        // The right: the chart, By damage type over By target, the
        // drill-down. With the drill-down open that body is short for
        // four: the two tables are at their least.
        Assert.Equal(new Box(941, 0, 499, 254), a.Panes["line"]);
        Assert.Equal(new Box(941, 255, 499, 132), a.Panes["types"]);
        Assert.Equal(new Box(941, 388, 499, 132), a.Panes["targets"]);
        Assert.Equal(new Box(941, 521, 499, 185), a.Panes["drill"]);
        Assert.Equal(6, a.Panes.Count);
        Assert.Equal(5, a.Dividers.Count);
    }

    [Fact]
    public void Damages_side_tables_have_the_drill_downs_room_until_an_action_is_picked()
    {
        // The drill-down is its heading until then, which is how the
        // section is nearly always seen.
        var a = SplitTree.Arrange(PaneLayouts.Damage, W, H, Limits, null, new HashSet<string> { "drill" });

        Assert.Equal(new Box(941, 0, 499, 254), a.Panes["line"]);
        Assert.Equal(new Box(941, 255, 499, 210), a.Panes["types"]);
        Assert.Equal(new Box(941, 466, 499, 209), a.Panes["targets"]);
        Assert.Equal(new Box(941, 676, 499, 30), a.Panes["drill"]);
        // The rule between the two tables can be moved; the one over the
        // folded drill-down cannot.
        Assert.False(a.Dividers.Single(d => d.Path == "110").Held);
        Assert.True(a.Dividers.Single(d => d.Path == "11").Held);
    }

    [Fact]
    public void The_two_side_tables_are_the_damage_sections_alone()
    {
        // Healing is never isolated, and the Compare section has tables of
        // its own, under keys of its own: a key names one pane of one section.
        Assert.Equal("types", PaneLayouts.Types);
        Assert.Equal("targets", PaneLayouts.Targets);
        Assert.DoesNotContain(SplitTree.Keys(PaneLayouts.Healing), k => k is PaneLayouts.Types or PaneLayouts.Targets);
        var all = SplitTree.Keys(PaneLayouts.Damage).Concat(SplitTree.Keys(PaneLayouts.Healing))
                           .Concat(SplitTree.Keys(PaneLayouts.Compare)).ToList();
        Assert.Equal(14, all.Count);
        Assert.Equal(all.Count, all.Distinct().Count());
    }

    [Fact]
    public void Healing_is_arranged_as_the_sheet_draws_it()
    {
        var a = SplitTree.Arrange(PaneLayouts.Healing, W, H, Limits);

        // Sheet 02: the per-character table ends at y 394 (224 tall).
        Assert.Equal(new Box(0, 0, 940, 224), a.Panes["hbars"]);
        Assert.Equal(new Box(0, 225, 940, 481), a.Panes["hactions"]);
        Assert.Equal(new Box(941, 0, 499, 296), a.Panes["hline"]);
        Assert.Equal(new Box(941, 297, 499, 409), a.Panes["hdrill"]);
    }

    [Fact]
    public void Compare_is_arranged_as_the_sheet_draws_it()
    {
        // Sheet 03 is 1000 tall and its bands take 172: the panes have 728.
        // Rules at x 940.5, y 476.5 and y 783.5 in a window whose panes
        // begin at y 248.
        var a = SplitTree.Arrange(PaneLayouts.Compare, W, 728, Limits);

        Assert.Equal(new Box(0, 0, 940, 728), a.Panes["cactors"]);
        Assert.Equal(new Box(941, 0, 499, 228), a.Panes["cpace"]);
        Assert.Equal(new Box(941, 229, 499, 306), a.Panes["ckinds"]);
        Assert.Equal(new Box(941, 536, 499, 192), a.Panes["ctargets"]);
        Assert.Equal(4, a.Panes.Count);
        Assert.Equal(3, a.Dividers.Count);
    }

    [Fact]
    public void Compare_needs_three_least_panes_down_its_second_column()
    {
        Assert.Equal((641.0, 398.0), SplitTree.Least(PaneLayouts.Compare, Limits));
        Assert.Equal(new[] { "cactors", "cpace", "ckinds", "ctargets" }, SplitTree.Keys(PaneLayouts.Compare));
    }

    [Fact]
    public void Panes_are_listed_in_reading_order()
    {
        // Down the left-hand column, then down the right: the order they
        // stand in when a narrow window has one column.
        Assert.Equal(new[] { "bars", "actions", "line", "types", "targets", "drill" }, SplitTree.Keys(PaneLayouts.Damage));
        Assert.Equal(new[] { "hbars", "hactions", "hline", "hdrill" }, SplitTree.Keys(PaneLayouts.Healing));
        Assert.Equal(new[] { "only" }, SplitTree.Keys(Pane("only")));
    }

    [Fact]
    public void The_installed_layouts_need_two_least_panes_across_and_as_many_down_as_their_longer_column()
    {
        Assert.Equal((641.0, 265.0), SplitTree.Least(Four, Limits));
        Assert.Equal((641.0, 265.0), SplitTree.Least(PaneLayouts.Healing, Limits));
        // Damage: four down its second column, three of them and a heading
        // while the drill-down has nothing picked.
        Assert.Equal((641.0, 531.0), SplitTree.Least(PaneLayouts.Damage, Limits));
        Assert.Equal((641.0, 429.0), SplitTree.Least(PaneLayouts.Damage, Limits, null, new HashSet<string> { "drill" }));
    }

    // ------------------------------------------------------------ the rules

    [Fact]
    public void The_panes_and_their_rules_fill_the_room_and_overlap_nothing()
    {
        var a = SplitTree.Arrange(Four, W, H, Limits);

        double area = a.Panes.Values.Sum(b => b.Width * b.Height) + a.Dividers.Sum(d => d.Line.Width * d.Line.Height);
        Assert.Equal(W * H, area, 6);
        foreach (var b in a.Panes.Values)
        {
            Assert.True(b.X >= 0 && b.Y >= 0 && b.Right <= W && b.Bottom <= H);
        }
    }

    [Fact]
    public void Each_split_has_a_rule_between_its_halves()
    {
        var a = SplitTree.Arrange(Four, W, H, Limits);

        Assert.Equal(3, a.Dividers.Count);
        var root = a.Dividers.Single(d => d.Path == "");
        Assert.Equal(SplitWay.Columns, root.Way);
        Assert.Equal(new Box(940, 0, 1, 706), root.Line);
        Assert.Equal(new Box(0, 0, W, H), root.Room);

        var left = a.Dividers.Single(d => d.Path == "0");
        Assert.Equal(SplitWay.Rows, left.Way);
        Assert.Equal(new Box(0, 334, 940, 1), left.Line);

        var right = a.Dividers.Single(d => d.Path == "1");
        Assert.Equal(new Box(941, 296, 499, 1), right.Line);
        Assert.Equal(new Box(941, 0, 499, 706), right.Room);
    }

    [Fact]
    public void A_path_leads_back_to_its_split()
    {
        var tree = Four;
        Assert.Same(tree, SplitTree.At(tree, ""));
        Assert.Equal(0.474, SplitTree.At(tree, "0")!.Ratio);
        Assert.Equal(0.42, SplitTree.At(tree, "1")!.Ratio);
        // A pane is not a split, and there is nothing under one.
        Assert.Null(SplitTree.At(tree, "00"));
        Assert.Null(SplitTree.At(tree, "000"));
        Assert.Null(SplitTree.At(tree, "x"));
    }

    [Fact]
    public void A_rule_says_how_far_it_may_be_moved()
    {
        var a = SplitTree.Arrange(Four, W, H, Limits);

        // Between the columns: each side keeps its 320.
        var root = a.Dividers.Single(d => d.Path == "");
        Assert.Equal(320, root.Low);
        Assert.Equal(1439 - 320, root.High);
        Assert.False(root.Held);

        // In the right-hand column, in the host's own coordinates: 132 a side.
        var right = a.Dividers.Single(d => d.Path == "1");
        Assert.Equal(132, right.Low);
        Assert.Equal(705 - 132, right.High);
        Assert.False(right.Held);
    }

    [Fact]
    public void A_share_is_of_the_room_less_the_rule()
    {
        var a = SplitTree.Arrange(Columns(0.5, Pane("a"), Pane("b")), 1001, 400, Limits);

        Assert.Equal(new Box(0, 0, 500, 400), a.Panes["a"]);
        Assert.Equal(new Box(501, 0, 500, 400), a.Panes["b"]);
    }

    [Fact]
    public void One_pane_alone_has_the_whole_room_and_no_rule()
    {
        var a = SplitTree.Arrange(Pane("only"), 800, 500, Limits);

        Assert.Equal(new Box(0, 0, 800, 500), a.Panes["only"]);
        Assert.Empty(a.Dividers);
    }

    // ----------------------------------------------------- the least sizes

    [Theory]
    // A share that would leave the first pane too narrow, then the second.
    [InlineData(0.05, 320, 1119)]
    [InlineData(0.99, 1119, 320)]
    [InlineData(0, 320, 1119)]
    [InlineData(1, 1119, 320)]
    // One that leaves both wide enough stands.
    [InlineData(0.25, 360, 1079)]
    public void No_pane_is_made_narrower_than_the_least(double ratio, double first, double second)
    {
        var a = SplitTree.Arrange(Columns(ratio, Pane("a"), Pane("b")), W, H, Limits);

        Assert.Equal(first, a.Panes["a"].Width);
        Assert.Equal(second, a.Panes["b"].Width);
    }

    [Theory]
    [InlineData(0.01, 132, 573)]
    [InlineData(0.99, 573, 132)]
    public void No_pane_is_made_shorter_than_the_least(double ratio, double first, double second)
    {
        var a = SplitTree.Arrange(Rows(ratio, Pane("a"), Pane("b")), W, H, Limits);

        Assert.Equal(first, a.Panes["a"].Height);
        Assert.Equal(second, a.Panes["b"].Height);
    }

    [Fact]
    public void A_column_of_two_panes_needs_the_width_of_one_and_the_height_of_both()
    {
        // The whole left-hand column is the first half of the root split:
        // it cannot be made narrower than a pane.
        var tree = Columns(0.01, Rows(0.5, Pane("a"), Pane("b")), Pane("c"));
        Assert.Equal((641.0, 265.0), SplitTree.Least(tree, Limits));

        var a = SplitTree.Arrange(tree, W, H, Limits);
        Assert.Equal(320, a.Panes["a"].Width);
        Assert.Equal(320, a.Panes["b"].Width);
    }

    [Fact]
    public void The_share_comes_back_when_the_room_does()
    {
        var tree = Columns(0.653, Pane("a"), Pane("b"));

        // 700 wide: the second pane would have 242, so it is given its 320.
        Assert.Equal(379, SplitTree.Arrange(tree, 700, H, Limits).Panes["a"].Width);
        // The tree was not changed by that: wide again, the share is 0.653.
        Assert.Equal(940, SplitTree.Arrange(tree, W, H, Limits).Panes["a"].Width);
    }

    [Fact]
    public void A_room_too_small_for_both_is_shared_and_nothing_is_negative()
    {
        // Two panes of 320 and a rule need 641.
        var a = SplitTree.Arrange(Columns(0.9, Pane("a"), Pane("b")), 501, 200, Limits);

        Assert.Equal(250, a.Panes["a"].Width);
        Assert.Equal(250, a.Panes["b"].Width);
        Assert.True(a.Dividers[0].Held);
        Assert.Equal(a.Dividers[0].Low, a.Dividers[0].High);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.5, 0.5)]
    [InlineData(-10, 300)]
    [InlineData(double.NaN, 300)]
    [InlineData(double.PositiveInfinity, 300)]
    public void No_room_at_all_is_arranged_without_complaint(double width, double height)
    {
        var a = SplitTree.Arrange(Four, width, height, Limits);

        Assert.Equal(4, a.Panes.Count);
        foreach (var b in a.Panes.Values)
        {
            Assert.True(b.Width >= 0 && b.Height >= 0, $"{b}");
            Assert.False(double.IsNaN(b.X) || double.IsNaN(b.Y) || double.IsNaN(b.Width) || double.IsNaN(b.Height));
        }
        foreach (var d in a.Dividers) Assert.True(d.Line.Width >= 0 && d.Line.Height >= 0);
    }

    [Fact]
    public void A_share_that_is_not_a_number_is_a_half()
    {
        var a = SplitTree.Arrange(Columns(double.NaN, Pane("a"), Pane("b")), 1001, 400, Limits);

        Assert.Equal(500, a.Panes["a"].Width);
    }

    // ------------------------------------------ panes with a height of their own

    [Fact]
    public void A_stand_in_takes_its_own_height_and_its_neighbour_the_rest()
    {
        // Sheet 02: the chart is floating; its stand-in is 46 tall and the
        // drill-down under it has the column.
        var a = SplitTree.Arrange(PaneLayouts.Healing, W, H, Limits, Own(("hline", 46)));

        Assert.Equal(new Box(941, 0, 499, 46), a.Panes["hline"]);
        Assert.Equal(new Box(941, 47, 499, 659), a.Panes["hdrill"]);
        Assert.True(a.Dividers.Single(d => d.Path == "1").Held);
        // The other column is as it was.
        Assert.Equal(new Box(0, 0, 940, 224), a.Panes["hbars"]);
        Assert.False(a.Dividers.Single(d => d.Path == "0").Held);
    }

    [Fact]
    public void A_drill_down_folded_to_its_heading_leaves_the_chart_the_column()
    {
        var a = SplitTree.Arrange(Four, W, H, Limits, Own(("drill", 30)));

        Assert.Equal(new Box(941, 0, 499, 675), a.Panes["line"]);
        Assert.Equal(new Box(941, 676, 499, 30), a.Panes["drill"]);
    }

    [Fact]
    public void Two_such_panes_stand_at_the_top_and_the_rest_is_empty()
    {
        var a = SplitTree.Arrange(Four, W, H, Limits, Own(("line", 46), ("drill", 30)));

        Assert.Equal(new Box(941, 0, 499, 46), a.Panes["line"]);
        Assert.Equal(new Box(941, 47, 499, 30), a.Panes["drill"]);
    }

    [Fact]
    public void A_pane_the_tree_says_is_folded_is_as_tall_as_a_heading()
    {
        var tree = Rows(0.5, Pane("a", folded: true), Pane("b"));

        var a = SplitTree.Arrange(tree, 500, 400, Limits);
        Assert.Equal(new Box(0, 0, 500, 30), a.Panes["a"]);
        Assert.Equal(new Box(0, 31, 500, 369), a.Panes["b"]);

        // A height it was given outranks the heading's.
        var given = SplitTree.Arrange(tree, 500, 400, Limits, Own(("a", 46)));
        Assert.Equal(46, given.Panes["a"].Height);
    }

    [Fact]
    public void Three_in_a_column_with_two_folded_gives_the_third_the_room()
    {
        // As the design's sheet 10 draws it: one folded, one a stand-in,
        // and the third "takes the room the other two gave up".
        var tree = Rows(0.3, Pane("a"), Rows(0.5, Pane("b"), Pane("c")));

        var a = SplitTree.Arrange(tree, 500, 600, Limits, Own(("b", 30), ("c", 46)));
        Assert.Equal(new Box(0, 0, 500, 522), a.Panes["a"]);
        Assert.Equal(new Box(0, 523, 500, 30), a.Panes["b"]);
        Assert.Equal(new Box(0, 554, 500, 46), a.Panes["c"]);
    }

    [Fact]
    public void A_height_of_its_own_does_not_narrow_a_pane_beside_another()
    {
        // Side by side, a stand-in still has its share of the width, and
        // is as tall as the room: it is not folded.
        var a = SplitTree.Arrange(Columns(0.5, Pane("a"), Pane("b")), 1001, 400, Limits, Own(("a", 46)));

        Assert.Equal(new Box(0, 0, 500, 400), a.Panes["a"]);
        Assert.Equal(500, a.Panes["b"].Width);
        Assert.Empty(a.Rails);
    }

    // ------------------------------------------------------------- the rail

    [Fact]
    public void A_folded_pane_alone_in_its_column_is_a_rail()
    {
        // Nobody over or under it to give its room to: it folds sideways,
        // as wide as a heading is tall, and its neighbour has the rest.
        var a = SplitTree.Arrange(Columns(0.5, Pane("a", folded: true), Pane("b")), 1001, 400, Limits);

        Assert.Equal(new Box(0, 0, 30, 400), a.Panes["a"]);
        Assert.Equal(new Box(31, 0, 970, 400), a.Panes["b"]);
        Assert.Equal(new[] { "a" }, a.Rails);
        // The rule beside a rail cannot be moved.
        Assert.True(a.Dividers.Single().Held);

        var right = SplitTree.Arrange(Columns(0.5, Pane("a"), Pane("b", folded: true)), 1001, 400, Limits);
        Assert.Equal(new Box(0, 0, 970, 400), right.Panes["a"]);
        Assert.Equal(new Box(971, 0, 30, 400), right.Panes["b"]);
        Assert.Equal(new[] { "b" }, right.Rails);
    }

    [Fact]
    public void Compares_table_of_characters_folds_to_a_rail()
    {
        // The one pane of the installed arrangements that is alone in its column.
        var a = SplitTree.Arrange(SplitTree.Fold(PaneLayouts.Compare, "cactors", true), W, 728, Limits);

        Assert.Equal(new Box(0, 0, 30, 728), a.Panes["cactors"]);
        Assert.Equal(new Box(31, 0, 1409, 228), a.Panes["cpace"]);
        Assert.Equal(new[] { "cactors" }, a.Rails);
        // And in a column it shares, a pane folds to its heading as before.
        var b = SplitTree.Arrange(SplitTree.Fold(PaneLayouts.Compare, "cpace", true), W, 728, Limits);
        Assert.Equal(new Box(941, 0, 499, 30), b.Panes["cpace"]);
        Assert.Empty(b.Rails);
    }

    [Fact]
    public void A_pane_folded_for_a_reason_of_its_own_is_arranged_as_a_folded_one()
    {
        // A drill-down with nothing picked: the host names it, the tree is not touched.
        var own = new HashSet<string> { "drill" };
        var a = SplitTree.Arrange(Four, W, H, Limits, null, own);
        Assert.Equal(new Box(941, 0, 499, 675), a.Panes["line"]);
        Assert.Equal(new Box(941, 676, 499, 30), a.Panes["drill"]);
        Assert.Empty(a.Rails);

        // Put in a column of its own, it is a rail until an action is picked.
        var moved = SplitTree.MoveToEdge(Four, "drill", PaneSide.Right);
        var b = SplitTree.Arrange(moved, W, H, Limits, null, own);
        Assert.Equal(new Box(1410, 0, 30, 706), b.Panes["drill"]);
        Assert.Equal(new[] { "drill" }, b.Rails);
        var open = SplitTree.Arrange(moved, W, H, Limits);
        Assert.Equal(new Box(960, 0, 480, 706), open.Panes["drill"]);
        Assert.Empty(open.Rails);
    }

    [Fact]
    public void A_stand_in_is_never_a_rail()
    {
        // A card that floats out of a folded pane alone in its column: the
        // stand-in needs its width, and has its own height.
        var a = SplitTree.Arrange(Columns(0.5, Pane("a", folded: true), Pane("b")), 1001, 400, Limits, Own(("a", 46)));

        Assert.Equal(500, a.Panes["a"].Width);
        Assert.Empty(a.Rails);
    }

    [Fact]
    public void Two_rails_stand_at_the_left_and_the_rest_is_empty()
    {
        var a = SplitTree.Arrange(Columns(0.5, Pane("a", folded: true), Pane("b", folded: true)), 1001, 400, Limits);

        Assert.Equal(new Box(0, 0, 30, 400), a.Panes["a"]);
        Assert.Equal(new Box(31, 0, 30, 400), a.Panes["b"]);
    }

    [Fact]
    public void A_rail_needs_a_headings_width_and_a_panes_height()
    {
        Assert.Equal((351.0, 132.0), SplitTree.Least(Columns(0.5, Pane("a", folded: true), Pane("b")), Limits));
        // Its neighbour is a column of two: the rail is as tall as they need.
        Assert.Equal((351.0, 265.0),
            SplitTree.Least(Columns(0.5, Pane("a", folded: true), Rows(0.5, Pane("b"), Pane("c"))), Limits));
        // A column of a rail's neighbour and a rail does not count as short.
        var column = Rows(0.5, Columns(0.5, Pane("a", folded: true), Pane("b")), Pane("c"));
        var a = SplitTree.Arrange(column, 800, 600, Limits);
        Assert.Equal(new Box(0, 0, 30, 300), a.Panes["a"]);
        Assert.False(a.Dividers.Single(d => d.Path == "").Held);
    }

    [Fact]
    public void The_least_height_counts_a_folded_pane_as_its_heading()
    {
        Assert.Equal((641.0, 265.0), SplitTree.Least(Four, Limits, Own(("drill", 30))));
        // Both right-hand panes short: the left-hand column still needs 265.
        Assert.Equal((641.0, 265.0), SplitTree.Least(Four, Limits, Own(("line", 46), ("drill", 30))));
        Assert.Equal((320.0, 163.0), SplitTree.Least(Rows(0.5, Pane("a", folded: true), Pane("b")), Limits));
        // Folded for a reason of its own: the same.
        Assert.Equal((641.0, 265.0), SplitTree.Least(Four, Limits, null, new HashSet<string> { "drill" }));
        Assert.Equal((320.0, 163.0),
            SplitTree.Least(Rows(0.5, Pane("a"), Pane("b")), Limits, null, new HashSet<string> { "a" }));
    }

    // --------------------------------------------------------- whole pixels

    [Fact]
    public void Every_edge_lands_on_a_whole_pixel_of_a_scaled_screen()
    {
        // 150%: a pixel is two thirds of a unit, and a rule of one unit is
        // drawn two pixels thick.
        double pixel = 1 / 1.5;
        var limits = new PaneLimits(Rule: 2 * pixel, Grain: pixel);

        var a = SplitTree.Arrange(Four, W, H, limits);

        foreach (var b in a.Panes.Values)
        {
            foreach (double edge in new[] { b.X, b.Y, b.Right, b.Bottom })
                Assert.Equal(Math.Round(edge * 1.5), edge * 1.5, 6);
        }
        Assert.Equal(2, a.Dividers[0].Line.Width * 1.5, 6);
        // Within a pixel of where the shares put them.
        Assert.InRange(a.Panes["bars"].Width, 939, 941);
    }
}
