using Zerg.Core.Charts;

namespace Zerg.Core.Tests;

public class BandMarksTests
{
    static CombatEvent Hit(double t, double dmg, string actor = "Hasaya") =>
        new() { T = t, Dmg = dmg, Hit = true, Actor = actor, Kind = "melee", Action = "Attack" };

    // ---------------------------------------------------------- the figures

    [Theory]
    [InlineData(1440, 5)]
    [InlineData(1040, 5)]
    [InlineData(1039, 3)]
    [InlineData(640, 3)]
    [InlineData(639, 2)]
    [InlineData(520, 2)]
    public void Five_figures_in_a_row_then_three_then_two(double width, int columns)
    {
        Assert.Equal(columns, BandMarks.Columns(width));
    }

    // Compare's band holds six, each two figures and a change: six across
    // while a cell has 200 units, and every row full under that.
    [Theory]
    [InlineData(1440, 6)]
    [InlineData(1200, 6)]
    [InlineData(1199, 3)]
    [InlineData(640, 3)]
    [InlineData(639, 2)]
    [InlineData(520, 2)]
    public void Six_pairs_in_a_row_then_three_then_two(double width, int columns)
    {
        Assert.Equal(columns, BandMarks.PairColumns(width));
        Assert.Equal(0, 6 % columns);
    }

    [Fact]
    public void A_cell_under_240_wide_has_no_room_for_its_mark()
    {
        Assert.True(BandMarks.RoomForMark(288));
        Assert.True(BandMarks.RoomForMark(240));
        Assert.False(BandMarks.RoomForMark(239.9));
        // 960 wide in five cells, as the design's narrower pieces are drawn.
        Assert.False(BandMarks.RoomForMark(960 / 5.0));
    }

    // ------------------------------------------------------------ the bucket

    [Theory]
    [InlineData(0, 20_000)]
    [InlineData(24_000, 20_000)]
    // Fourteen minutes twenty-six: the design's sheet, 44 bars of 20 seconds.
    [InlineData(866_000, 20_000)]
    [InlineData(880_000, 20_000)]
    // One millisecond more would be a forty-fifth bar.
    [InlineData(880_001, 40_000)]
    [InlineData(1_760_000, 40_000)]
    [InlineData(1_760_001, 80_000)]
    // Two hours: 160 seconds would need 45 bars.
    [InlineData(7_200_000, 320_000)]
    public void The_bucket_is_twenty_seconds_doubled_until_44_bars_are_enough(double span, double bucket)
    {
        Assert.Equal(bucket, BandMarks.Bucket(span));
        Assert.True(Math.Ceiling(span / bucket) <= BandMarks.MostBars);
    }

    [Fact]
    public void A_span_that_is_not_a_number_gets_the_shortest_bucket()
    {
        Assert.Equal(20_000, BandMarks.Bucket(double.NaN));
        Assert.Equal(20_000, BandMarks.Bucket(double.PositiveInfinity));
        Assert.Equal(20_000, BandMarks.Bucket(-5));
    }

    [Theory]
    [InlineData(20_000, "per 20 s")]
    [InlineData(40_000, "per 40 s")]
    [InlineData(320_000, "per 320 s")]
    [InlineData(1_280_000, "per 1,280 s")]
    public void The_caption_says_how_long_a_bar_is(double bucket, string caption)
    {
        Assert.Equal(caption, BandMarks.Caption(bucket));
    }

    // -------------------------------------------------------------- the pulse

    [Fact]
    public void Nothing_counted_is_no_mark()
    {
        Assert.Null(BandMarks.Pulse([], 60_000));
        // A miss, and a hit for nothing: neither is on the cumulative chart.
        Assert.Null(BandMarks.Pulse([new CombatEvent { T = 1000, Dmg = 0, Hit = false }, Hit(2000, 0)], 60_000));
    }

    [Fact]
    public void Each_bar_is_what_was_dealt_in_its_twenty_seconds()
    {
        var events = new[] { Hit(0, 100), Hit(19_999, 50), Hit(20_000, 30), Hit(45_000, 7) };
        var p = BandMarks.Pulse(events, 50_000)!;

        Assert.Equal(20_000, p.Bucket);
        Assert.Equal([150, 30, 7], p.Bars);
    }

    [Fact]
    public void Running_is_the_total_so_far_over_the_time_so_far()
    {
        var events = new[] { Hit(0, 100), Hit(19_999, 50), Hit(20_000, 30), Hit(45_000, 7) };
        var p = BandMarks.Pulse(events, 50_000)!;

        Assert.Equal(150 / 20.0, p.Running[0]);
        Assert.Equal(180 / 40.0, p.Running[1]);
        // The last stretch is still filling: it ends at the clock, so the
        // line ends on the figure printed beside it.
        Assert.Equal(187 / 50.0, p.Running[2]);
    }

    [Fact]
    public void A_silence_is_an_empty_bar_and_a_falling_rate()
    {
        var p = BandMarks.Pulse([Hit(1000, 400)], 100_000)!;

        Assert.Equal([400, 0, 0, 0, 0], p.Bars);
        Assert.Equal([20, 10, 400 / 60.0, 5, 4], p.Running);
    }

    [Fact]
    public void A_hit_on_the_last_instant_is_in_the_last_bar()
    {
        var p = BandMarks.Pulse([Hit(0, 1), Hit(40_000, 9)], 40_000)!;
        Assert.Equal([1, 9], p.Bars);
    }

    [Fact]
    public void Rows_dated_ahead_of_the_clock_stretch_the_span()
    {
        // As a fixture played in from a file arrives: all at once, the last
        // of it dated after the clock.
        var p = BandMarks.Pulse([Hit(5000, 10), Hit(65_000, 20)], 30_000)!;

        Assert.Equal([10, 0, 0, 20], p.Bars);
        Assert.Equal(30 / 65.0, p.Running[^1]);
    }

    [Fact]
    public void A_long_session_is_drawn_in_longer_bars_and_never_more_than_44()
    {
        var events = Enumerable.Range(0, 7200).Select(i => Hit(i * 1000.0, 1)).ToList();
        var p = BandMarks.Pulse(events, 7_200_000)!;

        Assert.Equal(320_000, p.Bucket);
        Assert.Equal(23, p.Bars.Count);
        Assert.Equal(320, p.Bars[0]);
        // 22 whole bars, then what is left of the two hours.
        Assert.Equal(7200 - 22 * 320, p.Bars[^1]);
        Assert.Equal(1.0, p.Running[^1]);
    }

    [Fact]
    public void The_bars_add_up_to_what_the_cumulative_lines_end_at()
    {
        var events = new List<CombatEvent>();
        for (int i = 0; i < 300; i++)
        {
            events.Add(Hit(i * 2900.0, 10 + i % 7, i % 3 == 0 ? "Hasaya" : "Kojiro"));
            events.Add(new CombatEvent { T = i * 2900.0 + 5, Actor = "Kojiro", Hit = false });
        }
        double elapsed = 300 * 2900.0;

        var p = BandMarks.Pulse(events, elapsed)!;
        var cum = Counting.Cumulative(events, ["Hasaya", "Kojiro"], 0, elapsed);

        Assert.Equal(cum.Series.Sum(s => s.Values[^1]), p.Bars.Sum());
        Assert.Equal(p.Bars.Sum() / (elapsed / 1000), p.Running[^1], 9);
    }

    // ------------------------------------------------------------ party share

    [Fact]
    public void A_share_bar_is_cut_in_proportion_with_a_gap_between_segments()
    {
        var s = BandMarks.Shares([60, 30, 10], 102, gap: 1, least: 1);

        // 100 units of bar and two gaps.
        Assert.Equal((0, 60), s[0]);
        Assert.Equal((61, 30), s[1]);
        Assert.Equal((92, 10), s[2]);
    }

    [Fact]
    public void A_share_too_small_to_see_is_given_the_least_and_the_others_give_it_up()
    {
        var s = BandMarks.Shares([999, 1], 101, gap: 1, least: 2);

        Assert.Equal(2, s[1].Width);
        Assert.Equal(98, s[0].Width);
        Assert.Equal(99, s[1].X);
        Assert.Equal(101, s[1].X + s[1].Width);
    }

    [Fact]
    public void Nobody_at_zero_has_a_segment_or_a_gap()
    {
        var s = BandMarks.Shares([50, 0, 50, double.NaN], 101, gap: 1, least: 1);

        Assert.Equal((0, 50), s[0]);
        Assert.Equal(0, s[1].Width);
        Assert.Equal((51, 50), s[2]);
        Assert.Equal(0, s[3].Width);
        Assert.All(BandMarks.Shares([0, 0], 100), x => Assert.Equal(0, x.Width));
        Assert.All(BandMarks.Shares([5, 5], 0), x => Assert.Equal(0, x.Width));
    }

    [Fact]
    public void A_bar_with_no_room_for_gaps_drops_them_and_still_fills_its_width()
    {
        var amounts = Enumerable.Repeat(1.0, 18).ToList();
        var s = BandMarks.Shares(amounts, 20, gap: 1, least: 1);

        Assert.Equal(20, s[^1].X + s[^1].Width, 9);
        Assert.All(s, x => Assert.Equal(20 / 18.0, x.Width, 9));
    }

    [Fact]
    public void The_segments_and_gaps_of_a_full_alliance_fill_the_bar()
    {
        var amounts = Enumerable.Range(1, 18).Select(i => (double)(i * i)).Reverse().ToList();
        var s = BandMarks.Shares(amounts, 110, gap: 1, least: 1);

        Assert.Equal(110, s[^1].X + s[^1].Width, 9);
        Assert.All(s, x => Assert.True(x.Width >= 1));
        for (int i = 1; i < s.Count; i++) Assert.Equal(s[i - 1].X + s[i - 1].Width + 1, s[i].X, 9);
    }

    // ------------------------------------------------------------ characters

    [Fact]
    public void Every_chip_is_on_the_line_when_all_fit()
    {
        // 66 + 4 + 75 + 4 + 59 = 208.
        Assert.Equal(3, BandMarks.Fit([66, 75, 59], gap: 4, room: 208, more: 33));
        Assert.Equal(0, BandMarks.Fit([], gap: 4, room: 0, more: 33));
    }

    [Fact]
    public void When_not_all_fit_the_line_keeps_room_for_the_chip_that_says_how_many_are_not_shown()
    {
        // One unit short of all three: two fit, with 4 and the 33 after them.
        Assert.Equal(2, BandMarks.Fit([66, 75, 59], gap: 4, room: 207, more: 33));
        // 66 + 4 + 75 + 4 + 33 = 182 is the least that holds two.
        Assert.Equal(2, BandMarks.Fit([66, 75, 59], gap: 4, room: 182, more: 33));
        Assert.Equal(1, BandMarks.Fit([66, 75, 59], gap: 4, room: 181, more: 33));
        // Not even one: the chip alone.
        Assert.Equal(0, BandMarks.Fit([66, 75, 59], gap: 4, room: 90, more: 33));
    }
}
