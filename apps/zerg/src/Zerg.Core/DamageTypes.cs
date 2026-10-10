namespace Zerg.Core;

/// <summary>
/// Isolating damage types: counting only the damage of the types picked.
/// A type is a line of Compare's By damage type table: melee, weaponskills,
/// magic, and so on.
///
/// <para><b>A row's type is its kind, but a pet's rows are one type.</b>
/// Whatever a pet did, it is "Pet": the table has always summed it so, and
/// a filter has to cut where the table does.</para>
///
/// <para>The filter itself is one line of <see cref="Counting.Counted"/>,
/// beside the one for targets (<see cref="Targets"/>). Unlike a target, a
/// type takes no rate away: a type is dealt all through a fight, so its
/// damage over the fight's clock is a DPS somebody dealt.</para>
/// </summary>
public static class DamageTypes
{
    /// <summary>The type a row is counted under.</summary>
    public static string Of(CombatEvent e) => e.IsPets ? "pet" : e.Kind;

    /// <summary>What a type is called: its line of the table ("Weaponskills"),
    /// or the kind as written for one the table does not know yet.</summary>
    public static string Label(string type)
    {
        foreach (var k in Compare.KindLabels)
            if (k.Key == type) return k.Label;
        return type;
    }

    /// <summary>
    /// Damage by type over every landed row, in the order the types were
    /// first dealt. Unfolded is right: this only sums.
    /// </summary>
    public static OrderedDictionary<string, double> Totals(IReadOnlyList<CombatEvent> events)
    {
        var map = new OrderedDictionary<string, double>(StringComparer.Ordinal);
        foreach (var e in events)
        {
            if (!e.Hit || e.Dmg == 0 || double.IsNaN(e.Dmg)) continue;
            var type = Of(e);
            map[type] = map.GetValueOrDefault(type) + e.Dmg;
        }
        return map;
    }

    /// <summary>Types in the order they are picked from: alphabetical by
    /// what they are called, as targets are by name.</summary>
    public static List<string> Sorted(IEnumerable<string> types) => Targets.SortedBy(types, Label);

    /// <summary>
    /// The same lines in the order a table of one parse lists them: the
    /// order of Compare's By damage type table (melee, weaponskills,
    /// skillchains, magic, ...: <see cref="Compare.KindLabels"/>), then any
    /// type that table does not know yet, by what it is called. Not by
    /// size, as targets are (<see cref="Targets.Ranked"/>): the types are
    /// the same few in every fight, and are found where they always are.
    /// A type that is picked and came to nothing stays in its place, with
    /// no total (not a number), where it can be let go. A line with no key
    /// cannot be picked and is not listed (<see cref="Targets.Unnamed"/>
    /// says what such rows came to).
    /// </summary>
    public static List<TargetTotal> Listed(IEnumerable<TargetTotal> list, IEnumerable<string>? picked = null)
    {
        var by = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var t in list)
            if (t.Name.Length > 0 && !double.IsNaN(t.Total)) by.TryAdd(t.Name, t.Total);
        var keys = new HashSet<string>(by.Keys, StringComparer.Ordinal);
        foreach (var k in picked ?? [])
            if (k.Length > 0) keys.Add(k);

        var lines = new List<TargetTotal>(keys.Count);
        foreach (var k in Compare.KindLabels)
            if (keys.Remove(k.Key)) lines.Add(Line(k.Key));
        foreach (var k in Sorted(keys)) lines.Add(Line(k));
        return lines;

        TargetTotal Line(string key) => new(key, by.TryGetValue(key, out double total) ? total : double.NaN);
    }

    /// <summary>Everything picked, as it is called, for a tooltip: "Magic, Melee".</summary>
    public static string Names(IEnumerable<string> picked) => string.Join(", ", Sorted(picked).Select(Label));

    /// <summary>
    /// The label over a total, whatever is isolated: "Total damage" with
    /// nothing; of targets alone, what <see cref="Targets.Heading"/> says
    /// ("Damage to Kirin"); of a type, "Melee damage", and of several,
    /// "Damage of 3 types"; of both, the two together ("Melee damage to Kirin").
    /// </summary>
    public static string Heading(IReadOnlyCollection<string> types, IReadOnlyCollection<string> targets)
    {
        if (types.Count == 0) return Targets.Heading(targets);
        var what = types.Count == 1 ? Label(types.First()) + " damage" : "Damage of " + Format.Int(types.Count) + " types";
        return what + (targets.Count == 0 ? ""
            : targets.Count == 1 ? " to " + Targets.Short(targets.First())
            : " to " + Format.Int(targets.Count) + " targets");
    }

    /// <summary>The same in a sentence, with every name, for the pointer;
    /// null with nothing isolated.</summary>
    public static string? Told(IReadOnlyCollection<string> types, IReadOnlyCollection<string> targets) =>
        types.Count == 0 && targets.Count == 0 ? null
        : "Only " + (types.Count > 0 ? Names(types) + " damage" : "the damage") +
          (targets.Count > 0 ? " dealt to " + Targets.Names(targets) : "");
}
