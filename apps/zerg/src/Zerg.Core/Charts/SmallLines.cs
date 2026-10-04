namespace Zerg.Core.Charts;

/// <summary>
/// "Group under 5%": on the cumulative chart, every character under a small
/// share of the party's damage can share one line ("3 others"), summed on the
/// shared grid so the crosshair and hover card read it like any other line.
/// </summary>
public static class SmallLines
{
    /// <summary>A character under this part of the party's total is small.</summary>
    public const double Share = 0.05;

    /// <summary>
    /// The characters to fold into one line, in the aggregate's order. Never
    /// the file's owner: theirs is the one line that must not vanish into a
    /// sum. Never a group of one: a single character renamed "1 others" says
    /// less than their own line did. Nobody at zero, who has no line to fold.
    /// </summary>
    public static List<string> Pick(Aggregate agg, string? owner, double share = Share)
    {
        var small = new List<string>();
        foreach (var a in agg.Actors)
            if (a.Total > 0 && a.Share < share && a.Name != owner) small.Add(a.Name);
        if (small.Count < 2) small.Clear();
        return small;
    }

    /// <summary>What the folded line is called.</summary>
    public static string Name(int count) => Js.NumberToString(count) + " others";
}
