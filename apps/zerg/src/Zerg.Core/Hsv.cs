namespace Zerg.Core;

/// <summary>
/// A colour as a colour picker holds it: a hue, how saturated it is and how
/// bright. Nothing here draws.
///
/// <para>The Settings page's picker (for the colours of Compare's two runs)
/// is a square and a strip: across the square the saturation, up it the
/// brightness, along the strip the hue. What it hands on is a colour as
/// red, green and blue, written as <see cref="RunColours.Hex"/> writes it;
/// this is the arithmetic between the two.</para>
///
/// <para>The way there loses nothing a screen can show: every one of the
/// 16.7 million colours comes back from <see cref="Of"/> and
/// <see cref="Rgb"/> as itself. The way back loses the hue of a gray and
/// both the hue and the saturation of black, which have none; a picker
/// that asked the colour for them would see its strip jump to red as the
/// square's marker reached an edge. <see cref="Keep"/> is for that.</para>
/// </summary>
/// <param name="H">The hue in degrees, 0 up to but not 360: red, yellow at
/// 60, green at 120, cyan, blue, magenta at 300.</param>
/// <param name="S">Saturation, 0 (a gray) to 1.</param>
/// <param name="V">Brightness, 0 (black) to 1.</param>
public readonly record struct Hsv(double H, double S, double V)
{
    /// <summary>The three as a picker may hold them: the hue turned into 0
    /// to 360, the other two kept between 0 and 1. Not a number is 0.</summary>
    public static Hsv At(double h, double s, double v) => new(Turn(h), Unit(s), Unit(v));

    static double Unit(double x) => double.IsNaN(x) ? 0 : Math.Clamp(x, 0, 1);

    static double Turn(double h)
    {
        if (double.IsNaN(h) || double.IsInfinity(h)) return 0;
        h %= 360;
        return h < 0 ? h + 360 : h;
    }

    /// <summary>A colour's hue, saturation and brightness. A gray's hue is
    /// 0, and so is black's saturation.</summary>
    public static Hsv Of((byte R, byte G, byte B) colour)
    {
        double r = colour.R / 255.0, g = colour.G / 255.0, b = colour.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), span = max - min;
        double h = 0;
        if (span > 0)
        {
            if (max == r) h = (g - b) / span;
            else if (max == g) h = (b - r) / span + 2;
            else h = (r - g) / span + 4;
            h = Turn(h * 60);
        }
        return new Hsv(h, max <= 0 ? 0 : span / max, max);
    }

    /// <summary>The colour, to the nearest of each channel's 256 steps.</summary>
    public (byte R, byte G, byte B) Rgb()
    {
        var (h, s, v) = At(H, S, V);
        double sector = h / 60;
        int i = (int)Math.Floor(sector) % 6;
        double f = sector - Math.Floor(sector);
        double p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
        var (r, g, b) = i switch
        {
            0 => (v, t, p),
            1 => (q, v, p),
            2 => (p, v, t),
            3 => (p, q, v),
            4 => (t, p, v),
            _ => (v, p, q),
        };
        return (Byte(r), Byte(g), Byte(b));
    }

    static byte Byte(double x) => (byte)Math.Clamp((int)Math.Round(x * 255, MidpointRounding.AwayFromZero), 0, 255);

    /// <summary>The hue alone, at full strength: what the picker's square
    /// is filled with, and one point of its strip.</summary>
    public static (byte R, byte G, byte B) Pure(double hue) => new Hsv(hue, 1, 1).Rgb();

    /// <summary>
    /// What a picker holds once it is given <paramref name="colour"/> while
    /// it held <paramref name="was"/>.
    ///
    /// <para>If what it held already comes out as that colour, nothing
    /// moves: the colour is its own, come back to it. Otherwise the
    /// colour's own three, except what the colour does not have: a gray
    /// keeps the hue that was there, and black the saturation too. So a
    /// marker dragged down to black and back, or across to the gray edge
    /// and back, comes back to the hue it left.</para>
    /// </summary>
    public static Hsv Keep((byte R, byte G, byte B) colour, Hsv was)
    {
        was = At(was.H, was.S, was.V);
        if (was.Rgb() == colour) return was;
        var now = Of(colour);
        if (now.V <= 0) return new Hsv(was.H, was.S, 0);
        if (now.S <= 0) return new Hsv(was.H, 0, now.V);
        return now;
    }
}
