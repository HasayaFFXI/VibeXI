namespace Zerg.Core.Tests;

public class RowShadeTests
{
    static (byte R, byte G, byte B) Rgb(string hex) =>
        (Convert.ToByte(hex[1..3], 16), Convert.ToByte(hex[3..5], 16), Convert.ToByte(hex[5..7], 16));

    // The tokens, as Themes/Dark.xaml, Themes/Light.xaml and Themes/Panel.xaml have them.
    const string DarkBg1 = "#0F1217", DarkBg2 = "#151921", DarkText2 = "#A6AFBF", DarkCrit = "#FF5063", DarkLive = "#22E05A";
    const string LightBg1 = "#FFFFFF", LightText2 = "#474E5C";
    const string PanelBackdrop = "#06080C", PanelText2 = "#B9C1CF";

    static int Dark(string colour, string surface = DarkBg1) =>
        RowShade.Strength(Rgb(colour), Rgb(surface), Rgb(DarkText2), RowShade.DarkTarget, RowShade.DarkCap);

    static int Light(string colour) =>
        RowShade.Strength(Rgb(colour), Rgb(LightBg1), Rgb(LightText2), RowShade.LightTarget, RowShade.LightCap);

    // The eighteen job colours of the dark theme, and the strength the
    // design's table gives each: 30% unless the colour is pale enough that
    // the quieter ink would fall under 4.5 to one on it.
    [Theory]
    [InlineData("WAR", "#E60000", 30)]
    [InlineData("MNK", "#F5C403", 27)]
    [InlineData("WHM", "#FFFFFF", 20)]
    [InlineData("BLM", "#A685BD", 30)]
    [InlineData("RDM", "#E86366", 30)]
    [InlineData("THF", "#08C21C", 30)]
    [InlineData("PLD", "#E6FA17", 22)]
    [InlineData("DRK", "#E600FF", 30)]
    [InlineData("BST", "#CCBA78", 29)]
    [InlineData("BRD", "#F7A3E8", 28)]
    [InlineData("RNG", "#6EC459", 30)]
    [InlineData("SAM", "#FC6E03", 30)]
    [InlineData("NIN", "#F53000", 30)]
    [InlineData("DRG", "#B34AFA", 30)]
    [InlineData("SMN", "#70FCB5", 23)]
    [InlineData("BLU", "#4C8FD4", 30)]
    [InlineData("COR", "#C97557", 30)]
    [InlineData("PUP", "#827AAB", 30)]
    public void Dark_a_job_colour_is_as_strong_as_leaves_the_quieter_ink_legible(string job, string colour, int percent)
    {
        Assert.True(percent == Dark(colour), $"{job}: {Dark(colour)}");
    }

    // The light theme's job colours are graded for a white page already:
    // every one takes the cap but the reddest.
    [Theory]
    [InlineData("WAR", "#C00000", 21)]
    [InlineData("MNK", "#9A7A00", 22)]
    [InlineData("WHM", "#55534D", 22)]
    [InlineData("BLM", "#6F4E8C", 22)]
    [InlineData("RDM", "#C0393C", 22)]
    [InlineData("THF", "#0E7C1B", 22)]
    [InlineData("PLD", "#7C8600", 22)]
    [InlineData("DRK", "#A400B8", 22)]
    [InlineData("BST", "#7D6A30", 22)]
    [InlineData("BRD", "#B4519F", 22)]
    [InlineData("RNG", "#3F7F2E", 22)]
    [InlineData("SAM", "#C25200", 22)]
    [InlineData("NIN", "#C22600", 22)]
    [InlineData("DRG", "#7B2CC0", 22)]
    [InlineData("SMN", "#1E8C5C", 22)]
    [InlineData("BLU", "#2A6BAA", 22)]
    [InlineData("COR", "#9A4E33", 22)]
    [InlineData("PUP", "#565080", 22)]
    public void Light_a_job_colour_is_as_strong_as_leaves_the_quieter_ink_legible(string job, string colour, int percent)
    {
        Assert.True(percent == Light(colour), $"{job}: {Light(colour)}");
    }

    // A character's heading in the Actions table lies on the raised surface,
    // which is a little lighter, so a pale colour is held back further
    // there. The design's sheet draws these three at 30, 24 and 25: the
    // rule gives the third one point more, which is not a difference
    // anyone can see, and the rule is what is drawn (as for a run's band).
    [Theory]
    [InlineData("#FC6E03", 30)]
    [InlineData("#F5C403", 24)]
    [InlineData("#CCBA78", 26)]
    public void On_a_heading_row_the_same_rule_over_the_raised_surface(string colour, int percent)
    {
        Assert.Equal(percent, Dark(colour, DarkBg2));
    }

    // A strip over the game: the panel's own, lighter ink over its
    // backdrop, and a higher cap.
    [Theory]
    [InlineData("#FC6E03", 34)]
    [InlineData("#70FCB5", 32)]
    [InlineData("#FFFFFF", 28)]
    public void A_strip_over_the_game_may_be_stronger(string colour, int percent)
    {
        Assert.Equal(percent, RowShade.Strength(Rgb(colour), Rgb(PanelBackdrop), Rgb(PanelText2), RowShade.StripTarget, RowShade.StripCap));
    }

    // The same as the panels ask for it (RowShade.Strip), for every job
    // colour a panel can show (a panel is dark whatever the theme, so these
    // are the dark theme's eighteen). Fifteen take the cap; three are held
    // back, the palest furthest. The design's sheet draws 31 for a summoner and 27
    // for a white mage: the inputs that give those could not be recovered,
    // and the owner settled on the rule as written here (the plan's Q13).
    [Theory]
    [InlineData("WAR", "#E60000", 34)]
    [InlineData("MNK", "#F5C403", 34)]
    [InlineData("WHM", "#FFFFFF", 28)]
    [InlineData("BLM", "#A685BD", 34)]
    [InlineData("RDM", "#E86366", 34)]
    [InlineData("THF", "#08C21C", 34)]
    [InlineData("PLD", "#E6FA17", 31)]
    [InlineData("DRK", "#E600FF", 34)]
    [InlineData("BST", "#CCBA78", 34)]
    [InlineData("BRD", "#F7A3E8", 34)]
    [InlineData("RNG", "#6EC459", 34)]
    [InlineData("SAM", "#FC6E03", 34)]
    [InlineData("NIN", "#F53000", 34)]
    [InlineData("DRG", "#B34AFA", 34)]
    [InlineData("SMN", "#70FCB5", 32)]
    [InlineData("BLU", "#4C8FD4", 34)]
    [InlineData("COR", "#C97557", 34)]
    [InlineData("PUP", "#827AAB", 34)]
    public void In_a_panel_a_job_colour_is_as_strong_as_leaves_the_panels_quieter_ink_legible(string job, string colour, int percent)
    {
        Assert.True(percent == RowShade.Strip(Rgb(colour)), $"{job}: {RowShade.Strip(Rgb(colour))}");
    }

    // A character's heading in the Actions and Heals panels is a table's
    // row on the panel's raised surface: the tables' cap, 30, which is what
    // the design's sheet draws there.
    [Theory]
    [InlineData("SAM", "#FC6E03", 30)]
    [InlineData("DRK", "#E600FF", 30)]
    [InlineData("BLM", "#A685BD", 30)]
    [InlineData("RNG", "#6EC459", 30)]
    [InlineData("MNK", "#F5C403", 30)]
    // The pale ones are held back, as everywhere.
    [InlineData("SMN", "#70FCB5", 27)]
    [InlineData("PLD", "#E6FA17", 25)]
    [InlineData("WHM", "#FFFFFF", 23)]
    public void A_heading_in_a_panel_has_the_tables_cap_over_the_panels_raised_surface(string job, string colour, int percent)
    {
        Assert.True(percent == RowShade.PanelHeading(Rgb(colour)), $"{job}: {RowShade.PanelHeading(Rgb(colour))}");
    }

    [Fact]
    public void A_panels_raised_surface_is_white_at_7_percent_over_its_backdrop()
    {
        Assert.Equal(Rgb("#18191D"), RowShade.PanelRaised);
        // Never stronger behind a heading than the same colour is in a strip.
        foreach (string colour in new[] { "#FC6E03", "#FFFFFF", "#70FCB5", "#E6FA17", "#101010", "#4C8FD4" })
            Assert.True(RowShade.PanelHeading(Rgb(colour)) <= RowShade.Strip(Rgb(colour)), colour);
    }

    [Fact]
    public void A_panels_shade_is_worked_out_against_the_panels_own_backdrop_and_ink()
    {
        Assert.Equal(Rgb(PanelBackdrop), RowShade.PanelBackdrop);
        Assert.Equal(Rgb(PanelText2), RowShade.PanelText2);
        // Never over the cap, and never nothing: a panel's ink is pale and
        // its backdrop near black, so every colour can be laid on at some strength.
        for (int r = 0; r <= 255; r += 51)
            for (int g = 0; g <= 255; g += 51)
                for (int b = 0; b <= 255; b += 51)
                    Assert.InRange(RowShade.Strip(((byte)r, (byte)g, (byte)b)), 1, RowShade.StripCap);
    }

    [Fact]
    public void In_a_panel_the_quieter_ink_keeps_its_contrast_on_the_palest_shade()
    {
        static (byte, byte, byte) Over((byte R, byte G, byte B) top, (byte R, byte G, byte B) under, double a) =>
            ((byte)Math.Round(top.R * a + under.R * (1 - a)), (byte)Math.Round(top.G * a + under.G * (1 - a)),
             (byte)Math.Round(top.B * a + under.B * (1 - a)));
        var white = Rgb("#FFFFFF");
        int p = RowShade.Strip(white);
        Assert.True(RowShade.Contrast(RowShade.PanelText2, Over(white, RowShade.PanelBackdrop, p / 100.0)) >= 4.45);
        // At the flat 70% the strip had before, the same ink was all but gone.
        Assert.True(RowShade.Contrast(RowShade.PanelText2, Over(white, RowShade.PanelBackdrop, 0.70)) < 2);
    }

    // A run's band in Compare is set by the red a worse change is written
    // in and the green of a better one; on the dark theme the red decides.
    [Theory]
    [InlineData("#E8792B", 19)]
    [InlineData("#3987E5", 21)]
    [InlineData("#FFFFFF", 10)]
    // A dark colour stops at the cap.
    [InlineData("#101010", 26)]
    public void A_runs_band_leaves_the_red_of_a_worse_change_legible(string colour, int percent)
    {
        Assert.Equal(percent, RowShade.RunBand(Rgb(colour), Rgb(DarkBg1), Rgb(DarkCrit), Rgb(DarkLive)));
    }

    [Fact]
    public void One_point_more_would_fall_under_the_target()
    {
        var white = Rgb("#FFFFFF");
        int p = Dark("#FFFFFF");
        static (byte, byte, byte) Over((byte R, byte G, byte B) top, (byte R, byte G, byte B) under, double a) =>
            ((byte)Math.Round(top.R * a + under.R * (1 - a)), (byte)Math.Round(top.G * a + under.G * (1 - a)),
             (byte)Math.Round(top.B * a + under.B * (1 - a)));
        Assert.True(RowShade.Contrast(Rgb(DarkText2), Over(white, Rgb(DarkBg1), p / 100.0)) >= 4.45);
        Assert.True(RowShade.Contrast(Rgb(DarkText2), Over(white, Rgb(DarkBg1), (p + 2) / 100.0)) < 4.5);
    }

    [Fact]
    public void Ink_that_is_never_legible_on_the_colour_gets_no_shade()
    {
        // The ink and the surface are the same colour: nothing reads at any strength.
        Assert.Equal(0, RowShade.Strength(Rgb("#808080"), Rgb("#808080"), Rgb("#808080"), 4.5, 30));
        Assert.Equal(0, RowShade.Strength(Rgb("#FFFFFF"), Rgb(DarkBg1), Rgb(DarkText2), 4.5, 0));
    }

    [Fact]
    public void Contrast_is_one_for_a_colour_on_itself_and_21_for_black_on_white()
    {
        Assert.Equal(1, RowShade.Contrast(Rgb("#123456"), Rgb("#123456")), 6);
        Assert.Equal(21, RowShade.Contrast(Rgb("#000000"), Rgb("#FFFFFF")), 6);
        Assert.Equal(21, RowShade.Contrast(Rgb("#FFFFFF"), Rgb("#000000")), 6);
    }
}
