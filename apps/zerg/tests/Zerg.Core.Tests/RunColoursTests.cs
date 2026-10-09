namespace Zerg.Core.Tests;

public class RunColoursTests
{
    static (byte R, byte G, byte B) Rgb(string hex) =>
        (Convert.ToByte(hex[1..3], 16), Convert.ToByte(hex[3..5], 16), Convert.ToByte(hex[5..7], 16));

    // The tokens, as Themes/Dark.xaml and Themes/Light.xaml have them.
    const string DarkBg1 = "#0F1217", DarkBg2 = "#151921", DarkCrit = "#FF5063", DarkLive = "#22E05A", DarkText1 = "#E9ECF2";
    const string LightBg1 = "#FFFFFF", LightCrit = "#C42B3A", LightLive = "#0B7A45", White = "#FFFFFF";
    // The installed pair of each theme (the theme files' RunA and RunB).
    const string DarkA = "#3987E5", DarkB = "#E8792B", LightA = "#2A78D6", LightB = "#C94F18";

    static (byte, byte, byte) Over(string top, string under, int percent)
    {
        var (t, u, a) = (Rgb(top), Rgb(under), percent / 100.0);
        return ((byte)Math.Round(t.R * a + u.R * (1 - a)), (byte)Math.Round(t.G * a + u.G * (1 - a)),
                (byte)Math.Round(t.B * a + u.B * (1 - a)));
    }

    // ------------------------------------------------------------ the band

    // The dark theme: the design's 19% for the installed orange, and 21%
    // for the installed blue where its table says 20 (computed, by the
    // owner's decision: one point is not seen, and an exception would
    // defeat the rule for a colour someone picks). The red decides every
    // one of these: the dark theme's green is 8 to one on any band.
    [Theory]
    [InlineData(DarkA, 21)]
    [InlineData(DarkB, 19)]
    // Paler picks get a fainter band; a dark one stops at the cap.
    [InlineData("#FFFFFF", 10)]
    [InlineData("#000000", 26)]
    public void Dark_a_band_is_as_strong_as_leaves_red_legible(string colour, int percent)
    {
        Assert.Equal(percent, RunColours.Band(Rgb(colour), Rgb(DarkBg1), Rgb(DarkCrit), Rgb(DarkLive)));
        Assert.Equal(percent, RowShade.Strength(Rgb(colour), Rgb(DarkBg1), Rgb(DarkCrit), RowShade.BandTarget, RowShade.BandCap));
    }

    // The light theme has no sheet. The same rule with its own surface, its
    // own red and its own green: there a band darkens the page and both
    // inks are dark, so it is a deep colour that has to be held back, and a
    // pale one takes the cap. The green is the fainter of the two inks on
    // white (5.4 to one, the red 5.6), so it decides: the installed pair
    // comes out at 14% and 13%, where the red alone gave 17% and 16% (the
    // design's "16%") and left green at 4.35.
    [Theory]
    [InlineData(LightA, 14)]
    [InlineData(LightB, 13)]
    [InlineData("#FFFFFF", 26)]
    [InlineData("#000000", 8)]
    // The dark theme's installed pair, picked and so used under both themes.
    [InlineData(DarkA, 16)]
    [InlineData(DarkB, 18)]
    public void Light_the_same_rule_and_the_green_decides(string colour, int percent)
    {
        Assert.Equal(percent, RunColours.Band(Rgb(colour), Rgb(LightBg1), Rgb(LightCrit), Rgb(LightLive)));
    }

    [Theory]
    [InlineData(LightA, 17)]
    [InlineData(LightB, 16)]
    public void Light_the_red_alone_would_allow_more(string colour, int percent)
    {
        Assert.Equal(percent, RowShade.Strength(Rgb(colour), Rgb(LightBg1), Rgb(LightCrit), RowShade.BandTarget, RowShade.BandCap));
    }

    [Theory]
    [InlineData(DarkA, DarkBg1, DarkCrit, DarkLive)]
    [InlineData(DarkB, DarkBg1, DarkCrit, DarkLive)]
    [InlineData(LightA, LightBg1, LightCrit, LightLive)]
    [InlineData(LightB, LightBg1, LightCrit, LightLive)]
    [InlineData("#FFFF00", DarkBg1, DarkCrit, DarkLive)]
    [InlineData("#7A0000", LightBg1, LightCrit, LightLive)]
    [InlineData("#22E05A", DarkBg1, DarkCrit, DarkLive)]
    [InlineData("#0B7A45", LightBg1, LightCrit, LightLive)]
    public void Red_and_green_keep_their_contrast_on_the_band_and_two_points_more_would_lose_it(
        string colour, string surface, string crit, string live)
    {
        int p = RunColours.Band(Rgb(colour), Rgb(surface), Rgb(crit), Rgb(live));
        Assert.InRange(p, 1, RowShade.BandCap);
        // Worked out on exact colours and checked here on whole ones: a hair of slack.
        Assert.True(RowShade.Contrast(Rgb(crit), Over(colour, surface, p)) >= RowShade.BandTarget - 0.05);
        Assert.True(RowShade.Contrast(Rgb(live), Over(colour, surface, p)) >= RowShade.BandTarget - 0.05);
        if (p < RowShade.BandCap)
            Assert.True(Math.Min(RowShade.Contrast(Rgb(crit), Over(colour, surface, p + 2)),
                                 RowShade.Contrast(Rgb(live), Over(colour, surface, p + 2))) < RowShade.BandTarget);
    }

    [Fact]
    public void Green_is_legible_on_the_installed_bands_of_both_themes()
    {
        // The design's own figure for the dark pair: 8.1 to one or better.
        foreach (var run in new[] { DarkA, DarkB })
        {
            int p = RunColours.Band(Rgb(run), Rgb(DarkBg1), Rgb(DarkCrit), Rgb(DarkLive));
            Assert.True(RowShade.Contrast(Rgb(DarkLive), Over(run, DarkBg1, p)) >= 8.0);
        }
        // Light: the design expected about 4.4 to one at 16%, a little under
        // the 4.5 it holds to elsewhere, and said it would need tuning. With
        // the red's strength alone it was 4.35; held to the green's too it
        // is 4.5, and the red has a little to spare.
        foreach (var run in new[] { LightA, LightB })
        {
            int p = RunColours.Band(Rgb(run), Rgb(LightBg1), Rgb(LightCrit), Rgb(LightLive));
            Assert.InRange(RowShade.Contrast(Rgb(LightLive), Over(run, LightBg1, p)), 4.5, 4.6);
            Assert.InRange(RowShade.Contrast(Rgb(LightCrit), Over(run, LightBg1, p)), 4.6, 4.8);
        }
    }

    [Fact]
    public void An_opened_row_lies_on_the_raised_surface_with_the_same_band()
    {
        // The design: on the opened row red is 4.2 to one on either band.
        foreach (var run in new[] { DarkA, DarkB })
        {
            int p = RunColours.Band(Rgb(run), Rgb(DarkBg1), Rgb(DarkCrit), Rgb(DarkLive));
            double c = RowShade.Contrast(Rgb(DarkCrit), Over(run, DarkBg2, p));
            Assert.InRange(c, 4.1, 4.5);
        }
    }

    // ------------------------------------------------------------- the ink

    [Theory]
    // The design: near-black on the installed pair (5.2 and 6.5 to one).
    [InlineData(DarkA, DarkText1, "#101010")]
    [InlineData(DarkB, DarkText1, "#101010")]
    [InlineData("#FFFFFF", DarkText1, "#101010")]
    // A deep colour takes the theme's light text instead.
    [InlineData("#1A2A6C", DarkText1, DarkText1)]
    [InlineData("#000000", DarkText1, DarkText1)]
    // The light theme's text colour is near-black too, so its pale ink is
    // white: on its installed pair, which is deeper, the letter is white,
    // as it was before the colours could be set; on a pale pick, dark.
    [InlineData(LightA, White, White)]
    [InlineData(LightB, White, White)]
    [InlineData("#FFFF00", White, "#101010")]
    public void A_badges_letter_is_whichever_ink_is_clearer(string colour, string pale, string ink)
    {
        Assert.Equal(Rgb(ink), RunColours.InkOn(Rgb(colour), Rgb(pale)));
    }

    [Fact]
    public void The_design_s_contrasts_for_the_letter_hold()
    {
        Assert.InRange(RowShade.Contrast(Rgb(DarkA), RunColours.DarkInk), 5.1, 5.3);
        Assert.InRange(RowShade.Contrast(Rgb(DarkB), RunColours.DarkInk), 6.4, 6.6);
    }

    // ---------------------------------------------------------- as written

    [Theory]
    [InlineData("#3987E5", 0x39, 0x87, 0xE5)]
    [InlineData("3987e5", 0x39, 0x87, 0xE5)]
    [InlineData("  #e8792b ", 0xE8, 0x79, 0x2B)]
    [InlineData("#FFF", 0xFF, 0xFF, 0xFF)]
    [InlineData("38e", 0x33, 0x88, 0xEE)]
    public void A_colour_is_read_as_it_is_written(string text, int r, int g, int b)
    {
        Assert.True(RunColours.TryParse(text, out var c));
        Assert.Equal(((byte)r, (byte)g, (byte)b), c);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#")]
    [InlineData("blue")]
    [InlineData("#3987E")]
    [InlineData("#3987E5FF")]
    [InlineData("#39 87 E5")]
    [InlineData("#GG0000")]
    [InlineData("+12345")]
    public void Anything_else_is_not_a_colour(string? text)
    {
        Assert.False(RunColours.TryParse(text, out _));
        Assert.Null(RunColours.Normal(text));
    }

    [Fact]
    public void A_setting_holds_the_colour_written_the_one_way()
    {
        Assert.Equal("#3987E5", RunColours.Normal("3987e5"));
        Assert.Equal("#3388EE", RunColours.Normal(" #38e"));
        Assert.Equal("#000000", RunColours.Hex((0, 0, 0)));
        Assert.True(RunColours.TryParse(RunColours.Hex((1, 171, 255)), out var back));
        Assert.Equal(((byte)1, (byte)171, (byte)255), back);
    }
}
