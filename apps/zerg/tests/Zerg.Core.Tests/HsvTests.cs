namespace Zerg.Core.Tests;

// The arithmetic under the Settings page's colour picker: a colour as hue,
// saturation and brightness, and back.
public class HsvTests
{
    static (byte R, byte G, byte B) Rgb(string hex) =>
        (Convert.ToByte(hex[1..3], 16), Convert.ToByte(hex[3..5], 16), Convert.ToByte(hex[5..7], 16));

    // ------------------------------------------------------ from a colour

    [Theory]
    [InlineData("#FF0000", 0, 1, 1)]
    [InlineData("#FFFF00", 60, 1, 1)]
    [InlineData("#00FF00", 120, 1, 1)]
    [InlineData("#00FFFF", 180, 1, 1)]
    [InlineData("#0000FF", 240, 1, 1)]
    [InlineData("#FF00FF", 300, 1, 1)]
    // Half as bright, half as saturated.
    [InlineData("#800000", 0, 1, 0.502)]
    [InlineData("#FF8080", 0, 0.498, 1)]
    // The installed pair of the dark theme.
    [InlineData("#3987E5", 212.791, 0.751, 0.898)]
    [InlineData("#E8792B", 24.762, 0.815, 0.910)]
    public void A_colour_has_its_hue_saturation_and_brightness(string colour, double h, double s, double v)
    {
        var got = Hsv.Of(Rgb(colour));
        Assert.Equal(h, got.H, 3);
        Assert.Equal(s, got.S, 3);
        Assert.Equal(v, got.V, 3);
    }

    // What has no hue says 0 for it, and black 0 for its saturation too.
    [Theory]
    [InlineData("#FFFFFF", 1)]
    [InlineData("#808080", 0.502)]
    [InlineData("#000000", 0)]
    public void A_gray_has_no_hue_and_no_saturation(string colour, double v)
    {
        var got = Hsv.Of(Rgb(colour));
        Assert.Equal(0, got.H);
        Assert.Equal(0, got.S);
        Assert.Equal(v, got.V, 3);
    }

    // Sheet 06 draws the picker open on the installed orange: the square's
    // marker 122.2 of 150 across and 9 of 100 down, the strip's 6.9 of 100
    // down.
    [Fact]
    public void The_sheets_markers_are_where_the_installed_orange_is()
    {
        var orange = Hsv.Of(Rgb("#E8792B"));
        Assert.Equal(122.2, orange.S * 150, 1);
        Assert.Equal(9.0, (1 - orange.V) * 100, 1);
        Assert.Equal(6.9, orange.H / 360 * 100, 1);
    }

    // --------------------------------------------------------- to a colour

    [Theory]
    [InlineData(0, 1, 1, "#FF0000")]
    [InlineData(60, 1, 1, "#FFFF00")]
    [InlineData(120, 1, 1, "#00FF00")]
    [InlineData(180, 1, 1, "#00FFFF")]
    [InlineData(240, 1, 1, "#0000FF")]
    [InlineData(300, 1, 1, "#FF00FF")]
    [InlineData(30, 1, 1, "#FF8000")]
    [InlineData(210, 0.5, 0.5, "#406080")]
    // No saturation: a gray, whatever the hue. No brightness: black.
    [InlineData(123, 0, 0.5, "#808080")]
    [InlineData(123, 0.7, 0, "#000000")]
    [InlineData(123, 0, 1, "#FFFFFF")]
    public void Three_numbers_are_a_colour(double h, double s, double v, string colour)
    {
        Assert.Equal(Rgb(colour), new Hsv(h, s, v).Rgb());
    }

    // A lattice through the whole cube, every gray, and every colour on
    // the cube's edges (where a channel is at an end and rounding is
    // likeliest to slip): each comes back as itself. (All 16,777,216 were
    // walked once, when this was written; none failed.)
    [Fact]
    public void A_colour_comes_back_as_itself()
    {
        static void Same(int r, int g, int b)
        {
            var colour = ((byte)r, (byte)g, (byte)b);
            if (Hsv.Of(colour).Rgb() != colour) Assert.Fail($"{r},{g},{b} came back as {Hsv.Of(colour).Rgb()}");
        }
        for (int r = 0; r < 256; r += 3)
            for (int g = 0; g < 256; g += 3)
                for (int b = 0; b < 256; b += 3)
                    Same(r, g, b);
        for (int i = 0; i < 256; i++)
        {
            Same(i, i, i);
            foreach (int end in new[] { 0, 255 })
            {
                Same(i, end, 255 - end);
                Same(end, i, 255 - end);
                Same(end, 255 - end, i);
                Same(i, end, end);
                Same(end, i, end);
                Same(end, end, i);
            }
        }
    }

    [Theory]
    [InlineData(0, "#FF0000")]
    [InlineData(60, "#FFFF00")]
    [InlineData(240, "#0000FF")]
    // The strip comes round to red again at its far end.
    [InlineData(360, "#FF0000")]
    [InlineData(-60, "#FF00FF")]
    [InlineData(420, "#FFFF00")]
    public void A_hue_alone_is_that_hue_at_full_strength(double hue, string colour)
    {
        Assert.Equal(Rgb(colour), Hsv.Pure(hue));
    }

    // ------------------------------------------------------ within bounds

    [Fact]
    public void What_a_picker_holds_is_kept_in_range()
    {
        Assert.Equal(new Hsv(10, 1, 0), Hsv.At(370, 2, -1));
        Assert.Equal(new Hsv(300, 0.25, 0.75), Hsv.At(-60, 0.25, 0.75));
        Assert.Equal(new Hsv(0, 0, 0), Hsv.At(double.NaN, double.NaN, double.NaN));
        Assert.Equal(new Hsv(0, 0.5, 0.5), Hsv.At(double.PositiveInfinity, 0.5, 0.5));
        // Out of range is a colour all the same.
        Assert.Equal(Rgb("#FFFF00"), new Hsv(420, 2, 5).Rgb());
    }

    // ---------------------------------------- what a colour does not have

    [Fact]
    public void Given_the_colour_it_already_shows_a_picker_does_not_move()
    {
        // Many triples round to one colour; the one held is not swapped for another.
        var held = new Hsv(200.4, 0.503, 0.497);
        Assert.Equal(held, Hsv.Keep(held.Rgb(), held));
    }

    [Fact]
    public void Given_another_colour_it_shows_that_colour()
    {
        var got = Hsv.Keep(Rgb("#E8792B"), new Hsv(200, 0.7, 0.4));
        Assert.Equal(Hsv.Of(Rgb("#E8792B")), got);
    }

    [Fact]
    public void Black_keeps_the_hue_and_the_saturation_that_were_there()
    {
        Assert.Equal(new Hsv(200, 0.7, 0), Hsv.Keep(Rgb("#000000"), new Hsv(200, 0.7, 0.4)));
    }

    [Fact]
    public void A_gray_keeps_the_hue_that_was_there()
    {
        var got = Hsv.Keep(Rgb("#808080"), new Hsv(200, 0.7, 0.4));
        Assert.Equal(200, got.H);
        Assert.Equal(0, got.S);
        Assert.Equal(128 / 255.0, got.V, 6);
    }

    // The square's marker dragged down to black, then up again: the hue it
    // comes back to is the one it left, not red.
    [Fact]
    public void A_marker_dragged_to_black_and_back_comes_back_to_its_hue()
    {
        var start = Hsv.Of(Rgb("#E8792B"));
        var atBlack = Hsv.Keep((start with { V = 0 }).Rgb(), start);
        Assert.Equal(Rgb("#000000"), atBlack.Rgb());
        var back = Hsv.Keep((atBlack with { V = start.V }).Rgb(), atBlack);
        Assert.Equal(Rgb("#E8792B"), back.Rgb());
        Assert.Equal(start.H, back.H, 6);
    }

    // And across to the gray edge and back.
    [Fact]
    public void A_marker_dragged_to_gray_and_back_comes_back_to_its_hue()
    {
        var start = Hsv.Of(Rgb("#3987E5"));
        var atGray = Hsv.Keep((start with { S = 0 }).Rgb(), start);
        Assert.Equal(0, atGray.S);
        Assert.Equal(start.H, atGray.H, 6);
        Assert.Equal(Rgb("#3987E5"), (atGray with { S = start.S }).Rgb());
    }
}
