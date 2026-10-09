using Zerg.Core.Layout;

namespace Zerg.Core.Tests;

public class ColumnsTests
{
    // The tables' columns as the cards write them (Views/BarsCard.xaml,
    // ActionsCard.xaml, HealActionsCard.xaml): the design's widths at 930
    // as weights, with the least each may be.
    const string Damage = "134*,70*,68*,64*,58*,62*,68*,52*,54*,58*,64*,54*,66*,58*";
    const string Actions = "278*150,50*40,50*40,76*60,60*48,56*48,60*52,160*112,104*100,32*0";
    const string Heals = "360*150,56*44,110*64,96*52,96*52,96*52,104*100,8*0";

    static double[] Widths(string text, double width, params int[] gone)
    {
        var columns = Columns.Parse(text);
        var present = Enumerable.Range(0, columns.Length).Select(i => !gone.Contains(i)).ToArray();
        var into = new double[columns.Length];
        Columns.Resolve(columns, present, width, into);
        return into;
    }

    // ------------------------------------------------------------ reading

    [Fact]
    public void A_number_is_units_a_star_is_a_share_and_a_second_number_its_least()
    {
        Assert.Equal([new Column(96, false), new Column(1, true), new Column(2.4, true), new Column(278, true, 150)],
                     Columns.Parse("96, *, 2.4*, 278*150"));
        Assert.Empty(Columns.Parse(""));
        Assert.Empty(Columns.Parse(null));
    }

    // ------------------------------------------------------------- widths

    [Fact]
    public void Fixed_columns_take_their_width_and_shares_divide_the_rest_by_weight()
    {
        // The strip: a name, then three figures.
        Assert.Equal([324, 72, 52, 52], Widths("*,72,52,52", 500));
        Assert.Equal([100, 200, 100], Widths("*,2*,100", 400));
    }

    [Fact]
    public void At_the_sheets_width_a_table_written_in_the_sheets_widths_is_as_drawn()
    {
        Assert.Equal([134, 70, 68, 64, 58, 62, 68, 52, 54, 58, 64, 54, 66, 58], Widths(Damage, 930));
        Assert.Equal([278, 50, 50, 76, 60, 56, 60, 160, 104, 32], Widths(Actions, 926).Select(w => Math.Round(w, 6)));
        // The figures' right-hand edges on the sheet, 8 in from each column's end.
        var w = Widths(Damage, 930);
        double x = 0;
        var edges = w.Select(c => (x += c) - 8).ToArray();
        // (Damage and Damage % four units to the left of the sheet's 268
        // and 332: the name gave DPS four units.)
        Assert.Equal([264, 328, 386, 448, 516, 568, 622, 680, 744, 798, 864, 922], edges[2..]);
    }

    [Fact]
    public void Wider_or_narrower_every_share_keeps_its_proportion()
    {
        var w = Widths(Damage, 1860);
        Assert.Equal(268, w[0], 6);
        Assert.Equal(116, w[13], 6);
        Assert.Equal(1860, w.Sum(), 6);
        Assert.Equal(900, Widths(Damage, 900).Sum(), 6);
    }

    [Fact]
    public void A_column_that_is_not_there_takes_no_room_and_the_rest_share_it()
    {
        // Names hidden: the Job column goes.
        var w = Widths(Damage, 930, gone: 1);
        Assert.Equal(0, w[1]);
        Assert.Equal(930, w.Sum(), 6);
        Assert.Equal(134 * 930.0 / 860, w[0], 6);
    }

    [Fact]
    public void A_share_is_never_narrower_than_its_least_and_the_others_give_way()
    {
        // 700 for the Actions table: every figure column would be three
        // quarters of its width; the ones that cannot be are held, and the
        // name gives up the difference.
        var w = Widths(Actions, 700);
        var c = Columns.Parse(Actions);
        for (int i = 0; i < w.Length; i++) Assert.True(w[i] >= c[i].Least - 1e-9, $"column {i}: {w[i]}");
        Assert.Equal(700, w.Sum(), 6);
        Assert.Equal(100, w[8]);                // Share, with its bar, is held
        Assert.True(w[0] < 278 * 700 / 926.0);  // and the name pays for it
    }

    [Fact]
    public void In_less_than_every_least_the_row_is_wider_than_it_was_given()
    {
        var w = Widths(Actions, 400);
        Assert.Equal(Columns.Least(Actions), w.Sum(), 6);
        Assert.Equal(150, w[0]);
        // No room at all, or a width that is not a number: still the leasts.
        Assert.Equal(Columns.Least(Actions), Widths(Actions, 0).Sum(), 6);
        Assert.Equal(Columns.Least(Actions), Widths(Actions, double.NaN).Sum(), 6);
    }

    [Fact]
    public void With_no_width_to_share_a_share_is_its_least_or_its_weight_in_loose_shares()
    {
        Assert.Equal([84, 72, 201.6], Widths("*,72,2.4*", double.PositiveInfinity).Select(w => Math.Round(w, 6)));
        Assert.Equal(Columns.Least(Actions), Widths(Actions, double.PositiveInfinity).Sum() - 0, 6);
    }

    [Fact]
    public void More_cells_than_columns_or_fewer_is_no_fault()
    {
        // Fewer cells than columns: the columns with no cell still take
        // their room (the blank column at a table's end has no cell).
        var columns = Columns.Parse("*,50,30");
        var into = new double[3];
        Columns.Resolve(columns, [true], 200, into);
        Assert.Equal([120, 50, 30], into);
    }

    // ---------------------------------------------------- dropping columns

    [Fact]
    public void The_narrowest_a_table_can_be_is_its_fixed_widths_and_every_least()
    {
        Assert.Equal(650, Columns.Least(Actions));
        Assert.Equal(514, Columns.Least(Heals));
        // Written with no leasts: the per-character tables keep their
        // proportions and scroll sideways instead (their own least width).
        Assert.Equal(0, Columns.Least(Damage));
        Assert.Equal(196, Columns.Least("*20,72,52,52"));
        Assert.Equal(0, Columns.Least(""));
    }

    [Fact]
    public void Without_the_columns_it_drops_a_table_can_be_narrower()
    {
        var actions = Columns.Parse(Actions);
        // Miss, Min and the mark gone.
        bool[] present = [true, true, false, true, true, false, true, false, true, true];
        Assert.Equal(450, Columns.Least(actions, present));
        var heals = Columns.Parse(Heals);
        // Min gone.
        Assert.Equal(462, Columns.Least(heals, [true, true, true, true, false, true, true, true]));
    }

    // The Actions table without its mark: what is left after the first step.
    const string ActionsLess = "278*150,50*40,50*40,76*60,60*48,56*48,60*52,104*100,32*0";

    [Theory]
    // The Actions pane on the design's sheet, 940 wide: everything.
    [InlineData(940, false, false)]
    [InlineData(664, false, false)]
    // The mark goes first: the table as the design draws it floating over
    // the game, 660 wide less its panel's edges.
    [InlineData(663.9, true, false)]
    [InlineData(645, true, false)]
    [InlineData(552, true, false)]
    // Then Miss and Min: sheet 10's narrow pane, and the least a pane may be.
    [InlineData(551.9, true, true)]
    [InlineData(499, true, true)]
    [InlineData(320, true, true)]
    public void The_actions_table_sheds_columns_in_two_steps(double pane, bool mark, bool missAndMin)
    {
        // 14 units are kept clear beside the rows for the scroll bar.
        Assert.Equal(mark, Columns.Sheds(Actions, pane, 14));
        Assert.Equal(missAndMin, Columns.Sheds(ActionsLess, pane, 14));
    }

    [Theory]
    [InlineData(940, false)]
    [InlineData(528, false)]
    [InlineData(527.9, true)]
    [InlineData(480, true)]
    public void The_heals_table_sheds_its_least_column_later(double pane, bool sheds)
    {
        Assert.Equal(sheds, Columns.Sheds(Heals, pane, 14));
    }

    [Fact]
    public void A_table_that_has_not_been_given_a_room_yet_sheds_nothing()
    {
        Assert.False(Columns.Sheds(Actions, 0, 14));
        Assert.False(Columns.Sheds(Actions, double.NaN, 14));
    }
}
