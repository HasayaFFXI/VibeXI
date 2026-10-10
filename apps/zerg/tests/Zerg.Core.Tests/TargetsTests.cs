namespace Zerg.Core.Tests;

/// <summary>Isolating targets: only the damage dealt to the ones picked is counted.</summary>
public class TargetsTests
{
    static readonly HashSet<string> Nobody = [];

    static HashSet<string> Only(params string[] names) => new(names, StringComparer.Ordinal);

    /// <summary>A session armed at 0 and fed these lines.</summary>
    static Tracker Session(params string[] lines)
    {
        var t = new Tracker();
        t.Follow("Hasaya_2026.07.30.jsonl");
        t.Start(0);
        t.Feed(lines);
        return t;
    }

    /// <summary>Two characters on two targets, and a third target nobody damaged.</summary>
    static Tracker Fight() => Session(
        Lines.Hit(10, "Hasaya", 100, target: "Kirin"),
        Lines.Hit(12, "Hasaya", 0, target: "Kirin", hit: false),
        Lines.Hit(14, "Tank", 40, target: "Kirin"),
        Lines.Hit(20, "Hasaya", 300, target: "Genbu"),
        Lines.Hit(22, "Tank", 60, target: "Genbu"),
        Lines.Hit(24, "Tank", 0, target: "Byakko", hit: false));

    // ------------------------------------------------------------ the count

    [Fact]
    public void With_no_target_picked_every_target_counts()
    {
        var all = Fight().Count(Nobody, true, 30_000);
        Assert.Equal(500, all.Totals.Total);
        Assert.Empty(all.Isolated);
        Assert.Equal(500, all.Whole);

        // An empty set is no filter either.
        var none = Fight().Count(Nobody, true, 30_000, Only());
        Assert.Equal(500, none.Totals.Total);
        Assert.Empty(none.Isolated);
    }

    [Fact]
    public void An_isolated_target_is_all_that_is_counted()
    {
        var c = Fight().Count(Nobody, true, 30_000, Only("Kirin"));
        Assert.Equal(140, c.Totals.Total);
        Assert.Equal(["Kirin"], c.Isolated);
        Assert.All(c.Events, e => Assert.Equal("Kirin", e.Target));

        var hasaya = c.Totals.Actors.Single(a => a.Name == "Hasaya");
        Assert.Equal(100, hasaya.Total);
        // Their swings at Kirin, and at nothing else: one landed of two.
        Assert.Equal(0.5, hasaya.AutoAcc);
        Assert.Equal(100.0 / 140, hasaya.Share);
        Assert.Equal(40, c.Totals.Actors.Single(a => a.Name == "Tank").Total);
    }

    [Fact]
    public void Several_targets_can_be_isolated_together()
    {
        var c = Fight().Count(Nobody, true, 30_000, Only("Kirin", "Genbu"));
        Assert.Equal(500, c.Totals.Total);
        Assert.Equal(["Genbu", "Kirin"], c.Isolated);

        // And a target nothing landed on can be: its misses are swings at it.
        var missed = Fight().Count(Nobody, true, 30_000, Only("Byakko"));
        Assert.Equal(0, missed.Totals.Total);
        Assert.Equal(0, missed.Totals.Actors.Single().AutoAcc);
    }

    [Fact]
    public void An_area_attack_counts_what_it_dealt_to_the_isolated_target_as_one_use()
    {
        // One Fireball on three targets: one use, a row each.
        var t = Session(
            Lines.Hit(10, "Mage", 200, kind: "magic", action: "Firaga", target: "Kirin"),
            Lines.Hit(10, "Mage", 150, kind: "magic", action: "Firaga", target: "Genbu"),
            Lines.Hit(10, "Mage", 0, kind: "magic", action: "Firaga", target: "Byakko", hit: false));
        var rows = t.Reader.Events;
        Assert.Equal(3, rows.Count);
        // The lines above were each given a use of their own: fold them by hand.
        var one = rows.Select(e => e with { Use = 77 }).ToList();
        var kirin = Counting.Filter(one, new FilterOptions { Targets = Only("Kirin") });
        var agg = Counting.Aggregate(kirin, 30);
        var firaga = agg.Actors.Single().ActionList.Single();
        Assert.Equal((1, 0, 200d, 200d), (firaga.Hits, firaga.Misses, firaga.Total, firaga.Max));
        // Folded, it names the one target it is counted at.
        Assert.Equal("Kirin", Counting.Collapse(kirin).Single().Target);
    }

    [Fact]
    public void The_clock_is_the_sessions_whatever_is_isolated()
    {
        // Genbu was first hit at 20 s. The zero is still the session's first row.
        var t = Fight();
        var c = t.Count(Nobody, true, 30_000, Only("Genbu"));
        Assert.Equal(10_000, t.Session.StartedAt);
        Assert.Equal(20_000, c.Elapsed);
        Assert.Equal(20, c.Totals.Duration);

        // Paused, the end is the last party row, at any target.
        t.Second(40_000);
        var held = t.Count(Nobody, true, 50_000, Only("Kirin"));
        Assert.Equal(25_000, t.Session.EndedAt);
        Assert.Equal(15_000, held.Elapsed);
    }

    [Fact]
    public void A_target_that_is_isolated_does_not_start_the_clock_late()
    {
        // Armed with Genbu isolated: the first counted row is still the zero.
        var t = Fight();
        t.Count(Nobody, true, 30_000, Only("Genbu"));
        Assert.Equal(10_000, t.Session.StartedAt);
    }

    [Fact]
    public void No_rate_is_given_while_a_target_is_isolated()
    {
        var t = Fight();
        var all = t.Count(Nobody, true, 30_000);
        Assert.Equal(25, all.Rate(500, 20));
        Assert.Equal(0, all.Rate(500, 0));

        var kirin = t.Count(Nobody, true, 30_000, Only("Kirin"));
        Assert.Null(kirin.Rate(140, 20));
    }

    [Fact]
    public void Healing_is_not_isolated()
    {
        var t = Session(Lines.Hit(10, "Hasaya", 100, target: "Kirin"), Lines.Heal(12, "Mage", 300, target: "Hasaya"));
        var c = t.Count(Nobody, true, 30_000, Only("Kirin"));
        Assert.Equal(300, c.Healing.Total);
        Assert.Single(c.Heals);
    }

    [Fact]
    public void Everyone_who_acted_keeps_their_chip()
    {
        // Mage never touched Kirin, and can still be excluded.
        var t = Session(Lines.Hit(10, "Hasaya", 100, target: "Kirin"), Lines.Hit(12, "Mage", 80, target: "Genbu"));
        var c = t.Count(Nobody, true, 30_000, Only("Kirin"));
        Assert.Equal(["Hasaya", "Mage"], c.Listed.Select(a => a.Name));
        Assert.Equal(["Hasaya"], c.Totals.Actors.Select(a => a.Name));
    }

    // -------------------------------------------------------------- the list

    [Fact]
    public void The_targets_are_listed_alphabetically_each_with_what_it_took()
    {
        var c = Fight().Count(Nobody, true, 30_000);
        // Byakko took nothing, and is not listed.
        Assert.Equal([new TargetTotal("Genbu", 360), new TargetTotal("Kirin", 140)], c.Targets);
    }

    [Fact]
    public void The_list_names_every_target_while_one_is_isolated()
    {
        var c = Fight().Count(Nobody, true, 30_000, Only("Kirin"));
        Assert.Equal([new TargetTotal("Genbu", 360), new TargetTotal("Kirin", 140)], c.Targets);
        // What the isolated total is a share of.
        Assert.Equal(500, c.Whole);
    }

    [Fact]
    public void The_list_is_of_the_characters_and_the_damage_that_count()
    {
        var t = Session(
            Lines.Hit(10, "Hasaya", 100, target: "Kirin"),
            Lines.Hit(12, "Tank", 40, target: "Genbu"),
            Lines.Hit(14, "Hasaya", 50, kind: "skillchain", action: "Skillchain: Light", target: "Kirin"));
        Assert.Equal([new TargetTotal("Genbu", 40), new TargetTotal("Kirin", 150)], t.Count(Nobody, true, 30_000).Targets);
        Assert.Equal([new TargetTotal("Genbu", 40), new TargetTotal("Kirin", 100)], t.Count(Nobody, false, 30_000).Targets);
        Assert.Equal([new TargetTotal("Kirin", 150)], t.Count(Only("Tank"), true, 30_000).Targets);
    }

    [Fact]
    public void Names_are_in_alphabetical_order_whatever_their_case()
    {
        Assert.Equal(["Aern", "byakko", "Genbu", "Goblin", "goblin", "Kirin"],
                     Targets.Sorted(["Kirin", "goblin", "Genbu", "Aern", "Goblin", "byakko", "Kirin"]));
        // What cannot be named cannot be picked.
        Assert.Equal(["Genbu", "Kirin"], Targets.Pickable(["Kirin", "", "Genbu"]));
    }

    [Fact]
    public void Rows_that_name_no_target_are_summed_but_not_offered()
    {
        var t = Session(Lines.Hit(10, "Hasaya", 100, target: "Kirin"), Lines.Hit(12, "Hasaya", 30, target: ""));
        var c = t.Count(Nobody, true, 30_000);
        Assert.Equal([new TargetTotal("Kirin", 100)], c.Targets);
        Assert.Equal(130, c.Whole);
        Assert.Equal(30, Targets.Totals(c.Events)[""]);
    }

    // ------------------------------------------------------------- the table

    [Fact]
    public void A_table_lists_the_same_targets_largest_first()
    {
        var t = Session(
            Lines.Hit(10, "Hasaya", 100, target: "Genbu"),
            Lines.Hit(12, "Hasaya", 400, target: "Kirin"),
            Lines.Hit(14, "Tank", 100, target: "byakko"),
            Lines.Hit(16, "Tank", 250, target: "Suzaku"));
        var c = t.Count(Nobody, true, 30_000);

        // The list a target is picked from is alphabetical; the table is
        // by what each took, and two that took the same are by name.
        Assert.Equal(["byakko", "Genbu", "Kirin", "Suzaku"], c.Targets.Select(x => x.Name));
        Assert.Equal([new TargetTotal("Kirin", 400), new TargetTotal("Suzaku", 250), new TargetTotal("byakko", 100),
                      new TargetTotal("Genbu", 100)], Targets.Ranked(c.Targets));
        // Nothing is added or lost by the order.
        Assert.Equal(c.AllTargets, Targets.Ranked(c.Targets).Sum(x => x.Total));
    }

    [Fact]
    public void A_table_goes_on_listing_what_is_picked_when_nothing_was_dealt_to_it()
    {
        // Picked, and then its character left out: it stays, with no
        // total, after the ones that have one, where it can be let go.
        var lines = Targets.Ranked([new TargetTotal("Genbu", 40), new TargetTotal("Kirin", 150)], ["Suzaku", "Kirin", "Aern"]);

        Assert.Equal(["Kirin", "Genbu", "Aern", "Suzaku"], lines.Select(x => x.Name));
        Assert.Equal([150, 40], lines.Take(2).Select(x => x.Total));
        Assert.All(lines.Skip(2), x => Assert.True(double.IsNaN(x.Total)));
        // Nothing picked, nothing added; and a pick is listed once.
        Assert.Equal(2, Targets.Ranked([new TargetTotal("Genbu", 40), new TargetTotal("Kirin", 150)], []).Count);
        Assert.Equal(["Kirin"], Targets.Ranked([new TargetTotal("Kirin", 150)], ["Kirin", "Kirin"]).Select(x => x.Name));
    }

    [Fact]
    public void A_table_orders_by_what_a_key_is_called_where_two_came_to_the_same()
    {
        // Keys that are called something else than they are written.
        var lines = Targets.Ranked([new TargetTotal("ws", 70), new TargetTotal("melee", 70), new TargetTotal("magic", 90)],
                                   ["pet"], DamageTypes.Label);

        Assert.Equal(["magic", "melee", "ws", "pet"], lines.Select(x => x.Name));
    }

    [Fact]
    public void A_table_has_no_line_to_pick_for_rows_that_name_no_target_and_says_what_they_came_to()
    {
        var t = Session(Lines.Hit(10, "Hasaya", 100, target: "Kirin"), Lines.Hit(12, "Hasaya", 30, target: ""));
        var c = t.Count(Nobody, true, 30_000);

        Assert.Equal([new TargetTotal("Kirin", 100)], Targets.Ranked(c.Targets));
        Assert.Equal(30, Targets.Unnamed(c.Targets, c.AllTargets));
        // A name that is none is not a line, wherever it came from.
        Assert.Equal(["Kirin"], Targets.Ranked([new TargetTotal("", 30), new TargetTotal("Kirin", 100)], [""]).Select(x => x.Name));
        Assert.Equal(30, Targets.Unnamed([new TargetTotal("", 30), new TargetTotal("Kirin", 100)], 130));
        // Every row named its target: nothing over, and never less than nothing.
        Assert.Equal(0, Targets.Unnamed(c.Targets, 100));
        Assert.Equal(0, Targets.Unnamed(c.Targets, 99));
        Assert.Equal(0, Targets.Unnamed([], 0));
    }

    [Fact]
    public void A_table_of_one_filter_is_of_what_the_other_leaves()
    {
        // The count already says so; the table only orders it.
        var t = Session(
            Lines.Hit(10, "Hasaya", 100, target: "Kirin"),
            Lines.Hit(12, "Hasaya", 300, kind: "ws", action: "Tachi: Gekko", target: "Genbu"),
            Lines.Hit(14, "Tank", 40, target: "Genbu"));

        var melee = t.Count(Nobody, true, 30_000, null, new HashSet<string> { "melee" });
        Assert.Equal([new TargetTotal("Kirin", 100), new TargetTotal("Genbu", 40)], Targets.Ranked(melee.Targets));
        var genbu = t.Count(Nobody, true, 30_000, new HashSet<string> { "Genbu" });
        Assert.Equal([new TargetTotal("melee", 40), new TargetTotal("ws", 300)],
                     DamageTypes.Listed(genbu.Types, genbu.IsolatedTypes));
        // Its own filter does not cut a table: every target is still listed.
        Assert.Equal(["Genbu", "Kirin"], Targets.Ranked(genbu.Targets, genbu.Isolated).Select(x => x.Name));
    }

    // ------------------------------------------------------------- the words

    [Fact]
    public void What_is_picked_is_said_in_a_word_or_two()
    {
        Assert.Equal("All", Targets.Label([]));
        Assert.Equal("Kirin", Targets.Label(["Kirin"]));
        Assert.Equal("Genbu +2", Targets.Label(["Kirin", "Genbu", "Suzaku"]));
        Assert.Equal("Genbu, Kirin, Suzaku", Targets.Names(["Kirin", "Genbu", "Suzaku"]));
    }

    [Fact]
    public void A_total_says_what_it_is_a_total_of()
    {
        Assert.Equal("Total damage", Targets.Heading([]));
        Assert.Equal("Damage to Kirin", Targets.Heading(["Kirin"]));
        Assert.Equal("Damage to 3 targets", Targets.Heading(["Kirin", "Genbu", "Suzaku"]));
        // A long name is cut: the label shares its line.
        Assert.Equal("Damage to Goblin Pathfind…", Targets.Heading(["Goblin Pathfinder"]));
        Assert.Equal("Goblin Pathfind…", Targets.Short("Goblin Pathfinder"));
        Assert.Equal("Steelshell Crab", Targets.Short("Steelshell Crab"));
        Assert.Equal("Sixteen letters.", Targets.Short("Sixteen letters."));
    }

    [Fact]
    public void Nothing_picked_is_no_filter()
    {
        Assert.Null(Targets.Only(null));
        Assert.Null(Targets.Only([]));
        Assert.Equal(["Kirin"], Targets.Only(["Kirin", "Kirin"])!);
    }

    // --------------------------------------------------------------- compare

    static ImportedParse Run(double end, params string[] lines)
    {
        var t = Session(lines);
        t.Count(Nobody, true, end * 1000);
        return ParseFile.Import(CompareSheet.Snapshot(t.Reader, t.Session, t.File, end * 1000)!);
    }

    static (ImportedParse A, ImportedParse B) Runs() => (
        Run(30, Lines.Hit(10, "Hasaya", 100, target: "Kirin"), Lines.Hit(20, "Hasaya", 200, target: "Genbu")),
        Run(50, Lines.Hit(10, "Hasaya", 150, target: "Kirin"), Lines.Hit(20, "Hasaya", 0, target: "Kirin", hit: false),
                Lines.Hit(30, "Hasaya", 500, kind: "ws", action: "Tachi: Gekko", target: "Suzaku")));

    [Fact]
    public void A_compared_run_is_measured_of_the_isolated_targets_alone()
    {
        var (a, b) = Runs();
        var m = Compare.Measure(b, targets: Only("Kirin"));
        Assert.Equal(150, m.Total);
        Assert.True(m.Isolated);
        Assert.Null(m.Dps);
        Assert.Equal(0.5, m.Accuracy);
        Assert.Null(m.WsTotal);
        // The run's clock is the run's.
        Assert.Equal(40, m.Duration);
        // Every target is still there to pick.
        Assert.Equal(new Dictionary<string, double> { ["Kirin"] = 150, ["Suzaku"] = 500 }, m.Targets);
        // The line of both runs is of the same damage.
        Assert.All(m.Events, e => Assert.Equal("Kirin", e.Target));

        var whole = Compare.Measure(a);
        Assert.False(whole.Isolated);
        Assert.Equal(15, whole.Dps);
    }

    [Fact]
    public void The_compare_sheet_says_what_is_isolated_and_gives_no_rate()
    {
        var (a, b) = Runs();
        var s = CompareSheet.Of(a, b, byJob: false, skillchains: true, hideNames: false, targets: Only("Kirin"));
        Assert.Equal(["Kirin"], s.Isolated);

        var total = s.DamageTiles[0];
        Assert.Equal(("Damage to Kirin", "100", "150"), (total.Label, total.A, total.B));
        Assert.Equal("+50.0%", total.Change.Main);

        var dps = s.DamageTiles.Single(t => t.Label == "Party DPS");
        Assert.Equal((CompareSheet.Dash, CompareSheet.Dash, Change.Nothing), (dps.A, dps.B, dps.Change));
        Assert.Equal(CompareSheet.NoRate, dps.Tip);

        var row = s.Actors.Single();
        Assert.Equal(new AB(CompareSheet.Dash, CompareSheet.Dash), row.Dps);
        Assert.Equal(Change.Nothing, row.DpsChange);
        Assert.Equal(("100", "150"), (row.Damage.TextA, row.Damage.TextB));

        // By target lists them all, largest first, as it did; the list to pick from is by name.
        Assert.Equal(["Suzaku", "Genbu", "Kirin"], s.Targets.Select(t => t.Name));
        Assert.Equal([new PairTotal("Genbu", 200, 0), new PairTotal("Kirin", 100, 150), new PairTotal("Suzaku", 0, 500)],
                     s.TargetList);
    }

    [Fact]
    public void A_compare_sheet_of_every_target_is_as_it_was()
    {
        var (a, b) = Runs();
        var s = CompareSheet.Of(a, b, byJob: false, skillchains: true, hideNames: false);
        Assert.Empty(s.Isolated);
        Assert.Equal("Total damage", s.DamageTiles[0].Label);
        Assert.Null(s.DamageTiles[0].Tip);
        var dps = s.DamageTiles.Single(t => t.Label == "Party DPS");
        Assert.Equal(("15.0", "16.3"), (dps.A, dps.B));
        Assert.Null(dps.Tip);
        Assert.Equal(new AB("15.0", "16.3"), s.Actors.Single().Dps);
    }
}
