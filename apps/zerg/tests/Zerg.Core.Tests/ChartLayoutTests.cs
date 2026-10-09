using Zerg.Core.Charts;

namespace Zerg.Core.Tests;

/// <summary>Where the charts put things: ticks, end labels, hover readouts and bins.</summary>
public class ChartLayoutTests
{
    sealed record Line(string Name, double[] Values, bool Group = false) : ILineSeries;

    /// <summary>Seven pixels a character: enough to make widths matter.</summary>
    static double Width(string s) => s.Length * 7;

    /// <summary>The same in either weight, unless a test says the stronger is wider.</summary>
    static double Width(string s, bool bold) => Width(s);

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

    [Fact]
    public void A_hover_card_is_a_heading_ruled_off_from_a_row_each()
    {
        // The card the design draws: ten characters at one instant.
        var rows = Enumerable.Range(0, 10).Select(i => (Label: i == 2 ? 55.0 : 40.0, Value: 36.0)).ToList();
        var l = HoverCard.Arrange(headWidth: 22, headSwatch: false, rows, swatches: true, chartHeight: 266, compact: false);

        // A swatch and its gap, the widest label, the least gap, the widest value, and 10 either side.
        Assert.Equal(12 + 55 + 14 + 36 + 20, l.Width);
        // The heading's band, the rule and what is under it, ten rows of 15, and the foot.
        Assert.Equal(23 + 3 + 150 + 2, l.Height);
        Assert.Equal(23, l.RuleY);
        Assert.Equal(11.5, l.HeadMid);
        Assert.Equal(10, l.HeadX);
        Assert.Equal(15, l.RowHeight);
        Assert.Equal(new CardRowPlace(10, 22, l.Width - 10, 26), l.Rows[0]);
        Assert.Equal(26 + 9 * 15, l.Rows[9].Top);
        Assert.All(l.Rows, r => Assert.Equal(l.Width - 10, r.ValueRight));
    }

    [Fact]
    public void A_hover_card_with_more_rows_than_the_chart_is_tall_runs_them_in_columns()
    {
        // An alliance over a chart floating in a small window.
        var rows = Enumerable.Range(0, 18).Select(i => (Label: 50.0 + i, Value: 30.0)).ToList();
        var l = HoverCard.Arrange(22, false, rows, swatches: true, chartHeight: 120, compact: true);

        // 120 less the air, the heading and the foot holds six rows of 13: three columns.
        Assert.Equal([23.0, 36, 49, 62, 75, 88], l.Rows.Take(6).Select(r => r.Top));
        Assert.Equal(l.Rows[0].Top, l.Rows[6].Top);
        Assert.Equal(l.Rows[0].Top, l.Rows[12].Top);
        Assert.Equal(20 + 3 + 6 * 13 + 2, l.Height);
        // Each column is as wide as its own widest row, and they stand 18 apart.
        Assert.Equal(8, l.Rows[0].SwatchX);
        Assert.Equal(8 + 12 + 55 + 14 + 30, l.Rows[0].ValueRight);
        Assert.Equal(l.Rows[0].ValueRight + 18, l.Rows[6].SwatchX);
        Assert.Equal(l.Rows[6].SwatchX + 12 + 61 + 14 + 30, l.Rows[6].ValueRight);
        Assert.Equal(l.Rows[17].ValueRight + 8, l.Width);

        // Never more than three, however short the chart.
        var cramped = HoverCard.Arrange(22, false, rows, true, chartHeight: 40, compact: true);
        Assert.Equal(6, cramped.Rows.Count(r => r.Top == cramped.Rows[0].Top) * 2);
    }

    [Fact]
    public void A_hover_card_is_never_narrower_than_its_heading_and_may_have_no_rows()
    {
        var wide = HoverCard.Arrange(200, headSwatch: true, [(30, 20)], swatches: false, chartHeight: 300, compact: false);
        Assert.Equal(12 + 200 + 20, wide.Width);
        Assert.Equal(22, wide.HeadX);                       // after its swatch
        Assert.Equal(10, wide.Rows[0].LabelX);              // no swatch, no room kept for one
        Assert.Equal(wide.Width - 10, wide.Rows[0].ValueRight);

        var bare = HoverCard.Arrange(30, false, [], false, 300, compact: false);
        Assert.Null(bare.RuleY);
        Assert.Equal((118, 23), (bare.Width, bare.Height));
        Assert.Equal(96, HoverCard.Arrange(30, false, [], false, 300, compact: true).Width);

        // The size is put on whole pixels when the caller says how.
        var snapped = HoverCard.Arrange(22, false, [(40.3, 36.4)], true, 266, false, v => Math.Ceiling(v));
        Assert.Equal(Math.Ceiling(12 + 40.3 + 14 + 36.4 + 20), snapped.Width);
        Assert.Equal(snapped.Width - 10, snapped.Rows[0].ValueRight);
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
    public void End_labels_name_every_line_and_are_kept_apart()
    {
        var times = new double[] { 0, 1000 };
        var lines = Enumerable.Range(0, 8).Select(i => new Line("Name" + i, [0, 1000 + i])).ToArray();
        var l = Layout(460, 264, times, lines, compact: true, endLabels: true);

        Assert.Equal(8, l.Labels.Count);
        Assert.All(l.Labels, x => Assert.True(x.Named));
        Assert.Equal("Name7", l.Labels[0].Text);            // the largest at the top
        var ys = l.Labels.Select(x => x.Y).ToList();
        for (int i = 1; i < ys.Count; i++) Assert.True(ys[i] - ys[i - 1] >= LineLayout.LabelPitch - 1e-9);
        // The right margin is as wide as the widest name needs: the gap
        // from the plot, the name, and the air after it.
        Assert.Equal(460 - 40 - (14 + Width("Name0") + 4), l.Plot.W);
        Assert.All(l.Labels, x => Assert.Equal(l.Plot.Right + 14, x.X));
        // Too narrow a chart to give up a column for the totals as well.
        Assert.All(l.Labels, x => Assert.Equal("", x.Total));
    }

    [Fact]
    public void At_the_width_the_design_draws_the_labels_are_names_alone()
    {
        // The pane of the design's sheet: 499 wide, 266 under its heading.
        var lines = new[] { new Line("Mireille", [0, 6433]), new Line("Parabellum", [0, 19226]), new Line("Hasaya", [0, 27256]) };
        var l = Layout(499, 266, [0, 866000], lines, endLabels: true);
        Assert.All(l.Labels, x => Assert.Equal("", x.Total));
        Assert.Equal(new Box(42, 14, 499 - 42 - (14 + Width("Parabellum") + 10), 230), l.Plot);
    }

    [Fact]
    public void A_chart_wide_enough_prints_each_total_after_the_name_in_a_column_of_its_own()
    {
        var lines = new[] { new Line("Mireille", [0, 6433]), new Line("Parabellum", [0, 19226]), new Line("Hasaya", [0, 27256]) };
        var l = Layout(900, 266, [0, 866000], lines, endLabels: true);
        Assert.Equal(["27.3K", "19.2K", "6,433"], l.Labels.Select(x => x.Total));
        // Names, a gap, then the totals against one right-hand edge.
        double right = l.Plot.Right + 14 + Width("Parabellum") + 6 + Width("27.3K");
        Assert.All(l.Labels, x => Assert.Equal(right, x.TotalRight));
        Assert.Equal(900 - 10, right);
        // The narrowest that still does: the plot keeps 400.
        double margins = 42 + 14 + Width("Parabellum") + 6 + Width("27.3K") + 10;
        Assert.NotEqual("", Layout(margins + 400, 266, [0, 866000], lines, endLabels: true).Labels[0].Total);
        Assert.Equal("", Layout(margins + 399, 266, [0, 866000], lines, endLabels: true).Labels[0].Total);
    }

    [Fact]
    public void End_labels_pushed_past_the_floor_settle_back_up_from_it()
    {
        var times = new double[] { 0, 1000 };
        var lines = new[] { new Line("Top", [0, 100000]) }
            .Concat(Enumerable.Range(0, 6).Select(i => new Line("Low" + i, [0, i]))).ToArray();
        var l = Layout(460, 264, times, lines, compact: true, endLabels: true);
        Assert.Equal(l.Plot.Bottom, l.Labels[^1].Y, 9);
        Assert.Equal(l.Plot.Bottom - 12, l.Labels[^2].Y, 9);
    }

    [Fact]
    public void A_label_moved_clear_of_its_neighbours_is_joined_to_its_marker()
    {
        // Two lines ending together and one far above them.
        var lines = new[] { new Line("Under", [0, 50000]), new Line("Over", [0, 50100]), new Line("Top", [0, 100000]) };
        var l = Layout(499, 266, [0, 1000], lines, endLabels: true);
        var (top, over, under) = (l.Labels[0], l.Labels[1], l.Labels[2]);
        Assert.Equal(["Top", "Over", "Under"], [top.Text, over.Text, under.Text]);

        // Level with their own markers: no line.
        Assert.Null(top.Elbow);
        Assert.Null(over.Elbow);
        Assert.Equal(l.Py(50100), over.Y, 9);
        // Moved down a pitch from the one above: out from the marker's
        // rim, across to the label's height, and level to just short of it.
        Assert.Equal(over.Y + 12, under.Y, 9);
        var e = under.Elbow!.Value;
        Assert.Equal((l.Plot.Right + 4, l.Plot.Right + 8, l.Plot.Right + 12), (e.X0, e.X1, e.X2));
        Assert.Equal(l.Py(50000), e.Y0, 9);
        Assert.Equal(under.Y, e.Y1, 9);
        Assert.True(e.X2 < under.X);
    }

    [Fact]
    public void A_chart_too_short_for_every_name_keeps_the_largest()
    {
        // An alliance in the least pane: 100 tall leaves a plot of 64,
        // which has six pitches of 12 in it.
        var lines = Enumerable.Range(0, 18).Select(i => new Line("N" + i, [0, 1000.0 * (i + 1)])).ToArray();
        var l = Layout(499, 100, [0, 1000], lines, endLabels: true);
        Assert.Equal(64, l.Plot.H);
        Assert.Equal(["N17", "N16", "N15", "N14", "N13", "N12"], l.Labels.Select(x => x.Text));
        Assert.True(l.Labels[0].Y >= l.Plot.Y - 1e-9);
        Assert.True(l.Labels[^1].Y <= l.Plot.Bottom + 1e-9);
        // Every line still has its marker, and the crosshair still reads them all.
        Assert.Equal(18, l.Markers.Count);
        Assert.Equal(18, l.Hover(l.Plot.Right, 50)!.Rows.Count);
        // The margin is sized to the names that are printed.
        Assert.Equal(499 - 42 - (14 + Width("N17") + 10), l.Plot.W);
    }

    [Fact]
    public void The_leader_is_the_largest_line_that_is_one_characters()
    {
        var lines = new[] { new Line("Small", [0, 10]), new Line("Big", [0, 900]), new Line("3 others", [0, 2000], Group: true) };
        var l = Layout(499, 266, [0, 1000], lines, endLabels: true);
        Assert.Equal(1, l.Leader);
        Assert.Equal(["3 others", "Big", "Small"], l.Labels.Select(x => x.Text));
        Assert.Equal([false, true, false], l.Labels.Select(x => x.Lead));

        // Its name is measured in the stronger weight it is drawn in.
        var wider = LineLayout.Compute(499, 266, [0, 1000], [new Line("Solo", [0, 5])], false, true, (s, bold) => s.Length * (bold ? 9 : 7));
        Assert.Equal(499 - 42 - (14 + 4 * 9 + 10), wider.Plot.W);

        // Two runs compared are equals: no names, so no leader.
        Assert.Equal(-1, Layout(499, 266, [0, 1000], lines).Leader);
        Assert.Equal(-1, Layout(499, 266, [], [], endLabels: true).Leader);
    }

    [Fact]
    public void A_long_name_is_cut_with_an_ellipsis()
    {
        var l = Layout(600, 264, [0, 1000], [new("An extraordinarily long name", [0, 5])], endLabels: true);
        Assert.Equal("An extraordi" + TextFit.Ellipsis, l.Labels[0].Text);
    }

    [Fact]
    public void A_stepped_edge_moves_the_time_axis_and_nothing_else()
    {
        // A live chart: the last grid time is the clock, and it moves on.
        var lines = new[] { new Line("A", [0, 400, 900, 900]), new Line("B", [0, 100, 2500, 2500]) };
        var l = Layout(499, 266, [0, 60000, 120000, 121000], lines, endLabels: true);
        var (plot, yTicks, markers, labels) = (l.Plot, l.YTicks.ToList(), l.Markers.ToList(), l.Labels.ToList());
        // A plot of 426 has room for nine labels: every 15 seconds of 2:01.
        Assert.Equal(["0:00", "0:15", "0:30", "0:45", "1:00", "1:15", "1:30", "1:45", "2:00"], l.XTicks.Select(t => t.Label));

        double[] later = [0, 60000, 120000, 185000];
        l.Retime(later);

        // What stands: the plot, the value axis, the markers and the names.
        Assert.Equal(plot, l.Plot);
        Assert.Equal(yTicks, l.YTicks);
        Assert.Equal(markers, l.Markers);
        Assert.Equal(labels, l.Labels);
        Assert.All(l.Markers, m => Assert.Equal(l.Plot.Right, m.X, 9));
        // What moves: the span, the time labels, and what the crosshair reads.
        Assert.Equal(185000, l.T1);
        Assert.Equal(l.Plot.Right, l.Px(185000), 9);
        Assert.Equal(["0:00", "0:30", "1:00", "1:30", "2:00", "2:30", "3:00"], l.XTicks.Select(t => t.Label));
        Assert.Equal("3:05", l.Hover(l.Plot.Right, 50)!.Head);

        // The same as working it all out again at the new time.
        var fresh = Layout(499, 266, later, lines, endLabels: true);
        Assert.Equal(fresh.XTicks, l.XTicks);
        Assert.Equal(fresh.Markers, l.Markers);
        Assert.Equal(fresh.Labels, l.Labels);
        Assert.Equal(fresh.YTicks, l.YTicks);
    }

    [Fact]
    public void Without_names_only_a_few_well_separated_lines_get_their_totals()
    {
        var times = new double[] { 0, 1000 };
        var apart = Layout(800, 320, times, [new("A", [0, 100]), new("B", [0, 50000])]);
        Assert.Equal(["100", "50.0K"], apart.Labels.OrderBy(x => x.Series).Select(x => x.Text));
        Assert.All(apart.Labels, x => Assert.False(x.Named));

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
        Assert.Equal(l.Px(1000) + LineLayout.BesideMarker, l.Labels.Single(x => x.Series == 0).X, 9);
        Assert.All(l.Labels, x => Assert.Null(x.Elbow));
        Assert.Equal(1, LineLayout.LastIndex([0, 500, double.NaN, double.NaN]));
        Assert.Equal(-1, LineLayout.LastIndex([double.NaN]));
    }

    [Fact]
    public void The_crosshair_reads_every_line_at_the_nearest_grid_time_largest_first()
    {
        var l = Layout(868, 320, [0, 60000, 120000, 180000, 240000],
                       [new("Small", [0, 10, 20, 30, 40]), new("Gone", [0, 5, double.NaN, double.NaN, double.NaN]),
                        new("Big", [0, 1000, 2500.4, 3000, 4000])]);
        // The plot is 42..808; halfway is the third grid time.
        Assert.Equal((42, 808), (l.Plot.X, l.Plot.Right));
        var h = l.Hover(42 + 766 / 2.0 + 20, 100)!;
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
        Assert.NotNull(l.Hover(l.Plot.X - 8, 100));
        Assert.Null(l.Hover(l.Plot.X - 9, 100));
        Assert.NotNull(l.Hover(l.Plot.Right + 8, l.Plot.Bottom + 8));
        Assert.Null(l.Hover(l.Plot.Right + 9, 100));
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

    // ------------------------------------------------------------ histogram

    static List<Bin> Bins(double lo, double width, params int[] counts) =>
        counts.Select((c, i) => new Bin(lo + i * width, lo + (i + 1) * width) { Count = c }).ToList();

    [Fact]
    public void Columns_fill_their_bins_and_empty_bins_draw_nothing()
    {
        // 438 wide: a margin of 36 for the counts and 2 at the right leave a plot of 400.
        var l = HistogramLayout.Compute(438, 170, Bins(0, 100, 4, 0, 8, 2), 180, 14);
        Assert.Equal(new Box(36, 20, 400, 128), l.Plot);
        Assert.Equal(100, l.Slot);
        Assert.Equal([0, 2, 3], l.Columns.Select(c => c.Bin));
        // A pixel between neighbours, half of it either side of the slot's edge.
        Assert.All(l.Columns, c => Assert.Equal(99, c.W));
        Assert.Equal([36.5, 236.5, 336.5], l.Columns.Select(c => c.X));
        Assert.All(l.Columns, c => Assert.Equal(2, c.Radius));
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
        var whole = HistogramLayout.Compute(462, 210, Bins(0, 100, 1, 1, 1, 1), 100, 4);
        var left = whole.Mean!;
        Assert.Equal(("avg 100", LabelAlign.Left), (left.Label, left.Align));
        Assert.Equal(left.X + 5, left.LabelX);
        // The rule stands a little above the plot, where its label is.
        Assert.Equal((whole.Plot.Y - 4, whole.Plot.Bottom), (left.Top, left.Bottom));

        var right = HistogramLayout.Compute(462, 210, Bins(0, 100, 1, 1, 1, 1), 390, 4).Mean!;
        Assert.Equal(LabelAlign.Right, right.Align);
        Assert.Equal(right.X - 5, right.LabelX);

        Assert.Null(HistogramLayout.Compute(462, 210, Bins(0, 100, 1, 1), 999, 2).Mean);

        // Told how wide the label is, it turns round exactly when the label
        // would run off the chart (the plot here is 26 to 436).
        var fits = HistogramLayout.Compute(438, 170, Bins(0, 100, 1, 1, 1, 1), 340, 4, labelWidth: (s, _) => s.Length * 7).Mean!;
        Assert.Equal((375.5, LabelAlign.Left), (fits.X, fits.Align));         // 375.5 + 5 + 49 is inside 438
        var runsOff = HistogramLayout.Compute(438, 170, Bins(0, 100, 1, 1, 1, 1), 350, 4, labelWidth: (s, _) => s.Length * 7).Mean!;
        Assert.Equal((385.5, LabelAlign.Right), (runsOff.X, runsOff.Align));  // 385.5 + 5 + 49 is not
    }

    [Fact]
    public void The_middle_half_is_a_band_the_height_of_the_plot()
    {
        var l = HistogramLayout.Compute(438, 170, Bins(0, 100, 4, 0, 8, 2), 180, 14, q1: 100, q3: 250, median: 200);
        Assert.Equal(new Box(136, 20, 150, 128), l.Band);

        // Kept to the plot; and hits that are all alike, or quartiles not given, have none.
        Assert.Equal(new Box(36, 20, 400, 128), HistogramLayout.Compute(438, 170, Bins(0, 100, 4, 0, 8, 2), 180, 14, -50, 900).Band);
        Assert.Null(HistogramLayout.Compute(438, 170, Bins(0, 100, 4, 0, 8, 2), 180, 14, 200, 200).Band);
        Assert.Null(HistogramLayout.Compute(438, 170, Bins(0, 100, 4, 0, 8, 2), 180, 14, double.NaN, 200).Band);
        Assert.Null(HistogramLayout.Compute(438, 170, Bins(0, 100, 4, 0, 8, 2), 180, 14).Band);
        Assert.Null(HistogramLayout.Compute(438, 170, Bins(0, 100, 4, 0, 8, 2), 180, 14, 450, 500).Band);
    }

    [Fact]
    public void The_median_is_a_tick_across_the_baseline()
    {
        var l = HistogramLayout.Compute(438, 170, Bins(0, 100, 4, 0, 8, 2), 180, 14, q1: 100, q3: 250, median: 200);
        // On a pixel centre, five above the floor and four below it.
        Assert.Equal(new MedianTick(236.5, l.Plot.Bottom - 5, l.Plot.Bottom + 4), l.Median);
        Assert.Null(HistogramLayout.Compute(438, 170, Bins(0, 100, 4, 0, 8, 2), 180, 14).Median);
        Assert.Null(HistogramLayout.Compute(438, 170, Bins(0, 100, 4, 0, 8, 2), 180, 14, median: 999).Median);
    }

    [Fact]
    public void The_left_margin_is_as_wide_as_the_longest_count()
    {
        static double Seven(string s, bool bold) => s.Length * 7;
        // Counts of one figure: the least margin.
        Assert.Equal(26, HistogramLayout.Compute(438, 170, Bins(0, 100, 4, 0, 8, 2), 180, 14, labelWidth: Seven).Plot.X);
        // Counts in the thousands: "1,200" and the gap to the plot.
        Assert.Equal(35 + 6 + 2, HistogramLayout.Compute(438, 170, Bins(0, 100, 400, 0, 1200, 2), 180, 1602, labelWidth: Seven).Plot.X);
        // Not told: a margin that fits those too.
        Assert.Equal(36, HistogramLayout.Compute(438, 170, Bins(0, 100, 4, 0, 8, 2), 180, 14).Plot.X);
    }

    [Fact]
    public void The_pointer_reads_a_bin()
    {
        var l = HistogramLayout.Compute(462, 210, Bins(1000, 250, 4, 0, 8, 2), 1500, 14);
        var h = l.Hover(l.Plot.X + 2.5 * l.Slot, 100)!;
        Assert.Equal(2, h.Bin);
        Assert.Equal("1,500 " + (char)0x2013 + " 1,750", h.Head);
        Assert.Equal([("Hits", "8"), ("Share", "57.1%")], h.Rows.Select(r => (r.Label, r.Value)));
        Assert.Null(l.Hover(l.Plot.X - 1, 100));
        Assert.Null(l.Hover(200, 0));
    }

    [Fact]
    public void Identical_hits_make_one_column()
    {
        var l = HistogramLayout.Compute(462, 210, [new Bin(500, 500) { Count = 3 }], 500, 3);
        Assert.Single(l.Columns);
        Assert.Equal(l.Plot.W, l.Columns[0].W + 1);   // the one bin is the whole plot
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
