using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zerg.Charts;
using Zerg.Core;
using Zerg.Core.Charts;

namespace Zerg;

// The Healing section: the Damage section's layout over the heals alone.
//
// One session, one clock, one set of filters: it is the same fight seen from
// the other side, so it is drawn from the same count as the damage cards and
// on every count, whichever section is on screen. Every figure is worked out
// in Zerg.Core (Healing.Totals); this only words and draws them.
//
// Healing leaves a pet's heals out, in the hero tile, the line and the bars
// alike. They are their owner's, in a column of their own, and in the heals
// table under the pet's name.
public sealed partial class MainViewModel
{
    // ----------------------------------------------------------------- tiles
    //
    // The clock is the Damage section's own (ClockText, ClockNote, Light):
    // there is one session.

    [ObservableProperty] private string healTotalText = "0";
    [ObservableProperty] private string healTotalNote = "";
    [ObservableProperty] private string hpsText = "0.0";
    [ObservableProperty] private string hpsNote = None;
    [ObservableProperty] private string topHealerName = None;
    [ObservableProperty] private string topHealerNote = None;
    [ObservableProperty] private string? topHealerTip;
    [ObservableProperty] private Brush topHealerSwatch = Brushes.Transparent;
    [ObservableProperty] private bool hasTopHealer;
    [ObservableProperty] private string bigHealText = "0";
    [ObservableProperty] private string bigHealNote = None;

    void DrawHealing(Snapshot c)
    {
        HealEmptyText = SessionText.Empty(Shown.Session, Viewing, what: "healing");
        DrawHealTiles(c);
        DrawHealLine(c);
        DrawHealBars(c);
        DrawHeals(c);
        DrawHealDrill(c);
    }

    void DrawHealTiles(Snapshot c)
    {
        var h = c.Healing;
        HealTotalText = Format.Int(h.Total);
        // Whoever healed anything, their pet's heals included.
        int n = h.Actors.Count(a => a.Total + a.PetTotal > 0);
        HpsNote = n > 0 ? n + " healer" + (n == 1 ? "" : "s") : None;

        HasTopHealer = c.TopHealer != null;
        if (c.TopHealer is { } top)
        {
            var job = JobBadge(top.Name);
            TopHealerSwatch = SwatchOf(top.Name);
            TopHealerName = NameOf(top.Name);
            TopHealerTip = Roster.JobTitle(top.Name) is { Length: > 0 } title ? title : NameOf(top.Name);
            TopHealerNote = (job.Length > 0 ? job + Dot : "") + Format.Num((top.Share ?? 0) * 100, 1) + "% of " +
                            Format.Int(h.Total) + " healing";
        }
        else
        {
            TopHealerSwatch = Brushes.Transparent;
            TopHealerName = TopHealerNote = None;
            TopHealerTip = null;
        }

        var best = c.BestHeal;
        BigHealText = best != null ? Format.Int(best.Max) : "0";
        BigHealNote = best != null ? NameOf(best.Who) + Dot + best.What : None;
    }

    // ---------------------------------------------------- cumulative healing

    [ObservableProperty] private LineModel? healLine;
    /// <summary>The same lines in a floating panel's colours.</summary>
    [ObservableProperty] private LineModel? panelHealLine;
    /// <summary>What an empty healing chart says.</summary>
    [ObservableProperty] private string healEmptyText = "";
    public ObservableCollection<LegendRow> HealLegend { get; } = [];

    void DrawHealLine(Snapshot c)
    {
        // A line for everyone who healed anything themselves. Every healer
        // keeps their own: a party has two or three, so there is no pack to
        // fold into an "others" line as the damage chart does.
        var names = c.Healing.Actors.Where(a => a.Total > 0).Select(a => a.Name).ToList();
        var rows = Healing.AsEvents(c.Heals.Where(h => !h.IsPets));

        double? now = Shown.Session.StartedAt != null ? c.Elapsed : null;
        var cum = Counting.Cumulative(rows, names, 0, now);

        // Largest total last, so the leading line is drawn on top.
        var lines = cum.Series.Select(s => new Drawn(s.Name, new LineSeries(NameOf(s.Name), s.Values, ColorOf(s.Name))))
                              .OrderBy(l => l.Series.Values.Length > 0 ? l.Series.Values[^1] : 0).ToList();
        HealLine = new LineModel(cum.Times, lines.Select(l => l.Series).ToList(), cum.Live == true);
        PanelHealLine = AppTheme.IsDark ? HealLine : HealLine with
        {
            Series = lines.Select(l => l.Series with { Color = PanelColorOf(l.Real!) }).ToList(),
        };

        lines.Reverse();
        Rows.Sync(HealLegend, lines.Count < 2 ? [] : lines, l => l.Real!, l => new LegendRow(l.Real!), (row, l) =>
        {
            row.Name = l.Series.Name;
            row.Job = JobBadge(l.Real!);
            row.Swatch = Solid(l.Series.Color);
        });
    }

    // -------------------------------------------------- healing by character

    [ObservableProperty] private IReadOnlyList<BarRow> healBars = [];
    [ObservableProperty] private double healBarsHeight = BarsLayout.HeightFor(0);
    public ObservableCollection<HealerRow> Healers { get; } = [];
    [ObservableProperty] private bool hasHealers;

    /// <summary>The card as it floats: one short row per character.</summary>
    public ObservableCollection<HealStripRow> HealStrip { get; } = [];

    void DrawHealBars(Snapshot c)
    {
        // Everyone who healed anything, a pet's heals counting: a character
        // whose pet did all of it has a row, with no bar to speak of and the
        // figure under Pet Healing.
        var actors = c.Healing.Actors.Where(a => a.Total + a.PetTotal > 0).ToList();

        HealBars = actors.Select(a =>
        {
            // No HPS here, for the reason the damage bars carry no DPS.
            var hover = new List<CardRow>();
            if (!Aliased(a.Name) && Roster.JobTitle(a.Name) is { Length: > 0 } job) hover.Add(new CardRow("Job", job));
            hover.Add(new CardRow("Healing", Format.Int(a.Total)));
            hover.Add(new CardRow("Share", Percent(a.Share)));
            hover.Add(new CardRow("Casts", Format.Int(a.Casts)));
            if (a.PetTotal != 0) hover.Add(new CardRow("Pet healing", Format.Int(a.PetTotal)));
            return new BarRow(NameOf(a.Name), a.Total, ColorOf(a.Name), hover, Key: a.Name);
        }).ToList();
        HealBarsHeight = BarsLayout.HeightFor(actors.Count);

        HasHealers = actors.Count > 0;
        Rows.Sync(Healers, actors, a => a.Name, a => new HealerRow(a.Name), (row, a) =>
        {
            row.Total = a.Total;
            row.Name = NameOf(a.Name);
            row.Swatch = SwatchOf(a.Name);
            row.Job = Roster.JobLabel(a.Name) is { Length: > 0 } job ? job : None;
            row.JobTip = Roster.JobTitle(a.Name);
            row.ShowJob = !HideNames;
            row.Healing = Format.Int(a.Total);
            row.Share = Percent(a.Share);
            row.Casts = Format.Int(a.Casts);
            row.Avg = Whole(a.Avg);
            // A dash, not a zero: most characters have no pet to have healed.
            row.PetHealing = a.PetTotal != 0 ? Format.Int(a.PetTotal) : None;
        });

        // Floating, the strip: Healing, its share, and casts. Nothing in it
        // moves with the clock, as nothing in the damage strip does.
        double top = actors.Count > 0 ? actors.Max(a => a.Total) : 0;
        Rows.Sync(HealStrip, actors, a => a.Name, a => new HealStripRow(a.Name), (row, a) =>
        {
            var full = Aliased(a.Name) ? "" : Roster.JobTitle(a.Name);
            row.Name = NameOf(a.Name);
            row.Job = JobBadge(a.Name);
            row.Fill = Solid(PanelColorOf(a.Name));
            row.Fraction = top > 0 ? a.Total / top : 0;
            row.Healing = Format.Int(a.Total);
            row.Share = Percent(a.Share);
            row.Casts = Format.Int(a.Casts);
            row.IsOwner = a.Name == Roster.Owner;
            row.Tip = NameOf(a.Name) + (full.Length > 0 ? Dot + full : "") +
                      (a.Avg is double avg ? Dot + Format.Int(avg) + " avg per cast" : "");
        });
    }

    // ----------------------------------------------------------------- heals

    public ObservableCollection<HealRow> Heals { get; } = [];
    [ObservableProperty] private bool hasHeals;
    /// <summary>The characters whose heals are showing under their heading,
    /// by real name. Everyone starts as a heading alone, as in the actions
    /// table.</summary>
    readonly HashSet<string> openHeals = new(StringComparer.Ordinal);

    /// <summary>Selecting a character shows their heals under the heading;
    /// selecting them again puts the heals away.</summary>
    [RelayCommand]
    void ToggleHeals(HealRow row)
    {
        if (!row.IsHeading) return;
        if (!openHeals.Remove(row.Actor)) openHeals.Add(row.Actor);
        // Nothing is counted again: the last count, with more or fewer of its lines.
        if (snapshot is { } c) DrawHeals(c);
    }

    void DrawHeals(Snapshot c)
    {
        var items = new List<(HealerTotals Actor, HealAction? Action)>();
        foreach (var a in c.Healing.Actors)
        {
            items.Add((a, null));
            if (!openHeals.Contains(a.Name)) continue;
            foreach (var act in a.ActionList) items.Add((a, act));
        }
        HasHeals = items.Count > 0;

        static string Key((HealerTotals Actor, HealAction? Action) i) =>
            i.Action == null ? i.Actor.Name : i.Actor.Name + KeyGap + i.Action.Name;

        Rows.Sync(Heals, items, Key, i => new HealRow(Key(i), i.Actor.Name, i.Action?.Name), (row, i) =>
        {
            var (a, act) = i;
            if (act == null)
            {
                row.Name = NameOf(a.Name);
                row.Swatch = SwatchOf(a.Name);
                row.PanelSwatch = Solid(PanelColorOf(a.Name));
                row.Job = JobBadge(a.Name);
                row.Total = Format.Int(a.Total);
                row.Pet = a.PetTotal != 0 ? "+ " + Format.Int(a.PetTotal) + " pet" : "";
                row.Open = openHeals.Contains(a.Name);
                row.Label = "Heals of " + NameOf(a.Name);
                return;
            }
            // Out of everything under this heading, the pet's heals too:
            // they are listed here, so the column adds up to 100.
            double all = a.Total + a.PetTotal;
            row.Name = act.Name;
            row.Casts = Format.Int(act.Casts);
            row.Total = Format.Int(act.Total);
            row.Avg = Format.Int(act.Avg);
            row.Min = Whole(act.Min);
            row.Max = Format.Int(act.Max);
            row.Share = Format.Num(all != 0 ? act.Total / all * 100 : 0, 1) + "%";
            row.Selected = healDrill is { } d && d.Actor == a.Name && d.Action == act.Name;
            row.Label = NameOf(a.Name) + ", " + act.Name;
        });
    }

    // ------------------------------------------------------------ drill-down

    /// <summary>The heal the drill-down is open on, by the character's real name.</summary>
    (string Actor, string Action)? healDrill;

    /// <summary>The heal drill-down just opened: the window brings it into view.</summary>
    public event Action? HealDrillOpened;

    [ObservableProperty] private bool healDrillOpen;
    [ObservableProperty] private string healDrillTitle = "";
    [ObservableProperty] private string healDrillNote = "";
    [ObservableProperty] private IReadOnlyList<StatTile> healDrillTiles = [];
    [ObservableProperty] private HistogramModel? healHistogram;
    [ObservableProperty] private Color healHistogramFill = Colors.Gray;
    /// <summary>The same, in a floating panel's colours.</summary>
    [ObservableProperty] private Color panelHealHistogramFill = Colors.Gray;
    [ObservableProperty] private string healDrillCaption = "";
    [ObservableProperty] private IReadOnlyList<HitRow> healDrillHits = [];

    /// <summary>Selecting a heal opens its drill-down; selecting it again closes it.</summary>
    [RelayCommand]
    void HealDrill(HealRow row)
    {
        if (row.Action == null) return;
        bool same = healDrill is { } d && d.Actor == row.Actor && d.Action == row.Action;
        healDrill = same ? null : (row.Actor, row.Action);
        Recount();
        if (healDrill != null) HealDrillOpened?.Invoke();
    }

    [RelayCommand]
    void CloseHealDrill()
    {
        healDrill = null;
        Recount();
    }

    void DrawHealDrill(Snapshot c)
    {
        if (healDrill is not { } on)
        {
            HealDrillOpen = false;
            return;
        }

        // What each cast healed, summed over everyone it reached: a Curaga
        // on five is one figure, as an area attack is in the damage
        // drill-down. A pet's heals are here too, under the pet's name.
        var d = Counting.Distribution(Healing.AsEvents(c.Heals), on.Actor, on.Action);
        HealDrillOpen = true;
        HealDrillTitle = on.Action;
        HealDrillNote = NameOf(on.Actor) + Dot + Format.Int(d.Count) + " cast" + (d.Count == 1 ? "" : "s") +
                        Dot + Format.Int(d.Total) + " healed";

        if (d.Count == 0)
        {
            HealDrillTiles = [new StatTile("No casts in range", "")];
            HealHistogram = null;
            HealDrillCaption = "";
            HealDrillHits = [];
            return;
        }

        HealDrillTiles =
        [
            new("Min", Format.Int(d.Min)),
            new("Average", Format.Int(d.Avg)),
            new("Max", Format.Int(d.Max)),
            new("Median", Format.Int(d.Median)),
            new("Std dev", Format.Int(d.Stdev)),
            // Not accuracy: a heal cannot miss.
            new("Casts", Format.Int(d.Count)),
        ];

        HealHistogram = HistogramModel.From(d);
        HealHistogramFill = ColorOf(on.Actor);
        PanelHealHistogramFill = PanelColorOf(on.Actor);
        HealDrillCaption = "Healed per cast" + Dot + d.Bins.Count.ToString(CultureInfo.InvariantCulture) + " bins" + Dot +
                           "IQR " + Format.Int(d.Q1) + "–" + Format.Int(d.Q3) + Dot +
                           "90th percentile " + Format.Int(d.P90);

        // Newest first.
        var hits = new List<HitRow>(d.Events.Count);
        for (int i = d.Events.Count - 1; i >= 0; i--)
        {
            var e = d.Events[i];
            var delta = e.Dmg - d.Avg;
            hits.Add(new HitRow(Format.Elapsed(e.T),
                                e.Target.Length > 0 ? NameOf(e.Target) : None,
                                Format.Int(e.Dmg),
                                (delta >= 0 ? "+" : "−") + Format.Int(Math.Abs(delta))));
        }
        HealDrillHits = hits;
    }
}
