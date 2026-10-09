using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zerg.Charts;
using Zerg.Core;
using Zerg.Core.Charts;

namespace Zerg;

// The Damage section: five tiles and four cards, each drawn from one count.
public sealed partial class MainViewModel
{
    const char KeyGap = (char)1;

    // ----------------------------------------------------------------- tiles

    [ObservableProperty] private string totalText = "0";
    [ObservableProperty] private string totalNote = "";
    /// <summary>What the total is a total of: "Total damage", or with
    /// targets isolated "Damage to Kirin", "Damage to 3 targets"; with
    /// damage types, "Melee damage"; with both, "Melee damage to Kirin".</summary>
    [ObservableProperty] private string totalLabel = Targets.Heading([]);
    /// <summary>With anything isolated, every one of them by name, for the
    /// pointer: the label cuts a long name and counts several.</summary>
    [ObservableProperty] private string? totalTip;
    /// <summary>With anything isolated, what leads the note under the total:
    /// how much of all the damage this is ("48.1% of 227,587 · ").</summary>
    string totalShare = "";
    /// <summary>With targets isolated, why the party's DPS is a dash.</summary>
    [ObservableProperty] private string? dpsTip;
    [ObservableProperty] private string clockText = "00:00";
    [ObservableProperty] private string clockNote = None;
    /// <summary>The word in the tag beside the clock, which also colours the
    /// clock: the session's state, or Saved over a parse in the View section.</summary>
    [ObservableProperty] private SessionTag tag;
    [ObservableProperty] private string dpsText = "0.0";
    [ObservableProperty] private string dpsNote = None;
    [ObservableProperty] private string topName = None;
    [ObservableProperty] private string topNote = None;
    [ObservableProperty] private string? topTip;
    [ObservableProperty] private Brush topSwatch = Brushes.Transparent;
    [ObservableProperty] private bool hasTop;
    [ObservableProperty] private string bigText = "0";
    [ObservableProperty] private string bigNote = None;

    // The marks beside three of the figures. Drawn from a count, as the
    // figures' notes are, and never on the draw beat: between two events
    // they stand still, while the rate beside one of them falls.

    /// <summary>There is something for the marks to draw: the clock has
    /// started and damage has been dealt.</summary>
    [ObservableProperty] private bool hasMarks;
    /// <summary>Beside the total: the party's damage in each stretch of the session.</summary>
    [ObservableProperty] private IReadOnlyList<double> totalMark = [];
    /// <summary>How long a stretch is: "per 20 s", longer in a long session.</summary>
    [ObservableProperty] private string totalMarkCaption = BandMarks.Caption(BandMarks.ShortestBucket);
    /// <summary>Beside Party DPS: the party's DPS as it stood at the end of each stretch.</summary>
    [ObservableProperty] private IReadOnlyList<double> dpsMark = [];
    /// <summary>Beside Top DPS: every character's share, largest first, in their colour.</summary>
    [ObservableProperty] private IReadOnlyList<ShareSlice> topMark = [];

    /// <summary>
    /// What a mark is given: the list it already has when the new one says
    /// the same, so a count that changed nothing in it (a toggle, a section
    /// switched) does not redraw it.
    /// </summary>
    static IReadOnlyList<T> Kept<T>(IReadOnlyList<T> had, IReadOnlyList<T>? now) =>
        now is null ? (had.Count == 0 ? had : []) : had.SequenceEqual(now) ? had : now;

    /// <summary>
    /// The Target and Type buttons' lists, and what isolating changes in the
    /// band of figures: the total's label and what it is a share of, and,
    /// with a target isolated, the reason the rate beside it is a dash.
    /// </summary>
    void DrawTargets(Snapshot c)
    {
        TargetFilter.Draw(c.Targets, c.AllTargets, NameOf);
        TypeFilter.Draw(c.Types, c.AllTypes, n => n);
        bool isolated = c.Isolated.Count > 0, any = isolated || c.IsolatedTypes.Count > 0;
        TotalLabel = DamageTypes.Heading(c.IsolatedTypes, c.Isolated);
        TotalTip = DamageTypes.Told(c.IsolatedTypes, c.Isolated.Select(NameOf).ToList());
        totalShare = any && c.Whole > 0
            ? Format.Num(c.Totals.Total / c.Whole * 100, 1) + "% of " + Format.Int(c.Whole) + Dot
            : "";
        DpsTip = isolated ? "No DPS while a target is isolated: the clock is the session’s, not the time that target was fought"
            : null;
    }

    void DrawTiles(Snapshot c)
    {
        var agg = c.Totals;
        int n = agg.Actors.Count;
        bool isolated = c.Isolated.Count > 0;
        TotalText = Format.Int(agg.Total);
        DpsNote = isolated ? "no rate for a target" : n > 0 ? n + " character" + (n == 1 ? "" : "s") : None;

        // Nothing before the clock starts. The rows are the ones the
        // cumulative chart is drawn from, so the bars add up to its lines.
        var pulse = Shown.Session.StartedAt != null ? BandMarks.Pulse(c.Events, c.Elapsed) : null;
        HasMarks = pulse != null;
        TotalMark = Kept(TotalMark, pulse?.Bars);
        if (pulse != null) TotalMarkCaption = BandMarks.Caption(pulse.Bucket);
        // No running rate either while a target is isolated: the mark goes.
        DpsMark = Kept(DpsMark, isolated ? null : pulse?.Running);
        TopMark = Kept(TopMark, pulse is null ? null
            : agg.Actors.Where(a => a.Total > 0).Select(a => new ShareSlice(a.Total, SwatchOf(a.Name))).ToList());

        // The count lists characters largest first, so the leader is the first.
        // The dot is the hue that character has on every chart.
        HasTop = n > 0;
        if (n > 0)
        {
            var top = agg.Actors[0];
            // Not beside a name that is the job: it would read "SAM/WAR · SAM/WAR".
            var job = JobBadge(top.Name);
            TopSwatch = SwatchOf(top.Name);
            TopName = NameOf(top.Name);
            TopTip = Roster.JobTitle(top.Name) is { Length: > 0 } title ? title : NameOf(top.Name);
            TopNote = (job.Length > 0 ? job + Dot : "") + Format.Num(top.Share * 100, 1) + "% of " +
                      Format.Int(agg.Total) + " damage";
        }
        else
        {
            TopSwatch = Brushes.Transparent;
            TopName = TopNote = None;
            TopTip = null;
        }

        var best = c.Best;
        BigText = best != null ? Format.Int(best.Max) : "0";
        BigNote = best != null ? NameOf(best.Who) + Dot + best.What : None;
    }

    // ----------------------------------------------------- cumulative damage

    [ObservableProperty] private LineModel? line;
    /// <summary>The same lines in a floating panel's colours. The very same
    /// model while the theme is dark, which is when the two agree.</summary>
    [ObservableProperty] private LineModel? panelLine;
    /// <summary>The session clock, for the chart's live edge.</summary>
    [ObservableProperty] private double edge = double.NaN;
    /// <summary>What an empty chart says: why there is nothing to draw.</summary>
    [ObservableProperty] private string emptyText = "";

    /// <summary>Who the line that stands for several is made of just now, as
    /// their names are drawn; null while there is no such line. The chart
    /// names the line "3 others" and cannot say who: the switch's tooltip
    /// does (<see cref="GroupTip"/>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GroupTip))]
    private string? groupMembers;

    /// <summary>One line as it is drawn, with the real name it is keyed by
    /// (null for the line that stands for several).</summary>
    sealed record Drawn(string? Real, LineSeries Series);

    void DrawLine(Snapshot c)
    {
        var agg = c.Totals;
        EmptyText = SessionText.Empty(Shown.Session, Viewing);

        // One line per character, and none for anyone at zero: a resisted
        // debuff makes an actor, not a line along the floor.
        var names = agg.Actors.Where(a => a.Total > 0).Select(a => a.Name).ToList();

        // The left edge is the session's zero and the right edge its clock,
        // running or held, so the axis covers the span DPS is divided by.
        double? now = Shown.Session.StartedAt != null ? c.Elapsed : null;
        var cum = Counting.Cumulative(c.Events, names, 0, now);

        var lines = cum.Series.Select(s => new Drawn(s.Name, new LineSeries(NameOf(s.Name), s.Values, ColorOf(s.Name)))).ToList();
        string? members = null;
        if (GroupSmallLines && SmallLines.Pick(agg, Roster.Owner) is { Count: > 0 } small)
        {
            var parts = lines.Where(l => small.Contains(l.Real!)).ToList();
            var sum = new double[cum.Times.Length];
            foreach (var p in parts)
                for (int i = 0; i < sum.Length; i++) sum[i] += p.Series.Values[i];
            lines.RemoveAll(parts.Contains);
            lines.Add(new Drawn(null, new LineSeries(SmallLines.Name(parts.Count), sum, default, Group: true)));
            // Largest first, as the table lists them.
            members = string.Join(", ", parts.OrderByDescending(p => p.Series.Values.Length > 0 ? p.Series.Values[^1] : 0)
                                             .Select(p => p.Series.Name));
        }
        GroupMembers = members;
        // Largest total last, so the leading line is drawn on top of the pack.
        lines = lines.OrderBy(l => l.Series.Values.Length > 0 ? l.Series.Values[^1] : 0).ToList();
        // Counting, and the edge is the clock: the chart rules its edge to
        // say so. Every change of the session's state is followed by a count.
        Line = new LineModel(cum.Times, lines.Select(l => l.Series).ToList(), cum.Live == true,
                             Running: cum.Live == true && Shown.Session.Running);
        PanelLine = AppTheme.IsDark ? Line : Line with
        {
            Series = lines.Select(l => l.Real != null ? l.Series with { Color = PanelColorOf(l.Real) } : l.Series).ToList(),
        };
    }

    // --------------------------------------------------- damage by character

    public ObservableCollection<ActorRow> Actors { get; } = [];
    [ObservableProperty] private bool hasActors;
    /// <summary>The line that closes the table: the party as a whole.</summary>
    public PartyRow Party { get; } = new();
    /// <summary>
    /// The Job column is dropped while names are hidden, not printed twice or
    /// blanked: every character but the owner already has their job where
    /// their name was, and a dash there would say "job unknown".
    /// </summary>
    [ObservableProperty] private bool showJob = true;

    /// <summary>The card as it floats: one short row per character.</summary>
    public ObservableCollection<StripRow> Strip { get; } = [];

    void DrawBars(Snapshot c)
    {
        // Nobody at zero. A character reaches the count by acting, not by
        // dealing damage (a bard whose only song was resisted is there with
        // 0), and a row of zeroes and dashes is nothing but height. Only this
        // card drops them: they keep their chip, so they can still be
        // excluded, and the actions table still shows what they did.
        var actors = c.Totals.Actors.Where(a => a.Total > 0).ToList();

        // The row is the bar: each is shaded to its damage out of the
        // leader's, not out of the party's. With ten to eighteen characters
        // nobody is over a fifth of the party, and bars to that scale would
        // all end inside the first two columns; the Damage % beside it
        // carries the share of the whole.
        double top = RowMarks.Largest(actors.Select(a => a.Total));

        // What the pointer resting on a row is told: who, their job in
        // full, and the one figure the table has no column for.
        string Tip(ActorTotals a)
        {
            var full = Aliased(a.Name) ? "" : Roster.JobTitle(a.Name);
            return NameOf(a.Name) + (full.Length > 0 ? Dot + full : "") + Dot + Format.Int(a.Avg) + " avg per action";
        }

        ShowJob = !HideNames;
        HasActors = actors.Count > 0;
        Rows.Sync(Actors, actors, a => a.Name, a => new ActorRow(a.Name), (row, a) =>
        {
            row.Total = a.Total;
            row.Shade = ShadeOf(a.Name);
            row.Fraction = RowMarks.Fraction(a.Total, top);
            row.IsOwner = a.Name == Roster.Owner;
            row.Tip = Tip(a);
            row.AccuracyRate = a.AutoAcc;
            row.WsAccuracyRate = a.WsAcc;
            row.PetAccuracyRate = a.PetAcc;
            row.Name = NameOf(a.Name);
            row.Swatch = SwatchOf(a.Name);
            row.Job = Roster.JobLabel(a.Name) is { Length: > 0 } job ? job : None;
            row.JobTip = Roster.JobTitle(a.Name);
            row.ShowJob = ShowJob;
            row.Damage = Format.Int(a.Total);
            row.Share = Percent(a.Share);
            row.Dps = Rate(a.Total, c.Elapsed / 1000);
            row.Accuracy = Percent(a.AutoAcc);
            row.WsDamage = Whole(a.WsTotal);
            row.WsAvg = Whole(a.WsAvg);
            row.WsShare = Percent(a.WsShare);
            row.WsAccuracy = Percent(a.WsAcc);
            row.ScDamage = Whole(a.ScTotal);
            row.ScShare = Percent(a.ScShare);
            row.PetDamage = Whole(a.PetTotal);
            row.PetAccuracy = Percent(a.PetAcc);
        });

        // The Party line, under the columns it sums. The split columns are
        // the party's own (Aggregate.Party): everyone's swings together,
        // the characters at zero included, not an average of the rows. The
        // number under Job is how many rows stand above it. Its DPS is the
        // party's, written by the draw beat with every row's.
        var party = c.Totals.Party;
        Party.Count = Format.Int(actors.Count);
        Party.Damage = Format.Int(c.Totals.Total);
        Party.Share = c.Totals.Total > 0 ? Percent(1) : None;
        Party.Accuracy = Percent(party.AutoAcc);
        Party.WsDamage = Whole(party.WsTotal);
        Party.WsAvg = Whole(party.WsAvg);
        Party.WsShare = Percent(party.WsShare);
        Party.WsAccuracy = Percent(party.WsAcc);
        Party.ScDamage = Whole(party.ScTotal);
        Party.ScShare = Percent(party.ScShare);
        Party.PetDamage = Whole(party.PetTotal);
        Party.PetAccuracy = Percent(party.PetAcc);

        // Floating, the card is a strip: the row shaded to the share as in
        // the table, at the strength a panel allows the colour, and the
        // three figures a glance during a fight is for. The other eleven
        // columns, DPS among them, stay on the table, so nothing in the strip
        // moves with the clock: every figure in it is a running total that
        // only a new event can change.
        Rows.Sync(Strip, actors, a => a.Name, a => new StripRow(a.Name), (row, a) =>
        {
            row.Name = NameOf(a.Name);
            row.Job = JobBadge(a.Name);
            row.Fill = PanelShadeOf(a.Name);
            row.Edge = Solid(PanelColorOf(a.Name));
            row.Fraction = RowMarks.Fraction(a.Total, top);
            row.Damage = Format.Int(a.Total);
            row.Share = Format.Num(a.Share * 100, 1) + "%";
            row.Accuracy = Percent(a.AutoAcc);
            row.IsOwner = a.Name == Roster.Owner;
            row.Tip = Tip(a);
        });
    }

    // A figure with nothing to measure is a dash, never zero: a mage with no
    // weaponskills has no weaponskill accuracy, and 0% would say every one missed.
    static string Percent(double? x) => x is double v ? Format.Num(v * 100, 1) + "%" : None;
    static string Whole(double? x) => x is double v ? Format.Int(v) : None;

    // --------------------------------------------------------------- actions

    public ObservableCollection<ActionRow> Actions { get; } = [];
    [ObservableProperty] private bool hasActions;
    /// <summary>The characters whose actions are showing under their heading,
    /// by real name. Everyone starts as a heading alone: an alliance is
    /// eighteen characters, and a few hundred lines with all of them open.</summary>
    readonly HashSet<string> openActions = new(StringComparer.Ordinal);

    /// <summary>Selecting a character shows their actions under the heading;
    /// selecting them again puts the actions away.</summary>
    [RelayCommand]
    void ToggleActions(ActionRow row)
    {
        if (!row.IsHeading) return;
        if (!openActions.Remove(row.Actor)) openActions.Add(row.Actor);
        // Nothing is counted again: the last count, with more or fewer of its lines.
        if (snapshot is { } c) DrawActions(c);
    }

    void DrawActions(Snapshot c)
    {
        // One scale, shaded or not: a row's share is drawn to its value
        // over the largest of its siblings. A heading's is the character's
        // damage out of the leader's, as in the table above; an action's is
        // its total out of that character's largest action (Most).
        var items = new List<(ActorTotals Actor, ActionTotals? Action, double Most)>();
        double top = RowMarks.Largest(c.Totals.Actors.Select(a => a.Total));
        foreach (var a in c.Totals.Actors)
        {
            items.Add((a, null, top));
            if (!openActions.Contains(a.Name)) continue;
            double most = RowMarks.Largest(a.ActionList.Select(x => x.Total));
            foreach (var act in a.ActionList) items.Add((a, act, most));
        }
        HasActions = items.Count > 0;

        static string Key((ActorTotals Actor, ActionTotals? Action, double Most) i) =>
            i.Action == null ? i.Actor.Name : i.Actor.Name + KeyGap + i.Action.Name;

        Rows.Sync(Actions, items, Key, i => new ActionRow(Key(i), i.Actor.Name, i.Action?.Name), (row, i) =>
        {
            var (a, act, most) = i;
            if (act == null)
            {
                row.Name = NameOf(a.Name);
                row.Swatch = SwatchOf(a.Name);
                row.PanelSwatch = Solid(PanelColorOf(a.Name));
                row.Shade = HeadingShadeOf(a.Name);
                row.PanelShade = PanelHeadingShadeOf(a.Name);
                row.Fraction = RowMarks.Fraction(a.Total, most);
                row.Job = JobBadge(a.Name);
                row.Total = Format.Int(a.Total);
                row.Share = Format.Num(a.Share * 100, 1) + "%";
                row.Open = openActions.Contains(a.Name);
                row.Label = "Actions of " + NameOf(a.Name);
                return;
            }
            row.Name = act.Name;
            row.Hits = Format.Int(act.Hits);
            row.Misses = act.Misses != 0 ? Format.Int(act.Misses) : None;
            row.Total = Format.Int(act.Total);
            row.Avg = Format.Int(act.Avg);
            row.Min = Format.Int(act.Min);
            row.Max = Format.Int(act.Max);
            row.Share = Format.Num(a.Total != 0 ? act.Total / a.Total * 100 : 0, 1) + "%";
            row.ShareFraction = RowMarks.Fraction(act.Total, most);
            // Least to greatest with the average between, on a line as long
            // as the character's biggest hit of any action.
            (row.SpreadMin, row.SpreadAvg, row.SpreadMax) = RowMarks.Spread(act.Min, act.Avg, act.Max, a.Max);
            row.Selected = drill is { } d && d.Actor == a.Name && d.Action == act.Name;
            row.Label = NameOf(a.Name) + ", " + act.Name;
        });
    }

    // ------------------------------------------------------------ drill-down

    /// <summary>The action the drill-down is open on, by the character's real name.</summary>
    (string Actor, string Action)? drill;

    /// <summary>The drill-down just opened: the window brings it into view.</summary>
    public event Action? DrillOpened;

    [ObservableProperty] private bool drillOpen;
    [ObservableProperty] private string drillTitle = "";
    [ObservableProperty] private string drillNote = "";
    /// <summary>Whose action it is, as a swatch before the note.</summary>
    [ObservableProperty] private Brush? drillSwatch;
    [ObservableProperty] private IReadOnlyList<StatTile> drillTiles = [];
    [ObservableProperty] private HistogramModel? histogram;
    [ObservableProperty] private Color histogramFill = Colors.Gray;
    /// <summary>The same, in a floating panel's colours.</summary>
    [ObservableProperty] private Color panelHistogramFill = Colors.Gray;
    [ObservableProperty] private string drillCaption = "";
    [ObservableProperty] private IReadOnlyList<HitRow> drillHits = [];

    /// <summary>Selecting an action opens its drill-down; selecting it again closes it.</summary>
    [RelayCommand]
    void Drill(ActionRow row)
    {
        if (row.Action == null) return;
        bool same = drill is { } d && d.Actor == row.Actor && d.Action == row.Action;
        drill = same ? null : (row.Actor, row.Action);
        Recount();
        if (drill != null)
        {
            OpenDrillPane(Zerg.Core.Layout.PaneLayouts.DamageSection, Zerg.Core.Layout.PaneLayouts.Drill);
            DrillOpened?.Invoke();
        }
    }

    [RelayCommand]
    void CloseDrill()
    {
        drill = null;
        Recount();
    }

    void DrawDrill(Snapshot c)
    {
        if (drill is not { } on)
        {
            DrillOpen = false;
            return;
        }

        // Folded per use, like every count: an area attack is one figure, what
        // the action hit for, not a point per target.
        var d = Counting.Distribution(c.Events, on.Actor, on.Action);
        DrillOpen = true;
        DrillTitle = on.Action;
        DrillSwatch = SwatchOf(on.Actor);
        DrillNote = NameOf(on.Actor) + Dot + Format.Int(d.Count) + " hit" + (d.Count == 1 ? "" : "s") +
                    (d.Misses != 0 ? Dot + d.Misses.ToString(CultureInfo.InvariantCulture) + " miss" + (d.Misses == 1 ? "" : "es") : "") +
                    Dot + Format.Int(d.Total) + " total damage";

        if (d.Count == 0)
        {
            DrillTiles = [new StatTile("No hits in range", "")];
            Histogram = null;
            DrillCaption = "";
            DrillHits = [];
            return;
        }

        DrillTiles =
        [
            new("Min", Format.Int(d.Min)),
            new("Average", Format.Int(d.Avg)),
            new("Max", Format.Int(d.Max)),
            new("Median", Format.Int(d.Median)),
            new("Std dev", Format.Int(d.Stdev)),
            // The same question the per-character table's Accuracy asks, so
            // drilling into "Attack" agrees with that cell.
            new("Accuracy", Format.Num(d.Accuracy * 100, 0) + "%"),
        ];

        Histogram = HistogramModel.From(d);
        HistogramFill = ColorOf(on.Actor);
        PanelHistogramFill = PanelColorOf(on.Actor);
        DrillCaption = "Damage distribution" + Dot + d.Bins.Count.ToString(CultureInfo.InvariantCulture) + " bins" + Dot +
                       "IQR " + Format.Int(d.Q1) + "–" + Format.Int(d.Q3) + Dot +
                       "90th percentile " + Format.Int(d.P90) +
                       (d.Crits != 0 ? Dot + Format.Num(d.CritRate * 100, 0) + "% crit" : "") +
                       (d.Bursts != 0 ? Dot + d.Bursts.ToString(CultureInfo.InvariantCulture) + " magic burst" : "");

        // Newest first. Each with how far it fell from the average, out of
        // the furthest any of them fell: the length of the small bar beside it.
        double furthest = RowMarks.Furthest(d.Events.Select(e => e.Dmg), d.Avg);
        var hits = new List<HitRow>(d.Events.Count);
        for (int i = d.Events.Count - 1; i >= 0; i--)
        {
            var e = d.Events[i];
            var delta = e.Dmg - d.Avg;
            hits.Add(new HitRow(Format.Elapsed(e.T),
                                e.Target.Length > 0 ? NameOf(e.Target) : None,
                                Format.Int(e.Dmg),
                                (delta >= 0 ? "+" : "−") + Format.Int(Math.Abs(delta)),
                                RowMarks.Offset(e.Dmg, d.Avg, furthest), e.Crit));
        }
        DrillHits = hits;
    }
}
