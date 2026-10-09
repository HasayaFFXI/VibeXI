namespace Zerg.Core;

/// <summary>What one target took: a line of the list a target is picked
/// from. Or what one damage type came to, under the type's key
/// (<see cref="DamageTypes.Of"/>): a line of the list a type is picked from.</summary>
public sealed record TargetTotal(string Name, double Total);

/// <summary>
/// Isolating targets: counting only the damage dealt to the ones picked.
///
/// <para><b>A target is its name.</b> The event file says what a row hit by
/// name and nothing else, so three Goblin Pathfinders are one target here,
/// as they are one line of Compare's By target table.</para>
///
/// <para>The filter itself is one line of <see cref="Counting.Counted"/>.
/// This is what stands round it: the list to pick from, and how what was
/// picked is said.</para>
/// </summary>
public static class Targets
{
    /// <summary>The longest a target's name is written in a figure's label
    /// before it is cut: the label shares its line with a mark's caption.</summary>
    public const int LabelLength = 16;

    /// <summary>
    /// Damage by target name over every landed row, in the order the targets
    /// were first hit. Unfolded is right: this only sums, and an area
    /// attack's damage belongs to each target it reached. A target nothing
    /// landed on is not listed. Rows that name no target are summed under
    /// "": a table can say what they came to, but there is nothing to pick
    /// (<see cref="Pickable"/>).
    /// </summary>
    public static OrderedDictionary<string, double> Totals(IReadOnlyList<CombatEvent> events)
    {
        var map = new OrderedDictionary<string, double>(StringComparer.Ordinal);
        foreach (var e in events)
        {
            if (!e.Hit || e.Dmg == 0 || double.IsNaN(e.Dmg)) continue;
            map[e.Target] = map.GetValueOrDefault(e.Target) + e.Dmg;
        }
        return map;
    }

    /// <summary>The names that can be picked,
    /// in the order they are picked from: all but the one that is no name.</summary>
    public static List<string> Pickable(IEnumerable<string> names) => Sorted(names.Where(n => n.Length > 0));

    /// <summary>
    /// Names in the order they are picked from: alphabetical, whatever their
    /// case. Not by damage: a list in that order moves under the pointer
    /// while a session counts, and a name is found by its letter.
    /// </summary>
    public static List<string> Sorted(IEnumerable<string> names) => SortedBy(names, n => n);

    /// <summary>The same order for keys that are called something else
    /// than they are written: by what <paramref name="called"/> says of each.</summary>
    public static List<string> SortedBy(IEnumerable<string> keys, Func<string, string> called) =>
        keys.Distinct(StringComparer.Ordinal)
            .OrderBy(called, StringComparer.OrdinalIgnoreCase).ThenBy(called, StringComparer.Ordinal).ToList();

    /// <summary>What a filter is handed: the names picked, or null for
    /// "every target" when none is.</summary>
    public static IReadOnlySet<string>? Only(IEnumerable<string>? picked)
    {
        if (picked == null) return null;
        var set = new HashSet<string>(picked, StringComparer.Ordinal);
        return set.Count > 0 ? set : null;
    }

    /// <summary>What is picked, in a word or two, for the button that picks:
    /// "All", "Kirin", "Kirin +2" (the first by name, and how many more).</summary>
    public static string Label(IReadOnlyCollection<string> picked)
    {
        if (picked.Count == 0) return "All";
        var first = Sorted(picked)[0];
        return picked.Count == 1 ? first : first + " +" + Format.Int(picked.Count - 1);
    }

    /// <summary>The label over a total: <paramref name="all"/> with nothing
    /// picked ("Total damage"), "Damage to Kirin" with one, "Damage to 3
    /// targets" with more. A long name is cut (<see cref="Short"/>).</summary>
    public static string Heading(IReadOnlyCollection<string> picked, string all = "Total damage") =>
        picked.Count == 0 ? all
        : picked.Count == 1 ? "Damage to " + Short(picked.First())
        : "Damage to " + Format.Int(picked.Count) + " targets";

    /// <summary>Everything picked, by name, for a tooltip: "Genbu, Kirin".</summary>
    public static string Names(IReadOnlyCollection<string> picked) => string.Join(", ", Sorted(picked));

    /// <summary>A name no longer than <paramref name="most"/> characters, its
    /// end given up for an ellipsis when it is.</summary>
    public static string Short(string name, int most = LabelLength) =>
        name.Length <= most ? name : name[..(most - 1)].TrimEnd() + "…";
}
