using System.Globalization;
using Zerg.Core.Charts;

namespace Zerg.Core;

/// <summary>How a change is said: as a share of A, in points, or as a length of time.</summary>
public enum ChangeKind
{
    /// <summary>"+12.3%", with the plain difference beside it.</summary>
    Percent,
    /// <summary>The figure is a percentage already, so the change is in points: "+2.1 pt".</summary>
    Points,
    /// <summary>"+4m 07s".</summary>
    Time,
}

/// <summary>Whether a change is good news, bad news, or neither.</summary>
public enum ChangeTone
{
    /// <summary>Nothing to compare: one side has no figure.</summary>
    None,
    /// <summary>No change, or a change that is neither better nor worse.</summary>
    Flat,
    Better,
    Worse,
}

/// <summary>
/// B against A, as it is printed. The sign is always in the text, so the tone
/// (a colour, wherever it is drawn) is never the only thing that says which
/// way a figure moved.
/// </summary>
/// <param name="Main">"+12.3%", "−2.1 pt", "new", "±0", or a dash.</param>
/// <param name="Sub">The plain difference under a percentage ("+1,204"), or "".</param>
public sealed record Change(string Main, string Sub, ChangeTone Tone)
{
    public static readonly Change Nothing = new(CompareSheet.Dash, "", ChangeTone.None);

    /// <param name="format">How the plain difference is spelled.</param>
    /// <param name="good">+1 when more is better, −1 when less is, 0 when it is neither.</param>
    public static Change Of(double? a, double? b, ChangeKind kind, Func<double, string> format, int good)
    {
        if (a is not double x || b is not double y) return Nothing;
        double d = y - x;
        int dir = Math.Abs(d) < 1e-9 ? 0 : d > 0 ? 1 : -1;
        var tone = dir == 0 || good == 0 ? ChangeTone.Flat : dir * good > 0 ? ChangeTone.Better : ChangeTone.Worse;
        string sign = dir > 0 ? "+" : dir < 0 ? "−" : "±";
        return kind switch
        {
            ChangeKind.Points => new(sign + Format.Num(Math.Abs(d) * 100, 1) + " pt", "", tone),
            ChangeKind.Time => new(sign + Format.Duration(Math.Abs(d)), "", tone),
            // Over a baseline of nothing there is no percentage to give:
            // anything at all is simply new.
            _ => new(x != 0 && !double.IsNaN(x) ? sign + Format.Num(Math.Abs(d / x) * 100, 1) + "%" : dir != 0 ? "new" : "±0",
                     sign + format(Math.Abs(d)), tone),
        };
    }
}

/// <summary>A figure for each run, A over B.</summary>
public sealed record AB(string A, string B);

/// <summary>
/// Two bars in one cell, each with its figure. A length is a share of the
/// table's largest value, 0 to 1; a figure with nothing behind it has no bar.
/// </summary>
public sealed record BarPair(double A, double B, string TextA, string TextB);

/// <summary>One of the six figures across the top.</summary>
/// <param name="SubA">A line under A's figure ("12 WS · avg 1,204"), or null.</param>
public sealed record CompareTile(string Label, string? Tip, string A, string B, string? SubA, string? SubB,
                                 Change Change, bool Hero = false);

/// <summary>One time of the cumulative chart, as a table row.</summary>
public sealed record PaceLine(string Time, string A, string B, string Difference);

/// <summary>One action of a character (or job), both runs.</summary>
public sealed record ActionLine(string Name, AB Uses, AB Accuracy, Change AccuracyChange, AB Avg, Change AvgChange,
                                AB Max, AB Total, Change TotalChange);

/// <summary>One character (or job), both runs. A side is absent when the
/// character was not in that run.</summary>
public sealed record ActorLine(string Key, bool InA, bool InB, BarPair Damage, Change DamageChange, AB Dps, Change DpsChange,
                               AB Accuracy, Change AccuracyChange, AB WsAvg, Change WsAvgChange, AB Share,
                               IReadOnlyList<ActionLine> Actions);

/// <summary>One heal of a healer (or job), both runs.</summary>
public sealed record HealLine(string Name, AB Casts, AB Total, Change TotalChange, AB Avg, Change AvgChange, AB Max);

/// <summary>One healer (or job), both runs; or the whole party, which has no key.</summary>
public sealed record HealerLine(string? Key, BarPair Healing, Change HealingChange, AB Casts, AB Avg, Change AvgChange,
                                AB Biggest, AB Pet, IReadOnlyList<HealLine> Heals);

/// <summary>One damage type, both runs.</summary>
public sealed record KindLine(string Label, BarPair Damage, Change Change, AB Share);

/// <summary>A total for both runs under one name: a target, or a healing spell.</summary>
public sealed record TotalLine(string Name, BarPair Amount, Change Change);

/// <summary>
/// Who a row is: what it is called, the job (or jobs) behind it, and which
/// colour marks it.
/// </summary>
/// <param name="Label">The name as drawn: the real one, or the job standing in for it.</param>
/// <param name="JobA">The character's job in run A, or "" (not in the run, or never reported).</param>
/// <param name="ColorJob">The job whose colour the row takes, or "" for the fallback colour of <paramref name="Slot"/>.</param>
/// <param name="Members">By job: who was on it, or how many while names are hidden.</param>
/// <param name="Hidden">The label is a job standing in for a name.</param>
public sealed record Identity(string Key, string Label, string JobA, string JobB, string ColorJob, int Slot,
                              string Members, bool Hidden);

/// <summary>What a slot says about the parse in it.</summary>
public sealed record RunSummary(string Started, string Length, string Party, string Skipped);

/// <summary>
/// Everything the Compare section prints for a pair of runs, as text and
/// lengths: the tiles, the two cumulative lines, and every table, for damage
/// and for healing both. Colours are the caller's.
///
/// <para>Nothing is counted here. <see cref="Compare"/> measures each run
/// with the same code the live sections use and lines the two up; this words
/// the result. A is the baseline throughout: every change is B − A.</para>
///
/// <para><b>Unmeasured is a dash, never zero.</b> A character missing from a
/// run has no DPS there, and a parse saved before the addon wrote heal lines
/// has no healing figures at all, which is not the same as a run nobody was
/// healed in. The one place a missing side counts as zero is the total of a
/// row that exists on the other side: against nothing, it reads "new".</para>
/// </summary>
public sealed class CompareSheet
{
    public const string Dash = "—";

    public Measurement A { get; private init; } = null!;
    public Measurement B { get; private init; } = null!;
    public bool ByJob { get; private init; }

    public IReadOnlyList<CompareTile> DamageTiles { get; private init; } = [];
    public IReadOnlyList<CompareTile> HealTiles { get; private init; } = [];

    /// <summary>Party damage since each run's first hit, on one clock.</summary>
    public Pace DamagePace { get; private init; } = new();
    /// <summary>The same for healing. A run with no heal lines has no line at all.</summary>
    public Pace HealPace { get; private init; } = new();

    public IReadOnlyList<ActorLine> Actors { get; private init; } = [];
    public IReadOnlyList<KindLine> Kinds { get; private init; } = [];
    public IReadOnlyList<TotalLine> Targets { get; private init; } = [];

    /// <summary>The whole party's healing, which leads the healing table.</summary>
    public HealerLine Party { get; private init; } = null!;
    public IReadOnlyList<HealerLine> Healers { get; private init; } = [];
    /// <summary>Why a side of the healing table is dashes, or that there was
    /// no healing at all; "" when there is nothing to explain.</summary>
    public string HealNote { get; private init; } = "";
    public IReadOnlyList<TotalLine> HealSpells { get; private init; } = [];
    public IReadOnlyList<TotalLine> HealTargets { get; private init; } = [];

    /// <summary>Every row of the damage and healing tables, by key.</summary>
    public IReadOnlyDictionary<string, Identity> Ids { get; private init; } = new Dictionary<string, Identity>();

    // ------------------------------------------------------------- spelling

    static string Whole(double? x) => x is double v ? Format.Int(v) : Dash;
    static string Rate(double? x) => x is double v ? Format.Num(v, 1) : Dash;
    static string Percent(double? x) => x is double v ? Format.Num(v * 100, 1) + "%" : Dash;
    static string Clock(double seconds) => Format.Stopwatch(seconds * 1000);

    /// <summary>A bar's length: its value out of the largest. A value that is
    /// there at all keeps a sliver, so a small figure still has a mark.</summary>
    static double Share(double? v, double max) =>
        v is double x && x != 0 && !double.IsNaN(x) && max > 0 ? Math.Clamp(x / max, 0.005, 1) : 0;

    static BarPair Bars(double? a, double? b, double max) => new(Share(a, max), Share(b, max), Whole(a), Whole(b));

    // ------------------------------------------------------------- the sheet

    /// <param name="skillchains">Whether skillchain damage counts, in both runs alike.</param>
    /// <param name="hideNames">Draw every character as their job. Every one:
    /// a comparison has no owner, since the two files may be two people's.</param>
    public static CompareSheet Of(ImportedParse a, ImportedParse b, bool byJob, bool skillchains, bool hideNames)
    {
        var ma = Compare.Measure(a, skillchains, byJob);
        var mb = Compare.Measure(b, skillchains, byJob);
        var d = Compare.Diff(ma, mb);
        var names = new Naming(d, ma, mb, byJob, hideNames);

        return new CompareSheet
        {
            A = ma, B = mb, ByJob = byJob,
            DamageTiles = TilesOfDamage(ma, mb, skillchains),
            HealTiles = TilesOfHealing(ma, mb),
            DamagePace = Compare.Pace(ma, mb),
            HealPace = HealingPace(ma, mb),
            Actors = d.Rows.Select(r => ActorOf(r, d)).ToList(),
            Kinds = KindsOf(d, ma, mb),
            Targets = TotalsOf(d.Targets, true, true, n => n),
            Party = HealerOf(null, ma, mb, Side.Of(ma, ma.Heal), Side.Of(mb, mb.Heal), HealMax(d), []),
            Healers = d.Heals.Select(r => HealerOf(r.Key, ma, mb, Side.Of(ma, r.A), Side.Of(mb, r.B), HealMax(d),
                                                   HealsOf(r, ma, mb))).ToList(),
            HealNote = NoteOf(ma, mb, d.Heals.Count > 0),
            HealSpells = TotalsOf(d.HealSpells, ma.HasHeals, mb.HasHeals, n => n),
            HealTargets = TotalsOf(d.HealTargets, ma.HasHeals, mb.HasHeals, names.Draw),
            Ids = names.Ids,
        };
    }

    // ----------------------------------------------------------------- tiles

    static CompareTile Tile(string label, double? a, double? b, Func<double, string> format, ChangeKind kind, int good,
                            string? tip = null, Func<Measurement, string>? sub = null, Measurement? ma = null,
                            Measurement? mb = null, bool hero = false) =>
        new(label, tip, a is double x ? format(x) : Dash, b is double y ? format(y) : Dash,
            sub != null ? sub(ma!) : null, sub != null ? sub(mb!) : null, Change.Of(a, b, kind, format, good), hero);

    static List<CompareTile> TilesOfDamage(Measurement ma, Measurement mb, bool skillchains) =>
    [
        Tile("Total damage", ma.Total, mb.Total, Format.Int, ChangeKind.Percent, 1, hero: true),
        // Neutral on purpose: a shorter run is not obviously a better one.
        Tile("Length", ma.Duration, mb.Duration, Clock, ChangeKind.Time, 0),
        Tile("Party DPS", ma.Dps, mb.Dps, v => Format.Num(v, 1), ChangeKind.Percent, 1),
        Tile("Accuracy", ma.Accuracy, mb.Accuracy, v => Percent(v), ChangeKind.Points, 1,
             "Party melee and ranged swings that connected, out of all attempted: the character table's Accuracy, for everyone"),
        Tile("WS damage", ma.WsTotal, mb.WsTotal, Format.Int, ChangeKind.Percent, 1,
             sub: m => Format.Int(m.WsCount) + " WS · avg " + Whole(m.WsAvg), ma: ma, mb: mb),
        Tile("Skillchain damage", ma.ScTotal, mb.ScTotal, Format.Int, ChangeKind.Percent, 1,
             sub: m => skillchains ? Format.Int(m.ScCount) + " chains" : "switched off", ma: ma, mb: mb),
    ];

    static List<CompareTile> TilesOfHealing(Measurement ma, Measurement mb)
    {
        static double? Hps(Measurement m) => m.HasHeals && m.Duration > 0 ? m.Heal.Total / m.Duration : null;
        static double? Of(Measurement m, Func<HealTotals, double?> pick) => m.HasHeals ? pick(m.Heal) : null;
        return
        [
            Tile("Total healing", Of(ma, h => h.Total), Of(mb, h => h.Total), Format.Int, ChangeKind.Percent, 1,
                 "Every cure, waltz and healing ability. Pet heals are not included", hero: true),
            Tile("Length", ma.Duration, mb.Duration, Clock, ChangeKind.Time, 0),
            Tile("Party HPS", Hps(ma), Hps(mb), v => Format.Num(v, 1), ChangeKind.Percent, 1),
            // Neutral: more casts is not better healing.
            Tile("Casts", Of(ma, h => h.Casts), Of(mb, h => h.Casts), Format.Int, ChangeKind.Percent, 0,
                 "One per cast, however many people it reached"),
            Tile("Avg / cast", Of(ma, h => h.Avg), Of(mb, h => h.Avg), Format.Int, ChangeKind.Percent, 1),
            Tile("Biggest heal", Of(ma, h => h.Max), Of(mb, h => h.Max), Format.Int, ChangeKind.Percent, 1,
                 "The most one target was healed for by one cast. Pet heals not included"),
        ];
    }

    // ------------------------------------------------------------- the chart

    /// <summary>A run with no heal lines draws no line, not one flat at zero.</summary>
    static Pace HealingPace(Measurement ma, Measurement mb)
    {
        var p = Compare.Pace(ma, mb, healing: true);
        if (!ma.HasHeals) Array.Fill(p.A, double.NaN);
        if (!mb.HasHeals) Array.Fill(p.B, double.NaN);
        return p;
    }

    /// <summary>The chart as a table: about sixteen of its times, and the last.</summary>
    public static List<PaceLine> Table(Pace p)
    {
        static string V(double x) => double.IsFinite(x) ? Format.Int(x) : Dash;
        return Sampling.Rows(p.Times.Length).Select(k =>
        {
            double a = p.A[k], b = p.B[k];
            var diff = double.IsFinite(a) && double.IsFinite(b) ? (b >= a ? "+" : "−") + Format.Int(Math.Abs(b - a)) : Dash;
            return new PaceLine(Format.Elapsed(p.Times[k]), V(a), V(b), diff);
        }).ToList();
    }

    // ---------------------------------------------------------------- damage

    static ActorLine ActorOf(Pair<ActorTotals> r, CompareRows d)
    {
        double max = 0;
        foreach (var x in d.Rows) max = Math.Max(max, Math.Max(x.A?.Total ?? 0, x.B?.Total ?? 0));
        var (a, b) = (r.A, r.B);
        return new ActorLine(r.Key, a != null, b != null,
            Bars(a?.Total, b?.Total, max),
            // Missing from one run, the total there is nothing: the row reads "new", or as all lost.
            Change.Of(a != null ? a.Total : 0, b != null ? b.Total : 0, ChangeKind.Percent, Format.Int, 1),
            new AB(Rate(a?.Dps), Rate(b?.Dps)),
            Change.Of(a?.Dps, b?.Dps, ChangeKind.Percent, v => Format.Num(v, 1), 1),
            new AB(Percent(a?.AutoAcc), Percent(b?.AutoAcc)),
            Change.Of(a?.AutoAcc, b?.AutoAcc, ChangeKind.Points, v => Percent(v), 1),
            new AB(Whole(a?.WsAvg), Whole(b?.WsAvg)),
            Change.Of(a?.WsAvg, b?.WsAvg, ChangeKind.Percent, Format.Int, 1),
            new AB(Percent(a?.Share), Percent(b?.Share)),
            Compare.Actions(r).Select(ActionOf).ToList());
    }

    static ActionLine ActionOf(Pair<ActionTotals> x)
    {
        // No accuracy for an action that cannot miss or land, and no average
        // for one that never hit: dashes, not 0% and 0.
        static double? Accuracy(ActionTotals? t) => t != null && t.Tries != 0 ? t.Accuracy : null;
        static double? Avg(ActionTotals? t) => t != null && t.Hits != 0 ? t.Avg : null;
        var (a, b) = (x.A, x.B);
        return new ActionLine(x.Key,
            new AB(Whole(a?.Swings), Whole(b?.Swings)),
            new AB(Percent(Accuracy(a)), Percent(Accuracy(b))),
            Change.Of(Accuracy(a), Accuracy(b), ChangeKind.Points, v => Percent(v), 1),
            new AB(Whole(Avg(a)), Whole(Avg(b))),
            Change.Of(Avg(a), Avg(b), ChangeKind.Percent, Format.Int, 1),
            new AB(Whole(a?.Max), Whole(b?.Max)),
            new AB(Whole(a?.Total), Whole(b?.Total)),
            Change.Of(a?.Total ?? 0, b?.Total ?? 0, ChangeKind.Percent, Format.Int, 1));
    }

    static List<KindLine> KindsOf(CompareRows d, Measurement ma, Measurement mb)
    {
        double max = 0;
        foreach (var k in d.Kinds) max = Math.Max(max, Math.Max(k.A, k.B));
        static double? Part(double v, double total) => total != 0 && !double.IsNaN(total) ? v / total : null;
        return d.Kinds.Select(k => new KindLine(k.Label, Bars(k.A, k.B, max),
            Change.Of(k.A, k.B, ChangeKind.Percent, Format.Int, 1),
            new AB(Percent(Part(k.A, ma.Total)), Percent(Part(k.B, mb.Total))))).ToList();
    }

    /// <param name="hasA">Whether run A could have this figure at all; without, its side is a dash.</param>
    static List<TotalLine> TotalsOf(List<PairTotal> rows, bool hasA, bool hasB, Func<string, string> draw)
    {
        double max = 0;
        foreach (var k in rows) max = Math.Max(max, Math.Max(k.A, k.B));
        return rows.Select(k =>
        {
            double? a = hasA ? k.A : null, b = hasB ? k.B : null;
            var name = draw(k.Key);
            return new TotalLine(name.Length > 0 ? name : Dash, Bars(a, b, max),
                                 Change.Of(a, b, ChangeKind.Percent, Format.Int, 1));
        }).ToList();
    }

    // --------------------------------------------------------------- healing

    /// <summary>One run's side of a healing row. Every figure is null when
    /// the run has no heal lines; in a run that has, a healer who is not
    /// there healed 0, though their average is still nothing.</summary>
    sealed record Side(double? Total, double? Casts, double? Avg, double? Max, double? Pet)
    {
        public static Side Of(Measurement m, HealTotals h) =>
            m.HasHeals ? new(h.Total, h.Casts, h.Avg, h.Max, h.PetTotal) : new(null, null, null, null, null);

        public static Side Of(Measurement m, HealerTotals? h) =>
            !m.HasHeals ? new(null, null, null, null, null)
            : h == null ? new(0, 0, null, 0, 0)
            : new(h.Total, h.Casts, h.Avg, h.Max, h.PetTotal);
    }

    static double HealMax(CompareRows d)
    {
        double max = 0;
        foreach (var r in d.Heals) max = Math.Max(max, Math.Max(r.A?.Total ?? 0, r.B?.Total ?? 0));
        return max;
    }

    static HealerLine HealerOf(string? key, Measurement ma, Measurement mb, Side a, Side b, double max,
                               IReadOnlyList<HealLine> heals) =>
        new(key, Bars(a.Total, b.Total, max),
            Change.Of(a.Total, b.Total, ChangeKind.Percent, Format.Int, 1),
            new AB(Whole(a.Casts), Whole(b.Casts)),
            new AB(Whole(a.Avg), Whole(b.Avg)),
            Change.Of(a.Avg, b.Avg, ChangeKind.Percent, Format.Int, 1),
            new AB(Whole(a.Max), Whole(b.Max)),
            new AB(Whole(a.Pet), Whole(b.Pet)),
            heals);

    static List<HealLine> HealsOf(Pair<HealerTotals> r, Measurement ma, Measurement mb)
    {
        static double? Of(Measurement m, HealAction? x, Func<HealAction, double> pick) =>
            !m.HasHeals ? null : x != null ? pick(x) : 0;
        static double? Avg(Measurement m, HealAction? x) => x != null && m.HasHeals ? x.Avg : null;
        return Compare.HealActions(r).Select(x => new HealLine(x.Key,
            new AB(Whole(Of(ma, x.A, h => h.Casts)), Whole(Of(mb, x.B, h => h.Casts))),
            new AB(Whole(Of(ma, x.A, h => h.Total)), Whole(Of(mb, x.B, h => h.Total))),
            Change.Of(Of(ma, x.A, h => h.Total), Of(mb, x.B, h => h.Total), ChangeKind.Percent, Format.Int, 1),
            new AB(Whole(Avg(ma, x.A)), Whole(Avg(mb, x.B))),
            Change.Of(Avg(ma, x.A), Avg(mb, x.B), ChangeKind.Percent, Format.Int, 1),
            new AB(Whole(Of(ma, x.A, h => h.Max)), Whole(Of(mb, x.B, h => h.Max))))).ToList();
    }

    static string NoteOf(Measurement ma, Measurement mb, bool anyRows)
    {
        const string since = " healing lines — healing is recorded by the VibeXI addon 0.3.0 and later";
        var note = (ma.HasHeals, mb.HasHeals) switch
        {
            (false, false) => "Neither parse has" + since + ".",
            (false, true) => "Parse A has no" + since + ", so its side reads as a dash.",
            (true, false) => "Parse B has no" + since + ", so its side reads as a dash.",
            _ => "",
        };
        return note.Length > 0 || anyRows ? note : "No healing in either run.";
    }

    // --------------------------------------------------------------- naming

    /// <summary>
    /// Who each row is, worked out once over the damage rows and then the
    /// healers who dealt none, so a healer who also dealt damage has the same
    /// colour and the same stand-in label in both tables.
    ///
    /// <para>By character, the job is read off whichever run the character
    /// was in, B first: the newer run is the one being judged. By job, the
    /// key is the job. A job is never borrowed from the other run: someone
    /// the party table did not report in one run was often on another job
    /// in it.</para>
    /// </summary>
    sealed class Naming
    {
        readonly Dictionary<string, Identity> ids = new(StringComparer.Ordinal);
        readonly Dictionary<string, int> used = new(StringComparer.Ordinal);
        readonly Dictionary<string, string> strangers = new(StringComparer.Ordinal);
        readonly Roster ra, rb;
        readonly bool hide;

        public IReadOnlyDictionary<string, Identity> Ids => ids;

        public Naming(CompareRows d, Measurement ma, Measurement mb, bool byJob, bool hideNames)
        {
            (ra, rb, hide) = (ma.Run.Source.Roster, mb.Run.Source.Roster, hideNames);

            var rows = d.Rows.Select(r => (r.Key, InA: r.A != null, InB: r.B != null)).ToList();
            var seen = rows.Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
            rows.AddRange(d.Heals.Where(r => !seen.Contains(r.Key)).Select(r => (r.Key, r.A != null, r.B != null)));

            for (int i = 0; i < rows.Count; i++)
            {
                var (key, inA, inB) = rows[i];
                if (byJob)
                {
                    var who = new List<string>();
                    foreach (var m in new[] { ma, mb })
                        if (m.Members.TryGetValue(key, out var mm))
                            foreach (var n in mm.Names) if (!who.Contains(n)) who.Add(n);
                    ids[key] = new Identity(key, key, "", "", key == Compare.UnknownJob ? "" : key, i,
                        hideNames ? who.Count.ToString(CultureInfo.InvariantCulture) + " character" + (who.Count == 1 ? "" : "s")
                                  : string.Join(", ", who),
                        false);
                    continue;
                }

                string jobA = inA ? ra.JobLabel(key) : "", jobB = inB ? rb.JobLabel(key) : "";
                var main = Main(inB ? rb.JobOf(key) : null) ?? Main(inA ? ra.JobOf(key) : null) ?? "";
                ids[key] = new Identity(key, hideNames ? Alias(jobB.Length > 0 ? jobB : jobA) : key, jobA, jobB, main, i, "",
                                        hideNames);
            }
        }

        static string? Main(JobInfo? j) => j != null && !string.IsNullOrEmpty(j.Main) && j.Main != "NON" ? j.Main : null;

        /// <summary>A job standing in for a name, numbered from the second
        /// character on it: "SAM/WAR", "SAM/WAR 2".</summary>
        string Alias(string job)
        {
            var label = job.Length > 0 ? job : Compare.UnknownJob;
            int k = used[label] = used.GetValueOrDefault(label) + 1;
            return k > 1 ? label + " " + k.ToString(CultureInfo.InvariantCulture) : label;
        }

        /// <summary>
        /// A name met outside the tables' own rows, as drawn: who a heal
        /// landed on. Someone with a row is drawn as their row is. While
        /// names are hidden everyone else is their job as well, numbered on
        /// from the rows: a name does not get through for having no row of
        /// its own, which every target has when the rows are jobs.
        /// </summary>
        public string Draw(string name)
        {
            if (ids.TryGetValue(name, out var id)) return id.Label;
            if (!hide || name.Length == 0) return name;
            if (!strangers.TryGetValue(name, out var alias))
            {
                var job = rb.JobLabel(name);
                strangers[name] = alias = Alias(job.Length > 0 ? job : ra.JobLabel(name));
            }
            return alias;
        }
    }

    // ----------------------------------------------------------------- slots

    /// <summary>When a run began, how long it measured, and how many players it saw.</summary>
    public static RunSummary Describe(ImportedParse run)
    {
        var sn = run.Session;
        double started = sn.StartedAt ?? 0;
        var day = DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Truncate(started)).ToLocalTime()
                                .ToString("yyyy'-'MM'-'dd", CultureInfo.InvariantCulture);
        int party = run.Source.Roster.Kinds.Count(k => k.Value == "player");
        return new RunSummary(
            day + " " + Format.Clock(started),
            Format.Stopwatch(sn.Elapsed(sn.PausedAt)),
            party.ToString(CultureInfo.InvariantCulture) + " character" + (party == 1 ? "" : "s"),
            run.Skipped > 0 ? Format.Int(run.Skipped) + " unreadable record" + (run.Skipped == 1 ? "" : "s") + " skipped" : "");
    }

    /// <summary>
    /// The session being measured, as the text of an exported parse, or null
    /// before its clock has started. A snapshot: the live session goes on
    /// counting and this copy does not. A running clock is frozen at
    /// <paramref name="now"/>, as a Pause would freeze it.
    ///
    /// <para>Through the export format on purpose. Read back with
    /// <see cref="ParseFile.Import"/>, it measures exactly as it would had it
    /// been saved to a file and opened.</para>
    /// </summary>
    public static string? Snapshot(EventReader reader, Session session, string? file, double now)
    {
        if (session.StartedAt == null) return null;
        var frozen = session.Clone();
        frozen.PausedAt ??= now;
        return ParseFile.Stringify(ParseFile.Export(reader, frozen, t => frozen.At(t) != null, file, now));
    }
}
