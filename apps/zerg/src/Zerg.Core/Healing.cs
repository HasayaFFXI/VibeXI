namespace Zerg.Core;

/// <summary>One healing action inside a character.</summary>
public sealed class HealAction
{
    public string Name { get; set; } = "";
    public string Via { get; set; } = "";
    /// <summary>A pet's heal (its first heal said so).</summary>
    public bool Pet { get; set; }
    public double Total { get; set; }
    public int Casts { get; set; }
    /// <summary>The smallest heal above zero, or null with none.</summary>
    public double? Min { get; set; }
    public double Max { get; set; }
    public double Avg { get; set; }

    internal readonly HashSet<double> Uses = [];
}

/// <summary>One character's healing. Pet heals are in <see cref="PetTotal"/> only.</summary>
public sealed class HealerTotals
{
    public string Name { get; set; } = "";
    public double Total { get; set; }
    public double PetTotal { get; set; }
    public int Casts { get; set; }
    public double Max { get; set; }
    public OrderedDictionary<string, HealAction> Actions { get; set; } = new(StringComparer.Ordinal);
    public List<string> ActionOrder { get; set; } = [];
    /// <summary>Total over casts, or null with no casts.</summary>
    public double? Avg { get; set; }
    /// <summary>Part of the party's healing, 0..1, or null when the party healed nothing.</summary>
    public double? Share { get; set; }
    public List<HealAction> ActionList { get; set; } = [];
}

/// <summary>The party's healing: every healer, largest first (pet heals included in the order).</summary>
public sealed class HealTotals
{
    public List<HealerTotals> Actors { get; set; } = [];
    public double Total { get; set; }
    public double PetTotal { get; set; }
    public int Casts { get; set; }
    public double Max { get; set; }
    public double? Avg { get; set; }
    /// <summary>Heal rows in.</summary>
    public int Lines { get; set; }
}

/// <summary>
/// Healing, from the addon's heal lines, which never enter the damage rows: no
/// damage figure can see them and nothing here can move one.
/// <list type="bullet">
/// <item>Healing: every heal not from a pet (spells and abilities).</item>
/// <item>Pet: every heal a pet did, credited to its owner with the pet's name
/// kept, as pet damage is.</item>
/// <item>Casts: one per action however many targets; a Curaga on five is one.</item>
/// <item>Average: total over casts.</item>
/// <item>Min / Max: one target's heal, raw. Min is the lowest above zero, so a
/// cure on a full-health target does not make every minimum read 0.</item>
/// </list>
/// <para><b>No overcure, on purpose.</b> The packet does not carry the target's
/// missing HP, so overcure could only be estimated (against the best a spell
/// had done so far), which forgives early casts and charges every cast after a
/// buffed one. It was tried and removed as not accurate enough. Don't bring it
/// back without a real source for missing HP; Max is the raw value for the
/// same reason.</para>
/// </summary>
public static class Healing
{
    /// <summary>
    /// The same scoping <see cref="Counting.Filter"/> applies to damage (session
    /// window, monsters, exclusions), so healing and damage are always measured
    /// over the same window. Copies, on the session clock, with a pet's heal
    /// credited to its owner. Skillchains do not apply.
    /// </summary>
    public static List<HealEvent> Filter(IReadOnlyList<HealEvent>? heals, FilterOptions? opts = null)
    {
        opts ??= new FilterOptions();
        var sn = opts.Session;
        var output = new List<HealEvent>();
        if (heals == null || (sn != null && sn.StartedAt == null)) return output;
        foreach (var h in heals)
        {
            double? el = null;
            if (sn != null)
            {
                el = sn.At(h.T);
                if (el == null) continue;
            }
            if (opts.Roster != null && opts.Roster.IsMob(h.Actor)) continue;
            var c = h;
            if (!string.IsNullOrEmpty(h.Owner) && h.Actor != h.Owner)
            {
                var by = !string.IsNullOrEmpty(h.Pet) ? h.Pet : h.Actor;
                c = c with { Actor = h.Owner, By = by, Action = by + ": " + h.Action };
            }
            if (opts.Actors != null && opts.Actors.TryGetValue(c.Actor, out var on) && !on) continue;
            if (el is double t) c = c with { Wall = h.T, T = t };
            output.Add(c);
        }
        return output;
    }

    /// <summary>
    /// The heals as rows the cumulative line and the distribution can read
    /// unchanged: the amount is the HP healed, and every one is a hit. A cure
    /// on a target at full health healed 0; it did not miss.
    /// </summary>
    public static List<CombatEvent> AsEvents(IEnumerable<HealEvent> heals) =>
        heals.Select(h => new CombatEvent
        {
            T = h.T, Use = h.Use, Kind = "heal", Actor = h.Actor, Action = h.Action,
            Target = h.Target, Dmg = h.Hp, Hit = true, Owner = h.Owner,
        }).ToList();

    /// <summary>Heals (already filtered) → per character, per action. Largest first.</summary>
    public static HealTotals Totals(IReadOnlyList<HealEvent> heals)
    {
        var by = new Dictionary<string, HealerTotals>(StringComparer.Ordinal);
        var order = new List<string>();
        double total = 0, petTotal = 0, max = 0;
        int casts = 0;

        foreach (var h in heals)
        {
            bool pet = h.IsPets;
            if (!by.TryGetValue(h.Actor, out var a))
            {
                by[h.Actor] = a = new HealerTotals { Name = h.Actor };
                order.Add(h.Actor);
            }
            if (!a.Actions.TryGetValue(h.Action, out var act))
            {
                a.Actions[h.Action] = act = new HealAction { Name = h.Action, Via = h.Via, Pet = pet };
                a.ActionOrder.Add(h.Action);
            }

            var v = h.Hp == 0 || double.IsNaN(h.Hp) ? 0 : h.Hp;
            bool newCast = h.Use is not double use || act.Uses.Add(use);

            if (v > act.Max) act.Max = v;
            if (v > 0 && (act.Min == null || v < act.Min)) act.Min = v;
            act.Total += v;
            if (newCast) act.Casts++;

            if (pet)
            {
                a.PetTotal += v;
                petTotal += v;
            }
            else
            {
                a.Total += v;
                if (v > a.Max) a.Max = v;
                if (newCast) a.Casts++;
                total += v;
                if (v > max) max = v;
                if (newCast) casts++;
            }
        }

        var actors = order.Select(n =>
        {
            var x = by[n];
            x.Avg = x.Casts != 0 ? x.Total / x.Casts : null;
            x.Share = total != 0 ? x.Total / total : null;
            x.ActionList = x.ActionOrder.Select(k =>
            {
                var y = x.Actions[k];
                y.Avg = y.Casts != 0 ? y.Total / y.Casts : 0;
                return y;
            }).ToList();
            Js.StableSort(x.ActionList, (p, q) => q.Total - p.Total);
            return x;
        }).ToList();
        Js.StableSort(actors, (p, q) => (q.Total + q.PetTotal) - (p.Total + p.PetTotal));

        return new HealTotals
        {
            Actors = actors, Total = total, PetTotal = petTotal, Casts = casts, Max = max,
            Avg = casts != 0 ? total / casts : null, Lines = heals.Count,
        };
    }
}
