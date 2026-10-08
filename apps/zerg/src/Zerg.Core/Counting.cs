namespace Zerg.Core;

/// <summary>Which rows count, and how they are scoped.</summary>
public sealed class FilterOptions
{
    /// <summary>Puts survivors on this session's clock and drops everything it
    /// does not cover. Null: rows pass with their wall clock, which a second,
    /// re-scoping pass over already-converted rows relies on.</summary>
    public Session? Session { get; init; }
    /// <summary>Drops every row whose actor is a monster. Null: no such drop.</summary>
    public Roster? Roster { get; init; }
    /// <summary>Per-character switches, keyed by name: <c>false</c> excludes.
    /// A name that is absent is included.</summary>
    public IReadOnlyDictionary<string, bool>? Actors { get; init; }
    /// <summary>False drops skillchain rows entirely.</summary>
    public bool Skillchains { get; init; } = true;
}

/// <summary>
/// Damage rows → the numbers Zerg draws. No UI here: plain data in, plain data
/// out, and the input is never changed.
///
/// <para><b>Party damage only.</b> A monster's damage is never shown, totalled
/// or charted. Monsters' rows are still read and kept, so the list stays true
/// to the file; they are dropped here, in one rule in one place.</para>
/// </summary>
public static class Counting
{
    /// <summary>A row that is an attempt at damage, hit or not.</summary>
    static bool IsCombat(CombatEvent e) => e.Kind != "defeat";

    /// <summary>
    /// A pet's damage is its owner's. The row names the master, so this is
    /// attribution, not a guess. The row is re-actored (rather than summed
    /// under the owner) because everything downstream keys on the actor, so
    /// this one rewrite is the whole change. The action keeps the pet's name as
    /// a prefix, so the owner's breakdown still separates the two: a
    /// beastmaster and their pet both swing an "Attack", and one average over
    /// both creatures would describe neither. <see cref="CombatEvent.By"/>
    /// keeps who swung.
    ///
    /// A copy, and idempotent: a second pass sees actor == owner and does nothing.
    /// </summary>
    public static CombatEvent Credit(CombatEvent e)
    {
        if (string.IsNullOrEmpty(e.Owner) || e.Actor == e.Owner) return e;
        var by = !string.IsNullOrEmpty(e.Pet) ? e.Pet : e.Actor;
        return e with
        {
            Actor = e.Owner,
            ActorKind = "player",   // the owner came out of a party slot
            By = by,
            Action = by + ": " + e.Action,
        };
    }

    /// <summary>
    /// Every drop rule that is not about time, in one place: the credited row,
    /// or null if it does not count. <see cref="Filter"/> and
    /// <see cref="FirstCounted"/> both use it, so the clock can never start on
    /// a row that is then not drawn.
    /// </summary>
    public static CombatEvent? Counted(CombatEvent e, Roster? roster,
                                       IReadOnlyDictionary<string, bool>? actors, bool skillchains)
    {
        if (!IsCombat(e)) return null;
        if (!skillchains && e.Kind == "skillchain") return null;
        // Asked of the row's own actor, so a pet is judged as the pet it is.
        if (roster != null && roster.IsMob(e.Actor)) return null;
        e = Credit(e);
        // Keyed on the credited name, so excluding an owner takes their pet too.
        if (actors != null && actors.TryGetValue(e.Actor, out var on) && !on) return null;
        return e;
    }

    /// <summary>
    /// The rows that count, credited, and (with a session) on its clock.
    ///
    /// <para>With a session this is the one place the clock changes: rows before
    /// the zero or inside a pause are dropped, and each survivor is copied with
    /// <c>T</c> moved from wall clock to milliseconds since the zero and the
    /// wall clock kept in <c>Wall</c>. Downstream there is no time of day left.
    /// A session with no zero yet (idle, or armed and waiting) selects nothing.</para>
    /// </summary>
    public static List<CombatEvent> Filter(IReadOnlyList<CombatEvent> events, FilterOptions? opts = null)
    {
        opts ??= new FilterOptions();
        var sn = opts.Session;
        var output = new List<CombatEvent>();
        if (sn != null && sn.StartedAt == null) return output;

        foreach (var row in events)
        {
            double? el = null;
            if (sn != null)
            {
                el = sn.At(row.T);
                if (el == null) continue;   // before the zero, or in a pause
            }
            var e = Counted(row, opts.Roster, opts.Actors, opts.Skillchains);
            if (e == null) continue;
            if (el is double t) e = e with { Wall = e.Wall ?? e.T, T = t };
            output.Add(e);
        }
        return output;
    }

    /// <summary>
    /// The zero an armed session is waiting for: the time of the first row at or
    /// after the Start press that would be counted, or null while none has.
    ///
    /// <para>"Would be counted" is the whole rule, the same test
    /// <see cref="Filter"/> applies: a monster's swing does not start the clock,
    /// nor does a skillchain with skillchains off, nor an excluded character's
    /// attack. With everyone excluded nothing qualifies and the clock never
    /// starts, which is consistent (nothing would be drawn) but is the one way
    /// arming can look stuck.</para>
    ///
    /// <para>A miss starts the clock like any other swing. Waiting for damage to
    /// land would put every miss before it outside the session and drop them
    /// from accuracy, the very figure a run of whiffs should move.</para>
    /// </summary>
    public static double? FirstCounted(IReadOnlyList<CombatEvent> events, Session? sn, FilterOptions? opts = null)
    {
        if (sn?.ArmedAt is not double armed || sn.StartedAt != null) return null;
        opts ??= new FilterOptions();
        foreach (var e in events)
        {
            if (e.T < armed) continue;
            if (Counted(e, opts.Roster, opts.Actors, opts.Skillchains) != null) return e.T;
        }
        return null;
    }

    /// <summary>
    /// What a paused session's clock is snapped back to
    /// (<see cref="Session.Snap"/>): the time of the last row the session
    /// covers that would be counted, or null when there is none.
    ///
    /// <para>The same test as the zero's, from the other end: a monster's
    /// swing does not hold the clock open, and a miss does. A heal does not
    /// either, as it does not start one. Asked without the viewer's switches,
    /// though: the zero is latched once and stays, but an end worked out from
    /// the exclusions would move with every chip, and the file an export
    /// writes carries no filters to work it out from again.</para>
    /// </summary>
    public static double? LastCounted(IReadOnlyList<CombatEvent> events, Session sn, FilterOptions? opts = null)
    {
        opts ??= new FilterOptions();
        double? last = null;
        foreach (var e in events)
        {
            if (last is double t && e.T <= t) continue;
            if (sn.At(e.T) == null) continue;
            if (Counted(e, opts.Roster, opts.Actors, opts.Skillchains) != null) last = e.T;
        }
        return last;
    }

    /// <summary>
    /// Rows → uses. One action is one use however many targets it reached, but
    /// it writes a row per target: unfolded, an AoE weaponskill on three mobs
    /// would count three hits, inflate the swing count accuracy divides by, and
    /// put the splash spread in the histogram.
    ///
    /// <para>The grouping is the addon's, from the packet's own target list.
    /// Damage sums; hit, crit and burst are "any" (a use that hit two targets
    /// and was evaded by a third is one landed hit). A use that reached several
    /// targets reports its target as "3 targets". Rows without a use pass
    /// through, and so does a single-target use, in effect.</para>
    ///
    /// <para>The invariant: folding moves counts, never damage.</para>
    /// </summary>
    public static List<CombatEvent> Collapse(IReadOnlyList<CombatEvent> events)
    {
        var output = new List<CombatEvent>();
        var byUse = new Dictionary<double, int>();   // use → index in output
        var targets = new Dictionary<int, List<string>>();

        foreach (var e in events)
        {
            if (e.Use is not double use) { output.Add(e); continue; }

            if (!byUse.TryGetValue(use, out var i))
            {
                byUse[use] = output.Count;
                targets[output.Count] = [e.Target];
                // A fresh use: who swung is not carried (By is dropped), as a
                // folded use is counted by actor and owner alone.
                output.Add(e with { By = null, Parts = 1 });
                continue;
            }

            var u = output[i];
            var list = targets[i];
            if (!list.Contains(e.Target)) list.Add(e.Target);
            output[i] = u with
            {
                Dmg = u.Dmg + e.Dmg,
                Hit = u.Hit || e.Hit,
                Crit = u.Crit || e.Crit,
                Burst = u.Burst || e.Burst,
                T = e.T < u.T ? e.T : u.T,   // the use happened when it was announced
                Parts = u.Parts + 1,
            };
        }

        foreach (var (i, list) in targets)
        {
            var u = output[i];
            output[i] = list.Count > 1
                ? u with { Targets = list, Target = list.Count + " targets", TargetKind = "" }
                : u with { Targets = list };
        }
        return output;
    }

    const double MsgShadows = 31, MsgDodge = 32, MsgMobHeal = 373;

    /// <summary>
    /// Did it connect: the accuracy question. True a hit, false a miss, null not
    /// an attempt at all. Not the same question as <c>Hit</c>, which asks
    /// whether damage landed and feeds every total, average and histogram.
    /// <list type="bullet">
    /// <item>31, a shadow absorbed it: a hit, melee or ranged. The swing got
    /// through and a shadow took it.</item>
    /// <item>373, the swing healed the target: a hit, melee only.</item>
    /// <item>32, a perfect dodge: not an attempt, melee only.</item>
    /// </list>
    /// A weaponskill connects when it dealt damage, summed over its targets, so
    /// this takes a folded use. Everything else connects exactly when it hit.
    /// </summary>
    public static bool? Connects(CombatEvent e)
    {
        if (e.Kind == "ws") return e.Dmg > 0;
        if (e.Kind is "melee" or "ranged")
        {
            if (e.Msg == MsgShadows) return true;
            if (e.Kind == "melee" && e.Msg == MsgDodge) return null;
            if (e.Kind == "melee" && e.Msg == MsgMobHeal) return true;
        }
        return e.Hit;
    }

    /// <summary>
    /// Per-character totals with a per-action breakdown inside each, counted per
    /// use.
    ///
    /// <para><paramref name="duration"/> is the session clock in seconds, and
    /// when given it divides every DPS here, the party's and each character's
    /// alike. One denominator is what makes the column add up, and a character
    /// who did nothing for a minute is one whose DPS fell, which is the question
    /// being asked. Each character's own first-to-last span is still reported,
    /// as <c>Window</c>; without a duration it is what DPS divides by.</para>
    /// </summary>
    public static Aggregate Aggregate(IReadOnlyList<CombatEvent> events, double? duration = null)
    {
        var byActor = new Dictionary<string, ActorTotals>(StringComparer.Ordinal);
        var order = new List<string>();
        int lines = events.Count;
        var uses = Collapse(events);

        foreach (var e in uses)
        {
            if (!byActor.TryGetValue(e.Actor, out var a))
            {
                byActor[e.Actor] = a = new ActorTotals { Name = e.Actor };
                order.Add(e.Actor);
            }
            a.Add(e);
            a.Split.Add(e);

            if (!a.Actions.TryGetValue(e.Action, out var act))
            {
                a.Actions[e.Action] = act = new ActionTotals { Name = e.Action, Kind = e.Kind };
                a.ActionOrder.Add(e.Action);
            }
            act.Add(e);
        }

        var actors = new List<ActorTotals>();
        var party = new Split();
        double grand = 0, tMin = double.PositiveInfinity, tMax = double.NegativeInfinity;

        foreach (var name in order)
        {
            var a = byActor[name];
            a.Finish();
            a.ActionList = a.ActionOrder.Select(k => { var x = a.Actions[k]; x.Finish(); return x; }).ToList();
            Js.StableSort(a.ActionList, (x, y) => y.Total - x.Total);
            var span = ((a.Last ?? 0) - (a.First ?? 0)) / 1000;
            a.Window = span;
            a.Duration = duration ?? span;
            a.Dps = a.Duration > 0 ? a.Total / a.Duration : 0;
            a.FinishSplit();
            party.Add(a.Split);
            grand += a.Total;
            if (a.First is double f && f < tMin) tMin = f;
            if (a.Last is double l && l > tMax) tMax = l;
            actors.Add(a);
        }

        Js.StableSort(actors, (x, y) => y.Total - x.Total);
        foreach (var a in actors)
            a.Share = grand != 0 && !double.IsNaN(grand) ? a.Total / grand : 0;

        bool none = double.IsPositiveInfinity(tMin);
        return new Aggregate
        {
            Actors = actors,
            Total = grand,
            Start = none ? null : tMin,
            End = double.IsNegativeInfinity(tMax) ? null : tMax,
            Window = none ? 0 : (tMax - tMin) / 1000,
            Duration = duration ?? (none ? 0 : (tMax - tMin) / 1000),
            Events = lines,
            Uses = uses.Count,
            Split = party,
            Party = party.Figures(grand),
        };
    }

    /// <summary>
    /// Running damage per character, on one shared time grid so one crosshair
    /// reads every line at the same instant. <paramref name="maxPoints"/> caps
    /// the grid (0 means the default, 400).
    ///
    /// <para>Not folded: it only sums damage into time bins, and folding would
    /// move an AoE's later targets back onto the announcement for no gain.</para>
    ///
    /// <para><paramref name="from"/> pins the left edge; on a session clock it is
    /// 0, the zero. Without it the grid starts at the first row, and a pull whose
    /// first swing landed twenty seconds in would draw an axis twenty seconds
    /// shorter than the span DPS is divided by.</para>
    ///
    /// <para><paramref name="now"/> is the live edge: the grid runs to it and
    /// each line is carried flat across the silence, which is what a running
    /// total did during it. The edge is one extra sample at exactly
    /// <paramref name="now"/> rather than another bin, so it can advance without
    /// the step changing.</para>
    /// </summary>
    public static Cumulative Cumulative(IReadOnlyList<CombatEvent> events, IReadOnlyList<string> names,
                                        double? from = null, double? now = null, int maxPoints = 0)
    {
        if (maxPoints == 0) maxPoints = 400;
        if (events.Count == 0 || names.Count == 0) return new Cumulative();

        double t0 = double.PositiveInfinity, t1 = double.NegativeInfinity;
        foreach (var e in events)
        {
            if (e.T < t0) t0 = e.T;
            if (e.T > t1) t1 = e.T;
        }
        if (from is double f && f < t0) t0 = f;
        var tEvent = t1;
        bool live = now is double n0 && n0 > t1;
        if (live) t1 = now!.Value;
        if (t1 <= t0) t1 = t0 + 1000;

        var step = Math.Max(1000, Math.Ceiling((t1 - t0) / maxPoints / 1000) * 1000);
        int nb = (int)Math.Floor((t1 - t0) / step) + 1;
        // The live sample always gets its slot, even when the last bin lands on
        // now: an animating caller needs it to exist before it can move it.
        int count = nb + (live ? 1 : 0);

        var times = new double[count];
        for (int i = 0; i < nb; i++) times[i] = t0 + i * step;
        if (live) times[nb] = t1;

        var idx = new Dictionary<string, int>(StringComparer.Ordinal);
        var series = new List<Series>();
        for (int i = 0; i < names.Count; i++)
        {
            idx[names[i]] = i;
            series.Add(new Series { Name = names[i], Values = new double[count] });
        }

        foreach (var e in events)
        {
            if (!e.Hit || e.Dmg == 0 || double.IsNaN(e.Dmg)) continue;
            if (!idx.TryGetValue(e.Actor, out var si)) continue;
            var bin = (int)Math.Max(0, Math.Min(nb - 1, Math.Floor((e.T - t0) / step)));
            series[si].Values[bin] += e.Dmg;
        }

        // Bin totals → running totals. The live sample holds no damage of its
        // own, so it carries the run and the tail comes out flat.
        foreach (var s in series)
        {
            double run = 0;
            for (int j = 0; j < count; j++) { run += s.Values[j]; s.Values[j] = run; }
        }

        return new Cumulative
        {
            Times = times, Series = series, Step = step, T0 = t0, T1 = t1, Live = live, TEvent = tEvent,
        };
    }

    /// <summary>Linear interpolation between the two nearest ranks; 0 when empty.</summary>
    public static double Quantile(IReadOnlyList<double> sorted, double q)
    {
        if (sorted.Count == 0) return 0;
        var pos = (sorted.Count - 1) * q;
        int lo = (int)Math.Floor(pos), hi = (int)Math.Ceiling(pos);
        if (lo == hi) return sorted[lo];
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (pos - lo);
    }

    /// <summary>The Freedman–Diaconis bin width for <paramref name="n"/> values
    /// with these quartiles, or 0 when they have no spread to take one from.</summary>
    public static double BinWidth(double q1, double q3, int n) =>
        q3 - q1 > 0 && n > 0 ? 2 * (q3 - q1) / Math.Pow(n, 1.0 / 3) : 0;

    /// <summary>
    /// Every hit of one character's action (<paramref name="action"/> null: all
    /// of them), with summary figures and a histogram. Folded first, so an AoE
    /// is one figure, what the action hit for, not one point per target.
    /// The bin count follows Freedman–Diaconis, clamped to 6..maxBins: a fixed
    /// count either flattens a tight weaponskill or shatters a wide one.
    /// </summary>
    public static Distribution Distribution(IReadOnlyList<CombatEvent> events, string actor, string? action,
                                            int maxBins = 0)
    {
        if (maxBins == 0) maxBins = 28;
        var picked = new List<CombatEvent>();
        int misses = 0, crits = 0, bursts = 0, tries = 0, lands = 0;

        foreach (var e in Collapse(events))
        {
            if (e.Actor != actor) continue;
            if (action != null && e.Action != action) continue;
            // The same accuracy rule as the per-character table, so drilling
            // into "Attack" agrees with that character's Accuracy cell.
            if (Connects(e) is bool o) { tries++; if (o) lands++; }
            if (!e.Hit) { misses++; continue; }
            picked.Add(e);
            if (e.Crit) crits++;
            if (e.Burst) bursts++;
        }

        var values = picked.Select(e => e.Dmg).ToList();
        var sorted = values.ToList();
        Js.StableSort(sorted, (a, b) => a - b);
        int n = sorted.Count;

        double sum = 0;
        for (int i = 0; i < n; i++) sum += sorted[i];
        var avg = n != 0 ? sum / n : 0;

        double varc = 0;
        for (int i = 0; i < n; i++) varc += (sorted[i] - avg) * (sorted[i] - avg);
        var stdev = n > 1 ? Math.Sqrt(varc / (n - 1)) : 0;

        var min = n != 0 ? sorted[0] : 0;
        var max = n != 0 ? sorted[n - 1] : 0;
        var q1 = Quantile(sorted, 0.25);
        var q3 = Quantile(sorted, 0.75);

        var bins = new List<Bin>();
        if (n > 0 && max > min)
        {
            var width = BinWidth(q1, q3, n);
            var raw = width > 0 ? Math.Ceiling((max - min) / width) : Math.Ceiling(Math.Sqrt(n));
            // A NaN or zero count means "the default", 6.
            var count = (int)Math.Max(6, Math.Min(maxBins, raw is 0 || double.IsNaN(raw) ? 6 : raw));

            var bw = (max - min) / count;
            for (int i = 0; i < count; i++) bins.Add(new Bin(min + i * bw, min + (i + 1) * bw));
            for (int i = 0; i < n; i++)
            {
                var b = (int)Math.Min(count - 1, Math.Floor((sorted[i] - min) / bw));
                bins[b].Count++;
            }
        }
        else if (n > 0)
        {
            bins.Add(new Bin(min, min) { Count = n });   // every hit identical
        }

        return new Distribution
        {
            Actor = actor,
            Action = action,
            Events = picked,
            Values = values,
            Sorted = sorted,
            Bins = bins,
            Count = n,
            Misses = misses,
            Swings = n + misses,
            Tries = tries,
            Accuracy = tries != 0 ? (double)lands / tries : 0,
            Crits = crits,
            CritRate = n != 0 ? (double)crits / n : 0,
            Bursts = bursts,
            Total = sum,
            Min = min,
            Max = max,
            Avg = avg,
            Median = Quantile(sorted, 0.5),
            Q1 = q1,
            Q3 = q3,
            P90 = Quantile(sorted, 0.9),
            Stdev = stdev,
        };
    }
}
