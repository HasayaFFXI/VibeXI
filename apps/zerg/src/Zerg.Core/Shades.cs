namespace Zerg.Core;

/// <summary>
/// Shades of one colour, for when a colour has to be handed out twice: a party
/// can hold two warriors, and two identical lines are not a chart.
/// </summary>
public static class Shades
{
    /// <summary>How far one step moves a colour toward white or black.</summary>
    const double StepSize = 0.30;
    const double Furthest = 0.75;

    /// <summary>
    /// The <paramref name="k"/>-th shade of a colour: 0 is the colour itself,
    /// and each one after it is a fixed lightness move, alternating away from
    /// and toward what it is drawn on (1 away, 2 toward, 3 further away, 4
    /// further toward).
    ///
    /// <para>The first step goes away from the page, lighter on a dark theme
    /// and darker on a light one, so a shade is never the harder one to see.
    /// The step is large on purpose: the two marks sit side by side on a line
    /// chart, and a difference that has to be looked for has failed.</para>
    /// </summary>
    public static (byte R, byte G, byte B) Step((byte R, byte G, byte B) colour, int k, bool lightTheme)
    {
        if (k <= 0) return colour;
        int level = (k + 1) / 2;
        bool away = k % 2 == 1;
        bool darker = lightTheme ? away : !away;
        double mix = Math.Min(Furthest, level * StepSize);
        double end = darker ? 0 : 255;

        byte Move(byte c) => (byte)Js.Round(c + (end - c) * mix);
        return (Move(colour.R), Move(colour.G), Move(colour.B));
    }
}
