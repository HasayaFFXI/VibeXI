namespace Zerg.Core.Layout;

/// <summary>
/// The measures of a floating panel, as arithmetic: how tall its parts are,
/// how tall a strip's panel has to open to show all of its rows, and how
/// wide a panel has to be for its bar to keep its title beside the tools.
/// No drawing; <c>PanelWindow</c> is the window that uses it.
///
/// <para><b>A panel's height is in units and its parts are in pixels.</b>
/// On a scaled screen every part is laid out on whole pixels: at 150% a
/// 20-unit row with its 1-unit gap is 32 pixels, not 31.5, and a one-unit
/// edge is two. A panel sized by adding up units comes out a pixel or two
/// short of its rows there, and a strip that is a pixel short opens with a
/// scroll bar. So the height is worked out in pixels, part by part, each
/// rounded up, for the scale of the screen the panel opens on, and turned
/// back into units rounded up.</para>
/// </summary>
public static class PanelFit
{
    /// <summary>The bar under the frame's top edge, with the rule along its
    /// bottom inside it: 30 units from the panel's top to under the rule,
    /// as the design has it.</summary>
    public const double Bar = 29;

    /// <summary>The frame's edge, on each side.</summary>
    public const double Edge = 1;

    /// <summary>A strip: the line of column headings, a row, the gap under
    /// each row, and what is left clear under the last gap.</summary>
    public const double StripHead = 20, StripRow = 20, StripGap = 1, StripFoot = 5;

    /// <summary>A strip's panel opens with room for at least this many rows,
    /// so one opened before the fight is not a bar with nothing under it.</summary>
    public const int LeastRows = 3;

    /// <summary>The narrowest a panel may be made: the pair, the clock and
    /// the total still stand on its bar.</summary>
    public const double LeastWidth = 250;

    /// <summary>Under this width the bar has no room for its title beside
    /// the tools: while the pointer is over the panel the tools stand in
    /// the title's place.</summary>
    public const double TitleLeaves = 360;

    /// <summary>Whether the title stays on the bar, left of the tools,
    /// while the pointer is over a panel this wide.</summary>
    public static bool TitleStays(double width) => width >= TitleLeaves;

    /// <summary>How many pixels a length in units takes on a screen at
    /// <paramref name="scale"/> (1 at 100%, 1.5 at 150%): whole pixels,
    /// rounded up, and never none.</summary>
    public static int Pixels(double units, double scale) =>
        units <= 0 ? 0 : Math.Max(1, (int)Math.Ceiling(units * Clean(scale) - 1e-9));

    /// <summary>
    /// How many pixels tall a strip's panel is with <paramref name="rows"/>
    /// rows showing: the frame's two edges, the bar, the headings, each row
    /// with its gap, and the foot, every part on whole pixels.
    /// </summary>
    public static int StripPixels(int rows, double scale) =>
        2 * Pixels(Edge, scale) + Pixels(Bar, scale) + Pixels(StripHead, scale) +
        Math.Max(rows, 0) * Pixels(StripRow + StripGap, scale) + Pixels(StripFoot, scale);

    /// <summary>
    /// The height, in whole units, a strip's panel opens at for
    /// <paramref name="rows"/> characters (three at least) on a screen at
    /// <paramref name="scale"/>: the least that holds every part's pixels.
    /// </summary>
    public static double StripHeight(int rows, double scale)
    {
        double s = Clean(scale);
        return Math.Ceiling(StripPixels(Math.Max(rows, LeastRows), s) / s - 1e-9);
    }

    /// <summary>A scale that is not one (zero, not a number) is 100%.</summary>
    static double Clean(double scale) => double.IsFinite(scale) && scale > 0 ? scale : 1;
}
