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
    const string OthersKey = "*others";

    // ----------------------------------------------------------------- tiles

    [ObservableProperty] private string totalText = "0";
    [ObservableProperty] private string totalNote = "";
    [ObservableProperty] private string clockText = "00:00";
    [ObservableProperty] private string clockNote = None;
    /// <summary>The session's state, for the colour of the clock.</summary>
    [ObservableProperty] private SessionLight light;
    [ObservableProperty] private string dpsText = "0.0";
    [ObservableProperty] private string dpsNote = None;
    [ObservableProperty] private string topName = None;
    [ObservableProperty] private string topNote = None;
    [ObservableProperty] private string? topTip;
    [ObservableProperty] private Brush topSwatch = Brushes.Transparent;
    [ObservableProperty] private bool hasTop;
    [ObservableProperty] private string bigText = "0";
    [ObservableProperty] private string bigNote = None;

    void DrawTiles(Snapshot c)
    {
        var agg = c.Totals;
        int n = agg.Actors.Count;
        TotalText = Format.Int(agg.Total);
        DpsNote = n > 0 ? n + " character" + (n == 1 ? "" : "s") : None;

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
    public ObservableCollection<LegendRow> Legend { get; } = [];

    /// <summary>One line as it is drawn, with the real name it is keyed by
    /// (null for the line that stands for several) and who is in it.</summary>
    sealed record Drawn(string? Real, LineSeries Series, string? Members = null);

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
        if (GroupSmallLines && SmallLines.Pick(agg, Roster.Owner) is { Count: > 0 } small)
        {
            var parts = lines.Where(l => small.Contains(l.Real!)).ToList();
            var sum = new double[cum.Times.Length];
            foreach (var p in parts)
                for (int i = 0; i < sum.Length; i++) sum[i] += p.Series.Values[i];
            lines.RemoveAll(parts.Contains);
            lines.Add(new Drawn(null, new LineSeries(SmallLines.Name(parts.Count), sum, default, Group: true),
                                string.Join(", ", parts.Select(p => p.Series.Name))));
        }
        // Largest total last, so the leading line is drawn on top of the pack.
        lines = lines.OrderBy(l => l.Series.Values.Length > 0 ? l.Series.Values[^1] : 0).ToList();
        Line = new LineModel(cum.Times, lines.Select(l => l.Series).ToList(), cum.Live == true);
        PanelLine = AppTheme.IsDark ? Line : Line with
        {
            Series = lines.Select(l => l.Real != null ? l.Series with { Color = PanelColorOf(l.Real) } : l.Series).ToList(),
        };

        // The legend, leader first. With one line the card's title says it.
        // The job is there as text: it is what makes job colours safe, since
        // two warriors differ by a shade in the swatch and by name beside it.
        lines.Reverse();
        Rows.Sync(Legend, lines.Count < 2 ? [] : lines, l => l.Real ?? OthersKey, l => new LegendRow(l.Real ?? OthersKey), (row, l) =>
        {
            row.Name = l.Series.Name;
            row.Group = l.Series.Group;
            row.Job = l.Real != null ? JobBadge(l.Real) : "";
            row.Swatch = l.Series.Group ? null : Solid(l.Series.Color);
            row.Tip = l.Members;
        });
    }

    // --------------------------------------------------- damage by character

    [ObservableProperty] private IReadOnlyList<BarRow> bars = [];
    [ObservableProperty] private double barsHeight = BarsLayout.HeightFor(0);
    public ObservableCollection<ActorRow> Actors { get; } = [];
    [ObservableProperty] private bool hasActors;
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

        Bars = actors.Select(a =>
        {
            // No DPS here. It is the one figure that falls with the clock,
            // and a hover card is written once per count: it would drift out
            // of step with the same character's live cell in the table below.
            var hover = new List<CardRow>();
            if (!Aliased(a.Name) && Roster.JobTitle(a.Name) is { Length: > 0 } job) hover.Add(new CardRow("Job", job));
            hover.Add(new CardRow("Damage", Format.Int(a.Total)));
            hover.Add(new CardRow("Share", Format.Num(a.Share * 100, 1) + "%"));
            hover.Add(new CardRow("Avg / action", Format.Int(a.Avg)));
            return new BarRow(NameOf(a.Name), a.Total, ColorOf(a.Name), hover, Key: a.Name);
        }).ToList();
        BarsHeight = BarsLayout.HeightFor(actors.Count);

        ShowJob = !HideNames;
        HasActors = actors.Count > 0;
        Rows.Sync(Actors, actors, a => a.Name, a => new ActorRow(a.Name), (row, a) =>
        {
            row.Total = a.Total;
            row.Name = NameOf(a.Name);
            row.Swatch = SwatchOf(a.Name);
            row.Job = Roster.JobLabel(a.Name) is { Length: > 0 } job ? job : None;
            row.JobTip = Roster.JobTitle(a.Name);
            row.ShowJob = ShowJob;
            row.Damage = Format.Int(a.Total);
            row.Share = Percent(a.Share);
            row.Dps = Format.Num(a.Dps, 1);
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

        // Floating, the card is a strip: the bar behind the name, and the
        // three figures a glance during a fight is for. The other eleven
        // columns, DPS among them, stay on the table, so nothing in the strip
        // moves with the clock: every figure in it is a running total that
        // only a new event can change.
        double top = actors.Count > 0 ? actors.Max(a => a.Total) : 0;
        Rows.Sync(Strip, actors, a => a.Name, a => new StripRow(a.Name), (row, a) =>
        {
            var full = Aliased(a.Name) ? "" : Roster.JobTitle(a.Name);
            row.Name = NameOf(a.Name);
            row.Job = JobBadge(a.Name);
            row.Fill = Solid(PanelColorOf(a.Name));
            row.Fraction = top > 0 ? a.Total / top : 0;
            row.Damage = Format.Int(a.Total);
            row.Share = Format.Num(a.Share * 100, 1) + "%";
            row.Accuracy = Percent(a.AutoAcc);
            row.IsOwner = a.Name == Roster.Owner;
            row.Tip = NameOf(a.Name) + (full.Length > 0 ? Dot + full : "") + Dot + Format.Int(a.Avg) + " avg per action";
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
        var items = new List<(ActorTotals Actor, ActionTotals? Action)>();
        foreach (var a in c.Totals.Actors)
        {
            items.Add((a, null));
            if (!openActions.Contains(a.Name)) continue;
            foreach (var act in a.ActionList) items.Add((a, act));
        }
        HasActions = items.Count > 0;

        static string Key((ActorTotals Actor, ActionTotals? Action) i) =>
            i.Action == null ? i.Actor.Name : i.Actor.Name + KeyGap + i.Action.Name;

        Rows.Sync(Actions, items, Key, i => new ActionRow(Key(i), i.Actor.Name, i.Action?.Name), (row, i) =>
        {
            var (a, act) = i;
            if (act == null)
            {
                row.Name = NameOf(a.Name);
                row.Swatch = SwatchOf(a.Name);
                row.PanelSwatch = Solid(PanelColorOf(a.Name));
                row.Job = JobBadge(a.Name);
                row.Total = Format.Int(a.Total);
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
        if (drill != null) DrillOpened?.Invoke();
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

        // Newest first.
        var hits = new List<HitRow>(d.Events.Count);
        for (int i = d.Events.Count - 1; i >= 0; i--)
        {
            var e = d.Events[i];
            var delta = e.Dmg - d.Avg;
            hits.Add(new HitRow(Format.Elapsed(e.T),
                                e.Target.Length > 0 ? NameOf(e.Target) : None,
                                Format.Int(e.Dmg) + (e.Crit ? " ✦" : ""),
                                (delta >= 0 ? "+" : "−") + Format.Int(Math.Abs(delta))));
        }
        DrillHits = hits;
    }
}
