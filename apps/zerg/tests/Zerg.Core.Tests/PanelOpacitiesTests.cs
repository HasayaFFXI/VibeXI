namespace Zerg.Core.Tests;

public class PanelOpacitiesTests
{
    [Fact]
    public void A_panel_opens_see_through_until_someone_says_otherwise()
    {
        var o = new PanelOpacities();
        Assert.Equal(85, o.Default);
        Assert.Equal(85, o.Of("line"));
        Assert.Empty(o.Own);
    }

    [Fact]
    public void A_panel_with_no_value_of_its_own_follows_the_default()
    {
        var o = new PanelOpacities(60, new Dictionary<string, int> { ["bars"] = 40 });
        Assert.Equal(60, o.Of("line"));
        Assert.Equal(40, o.Of("bars"));
    }

    [Fact]
    public void A_panels_own_value_touches_no_other_panel()
    {
        var o = new PanelOpacities(60);
        Assert.Equal(30, o.Set("line", 30));
        Assert.Equal(30, o.Of("line"));
        Assert.Equal(60, o.Of("bars"));
        Assert.Equal(60, o.Default);
        Assert.Equal(["line"], o.Own.Keys);
    }

    [Fact]
    public void Setting_the_default_moves_only_the_panels_with_no_value_of_their_own()
    {
        var o = new PanelOpacities(60, new Dictionary<string, int> { ["line"] = 30 });
        Assert.Equal(70, o.SetDefault(70));
        Assert.Equal(30, o.Of("line"));
        Assert.Equal(70, o.Of("bars"));
        Assert.Equal(["line"], o.Own.Keys);

        // A panel's own slider takes it out of the default's reach from then on.
        o.Set("bars", 50);
        Assert.Equal(40, o.SetDefault(40));
        Assert.Equal(30, o.Of("line"));
        Assert.Equal(50, o.Of("bars"));
        Assert.Equal(40, o.Of("drill"));
    }

    [Theory]
    [InlineData(15, 15)]
    [InlineData(100, 100)]
    [InlineData(14, 15)]
    [InlineData(1, 15)]
    [InlineData(-20, 15)]
    [InlineData(101, 100)]
    [InlineData(5000, 100)]
    [InlineData(49.5, 50)]
    [InlineData(49.4, 49)]
    public void A_value_is_a_whole_percentage_within_range(double given, int expected)
    {
        Assert.Equal(expected, PanelOpacities.Clamp(given, 85));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.4)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Nothing_usable_is_the_fallback_not_the_faintest_value(double given)
    {
        Assert.Equal(85, PanelOpacities.Clamp(given, 85));
        Assert.Equal(60, PanelOpacities.Clamp(given, 60));
    }

    [Fact]
    public void A_damaged_settings_file_still_gives_usable_values()
    {
        var o = new PanelOpacities(0, new Dictionary<string, int> { ["line"] = 0, ["bars"] = 900, ["drill"] = -3 });
        Assert.Equal(85, o.Default);
        Assert.Equal(85, o.Of("line"));
        Assert.Equal(100, o.Of("bars"));
        Assert.Equal(15, o.Of("drill"));
    }

    [Fact]
    public void An_unusable_default_keeps_the_one_in_force()
    {
        var o = new PanelOpacities(60);
        Assert.Equal(60, o.SetDefault(double.NaN));
        Assert.Equal(60, o.SetDefault(0));
        Assert.Equal(60, o.Default);
    }
}
