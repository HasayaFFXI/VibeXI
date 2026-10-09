namespace Zerg.Core.Tests;

public class LowMarkTests
{
    // "Within 0.03", with room for the last digit of a double.
    const double Near = 0.0301;

    // The nine marks the design's sheets draw, with the threshold as
    // installed: the rule alone just under it, then the cell cut out of its
    // row more and more, then the rule gone and the cut-out at its strongest.
    [Theory]
    [InlineData(0.896, 0.56, 0, 0, 0)]
    [InlineData(0.881, 0.58, 0, 0, 0)]
    // The fit gives a faint cut-out here and the sheet draws none.
    [InlineData(0.844, 0.57, 0, 0, 0)]
    [InlineData(0.807, 0.37, 0.46, 0.08, 0.17)]
    [InlineData(0.804, 0.35, 0.49, 0.09, 0.19)]
    [InlineData(0.791, 0.28, 0.61, 0.11, 0.24)]
    [InlineData(0.784, 0.24, 0.67, 0.13, 0.28)]
    [InlineData(0.571, 0, 1.00, 0.34, 0.72)]
    public void The_marks_the_sheets_draw(double rate, double rule, double surface, double wash, double outline)
    {
        var look = LowMark.Of(rate, LowMark.Installed);
        Assert.InRange(look.Rule, rule - Near, rule + Near);
        Assert.InRange(look.Surface, surface - Near, surface + Near);
        Assert.InRange(look.Wash, wash - Near, wash + Near);
        Assert.InRange(look.Outline, outline - Near, outline + Near);
        Assert.Equal(surface > 0, look.CutOut);
    }

    [Theory]
    [InlineData(0.90)]
    [InlineData(0.9001)]
    [InlineData(0.97)]
    [InlineData(1.0)]
    public void At_the_threshold_or_over_it_nothing_is_drawn(double rate)
    {
        Assert.True(LowMark.Of(rate, 90).None);
    }

    [Fact]
    public void Nothing_to_measure_is_not_marked()
    {
        Assert.True(LowMark.Of(null, 90).None);
        Assert.True(LowMark.Of(double.NaN, 90).None);
    }

    [Fact]
    public void Just_under_the_threshold_is_a_rule_and_nothing_else()
    {
        var look = LowMark.Of(0.899, 90);
        Assert.False(look.None);
        Assert.False(look.CutOut);
        Assert.InRange(look.Rule, 0.55, 0.56);
        Assert.Equal(0, look.Wash);
        Assert.Equal(0, look.Outline);
    }

    [Fact]
    public void The_mark_only_deepens_as_the_rate_falls()
    {
        double surface = 0, wash = 0, outline = 0;
        for (double rate = 0.90; rate >= 0; rate -= 0.002)
        {
            var look = LowMark.Of(rate, 90);
            Assert.True(look.Surface >= surface - 1e-9, $"surface at {rate}");
            Assert.True(look.Wash >= wash - 1e-9, $"wash at {rate}");
            Assert.True(look.Outline >= outline - 1e-9, $"outline at {rate}");
            (surface, wash, outline) = (look.Surface, look.Wash, look.Outline);
        }
    }

    [Theory]
    [InlineData(0.60)]
    [InlineData(0.45)]
    [InlineData(0.0)]
    public void At_sixty_percent_and_under_it_is_at_its_strongest(double rate)
    {
        var look = LowMark.Of(rate, 90);
        Assert.Equal(0, look.Rule);
        Assert.Equal(1, look.Surface, 6);
        Assert.Equal(0.34, look.Wash, 6);
        Assert.Equal(0.72, look.Outline, 6);
    }

    [Fact]
    public void The_cut_out_is_whole_at_three_quarters()
    {
        Assert.Equal(1, LowMark.Of(0.75, 90).Surface, 6);
        Assert.InRange(LowMark.Of(0.751, 90).Surface, 0.98, 1);
        // The rule has all but gone by then, and is gone below.
        Assert.Equal(0, LowMark.Of(0.74, 90).Rule);
        Assert.InRange(LowMark.Of(0.76, 90).Rule, 0.05, 0.15);
    }

    [Fact]
    public void A_higher_threshold_marks_what_the_installed_one_leaves_alone()
    {
        Assert.True(LowMark.Of(0.93, 90).None);
        Assert.False(LowMark.Of(0.93, 95).None);
        Assert.False(LowMark.Of(0.999, 100).None);
        Assert.True(LowMark.Of(1.0, 100).None);
    }

    [Fact]
    public void Under_a_low_threshold_the_strongest_is_fifteen_points_further_down()
    {
        // At 70% the strongest is 55%, not 60%: there would be no room to grade in.
        Assert.True(LowMark.Of(0.70, 70).None);
        Assert.False(LowMark.Of(0.69, 70).CutOut);
        Assert.InRange(LowMark.Of(0.65, 70).Surface, 0.01, 0.999);
        Assert.Equal(0.72, LowMark.Of(0.55, 70).Outline, 6);
        // At the least threshold: 50% down to 35%.
        Assert.Equal(0.72, LowMark.Of(0.35, 50).Outline, 6);
        Assert.InRange(LowMark.Of(0.40, 50).Wash, 0.195, 0.34);
    }

    [Theory]
    [InlineData(90, 90)]
    [InlineData(49, 50)]
    [InlineData(-3, 50)]
    [InlineData(100, 100)]
    [InlineData(250, 100)]
    public void A_threshold_is_held_between_fifty_and_a_hundred(int asked, int taken)
    {
        Assert.Equal(taken, LowMark.Clamp(asked));
        // And a threshold out of range is read as the nearest that is in it.
        Assert.Equal(LowMark.Of(0.7, taken), LowMark.Of(0.7, asked));
    }
}
