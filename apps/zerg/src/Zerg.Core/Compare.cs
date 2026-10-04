namespace Zerg.Core;

/// <summary>A damage type, in the order Compare lists them.</summary>
public sealed record KindLabel(string Key, string Label);

/// <summary>The best single hit of a run.</summary>
public sealed record BestHit(double Max, string Who, string What);

/// <summary>Who stands behind a Compare row: the characters, and the job(s) they were on.</summary>
public sealed class Members
{
    public List<string> Names { get; set; } = [];
    public List<string> Jobs { get; set; } = [];
}

/// <summary>One run, measured: everything Compare draws of it.</summary>
public sealed class Measurement
{
    public ImportedParse Run { get; set; } = null!;
    /// <summary><c>actor</c> or <c>job</c>: what a row is.</summary>
    public string By { get; set; } = "actor";
    /// <summary>The counted rows, on the run's clock.</summary>
    public List<CombatEvent> Events { get; set; } = [];
    public Aggregate Agg { get; set; } = new();
    public OrderedDictionary<string, Members> Members { get; set; } = new(StringComparer.Ordinal);
    /// <summary>The run's clock, frozen at its Pause, in seconds.</summary>
    public double Duration { get; set; }
    public double Total { get; set; }
    public double Dps { get; set; }
    public double? Accuracy { get; set; }
    public double? WsTotal { get; set; }
    public int WsCount { get; set; }
    public double? WsAvg { get; set; }
    public double? WsAcc { get; set; }
    public double? ScTotal { get; set; }
    public int ScCount { get; set; }
    /// <summary>Rows with any damage.</summary>
    public int Characters { get; set; }
    public BestHit? Best { get; set; }
    /// <summary>Damage by type (a pet's rows are one type, <c>pet</c>).</summary>
    public OrderedDictionary<string, double> Kinds { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Damage by target name.</summary>
    public OrderedDictionary<string, double> Targets { get; set; } = new(StringComparer.Ordinal);
    public HealTotals Heal { get; set; } = new();
    /// <summary>Healing as time/amount points for the cumulative line (no pet heals).</summary>
    public List<CombatEvent> HealEvents { get; set; } = [];
    /// <summary>Party healing by spell, pet heals included.</summary>
    public OrderedDictionary<string, double> HealSpells { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Party healing by who received it, pet heals included.</summary>
    public OrderedDictionary<string, double> HealTargets { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Whether the parse could have healing at all: one exported before
    /// the addon recorded heals has none, which is not the same as healing 0.</summary>
    public bool HasHeals { get; set; }
}

/// <summary>A row present in either run; a side is null where the key was absent.</summary>
public sealed record Pair<T>(string Key, T? A, T? B) where T : class;

/// <summary>A summed figure for both runs (0 where absent).</summary>
public sealed record PairTotal(string Key, double A, double B);

/// <summary>A damage type for both runs.</summary>
public sealed record KindPair(string Key, string Label, double A, double B);

/// <summary>Two measurements, lined up.</summary>
public sealed class CompareRows
{
    /// <summary>One per character (or job), nobody at zero on both sides, largest first.</summary>
    public List<Pair<ActorTotals>> Rows { get; set; } = [];
    /// <summary>Every damage type either run dealt any of.</summary>
    public List<KindPair> Kinds { get; set; } = [];
    /// <summary>Damage by target, largest first.</summary>
    public List<PairTotal> Targets { get; set; } = [];
    public List<Pair<HealerTotals>> Heals { get; set; } = [];
    public List<PairTotal> HealSpells { get; set; } = [];
    public List<PairTotal> HealTargets { get; set; } = [];
}

/// <summary>Both runs' running party total on one grid. The shorter run is NaN
/// past its own end.</summary>
public sealed class Pace
{
    public double[] Times { get; set; } = [];
    public double Step { get; set; }
    public double[] A { get; set; } = [];
    public double[] B { get; set; } = [];
}

/// <summary>B against A: the difference, and that over A (null when A is zero
/// or either side has nothing to measure).</summary>
public sealed record Delta(double? D, double? Pct);

/// <summary>
/// Two exported parses → an A/B comparison.
///
/// <para>A run is an import, measured by exactly the <see cref="Counting.Filter"/>
/// and <see cref="Counting.Aggregate"/> the live view draws with, so a compared
/// figure is the figure Zerg prints when that file is imported. Nothing here
/// counts damage a second way; it only lines two answers up.</para>
///
/// <para>A is the baseline: every delta is B − A, every percentage that over A,
/// so a sign means the same thing in every cell.</para>
/// </summary>
public static class Compare
{
    public static readonly IReadOnlyList<KindLabel> KindLabels =
    [
        new("melee", "Melee"),
        new("ws", "Weaponskills"),
        new("skillchain", "Skillchains"),
        new("magic", "Magic"),
        new("pet", "Pet"),
        new("ability", "Job abilities"),
        new("ranged", "Ranged"),
        new("addl", "Additional effects"),
        new("reaction", "Counters & spikes"),
        new("mobtp", "Other"),
    ];

    public const string UnknownJob = "Unknown job";

    /// <summary>A character's main job in this run, or <see cref="UnknownJob"/>.
    /// Never borrowed from the other run: someone missing from the party table
    /// in one run was often on a different job in it.</summary>
    static string MainJob(Roster roster, string name)
    {
        var j = roster.JobOf(name);
        return j != null && !string.IsNullOrEmpty(j.Main) && j.Main != "NON" ? j.Main : UnknownJob;
    }

    static void Add(OrderedDictionary<string, double> map, string key, double v) =>
        map[key] = map.GetValueOrDefault(key) + v;

    /// <summary>
    /// One run, measured. <paramref name="byJob"/> re-actors every row onto its
    /// character's main job before the aggregate, so a job's accuracy, WS
    /// average and action breakdown are counted over its combined swings rather
    /// than averaged from per-character figures.
    /// </summary>
    public static Measurement Measure(ImportedParse run, bool skillchains = true, bool byJob = false)
    {
        var roster = run.Source.Roster;
        var sn = run.Session;

        var scoped = Counting.Filter(run.Source.Events,
            new FilterOptions { Session = sn, Roster = roster, Skillchains = skillchains });

        // The export's own clock, frozen at its Pause: the fixed denominator
        // the exporter was looking at.
        var secs = sn.Elapsed(sn.PausedAt) / 1000;

        var members = new OrderedDictionary<string, Members>(StringComparer.Ordinal);
        void Note(string key, string name)
        {
            if (!members.TryGetValue(key, out var m)) members[key] = m = new Members();
            if (!m.Names.Contains(name)) m.Names.Add(name);
            var lab = roster.JobLabel(name);
            if (lab.Length > 0 && !m.Jobs.Contains(lab)) m.Jobs.Add(lab);
        }

        List<CombatEvent> rows;
        if (byJob)
        {
            rows = scoped.Select(e =>
            {
                var key = MainJob(roster, e.Actor);
                Note(key, e.Actor);
                return e with { Actor = key };
            }).ToList();
        }
        else
        {
            rows = scoped;
            foreach (var e in scoped) Note(e.Actor, e.Actor);
        }

        var agg = Counting.Aggregate(rows, secs);

        // Healing over the same window, re-keyed by job the same way.
        var hl = Healing.Filter(run.Source.Heals, new FilterOptions { Session = sn, Roster = roster });
        if (byJob)
        {
            hl = hl.Select(h =>
            {
                var key = MainJob(roster, h.Actor);
                Note(key, h.Actor);
                return h with { Actor = key };
            }).ToList();
        }
        var heal = Healing.Totals(hl);

        // For Healing mode: the cumulative line (no pet heals, as in the Healing
        // column), and party healing by spell and by receiver (pet heals
        // included: these say where healing went, not whose column it is in).
        var healEvents = new List<CombatEvent>();
        var healSpells = new OrderedDictionary<string, double>(StringComparer.Ordinal);
        var healTargets = new OrderedDictionary<string, double>(StringComparer.Ordinal);
        foreach (var h in hl)
        {
            if (!h.IsPets) healEvents.Add(new CombatEvent { T = h.T, Hit = true, Dmg = h.Hp });
            var spell = !string.IsNullOrEmpty(h.By) ? Slice(h.Action, h.By.Length + 2) : h.Action;
            Add(healSpells, spell, h.Hp);
            Add(healTargets, h.Target, h.Hp);
        }

        // Damage by type and by target over every landed row. Unfolded is right
        // for both: they only sum, and an AoE's damage belongs to each target.
        var kinds = new OrderedDictionary<string, double>(StringComparer.Ordinal);
        var targets = new OrderedDictionary<string, double>(StringComparer.Ordinal);
        foreach (var e in scoped)
        {
            if (!e.Hit || e.Dmg == 0 || double.IsNaN(e.Dmg)) continue;
            Add(kinds, e.IsPets ? "pet" : e.Kind, e.Dmg);
            Add(targets, e.Target, e.Dmg);
        }

        // Party figures, summed from the per-row splits so they agree with the
        // table by construction.
        int autoTries = 0, autoHits = 0, wsTries = 0, wsHits = 0, scRows = 0;
        double wsTotal = 0, scTotal = 0;
        foreach (var a in agg.Actors)
        {
            var s = a.Split;
            autoTries += s.AutoTries; autoHits += s.AutoHits;
            wsTotal += s.WsTotal; wsTries += s.WsTries; wsHits += s.WsHits;
            scTotal += s.ScTotal; scRows += s.ScRows;
        }

        BestHit? best = null;
        foreach (var a in agg.Actors)
            foreach (var act in a.ActionList)
                if (best == null || act.Max > best.Max) best = new BestHit(act.Max, a.Name, act.Name);

        return new Measurement
        {
            Run = run,
            By = byJob ? "job" : "actor",
            Events = scoped,
            Agg = agg,
            Members = members,
            Duration = secs,
            Total = agg.Total,
            Dps = secs > 0 ? agg.Total / secs : 0,
            Accuracy = autoTries != 0 ? (double)autoHits / autoTries : null,
            WsTotal = wsTries != 0 ? wsTotal : null,
            WsCount = wsTries,
            WsAvg = wsHits != 0 ? wsTotal / wsHits : null,
            WsAcc = wsTries != 0 ? (double)wsHits / wsTries : null,
            ScTotal = scRows != 0 ? scTotal : null,
            ScCount = scRows,
            Characters = agg.Actors.Count(a => a.Total > 0),
            Best = best,
            Kinds = kinds,
            Targets = targets,
            Heal = heal,
            HealEvents = healEvents,
            HealSpells = healSpells,
            HealTargets = healTargets,
            HasHeals = run.Source.Heals.Count > 0,
        };
    }

    /// <summary>String slice from <paramref name="start"/>, clamped like JavaScript's.</summary>
    static string Slice(string s, int start) => start >= s.Length ? "" : s[start..];

    /// <summary>The keys of either map, A's first, each with what either side had.</summary>
    static List<(string Key, bool InA, T? A, bool InB, T? B)> PairUp<T>(
        OrderedDictionary<string, T> a, OrderedDictionary<string, T> b)
    {
        var keys = Js.Keys(a.Keys.ToList()).ToList();
        foreach (var k in Js.Keys(b.Keys.ToList())) if (!a.ContainsKey(k)) keys.Add(k);
        return keys.Select(k =>
        {
            bool inA = a.TryGetValue(k, out var va), inB = b.TryGetValue(k, out var vb);
            return (k, inA, va, inB, vb);
        }).ToList();
    }

    static OrderedDictionary<string, T> ByName<T>(IEnumerable<T> list, Func<T, string> name)
    {
        var m = new OrderedDictionary<string, T>(StringComparer.Ordinal);
        foreach (var x in list) m[name(x)] = x;
        return m;
    }

    static double Size(Bucket? x) => x?.Total ?? 0;
    static double Size(HealAction? x) => x?.Total ?? 0;
    /// <summary>Everything a healer put out, pet included: for ordering and the zero test.</summary>
    static double HealSize(HealerTotals? x) => x == null ? 0 : x.Total + x.PetTotal;

    static List<Pair<T>> Pairs<T>(OrderedDictionary<string, T> a, OrderedDictionary<string, T> b) where T : class =>
        PairUp(a, b).Select(p => new Pair<T>(p.Key, p.A, p.B)).ToList();

    static List<PairTotal> Totals(OrderedDictionary<string, double> a, OrderedDictionary<string, double> b)
    {
        var list = PairUp(a, b).Select(p => new PairTotal(p.Key, p.A, p.B)).ToList();
        Js.StableSort(list, (x, y) => Math.Max(y.A, y.B) - Math.Max(x.A, x.B));
        return list;
    }

    /// <summary>Two measurements → rows Compare can print.</summary>
    public static CompareRows Diff(Measurement ma, Measurement mb)
    {
        var rows = Pairs(ByName(ma.Agg.Actors, x => x.Name), ByName(mb.Agg.Actors, x => x.Name))
            .Where(r => Size(r.A) > 0 || Size(r.B) > 0).ToList();
        Js.StableSort(rows, (x, y) => Math.Max(Size(y.A), Size(y.B)) - Math.Max(Size(x.A), Size(x.B)));

        var kinds = new List<KindPair>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var k in KindLabels)
        {
            seen.Add(k.Key);
            double a = ma.Kinds.GetValueOrDefault(k.Key), b = mb.Kinds.GetValueOrDefault(k.Key);
            if (Truthy(a) || Truthy(b)) kinds.Add(new KindPair(k.Key, k.Label, a, b));
        }
        // A type this list does not know yet still gets a row rather than vanishing.
        foreach (var r in PairUp(ma.Kinds, mb.Kinds))
            if (!seen.Contains(r.Key)) kinds.Add(new KindPair(r.Key, r.Key, r.A, r.B));

        var targets = Totals(ma.Targets, mb.Targets);

        var heals = Pairs(ByName(ma.Heal.Actors, x => x.Name), ByName(mb.Heal.Actors, x => x.Name))
            .Where(r => HealSize(r.A) > 0 || HealSize(r.B) > 0).ToList();
        Js.StableSort(heals, (x, y) =>
            Math.Max(HealSize(y.A), HealSize(y.B)) - Math.Max(HealSize(x.A), HealSize(x.B)));

        return new CompareRows
        {
            Rows = rows, Kinds = kinds, Targets = targets, Heals = heals,
            HealSpells = Totals(ma.HealSpells, mb.HealSpells),
            HealTargets = Totals(ma.HealTargets, mb.HealTargets),
        };
    }

    static bool Truthy(double v) => v != 0 && !double.IsNaN(v);

    /// <summary>
    /// One row's actions, paired, largest first. Only actions that did damage in
    /// at least one run: an Erase or a resisted Stun reaches the aggregate by
    /// being used, and a row of dashes for it is noise. An action that did
    /// damage in one run and none in the other stays; that zero is the news.
    /// </summary>
    public static List<Pair<ActionTotals>> Actions(Pair<ActorTotals> row)
    {
        var la = row.A != null ? ByName(row.A.ActionList, x => x.Name) : new(StringComparer.Ordinal);
        var lb = row.B != null ? ByName(row.B.ActionList, x => x.Name) : new(StringComparer.Ordinal);
        var list = Pairs(la, lb).Where(x => Size(x.A) > 0 || Size(x.B) > 0).ToList();
        Js.StableSort(list, (x, y) => Math.Max(Size(y.A), Size(y.B)) - Math.Max(Size(x.A), Size(x.B)));
        return list;
    }

    /// <summary>One healer's heals, paired by name, largest first.</summary>
    public static List<Pair<HealAction>> HealActions(Pair<HealerTotals> row)
    {
        var la = row.A != null ? ByName(row.A.ActionList, x => x.Name) : new(StringComparer.Ordinal);
        var lb = row.B != null ? ByName(row.B.ActionList, x => x.Name) : new(StringComparer.Ordinal);
        var list = Pairs(la, lb);
        Js.StableSort(list, (x, y) => Math.Max(Size(y.A), Size(y.B)) - Math.Max(Size(x.A), Size(x.B)));
        return list;
    }

    /// <summary>
    /// Both runs' running party total on one grid, so a crosshair reads both at
    /// the same elapsed instant ("where was each run at minute forty"). The grid
    /// runs to the longer run's clock; the shorter run is NaN past its own end
    /// rather than carried flat, because a flat line would claim it was still
    /// being measured, and the chart lifts the pen on NaN.
    /// <paramref name="healing"/> draws the heal points instead of the damage rows.
    /// </summary>
    public static Pace Pace(Measurement ma, Measurement mb, bool healing = false, int maxPoints = 0)
    {
        if (maxPoints == 0) maxPoints = 400;
        var end = Math.Max(ma.Duration, mb.Duration) * 1000;
        if (end <= 0 || double.IsNaN(end)) end = 1000;
        var step = Math.Max(1000, Math.Ceiling(end / maxPoints / 1000) * 1000);
        int n = (int)Math.Ceiling(end / step) + 1;
        var times = new double[n];
        for (int i = 0; i < n; i++) times[i] = Math.Min(i * step, end);

        double[] Line(Measurement m)
        {
            var v = new double[n];
            var stop = m.Duration * 1000;
            foreach (var e in healing ? m.HealEvents : m.Events)
            {
                if (!e.Hit || e.Dmg == 0 || double.IsNaN(e.Dmg)) continue;
                // Into the first sample at or after the hit, so a sample reads
                // the damage done by that instant, never a hit still to come.
                var bin = (int)Math.Min(n - 1, Math.Max(0, Math.Ceiling(e.T / step)));
                v[bin] += e.Dmg;
            }
            double run = 0;
            for (int j = 0; j < n; j++)
            {
                run += v[j];
                v[j] = times[j] <= stop + step - 1 ? run : double.NaN;
            }
            return v;
        }

        return new Pace { Times = times, Step = step, A = Line(ma), B = Line(mb) };
    }

    public static Delta Delta(double? a, double? b)
    {
        if (a is not double x || b is not double y) return new Delta(null, null);
        return new Delta(y - x, x != 0 && !double.IsNaN(x) ? (y - x) / Math.Abs(x) : null);
    }
}
