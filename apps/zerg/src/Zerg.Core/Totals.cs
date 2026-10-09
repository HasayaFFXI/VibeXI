namespace Zerg.Core;

/// <summary>
/// Totals over a set of uses: one character's, or one action's inside a
/// character. Counts are per use, not per damage row (<see cref="Counting.Collapse"/>).
/// </summary>
public class Bucket
{
    public string Name { get; set; } = "";
    /// <summary>Damage from the uses that hit.</summary>
    public double Total { get; set; }
    public int Hits { get; set; }
    public int Misses { get; set; }
    public int Crits { get; set; }
    public int Bursts { get; set; }
    /// <summary>Accuracy's denominator and numerator (<see cref="Counting.Connects"/>),
    /// which are a different question from <see cref="Hits"/>.</summary>
    public int Tries { get; set; }
    public int Lands { get; set; }
    /// <summary>The smallest hit, or 0 with none.</summary>
    public double Min { get; set; } = double.PositiveInfinity;
    public double Max { get; set; }
    /// <summary>The first and last use's time, or null with none.</summary>
    public double? First { get; set; }
    public double? Last { get; set; }
    /// <summary>Every hit's damage, in order.</summary>
    public List<double> Values { get; set; } = [];

    public int Swings { get; set; }
    public double Avg { get; set; }
    public double AvgPerSwing { get; set; }
    /// <summary>Lands / tries, or 0 with no tries.</summary>
    public double Accuracy { get; set; }

    internal double FirstRaw = double.PositiveInfinity, LastRaw = double.NegativeInfinity;

    internal void Add(CombatEvent e)
    {
        if (e.Hit)
        {
            Hits++;
            Total += e.Dmg;
            Values.Add(e.Dmg);
            if (e.Dmg < Min) Min = e.Dmg;
            if (e.Dmg > Max) Max = e.Dmg;
            if (e.Crit) Crits++;
            if (e.Burst) Bursts++;
        }
        else Misses++;
        // Counted apart from Hits: those feed the total, the average and the
        // histogram, and a swing into shadows is not a hit there.
        if (Counting.Connects(e) is bool o) { Tries++; if (o) Lands++; }
        if (e.T < FirstRaw) FirstRaw = e.T;
        if (e.T > LastRaw) LastRaw = e.T;
    }

    internal void Finish()
    {
        Swings = Hits + Misses;
        Avg = Hits != 0 ? Total / Hits : 0;
        AvgPerSwing = Swings != 0 ? Total / Swings : 0;
        Accuracy = Tries != 0 ? (double)Lands / Tries : 0;
        if (double.IsPositiveInfinity(Min)) Min = 0;
        if (double.IsPositiveInfinity(FirstRaw)) { First = null; Last = null; }
        else { First = FirstRaw; Last = LastRaw; }
    }
}

/// <summary>One action inside a character.</summary>
public sealed class ActionTotals : Bucket
{
    /// <summary>The row kind of the action's first use.</summary>
    public string Kind { get; set; } = "";
}

/// <summary>
/// The raw counts behind the per-character table's split columns. Taken from
/// folded uses.
/// <list type="bullet">
/// <item>auto: the character's own melee and ranged swings, together.</item>
/// <item>ws: their own weaponskills (damage, uses, uses that dealt damage).
/// Skillchains are rows of their own and are not in it.</item>
/// <item>sc: the chains this character closed; none at all while skillchains
/// are switched off, because <see cref="Counting.Filter"/> has dropped them.</item>
/// <item>pet: every row a pet produced. Damage is all of it, abilities and
/// reactions included; accuracy is the pet's melee only.</item>
/// </list>
/// A pet's rows are told apart by <c>Owner</c>, which crediting leaves on the
/// copy. They are the owner's damage, never the owner's swings.
/// </summary>
public sealed class Split
{
    public int AutoTries { get; set; }
    public int AutoHits { get; set; }
    public double WsTotal { get; set; }
    public int WsTries { get; set; }
    public int WsHits { get; set; }
    public double ScTotal { get; set; }
    public int ScRows { get; set; }
    public double PetTotal { get; set; }
    public int PetRows { get; set; }
    public int PetTries { get; set; }
    public int PetHits { get; set; }

    internal void Add(CombatEvent e)
    {
        var dmg = e.Hit ? e.Dmg : 0;
        var o = Counting.Connects(e);
        if (e.IsPets)
        {
            PetRows++;
            PetTotal += dmg;
            if (e.Kind == "melee" && o is bool p) { PetTries++; if (p) PetHits++; }
        }
        else if (e.Kind is "melee" or "ranged")
        {
            if (o is bool a) { AutoTries++; if (a) AutoHits++; }
        }
        else if (e.Kind == "ws")
        {
            WsTries++;
            WsTotal += dmg;
            if (o == true) WsHits++;
        }
        else if (e.Kind == "skillchain")
        {
            // Credited to whoever closed the chain, who is the row's actor.
            ScRows++;
            ScTotal += dmg;
        }
    }

    /// <summary>Adds another's counts to these: a party's split is the sum
    /// of its characters'.</summary>
    internal void Add(Split other)
    {
        AutoTries += other.AutoTries;
        AutoHits += other.AutoHits;
        WsTotal += other.WsTotal;
        WsTries += other.WsTries;
        WsHits += other.WsHits;
        ScTotal += other.ScTotal;
        ScRows += other.ScRows;
        PetTotal += other.PetTotal;
        PetRows += other.PetRows;
        PetTries += other.PetTries;
        PetHits += other.PetHits;
    }

    /// <summary>
    /// The split columns these counts print as, for whoever dealt
    /// <paramref name="total"/> in all: one character, or the party. The one
    /// place they are worked out, so the Party line under the table is
    /// counted exactly as the rows above it are.
    /// </summary>
    internal SplitFigures Figures(double total)
    {
        bool whole = total != 0 && !double.IsNaN(total);
        return new SplitFigures(
            AutoAcc: AutoTries != 0 ? (double)AutoHits / AutoTries : null,
            WsTotal: WsTries != 0 ? WsTotal : null,
            WsAvg: WsHits != 0 ? WsTotal / WsHits : null,
            WsShare: WsTries != 0 && whole ? WsTotal / total : null,
            WsAcc: WsTries != 0 ? (double)WsHits / WsTries : null,
            ScTotal: ScRows != 0 ? ScTotal : null,
            ScShare: ScRows != 0 && whole ? ScTotal / total : null,
            PetTotal: PetRows != 0 ? PetTotal : null,
            PetAcc: PetTries != 0 ? (double)PetHits / PetTries : null);
    }
}

/// <summary>
/// The per-character table's split columns, worked out from a
/// <see cref="Split"/>. A column is null, not zero, when there is nothing to
/// measure, and is drawn as a dash.
/// </summary>
public readonly record struct SplitFigures(double? AutoAcc, double? WsTotal, double? WsAvg, double? WsShare, double? WsAcc,
                                           double? ScTotal, double? ScShare, double? PetTotal, double? PetAcc);

/// <summary>
/// One character: totals, the per-action breakdown, and the per-character
/// table's columns. A column is null, not zero, when there is nothing to
/// measure: a mage who never weaponskilled has no WS accuracy, and 0% would say
/// they missed every one. The table draws null as a dash.
/// </summary>
public sealed class ActorTotals : Bucket
{
    /// <summary>Actions by name, in order of first use.</summary>
    public OrderedDictionary<string, ActionTotals> Actions { get; set; } = new(StringComparer.Ordinal);
    public List<string> ActionOrder { get; set; } = [];
    /// <summary>The actions, largest total first.</summary>
    public List<ActionTotals> ActionList { get; set; } = [];
    public Split Split { get; set; } = new();

    /// <summary>First to last use, in seconds: shown as a span, not divided by.</summary>
    public double Window { get; set; }
    /// <summary>What DPS is divided by, in seconds: the session clock when one was given.</summary>
    public double Duration { get; set; }
    public double Dps { get; set; }
    /// <summary>This character's part of the party's damage, 0..1.</summary>
    public double Share { get; set; }

    public double? AutoAcc { get; set; }
    public double? WsTotal { get; set; }
    /// <summary>WS damage over the weaponskills that dealt damage.</summary>
    public double? WsAvg { get; set; }
    public double? WsShare { get; set; }
    public double? WsAcc { get; set; }
    public double? ScTotal { get; set; }
    public double? ScShare { get; set; }
    public double? PetTotal { get; set; }
    public double? PetAcc { get; set; }

    internal void FinishSplit()
    {
        var f = Split.Figures(Total);
        AutoAcc = f.AutoAcc;
        WsTotal = f.WsTotal;
        WsAvg = f.WsAvg;
        WsShare = f.WsShare;
        WsAcc = f.WsAcc;
        ScTotal = f.ScTotal;
        ScShare = f.ScShare;
        PetTotal = f.PetTotal;
        PetAcc = f.PetAcc;
    }
}

/// <summary>The party: every character, largest first, and the span they cover.</summary>
public sealed class Aggregate
{
    public List<ActorTotals> Actors { get; set; } = [];
    public double Total { get; set; }
    /// <summary>The first and last use's time, or null with none.</summary>
    public double? Start { get; set; }
    public double? End { get; set; }
    /// <summary>First to last use, in seconds.</summary>
    public double Window { get; set; }
    /// <summary>The DPS denominator, in seconds.</summary>
    public double Duration { get; set; }
    /// <summary>Damage rows in.</summary>
    public int Events { get; set; }
    /// <summary>The uses they folded to.</summary>
    public int Uses { get; set; }

    /// <summary>The party's own split: every character's counts added up,
    /// the ones at zero included.</summary>
    public Split Split { get; set; } = new();
    /// <summary>
    /// The split columns for the party as a whole, which the Party line
    /// under the per-character table prints: worked out from the summed
    /// counts as each character's are from their own, so the party's
    /// Accuracy is over everyone's swings together and not an average of
    /// the rows' percentages. Null, a dash, where nobody had anything to
    /// measure.
    /// </summary>
    public SplitFigures Party { get; set; }
}

/// <summary>One line of a cumulative chart: a running total per grid time.</summary>
public sealed class Series
{
    public string Name { get; set; } = "";
    public double[] Values { get; set; } = [];
}

/// <summary>Running totals per character on one shared time grid.</summary>
public sealed class Cumulative
{
    public double[] Times { get; set; } = [];
    public List<Series> Series { get; set; } = [];
    /// <summary>The grid step in milliseconds; 0 when there is nothing to draw.</summary>
    public double Step { get; set; }
    public double? T0 { get; set; }
    public double? T1 { get; set; }
    /// <summary>The last sample is the live edge, not an event.</summary>
    public bool? Live { get; set; }
    /// <summary>The newest event, whatever the edge says.</summary>
    public double? TEvent { get; set; }
}

/// <summary>A histogram bin, [Lo, Hi).</summary>
public sealed record Bin(double Lo, double Hi)
{
    public int Count { get; set; }
}

/// <summary>Every hit of one character's action (or of all their actions),
/// with summary figures and a histogram.</summary>
public sealed class Distribution
{
    public string Actor { get; set; } = "";
    /// <summary>The action, or null for all of them.</summary>
    public string? Action { get; set; }
    /// <summary>The uses that hit, folded.</summary>
    public List<CombatEvent> Events { get; set; } = [];
    public List<double> Values { get; set; } = [];
    public List<double> Sorted { get; set; } = [];
    public List<Bin> Bins { get; set; } = [];
    public int Count { get; set; }
    public int Misses { get; set; }
    public int Swings { get; set; }
    public int Tries { get; set; }
    public double Accuracy { get; set; }
    public int Crits { get; set; }
    public double CritRate { get; set; }
    public int Bursts { get; set; }
    public double Total { get; set; }
    public double Min { get; set; }
    public double Max { get; set; }
    public double Avg { get; set; }
    public double Median { get; set; }
    public double Q1 { get; set; }
    public double Q3 { get; set; }
    public double P90 { get; set; }
    public double Stdev { get; set; }
}
