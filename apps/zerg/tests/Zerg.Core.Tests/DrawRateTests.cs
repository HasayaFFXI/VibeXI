namespace Zerg.Core.Tests;

public class DrawRateTests
{
    [Fact]
    public void A_live_chart_is_drawn_thirty_times_a_second_until_someone_says_otherwise()
    {
        Assert.Equal(30, DrawRate.Initial);
        Assert.Equal(30, DrawRate.Clamp(double.NaN));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(60, 60)]
    [InlineData(30, 30)]
    [InlineData(-5, 1)]
    [InlineData(61, 60)]
    [InlineData(5000, 60)]
    [InlineData(29.5, 30)]
    [InlineData(29.4, 29)]
    public void A_value_is_a_whole_number_of_draws_within_range(double given, int expected)
    {
        Assert.Equal(expected, DrawRate.Clamp(given));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.4)]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    public void Nothing_usable_is_the_fallback_not_the_slowest_value(double given)
    {
        Assert.Equal(30, DrawRate.Clamp(given));
        Assert.Equal(12, DrawRate.Clamp(given, 12));
    }

    [Theory]
    [InlineData(1, 1000)]
    [InlineData(4, 250)]
    [InlineData(30, 1000 / 30.0)]
    [InlineData(60, 1000 / 60.0)]
    public void The_interval_is_a_second_shared_out_between_the_draws(int perSecond, double ms)
    {
        Assert.Equal(ms, DrawRate.Interval(perSecond).TotalMilliseconds, 3);
    }

    [Fact]
    public void An_interval_is_never_asked_of_a_value_out_of_range()
    {
        Assert.Equal(DrawRate.Interval(30), DrawRate.Interval(0));
        Assert.Equal(DrawRate.Interval(60), DrawRate.Interval(1000));
    }
}
