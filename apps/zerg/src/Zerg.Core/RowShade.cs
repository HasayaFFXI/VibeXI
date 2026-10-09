namespace Zerg.Core;

/// <summary>
/// How strong a row's shade may be. A share is drawn as the row's own
/// background, in the character's colour, from the row's left edge to the
/// share's length; figures are then read on top of it, so the colour is laid
/// on at a strength that keeps them legible, worked out per colour and never
/// listed.
///
/// <para><b>The rule:</b> the largest whole percentage, no greater than a
/// cap, at which the quieter ink (<c>Text2</c>) drawn on the colour at that
/// strength over the surface still has a WCAG contrast ratio of at least the
/// target. Dark: 4.5 and 30. Light: 5.5 and 22. A strip floating over the
/// game: 4.5 and 34, with the panel's own ink over its backdrop.</para>
///
/// <para>A run's band in Compare has the same rule with two other inks:
/// the red a worse change is written in and the green of a better one,
/// the two coloured inks that sit on a band. The band is as strong as
/// both allow (<see cref="RunBand"/>).</para>
/// </summary>
public static class RowShade
{
    /// <summary>The contrast the quieter ink keeps on a shaded row, and the
    /// strongest a shade is ever drawn, in the dark theme.</summary>
    public const double DarkTarget = 4.5;
    public const int DarkCap = 30;

    /// <summary>The same in the light theme: more contrast is asked of dark
    /// ink on a pale shade, and the shade is fainter.</summary>
    public const double LightTarget = 5.5;
    public const int LightCap = 22;

    /// <summary>A strip over the game: the dark theme's target, a higher cap.</summary>
    public const double StripTarget = 4.5;
    public const int StripCap = 34;

    /// <summary>What a shade in a floating panel is worked out against: the
    /// panel's backdrop as the design draws it (the tint itself is black at
    /// whatever strength its slider says, with the game behind it, so no
    /// one colour is "the" surface; this is the one the design's figures
    /// were made on), and the quieter ink a panel has
    /// (<c>Themes/Panel.xaml</c>'s <c>Text2Brush</c>). A panel is dark
    /// whatever the theme, so there is one pair.</summary>
    public static readonly (byte R, byte G, byte B) PanelBackdrop = (0x06, 0x08, 0x0C), PanelText2 = (0xB9, 0xC1, 0xCF);

    /// <summary>A run's band in Compare.</summary>
    public const double BandTarget = 4.5;
    public const int BandCap = 26;

    /// <summary>
    /// The strength, in whole percent, <paramref name="colour"/> is laid over
    /// <paramref name="surface"/> at: the largest, up to <paramref name="cap"/>,
    /// that leaves <paramref name="text"/> at <paramref name="target"/> to one
    /// or better on it. 0 when even 1% does not.
    /// </summary>
    public static int Strength((byte R, byte G, byte B) colour, (byte R, byte G, byte B) surface,
                               (byte R, byte G, byte B) text, double target, int cap)
    {
        double ink = Luminance(text.R, text.G, text.B);
        for (int percent = Math.Clamp(cap, 0, 100); percent > 0; percent--)
        {
            double a = percent / 100.0;
            double under = Luminance(Over(colour.R, surface.R, a), Over(colour.G, surface.G, a), Over(colour.B, surface.B, a));
            if (Contrast(ink, under) >= target) return percent;
        }
        return 0;
    }

    /// <summary>
    /// How strong a character's shade is in a floating panel, in whole
    /// percent: behind their line of a strip, and behind their heading in
    /// the Actions and Heals panels. As strong as leaves the panel's quieter
    /// ink at 4.5 to one on it over the panel's backdrop, and never over
    /// 34%: most job colours take the cap, and the pale ones are held back
    /// (white 28, a paladin's yellow 31, a summoner's green 32), where a
    /// flat strength left pale figures on a bright bar.
    /// </summary>
    public static int Strip((byte R, byte G, byte B) colour) => Strength(colour, PanelBackdrop, PanelText2, StripTarget, StripCap);

    /// <summary>The raised surface of a floating panel, which a character's
    /// heading in the Actions and Heals panels lies on: white at 7% over the
    /// backdrop (<c>Themes/Panel.xaml</c>'s <c>Bg2Brush</c>, <c>#12FFFFFF</c>).</summary>
    public static readonly (byte R, byte G, byte B) PanelRaised = Raised(PanelBackdrop, 0x12 / 255.0);

    static (byte, byte, byte) Raised((byte R, byte G, byte B) under, double white) =>
        ((byte)Math.Round(Over(255, under.R, white)), (byte)Math.Round(Over(255, under.G, white)), (byte)Math.Round(Over(255, under.B, white)));

    /// <summary>
    /// How strong a character's shade is behind their heading in the
    /// Actions and Heals panels. A heading is a table's row, not a strip's:
    /// the tables' own cap (30%), over the panel's raised surface, with the
    /// panel's quieter ink. A point or two fainter than the same colour in
    /// a strip, as a heading in the main window is fainter than a row.
    /// </summary>
    public static int PanelHeading((byte R, byte G, byte B) colour) => Strength(colour, PanelRaised, PanelText2, DarkTarget, DarkCap);

    /// <summary>
    /// How strong a run's band is in Compare: as strong as leaves both the
    /// red of a worse change (<paramref name="crit"/>) and the green of a
    /// better one (<paramref name="live"/>) at 4.5 to one on it, and never
    /// over 26%. The lower of the two strengths the inks allow. On the dark
    /// theme the red decides (its green is far the lighter ink, 8 to one on
    /// any band); on the light theme the green does, and holds the band a
    /// few points under what the red alone would let it be.
    /// </summary>
    public static int RunBand((byte R, byte G, byte B) colour, (byte R, byte G, byte B) surface,
                              (byte R, byte G, byte B) crit, (byte R, byte G, byte B) live) =>
        Math.Min(Strength(colour, surface, crit, BandTarget, BandCap), Strength(colour, surface, live, BandTarget, BandCap));

    /// <summary>The WCAG contrast ratio of two colours, 1 to 21.</summary>
    public static double Contrast((byte R, byte G, byte B) one, (byte R, byte G, byte B) other) =>
        Contrast(Luminance(one.R, one.G, one.B), Luminance(other.R, other.G, other.B));

    static double Contrast(double a, double b) => (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);

    /// <summary>One channel of a colour laid over another at a strength, 0 to 255, not rounded.</summary>
    static double Over(byte top, byte under, double strength) => top * strength + under * (1 - strength);

    /// <summary>WCAG relative luminance, from channels 0 to 255.</summary>
    static double Luminance(double r, double g, double b) => 0.2126 * Linear(r) + 0.7152 * Linear(g) + 0.0722 * Linear(b);

    static double Linear(double channel)
    {
        double c = channel / 255;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
