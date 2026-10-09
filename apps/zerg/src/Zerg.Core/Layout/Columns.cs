using System.Globalization;

namespace Zerg.Core.Layout;

/// <summary>One column of a table.</summary>
/// <param name="Size">A width in units, or a share's weight.</param>
/// <param name="Share">The column takes a share of what the fixed columns leave.</param>
/// <param name="Least">A share is never narrower than this.</param>
public readonly record struct Column(double Size, bool Share, double Least = 0);

/// <summary>
/// The columns of a table: how wide each is in a row of a given width, and
/// how narrow the row may get before a column has to be dropped. No
/// drawing; <c>Views/Cells</c> is the panel that uses it.
///
/// <para>A table's columns are written once, as text: <c>"138*,70*,68"</c>.
/// A number is that many units. A number and a star is a share: what the
/// fixed columns leave is divided between the shares by weight, so a table
/// drawn 930 wide on the design's sheet can be written with the sheet's own
/// widths as weights and comes out as drawn at that width, and in
/// proportion at any other. A second number after the star is the least the
/// share may be (<c>"278*150"</c>): a share that would be narrower is held
/// there, and the others divide what is left.</para>
///
/// <para>A column that is not there (its cell is collapsed) takes no room,
/// and the rest are worked out without it: that is how a table drops a
/// column.</para>
/// </summary>
public static class Columns
{
    /// <summary>What a share's weight is worth when the row is given no width to share out.</summary>
    public const double LooseShare = 84;

    /// <summary>Reads a table's columns. A star alone is a weight of 1.</summary>
    public static Column[] Parse(string? text)
    {
        var parts = (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var columns = new Column[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            var p = parts[i];
            int star = p.IndexOf('*');
            if (star < 0)
            {
                columns[i] = new Column(Number(p), false);
                continue;
            }
            double weight = star == 0 ? 1 : Number(p[..star]);
            double least = star == p.Length - 1 ? 0 : Number(p[(star + 1)..]);
            columns[i] = new Column(weight, true, least);
        }
        return columns;
    }

    static double Number(string text) => double.Parse(text, CultureInfo.InvariantCulture);

    /// <summary>
    /// The width of each column in a row <paramref name="width"/> wide, into
    /// <paramref name="into"/>, which is as long as the columns. A column
    /// <paramref name="present"/> says is not there is 0. With no width to
    /// share out (infinity), a share is its least; in a table written with
    /// no leasts at all, its weight times <see cref="LooseShare"/>.
    ///
    /// <para>In a row too narrow for the fixed columns and every share's
    /// least, each keeps what it must have and the row is wider than it was
    /// given: whoever holds it scrolls, or has dropped a column first
    /// (<see cref="Least"/>).</para>
    /// </summary>
    public static void Resolve(ReadOnlySpan<Column> columns, ReadOnlySpan<bool> present, double width, Span<double> into)
    {
        double left = width, weights = 0;
        // Which shares are still being divided between; held[i] once a
        // share has been stood at its least.
        Span<bool> held = columns.Length <= 64 ? stackalloc bool[columns.Length] : new bool[columns.Length];
        for (int i = 0; i < columns.Length; i++)
        {
            into[i] = 0;
            if (!Present(present, i)) { held[i] = true; continue; }
            if (columns[i].Share) weights += columns[i].Size;
            else { left -= into[i] = columns[i].Size; held[i] = true; }
        }

        if (double.IsInfinity(width) || double.IsNaN(width))
        {
            // A table written with leasts is as narrow as they allow; one
            // written without any has nothing to go by but its weights.
            bool floored = false;
            for (int i = 0; i < columns.Length; i++) floored |= !held[i] && columns[i].Least > 0;
            for (int i = 0; i < columns.Length; i++)
                if (!held[i]) into[i] = floored ? columns[i].Least : columns[i].Size * LooseShare;
            return;
        }

        // A share that would come out under its least is stood at its
        // least, and the rest divide what is left; again, until none does.
        // Each round stands at least one, so there are no more rounds than
        // shares.
        for (bool again = true; again && weights > 0;)
        {
            again = false;
            double one = Math.Max(0, left) / weights;
            for (int i = 0; i < columns.Length; i++)
            {
                if (held[i] || one * columns[i].Size >= columns[i].Least) continue;
                into[i] = columns[i].Least;
                left -= columns[i].Least;
                weights -= columns[i].Size;
                held[i] = true;
                again = true;
            }
        }
        if (weights <= 0) return;
        double each = Math.Max(0, left) / weights;
        for (int i = 0; i < columns.Length; i++)
            if (!held[i]) into[i] = each * columns[i].Size;
    }

    /// <summary>
    /// The narrowest a row of these columns can be: the fixed widths and
    /// every share's least. In less, the columns do not fit: a table that
    /// can drop columns does so under this width.
    /// </summary>
    public static double Least(ReadOnlySpan<Column> columns, ReadOnlySpan<bool> present = default)
    {
        double sum = 0;
        for (int i = 0; i < columns.Length; i++)
            if (Present(present, i)) sum += columns[i].Share ? columns[i].Least : columns[i].Size;
        return sum;
    }

    /// <summary>The narrowest a row written as <paramref name="text"/> can be.</summary>
    public static double Least(string? text) => Least(Parse(text));

    /// <summary>
    /// A table in a room <paramref name="room"/> wide has to drop columns:
    /// its full set, written as <paramref name="full"/>, needs more than the
    /// room has once <paramref name="margin"/> is kept clear beside it (the
    /// room a scroll bar is drawn in).
    /// </summary>
    public static bool Sheds(string? full, double room, double margin = 0) =>
        !double.IsNaN(room) && room > 0 && room - margin < Least(full);

    /// <summary>A column past the end of <paramref name="present"/> is there.</summary>
    static bool Present(ReadOnlySpan<bool> present, int i) => i >= present.Length || present[i];
}
