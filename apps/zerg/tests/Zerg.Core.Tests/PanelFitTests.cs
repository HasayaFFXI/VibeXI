using Zerg.Core.Layout;

namespace Zerg.Core.Tests;

public class PanelFitTests
{
    // The scales Windows offers, and a few a custom setting can give.
    public static readonly TheoryData<double> Scales = [1, 1.25, 1.5, 1.75, 2, 2.25, 2.5, 3, 3.5, 1.1, 1.33];

    // The design's strip (sheet 04, panel 1): ten characters in a panel 266
    // tall. The frame's edge and 29 of bar (30 to under the bar's rule), 20
    // of headings, ten rows of 20 with a 1-unit gap each, 5 under the last
    // gap, and the frame's other edge: 266 at 100%, as drawn.
    [Fact]
    public void At_100_percent_a_strip_is_its_parts_added_up()
    {
        Assert.Equal(30, PanelFit.Edge + PanelFit.Bar);
        Assert.Equal(2 + 29 + 20 + 10 * 21 + 5, PanelFit.StripPixels(10, 1));
        Assert.Equal(266, PanelFit.StripHeight(10, 1));
        Assert.Equal(2 + 29 + 20 + 18 * 21 + 5, PanelFit.StripHeight(18, 1));
    }

    // At 150% a row and its gap are 32 pixels, not 31.5, and an edge is two:
    // ten rows are 406 pixels, which is 270 and two thirds units. Adding
    // units up gives 266, seven pixels short: a scroll bar.
    [Fact]
    public void At_150_percent_every_part_is_rounded_up_to_whole_pixels()
    {
        Assert.Equal(32, PanelFit.Pixels(PanelFit.StripRow + PanelFit.StripGap, 1.5));
        Assert.Equal(2, PanelFit.Pixels(PanelFit.Edge, 1.5));
        Assert.Equal(4 + 44 + 30 + 10 * 32 + 8, PanelFit.StripPixels(10, 1.5));
        Assert.Equal(271, PanelFit.StripHeight(10, 1.5));
        Assert.True(266 * 1.5 < PanelFit.StripPixels(10, 1.5));
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public void A_strips_panel_is_never_a_pixel_short_of_its_rows(double scale)
    {
        for (int rows = 0; rows <= 24; rows++)
        {
            double units = PanelFit.StripHeight(rows, scale);
            int shown = Math.Max(rows, PanelFit.LeastRows);
            // The window is given whole units; whatever way Windows rounds
            // that to pixels, down is the worst.
            Assert.True(Math.Floor(units * scale + 1e-9) >= PanelFit.StripPixels(shown, scale), $"{rows} rows at {scale}: {units}");
            // And no taller than it has to be: a unit less would be short.
            Assert.True(Math.Floor((units - 1) * scale + 1e-9) < PanelFit.StripPixels(shown, scale), $"{rows} rows at {scale}: {units} is too tall");
            Assert.Equal(units, Math.Floor(units));
        }
    }

    [Fact]
    public void A_strip_opens_with_room_for_three_rows_at_least()
    {
        Assert.Equal(PanelFit.StripHeight(3, 1), PanelFit.StripHeight(0, 1));
        Assert.Equal(PanelFit.StripHeight(3, 1.5), PanelFit.StripHeight(1, 1.5));
        Assert.True(PanelFit.StripHeight(4, 1) > PanelFit.StripHeight(3, 1));
    }

    [Fact]
    public void A_length_is_whole_pixels_and_never_none()
    {
        Assert.Equal(30, PanelFit.Pixels(30, 1));
        Assert.Equal(45, PanelFit.Pixels(30, 1.5));
        Assert.Equal(38, PanelFit.Pixels(30, 1.25));
        Assert.Equal(1, PanelFit.Pixels(0.2, 1));
        Assert.Equal(0, PanelFit.Pixels(0, 2));
        // A length that is whole pixels already is not pushed up by the
        // last digit of a multiplication.
        Assert.Equal(21, PanelFit.Pixels(21, 1));
        Assert.Equal(63, PanelFit.Pixels(21, 3));
    }

    [Fact]
    public void A_scale_that_is_not_one_is_taken_for_100_percent()
    {
        Assert.Equal(PanelFit.StripHeight(10, 1), PanelFit.StripHeight(10, 0));
        Assert.Equal(PanelFit.StripHeight(10, 1), PanelFit.StripHeight(10, double.NaN));
        Assert.Equal(PanelFit.StripPixels(10, 1), PanelFit.StripPixels(10, -2));
    }

    // The design's bar works down to about 250 wide; its tools take the
    // title's place under 360 (the plan's Q7).
    [Fact]
    public void The_title_stays_beside_the_tools_from_360_units_wide()
    {
        Assert.True(PanelFit.TitleStays(360));
        Assert.True(PanelFit.TitleStays(640));
        Assert.False(PanelFit.TitleStays(359.5));
        Assert.False(PanelFit.TitleStays(PanelFit.LeastWidth));
    }
}
