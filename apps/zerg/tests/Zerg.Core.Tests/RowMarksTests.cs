namespace Zerg.Core.Tests;

public class RowMarksTests
{
    // ---------------------------------------------------- a row's share

    [Fact]
    public void A_rows_shade_is_its_total_out_of_the_leaders()
    {
        // The design's sheet: the leader's row is shaded its whole length,
        // and 31,927 of 40,659 comes to 738.12 of 940.
        Assert.Equal(1, RowMarks.Fraction(40659, 40659));
        Assert.Equal(738.12, RowMarks.Fraction(31927, 40659) * 940, 2);
        Assert.Equal(0, RowMarks.Fraction(0, 40659));
    }

    [Fact]
    public void An_actions_bar_is_its_total_out_of_that_characters_largest_action()
    {
        // 10,629 of 13,105 on a bar 42 long is the sheet's 34.06.
        Assert.Equal(34.06, RowMarks.Fraction(10629, 13105) * 42, 2);
    }

    [Theory]
    [InlineData(5, 0)]
    [InlineData(5, -1)]
    [InlineData(double.NaN, 10)]
    [InlineData(double.PositiveInfinity, 10)]
    [InlineData(5, double.NaN)]
    [InlineData(5, double.PositiveInfinity)]
    public void Nothing_to_be_a_share_of_is_no_length(double value, double largest)
    {
        Assert.Equal(0, RowMarks.Fraction(value, largest));
    }

    [Fact]
    public void A_share_is_never_longer_than_the_room_or_shorter_than_nothing()
    {
        Assert.Equal(1, RowMarks.Fraction(12, 10));
        Assert.Equal(0, RowMarks.Fraction(-4, 10));
    }

    [Fact]
    public void The_largest_of_none_is_nothing()
    {
        Assert.Equal(0, RowMarks.Largest([]));
        Assert.Equal(0, RowMarks.Largest([0, 0]));
        Assert.Equal(7, RowMarks.Largest([3, 7, 5]));
        Assert.Equal(7, RowMarks.Largest([3, double.NaN, 7, double.PositiveInfinity]));
    }

    // -------------------------------------------- least, average, greatest

    [Fact]
    public void An_actions_spread_is_drawn_to_the_characters_biggest_hit()
    {
        // The sheet's Tachi: Gekko, 452 to 1,046 averaging 819, on a line
        // 132 long that ends at the character's biggest hit, 1,046.
        var (min, avg, max) = RowMarks.Spread(452, 819, 1046, 1046);
        Assert.Equal(57.04, min * 132, 2);
        Assert.Equal(103.36, avg * 132, 1);
        Assert.Equal(132, max * 132, 2);

        // And Attack on the same line: 17 to 129 averaging 53.
        (min, avg, max) = RowMarks.Spread(17, 53, 129, 1046);
        Assert.Equal(2.15, min * 132, 2);
        Assert.Equal(16.28, max * 132, 2);
        Assert.InRange(avg, min, max);
    }

    [Fact]
    public void A_spread_is_in_order_whatever_it_is_given()
    {
        var (min, avg, max) = RowMarks.Spread(50, 500, 40, 100);
        Assert.True(min <= avg && avg <= max);
        Assert.Equal((0, 0, 0), RowMarks.Spread(10, 20, 30, 0));
        // An action that never hit: all three are 0.
        Assert.Equal((0, 0, 0), RowMarks.Spread(0, 0, 0, 1046));
    }

    // -------------------------------------------- one hit against the average

    [Fact]
    public void A_hits_bar_is_how_far_it_fell_from_the_average_out_of_the_furthest()
    {
        // The sheet's Attack: average 53, hits from 17 to 129, so the
        // furthest is 76 over; +55 on a bar 36 long is the sheet's 26.03.
        double furthest = RowMarks.Furthest([17, 47, 57, 108, 129], 53);
        Assert.Equal(76, furthest);
        Assert.Equal(26.05, RowMarks.Offset(108, 53, furthest) * 36, 2);
        Assert.Equal(1, RowMarks.Offset(129, 53, furthest));
        Assert.True(RowMarks.Offset(47, 53, furthest) < 0);
        Assert.Equal(0, RowMarks.Offset(53, 53, furthest));
    }

    [Fact]
    public void Hits_that_are_all_alike_have_no_bar()
    {
        double furthest = RowMarks.Furthest([40, 40, 40], 40);
        Assert.Equal(0, furthest);
        Assert.Equal(0, RowMarks.Offset(40, 40, furthest));
        Assert.Equal(0, RowMarks.Furthest([], 40));
    }

    [Fact]
    public void An_offset_stays_between_minus_one_and_one()
    {
        Assert.Equal(1, RowMarks.Offset(500, 50, 10));
        Assert.Equal(-1, RowMarks.Offset(-500, 50, 10));
        Assert.Equal(0, RowMarks.Offset(double.NaN, 50, 10));
    }
}
