using System.Globalization;

namespace Zerg.Core;

/// <summary>
/// The colours of Compare's two runs, A and B, as rules: nothing here draws.
///
/// <para>Each run's colour is the user's to set. Until it is set it is the
/// installed one of the theme in use (the theme files' <c>RunA</c> and
/// <c>RunB</c>); once set, the one colour serves both themes. A colour
/// belongs to its slot: Swap exchanges the parses, not the colours.</para>
///
/// <para>Three things follow from a run's colour and are worked out, never
/// listed, so that any colour a person picks comes out legible:</para>
/// <list type="bullet">
/// <item>the strength of the band behind a row (<see cref="Band"/>: as
/// strong as leaves the red of a worse change and the green of a better
/// one at 4.5 to one on it, and never over 26%;
/// <see cref="RowShade.RunBand"/> has the arithmetic);</item>
/// <item>the ink of the letter on the run's badge (<see cref="InkOn"/>:
/// near-black or a pale ink, whichever is clearer);</item>
/// <item>how the colour is written down (<see cref="Hex"/>) and read back
/// (<see cref="TryParse"/>), in <c>settings.json</c> and on the Settings
/// page.</item>
/// </list>
/// </summary>
public static class RunColours
{
    /// <summary>The dark ink of a badge's letter, on a colour light enough to carry it.</summary>
    public static readonly (byte R, byte G, byte B) DarkInk = (0x10, 0x10, 0x10);

    /// <summary>
    /// A colour as it is written: <c>#3987E5</c>, six hexadecimal digits in
    /// either case, with or without the sign before them; or three
    /// (<c>#38E</c>), each standing for a pair. Room either side is let
    /// pass. Anything else is not a colour, and whoever asked falls back to
    /// the installed one.
    /// </summary>
    public static bool TryParse(string? text, out (byte R, byte G, byte B) colour)
    {
        colour = default;
        if (text is null) return false;
        var s = text.AsSpan().Trim();
        if (s.Length > 0 && s[0] == '#') s = s[1..];
        if (s.Length != 6 && s.Length != 3) return false;
        foreach (char c in s)
            if (!Uri.IsHexDigit(c)) return false;
        if (s.Length == 3)
        {
            Span<char> wide = stackalloc char[6];
            for (int i = 0; i < 3; i++) wide[2 * i] = wide[2 * i + 1] = s[i];
            return TryParse(new string(wide), out colour);
        }
        colour = (byte.Parse(s[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                  byte.Parse(s[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                  byte.Parse(s[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        return true;
    }

    /// <summary>The colour as it is written down: <c>#3987E5</c>.</summary>
    public static string Hex((byte R, byte G, byte B) colour) =>
        string.Create(CultureInfo.InvariantCulture, $"#{colour.R:X2}{colour.G:X2}{colour.B:X2}");

    /// <summary>What a setting holds for <paramref name="text"/>: the colour
    /// written the one way, or null ("as installed") when it is not one.</summary>
    public static string? Normal(string? text) => TryParse(text, out var colour) ? Hex(colour) : null;

    /// <summary>
    /// The ink of the letter on a run's badge: near-black, or the pale ink
    /// (<paramref name="pale"/>: the dark theme's text colour; white under
    /// the light theme, whose own text colour is near-black too),
    /// whichever stands out more from the run's colour. On the installed
    /// pair of the dark theme it is near-black on both; on the light
    /// theme's deeper pair, white on both.
    /// </summary>
    public static (byte R, byte G, byte B) InkOn((byte R, byte G, byte B) colour, (byte R, byte G, byte B) pale) =>
        RowShade.Contrast(colour, DarkInk) >= RowShade.Contrast(colour, pale) ? DarkInk : pale;

    /// <summary>How strong the run's band is behind a row, in whole percent,
    /// on the pane's surface, with the theme's red and the theme's green.</summary>
    public static int Band((byte R, byte G, byte B) colour, (byte R, byte G, byte B) surface,
                           (byte R, byte G, byte B) crit, (byte R, byte G, byte B) live) =>
        RowShade.RunBand(colour, surface, crit, live);
}
