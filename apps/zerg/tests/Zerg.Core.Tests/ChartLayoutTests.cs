using Zerg.Core.Charts;

namespace Zerg.Core.Tests;

/// <summary>Where the charts put things: ticks, end labels, hover readouts, bars and bins.</summary>
public class ChartLayoutTests
{
    sealed record Line(string Name, double[] Values, bool Group = false) : ILineSeries;
    sealed record Bar(string Label, double Value) : IBarRow;

    /// <summary>Seven pixels a character: enough to make widths matter.</summary>
    static double Width(string s) => s.Length * 7;

    static LineLayout Layout(double w, double h, double[] times, Line[] lines, bool compact = false, bool endLabels = false) =>
        LineLayout.Compute(w, h, times, lines, compact, endLabels, Width);

    // ---------------------------------------------------------------- ticks

    [Fact]
    public void Nice_ticks_are_round_and_stop_at_the_maximum()
    {
        Assert.Equal([0, 10000, 20000, 30000], Ticks.Nice(0, 39310, 5));
        Assert.Equal([0, 50000, 100000, 150000], Ticks.Nice(0, 151387, 5));
        Assert.Equal([0, 5000, 10000], Ticks.Nice(0, 10933, 3));
        Assert.Equal([0, 10, 20, 30], Ticks.Nice(0, 39, 4));
        Assert.Equal([100, 200, 300, 400, 500, 600, 700], Ticks.Nice(38, 745, 10));
    }

    [Fact]
    public void Nice_ticks_of_an_empty_range_are_its_start()
    {
        Assert.Equal([0], Ticks.Nice(0, 0, 5));
        Assert.Equal([7], Ticks.Nice(7, 7, 5));
        Assert.Equal([0], Ticks.Nice(double.NaN, 5, 5));
    }

    [Fact]
    public void Time_ticks_count_round_steps_from_zero()
    {
        // 24:01 across twelve labels: five-minute steps.
        Assert.Equal([0, 300000, 600000, 900000, 1200000], Ticks.Time(0, 1441000, 12));
        // 4:58 asked for three: one-minute steps would be too many, two minutes it is.
        Assert.Equal([0, 120000, 240000], Ticks.Time(0, 298000, 3));
        Assert.Equal([0, 1000], Ticks.Time(0, 1000, 3));
    }

    // ----------------------------------------------------------- hover card

    [Fact]
    public void A_hover_card_sits_right_of_the_point_and_flips_when_there_is_no_room()
    {
        Assert.Equal((114, 88), HoverCard.Place(100, 100, 150, 80, 800, 300));
        Assert.Equal((536, 88), HoverCard.Place(700, 100, 150, 80, 800, 300));
    }

    [Fact]
    public void A_hover_card_stays_inside_the_chart()
    {
        Assert.Equal((114, 216), HoverCard.Place(100, 290, 150, 80, 800, 300));   // held off the bottom
        Assert.Equal((114, 4), HoverCard.Place(100, 5, 150, 80, 800, 300));       // and off the top
        Assert.Equal((4, 4), HoverCard.Place(100, 100, 300, 400, 320, 300));      // too big: top-left corner
    }

    // ----------------------------------------------------------------- line

    [Fact]
    public void A_line_chart_with_nothing_in_it_is_empty()
    {
        Assert.True(Layout(800, 300, [], [new("A", [])]).Empty);
        Assert.True(Layout(800, 300, [0, 1000], []).Empty);
        Assert.Null(Layout(800, 300, [], []).Hover(100, 100));
    }

    [Fact]
    public void The_scale_tops_out_at_the_largest_value_and_starts_at_zero()
    {
        var l = Layout(800, 320, [0, 60000], [new("A", [0, 39310]), new("B", [0, 20500])]);
        Assert.Equal(39310, l.YTop);
        Assert.Equal(["0", "10.0K", "20.0K", "30.0K"], l.YTicks.Select(t => t.Label));
        Assert.Equal(l.Plot.Y, l.Py(39310), 9);
        Assert.Equal(l.Plot.Bottom, l.Py(0), 9);
    }

    [Fact]
    public void A_short_plot_gets_three_gridlines()
    {
        var times = new double[] { 0, 60000 };
        var lines = new[] { new Line("A", [0, 39310]) };
        Assert.Equal(4, Layout(800, 320, times, lines).YTicks.Count);        // 0, 10K, 20K, 30K
        Assert.Equal(2, Layout(800, 180, times, lines).YTicks.Count);        // 0, 20K
    }

    [Fact]
    public void End_labels_are_named_kept_apart_and_never_dropped()
    {
        var times = new double[] { 0, 1000 };
        var lines = Enumerable.Range(0, 8).Select(i => new Line("Name" + i, [0, 1000 + i])).ToArray();
        var l = Layout(460, 264, times, lines, compact: true, endLabels: true);

        Assert.Equal(8, l.Labels.Count);
        Assert.All(l.Labels, x => Assert.True(x.Swatch));
        Assert.Contains(l.Labels, x => x.Text == "Name7 1,007");
        var ys = l.Labels.Select(x => x.Y).ToList();
        for (int i = 1; i < ys.Count; i++) Assert.True(ys[i] - ys[i - 1] >= 13 - 1e-9);
        // The right margin is as wide as the widest label needs.
        Assert.Equal(460 - 46 - (Math.Ceiling(Width("Name0 1,000")) + 34), l.Plot.W);
    }

    [Fact]
    public void End_labels_pushed_past_the_floor_settle_back_up_from_it()
    {
        var times = new double[] { 0, 1000 };
        var lines = new[] { new Line("Top", [0, 100000]) }
            .Concat(Enumerable.Range(0, 6).Select(i => new Line("Low" + i, [0, i]))).ToArray();
        var l = Layout(460, 264, times, lines, compact: true, endLabels: true);
        Assert.Equal(l.Plot.Bottom, l.Labels[^1].Y, 9);
        Assert.Equal(l.Plot.Bottom - 13, l.Labels[^2].Y, 9);
    }

    [Fact]
    public void A_long_name_is_cut_with_an_ellipsis()
    {
        var l = Layout(600, 264, [0, 1000], [new("An extraordinarily long name", [0, 5])], endLabels: true);
        Assert.Equal("An extraordin" + TextFit.Ellipsis + " 5", l.Labels[0].Text);
    }

    [Fact]
    public void Without_names_only_a_few_well_separated_lines_get_their_totals()
    {
        var times = new double[] { 0, 1000 };
        var apart = Layout(800, 320, times, [new("A", [0, 100]), new("B", [0, 50000])]);
        Assert.Equal(["100", "50.0K"], apart.Labels.OrderBy(x => x.Series).Select(x => x.Text));
        Assert.All(apart.Labels, x => Assert.False(x.Swatch));

        Assert.Empty(Layout(800, 320, times, [new("A", [0, 50000]), new("B", [0, 50100])]).Labels);       // too close
        Assert.Empty(Layout(800, 320, times, Enumerable.Range(1, 5).Select(i => new Line("L" + i, [0, i * 1000.0])).ToArray()).Labels);
    }

    [Fact]
    public void A_line_that_stops_early_is_marked_and_labelled_where_it_stops()
    {
        var l = Layout(800, 320, [0, 1000, 2000, 3000],
                       [new("Short", [0, 500, double.NaN, double.NaN]), new("Long", [0, 100, 200, 9000])]);
        var mark = l.Markers.Single(m => m.Series == 0);
        Assert.Equal(l.Px(1000), mark.X, 9);
        Assert.Equal(l.Py(500), mark.Y, 9);
        Assert.Equal(l.Px(1000) + 11, l.Labels.Single(x => x.Series == 0).X, 9);
        Assert.Equal(1, LineLayout.LastIndex([0, 500, double.NaN, double.NaN]));
        Assert.Equal(-1, LineLayout.LastIndex([double.NaN]));
    }

    [Fact]
    public void The_crosshair_reads_every_line_at_the_nearest_grid_time_largest_first()
    {
        var l = Layout(868, 320, [0, 60000, 120000, 180000, 240000],
                       [new("Small", [0, 10, 20, 30, 40]), new("Gone", [0, 5, double.NaN, double.NaN, double.NaN]),
                        new("Big", [0, 1000, 2500.4, 3000, 4000])]);
        // The plot is 68..792; halfway is the third grid time.
        var h = l.Hover(68 + 724 / 2.0 + 20, 100)!;
        Assert.Equal(2, h.Index);
        Assert.Equal("2:00", h.Head);
        Assert.Equal([("Big", "2,500", 2), ("Small", "20", 0)], h.Rows.Select(r => (r.Label, r.Value, r.Series)));
        Assert.Equal(2, h.Dots.Count);
        Assert.Equal(Math.Round(l.Px(120000)) + 0.5, h.X);
    }

    [Fact]
    public void The_crosshair_reaches_eight_pixels_past_the_plot_and_no_further()
    {
        var l = Layout(868, 320, [0, 1000], [new("A", [0, 10])]);
        Assert.NotNull(l.Hover(60, 100));
        Assert.Null(l.Hover(59, 100));
        Assert.NotNull(l.Hover(800, l.Plot.Bottom + 8));
        Assert.Null(l.Hover(801, 100));
        Assert.Null(l.Hover(400, l.Plot.Bottom + 9));
    }

    // ---------------------------------------------------------- small lines

    static Aggregate Party(params (string Name, double Total)[] actors)
    {
        var sum = actors.Sum(a => a.Total);
        return new Aggregate
        {
            Total = sum,
            Actors = actors.Select(a => new ActorTotals { Name = a.Name, Total = a.Total, Share = sum > 0 ? a.Total / sum : 0 }).ToList(),
        };
    }

    [Fact]
    public void Small_lines_are_everyone_under_five_percent_but_never_the_owner()
    {
        var agg = Party(("Big", 900), ("Owner", 20), ("Tiny", 30), ("Wee", 40), ("Idle", 0));
        Assert.Equal(["Tiny", "Wee"], SmallLines.Pick(agg, "Owner"));
        Assert.Equal(["Owner", "Tiny", "Wee"], SmallLines.Pick(agg, null));
        Assert.Equal("3 others", SmallLines.Name(3));
    }

    [Fact]
    public void One_small_line_is_left_alone()
    {
        Assert.Empty(SmallLines.Pick(Party(("Big", 900), ("Tiny", 30), ("Mid", 400)), null));
    }

    // ----------------------------------------------------------------- bars

    [Fact]
    public void Bars_are_scaled_to_the_largest_capped_in_thickness_and_never_vanish()
    {
        var rows = new[] { new Bar("Hasaya", 1000), new Bar("Clarice", 500), new Bar("Selene", 0) };
        var l = BarsLayout.Compute(626, BarsLayout.HeightFor(3), rows, Width);
        Assert.Equal(120, BarsLayout.HeightFor(3));
        Assert.Equal(152, BarsLayout.HeightFor(4));

        double w = 626 - 70 - 126;
        Assert.Equal([w, w / 2, 2], l.Rows.Select(b => b.BarW));
        Assert.All(l.Rows, b => Assert.Equal(24, b.BarH));
        Assert.Equal(["1,000", "500", "0"], l.Rows.Select(b => b.ValueText));
        Assert.Equal(126 + w + 8, l.Rows[0].ValueX);
    }

    [Fact]
    public void A_bar_is_picked_by_its_whole_band()
    {
        var l = BarsLayout.Compute(600, 120, [new Bar("A", 1), new Bar("B", 2), new Bar("C", 3)], Width);
        Assert.Equal(0, l.RowAt(0));
        Assert.Equal(0, l.RowAt(39.9));
        Assert.Equal(1, l.RowAt(40));
        Assert.Equal(2, l.RowAt(119));
        Assert.Equal(-1, l.RowAt(120));
        Assert.Equal(-1, l.RowAt(-1));
    }

    [Fact]
    public void A_long_bar_label_is_cut_to_its_column()
    {
        var l = BarsLayout.Compute(600, 120, [new Bar("An extraordinarily long name", 1)], Width);
        Assert.Equal("An extraordinar" + TextFit.Ellipsis, l.Rows[0].Label);
        Assert.True(BarsLayout.Compute(600, 120, [], Width).Empty);
    }

    // ------------------------------------------------------------ histogram

    static List<Bin> Bins(double lo, double width, params int[] counts) =>
        counts.Select((c, i) => new Bin(lo + i * width, lo + (i + 1) * width) { Count = c }).ToList();

    [Fact]
    public void Columns_fill_their_bins_and_empty_bins_draw_nothing()
    {
        var l = HistogramLayout.Compute(462, 210, Bins(0, 100, 4, 0, 8, 2), 180, 14);
        Assert.Equal(100, l.Slot);
        Assert.Equal([0, 2, 3], l.Columns.Select(c => c.Bin));
        Assert.All(l.Columns, c => Assert.Equal(98, c.W));
        Assert.Equal(l.Plot.Y, l.Columns.Single(c => c.Bin == 2).Y, 9);      // the tallest reaches the top
        Assert.Equal(["0", "2", "4", "6", "8"], l.YTicks.Select(t => t.Label));
    }

    [Fact]
    public void Both_ends_of_the_range_are_always_labelled()
    {
        var l = HistogramLayout.Compute(462, 210, Bins(0, 102.5, 1, 2, 3, 4), 200, 10);
        Assert.Equal(("0", LabelAlign.Left), (l.XLabels[^2].Text, l.XLabels[^2].Align));
        Assert.Equal(("410", LabelAlign.Right), (l.XLabels[^1].Text, l.XLabels[^1].Align));
        // 0 and 400 would sit on top of the ends; 200 has room.
        Assert.Equal(["200"], l.XLabels.Take(l.XLabels.Count - 2).Select(x => x.Text));
    }

    [Fact]
    public void The_mean_is_marked_and_its_label_flips_near_the_right_edge()
    {
        var left = HistogramLayout.Compute(462, 210, Bins(0, 100, 1, 1, 1, 1), 100, 4).Mean!;
        Assert.Equal(("avg 100", LabelAlign.Left), (left.Label, left.Align));
        Assert.Equal(left.X + 4, left.LabelX);

        var right = HistogramLayout.Compute(462, 210, Bins(0, 100, 1, 1, 1, 1), 390, 4).Mean!;
        Assert.Equal(LabelAlign.Right, right.Align);
        Assert.Equal(right.X - 4, right.LabelX);

        Assert.Null(HistogramLayout.Compute(462, 210, Bins(0, 100, 1, 1), 999, 2).Mean);
    }

    [Fact]
    public void The_pointer_reads_a_bin()
    {
        var l = HistogramLayout.Compute(462, 210, Bins(1000, 250, 4, 0, 8, 2), 1500, 14);
        var h = l.Hover(46 + 250, 100)!;
        Assert.Equal(2, h.Bin);
        Assert.Equal("1,500 " + (char)0x2013 + " 1,750", h.Head);
        Assert.Equal([("Hits", "8"), ("Share", "57.1%")], h.Rows.Select(r => (r.Label, r.Value)));
        Assert.Null(l.Hover(45, 100));
        Assert.Null(l.Hover(200, 0));
    }

    [Fact]
    public void Identical_hits_make_one_column()
    {
        var l = HistogramLayout.Compute(462, 210, [new Bin(500, 500) { Count = 3 }], 500, 3);
        Assert.Single(l.Columns);
        Assert.Equal(400, l.Columns[0].W + 2);   // the one bin is the whole plot
        Assert.Equal(("500", "501"), (l.XLabels[^2].Text, l.XLabels[^1].Text));
        Assert.True(HistogramLayout.Compute(462, 210, [], 0, 0).Empty);
        Assert.True(HistogramLayout.Compute(462, 210, null, 0, 0).Empty);
    }

    // ----------------------------------------------------- paired histogram

    static List<PairBin> Pairs(double lo, double width, params (int A, int B)[] counts) =>
        counts.Select((c, i) => new PairBin(lo + i * width, lo + (i + 1) * width) { A = c.A, B = c.B }).ToList();

    [Fact]
    public void A_bin_holds_a_column_for_each_run_as_a_share_of_that_runs_hits()
    {
        // A's four hits are all in the first bin; B's four are one and three.
        var l = PairedHistogramLayout.Compute(462, 210, Pairs(0, 100, (4, 1), (0, 3)), null, null, 4, 4, Width);
        Assert.Equal(200, l.Slot);
        Assert.Equal([(0, 0), (0, 1), (1, 1)], l.Columns.Select(c => (c.Bin, c.Run)));
        Assert.All(l.Columns, c => Assert.Equal(97.5, c.W));
        // A beside B, a pixel apart, inside the bin.
        Assert.Equal((48, 146.5, 346.5), (l.Columns[0].X, l.Columns[1].X, l.Columns[2].X));
        // By share, not by count: A's one column is all of A and reaches the top.
        Assert.Equal(l.Plot.Y, l.Columns[0].Y, 9);
        Assert.Equal(l.Plot.H * 0.25, l.Columns[1].H, 9);
        Assert.Equal(l.Plot.H * 0.75, l.Columns[2].H, 9);
        Assert.Equal(["0%", "50%", "100%"], l.YTicks.Select(t => t.Label));
        Assert.Empty(l.Means);
    }

    [Fact]
    public void A_longer_run_does_not_dwarf_a_shorter_one()
    {
        // Thirty hits against three, spread alike: the columns are the same height.
        var l = PairedHistogramLayout.Compute(462, 210, Pairs(0, 100, (10, 1), (20, 2)), null, null, 30, 3, Width);
        Assert.Equal(l.Columns[0].H, l.Columns[1].H, 9);
        Assert.Equal(l.Columns[2].H, l.Columns[3].H, 9);
    }

    [Fact]
    public void The_two_means_labels_turn_their_backs_on_each_other()
    {
        var l = PairedHistogramLayout.Compute(462, 210, Pairs(0, 100, (1, 1), (1, 1)), 100, 150, 2, 2, Width);
        Assert.Equal([0, 1], l.Means.Select(m => m.Run));
        var (a, b) = (l.Means[0].Rule, l.Means[1].Rule);
        Assert.Equal(("A avg 100", 246.5, LabelAlign.Right, 242.5), (a.Label, a.X, a.Align, a.LabelX));
        Assert.Equal(("B avg 150", 346.5, LabelAlign.Left, 350.5), (b.Label, b.X, b.Align, b.LabelX));
        Assert.Equal(22, l.Plot.Y);
        Assert.Equal((16, 16), (a.Top, b.Top));

        // Whichever run is the lower reads to the left.
        var swapped = PairedHistogramLayout.Compute(462, 210, Pairs(0, 100, (1, 1), (1, 1)), 150, 100, 2, 2, Width);
        Assert.Equal((LabelAlign.Left, LabelAlign.Right), (swapped.Means[0].Rule.Align, swapped.Means[1].Rule.Align));
    }

    [Fact]
    public void A_label_with_no_room_on_its_own_side_is_turned_round_and_lifted_clear()
    {
        // Both means near the left edge: A's label cannot go left, and to the
        // right it would run across B's rule.
        var l = PairedHistogramLayout.Compute(462, 210, Pairs(0, 100, (1, 1), (1, 1)), 10, 20, 2, 2, Width);
        var (a, b) = (l.Means[0].Rule, l.Means[1].Rule);
        Assert.Equal((LabelAlign.Left, LabelAlign.Left), (a.Align, b.Align));
        // The plot gives up a line for it.
        Assert.Equal(38, l.Plot.Y);
        Assert.Equal((16, 32), (a.Top, b.Top));
        Assert.Equal(l.Plot.Bottom, a.Bottom);
    }

    [Fact]
    public void A_run_with_no_hits_has_no_mean_and_no_share()
    {
        var l = PairedHistogramLayout.Compute(462, 210, Pairs(1000, 250, (4, 0), (0, 0), (8, 0), (2, 0)), 1500, null, 14, 0, Width);
        Assert.Equal([0], l.Means.Select(m => m.Run));
        Assert.Equal(LabelAlign.Left, l.Means[0].Rule.Align);
        Assert.All(l.Columns, c => Assert.Equal(0, c.Run));
        var h = l.Hover(46 + 250, 100)!;
        Assert.Equal([("A", "8 hits · 57.1%", 0), ("B", "—", 1)], h.Rows.Select(r => (r.Label, r.Value, r.Series)));

        // Alone near the right edge, the label flips as a single histogram's does.
        var right = PairedHistogramLayout.Compute(462, 210, Pairs(0, 100, (1, 0), (1, 0)), 195, null, 2, 0, Width);
        Assert.Equal(LabelAlign.Right, Assert.Single(right.Means).Rule.Align);
    }

    [Fact]
    public void The_pointer_reads_both_runs_of_a_bin()
    {
        var l = PairedHistogramLayout.Compute(462, 210, Pairs(1000, 250, (4, 1), (0, 1), (8, 1), (2, 1)), 1500, 1500, 14, 4, Width);
        var h = l.Hover(46 + 250, 100)!;
        Assert.Equal(2, h.Bin);
        Assert.Equal("1,500 " + (char)0x2013 + " 1,750", h.Head);
        Assert.Equal([("A", "8 hits · 57.1%"), ("B", "1 hit · 25.0%")], h.Rows.Select(r => (r.Label, r.Value)));
        Assert.Null(l.Hover(45, 100));

        // A heal's are casts.
        var heals = PairedHistogramLayout.Compute(462, 210, Pairs(1000, 250, (4, 1), (0, 1), (8, 1), (2, 1)), 1500, 1500, 14, 4,
                                                  Width, "cast");
        Assert.Equal(["8 casts · 57.1%", "1 cast · 25.0%"], heals.Hover(46 + 250, 100)!.Rows.Select(r => r.Value));

        Assert.True(PairedHistogramLayout.Compute(462, 210, [], null, null, 0, 0, Width).Empty);
        Assert.True(PairedHistogramLayout.Compute(462, 210, null, null, null, 0, 0, Width).Empty);
        Assert.True(PairedHistogramLayout.Compute(462, 210, Pairs(0, 100, (0, 0)), null, null, 0, 0, Width).Empty);
    }
}
