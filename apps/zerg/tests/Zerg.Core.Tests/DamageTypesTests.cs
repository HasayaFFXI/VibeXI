namespace Zerg.Core.Tests;

/// <summary>Isolating damage types: only the damage of the types picked is counted.</summary>
public class DamageTypesTests
{
    static readonly HashSet<string> Nobody = [];

    static HashSet<string> Only(params string[] names) => new(names, StringComparer.Ordinal);

    static ImportedParse Run(double end, params string[] lines)
    {
        var t = new Tracker();
        t.Follow("Hasaya_2026.07.30.jsonl");
        t.Start(0);
        t.Feed(lines);
        t.Count(Nobody, true, end * 1000);
        return ParseFile.Import(CompareSheet.Snapshot(t.Reader, t.Session, t.File, end * 1000)!);
    }

    /// <summary>Melee, a weaponskill, magic and a pet, on two targets.</summary>
    static string[] FightLines(double scale = 1) =>
    [
        Lines.Hit(10, "Hasaya", 100 * scale, target: "Kirin"),
        Lines.Hit(12, "Hasaya", 0, target: "Kirin", hit: false),
        Lines.Hit(14, "Hasaya", 400 * scale, kind: "ws", action: "Tachi: Gekko", target: "Kirin"),
        Lines.Hit(16, "Hasaya", 60 * scale, target: "Genbu"),
        Lines.Hit(18, "Mage", 300 * scale, kind: "magic", action: "Fire IV", target: "Genbu"),
        Lines.Hit(20, "Fluffikins", 80 * scale, actorKind: "pet", owner: "Beast", target: "Kirin"),
        Lines.Hit(22, "Fluffikins", 50 * scale, kind: "ability", action: "Big Scissors", actorKind: "pet", owner: "Beast",
                  target: "Genbu"),
    ];

    /// <summary>That fight as a saved run, 20 seconds long.</summary>
    static ImportedParse Fight(double scale = 1) => Run(30, FightLines(scale));

    /// <summary>And as a session armed at 0, to be counted.</summary>
    static Tracker Session()
    {
        var t = new Tracker();
        t.Follow("Hasaya_2026.07.30.jsonl");
        t.Start(0);
        t.Feed(FightLines());
        return t;
    }

    // ------------------------------------------------------------ the session

    [Fact]
    public void The_session_is_counted_of_the_isolated_types_alone()
    {
        var c = Session().Count(Nobody, true, 30_000, types: Only("melee"));
        Assert.Equal(160, c.Totals.Total);
        Assert.Equal(["melee"], c.IsolatedTypes);
        Assert.Empty(c.Isolated);
        Assert.Equal(["Hasaya"], c.Totals.Actors.Select(a => a.Name));
        // What it is a share of: everything.
        Assert.Equal(990, c.Whole);
        // The clock is the session's, and so the rate stays: 160 over 20 seconds.
        Assert.Equal(20_000, c.Elapsed);
        Assert.Equal(8, c.Rate(c.Totals.Total, c.Elapsed / 1000));
    }

    [Fact]
    public void A_pets_damage_is_the_one_type_under_its_owner()
    {
        var c = Session().Count(Nobody, true, 30_000, types: Only("pet"));
        var beast = c.Totals.Actors.Single();
        Assert.Equal(("Beast", 130d), (beast.Name, beast.Total));
        // Whoever acted keeps their chip.
        Assert.Equal(3, c.Listed.Count);
    }

    [Fact]
    public void Each_of_the_sessions_lists_is_of_what_the_other_filter_leaves()
    {
        TargetTotal[] types = [new("magic", 300), new("melee", 160), new("pet", 130), new("ws", 400)];
        TargetTotal[] targets = [new("Genbu", 410), new("Kirin", 580)];

        var all = Session().Count(Nobody, true, 30_000);
        Assert.Equal(types, all.Types);
        Assert.Equal(990, all.AllTypes);
        Assert.Equal(targets, all.Targets);
        Assert.Equal(990, all.AllTargets);

        // A type isolated: every type still listed; the targets are of that type.
        var melee = Session().Count(Nobody, true, 30_000, types: Only("melee"));
        Assert.Equal(types, melee.Types);
        Assert.Equal(990, melee.AllTypes);
        Assert.Equal([new TargetTotal("Genbu", 60), new TargetTotal("Kirin", 100)], melee.Targets);
        Assert.Equal(160, melee.AllTargets);

        // A target isolated: every target still listed; the types are of that target.
        var kirin = Session().Count(Nobody, true, 30_000, Only("Kirin"));
        Assert.Equal(targets, kirin.Targets);
        Assert.Equal(990, kirin.AllTargets);
        Assert.Equal([new TargetTotal("melee", 100), new TargetTotal("pet", 80), new TargetTotal("ws", 400)], kirin.Types);
        Assert.Equal(580, kirin.AllTypes);

        // Both: the total is of both, and a target still takes the rate away.
        var both = Session().Count(Nobody, true, 30_000, Only("Kirin"), Only("melee"));
        Assert.Equal(100, both.Totals.Total);
        Assert.Equal(990, both.Whole);
        Assert.Null(both.Rate(100, 20));
    }

    [Fact]
    public void A_type_moves_no_clock_and_no_healing()
    {
        var t = Session();
        t.Feed([Lines.Heal(15, "Mage", 300, target: "Hasaya")]);
        // Magic was first dealt at 18 s: the zero is still the first row's.
        var c = t.Count(Nobody, true, 30_000, types: Only("magic"));
        Assert.Equal(10_000, t.Session.StartedAt);
        Assert.Equal(300, c.Healing.Total);
    }

    // ---------------------------------------------------------------- a type

    [Fact]
    public void A_rows_type_is_its_kind_but_a_pets_rows_are_one_type()
    {
        var rows = Fight().Source.Events;
        Assert.Equal(["melee", "melee", "ws", "melee", "magic", "pet", "pet"], rows.Select(DamageTypes.Of));
        // Credited to its owner, a pet's row is still a pet's.
        Assert.Equal("pet", DamageTypes.Of(Counting.Credit(rows[^1])));
    }

    [Fact]
    public void A_type_is_called_what_its_line_of_the_table_is()
    {
        Assert.Equal("Weaponskills", DamageTypes.Label("ws"));
        Assert.Equal("Counters & spikes", DamageTypes.Label("reaction"));
        // One the table does not know yet is the kind as written.
        Assert.Equal("drain", DamageTypes.Label("drain"));
    }

    [Fact]
    public void Types_are_picked_from_in_alphabetical_order_of_what_they_are_called()
    {
        // Job abilities, Magic, Melee, Pet, Weaponskills.
        Assert.Equal(["ability", "magic", "melee", "pet", "ws"], DamageTypes.Sorted(["ws", "pet", "melee", "magic", "ability"]));
        Assert.Equal("Magic, Weaponskills", DamageTypes.Names(["ws", "magic"]));
    }

    // -------------------------------------------------------------- counting

    [Fact]
    public void An_isolated_type_is_all_that_is_counted()
    {
        var rows = Fight().Source.Events;
        var melee = Counting.Filter(rows, new FilterOptions { Types = Only("melee") });
        Assert.Equal(3, melee.Count);
        Assert.All(melee, e => Assert.Equal("melee", e.Kind));
        Assert.Null(melee.FirstOrDefault(e => e.Owner != null));

        // The pet's two rows, whatever kind each was.
        var pet = Counting.Filter(rows, new FilterOptions { Types = Only("pet") });
        Assert.Equal([80d, 50d], pet.Select(e => e.Dmg));
        // Several, and none.
        Assert.Equal(5, Counting.Filter(rows, new FilterOptions { Types = Only("melee", "pet") }).Count);
        Assert.Equal(7, Counting.Filter(rows, new FilterOptions { Types = Only() }).Count);
    }

    [Fact]
    public void A_type_and_a_target_isolate_together()
    {
        var rows = Fight().Source.Events;
        var both = Counting.Filter(rows, new FilterOptions { Types = Only("melee"), Targets = Only("Kirin") });
        Assert.Equal([100d, 0d], both.Select(e => e.Dmg));
    }

    [Fact]
    public void A_compared_run_is_measured_of_the_isolated_types_alone()
    {
        var m = Compare.Measure(Fight(), types: Only("ws", "magic"));
        Assert.Equal(700, m.Total);
        Assert.Equal(["Hasaya", "Mage"], m.Agg.Actors.Select(a => a.Name));
        // No melee or ranged swing is left to have an accuracy of.
        Assert.Null(m.Accuracy);
        Assert.Equal(400, m.WsTotal);
    }

    [Fact]
    public void A_type_takes_no_rate_away()
    {
        // 160 of melee over the 20 seconds of the run: a DPS somebody dealt.
        var m = Compare.Measure(Fight(), types: Only("melee"));
        Assert.False(m.Isolated);
        Assert.Equal(160, m.Total);
        Assert.Equal(160.0 / m.Duration, m.Dps);
    }

    // ------------------------------------------------------------- the lists

    [Fact]
    public void Every_type_is_listed_while_one_is_isolated()
    {
        var m = Compare.Measure(Fight(), types: Only("melee"));
        Assert.Equal(new Dictionary<string, double> { ["melee"] = 160, ["ws"] = 400, ["magic"] = 300, ["pet"] = 130 }, m.Kinds);
    }

    [Fact]
    public void Each_list_is_of_what_the_other_filter_leaves()
    {
        // A type isolated: the targets are of what that type dealt to each.
        var melee = Compare.Measure(Fight(), types: Only("melee"));
        Assert.Equal(new Dictionary<string, double> { ["Kirin"] = 100, ["Genbu"] = 60 }, melee.Targets);

        // A target isolated: the types are of the damage dealt to it.
        var kirin = Compare.Measure(Fight(), targets: Only("Kirin"));
        Assert.Equal(new Dictionary<string, double> { ["melee"] = 100, ["ws"] = 400, ["pet"] = 80 }, kirin.Kinds);

        // Both: each list by the other, and the total by both.
        var both = Compare.Measure(Fight(), targets: Only("Kirin"), types: Only("melee"));
        Assert.Equal(100, both.Total);
        Assert.Equal(new Dictionary<string, double> { ["Kirin"] = 100, ["Genbu"] = 60 }, both.Targets);
        Assert.Equal(new Dictionary<string, double> { ["melee"] = 100, ["ws"] = 400, ["pet"] = 80 }, both.Kinds);
        // And a target still takes the rate away.
        Assert.Null(both.Dps);
    }

    // ------------------------------------------------------------- the table

    [Fact]
    public void A_table_of_one_parse_lists_the_types_in_the_order_of_compares_table()
    {
        var c = Session().Count(Nobody, true, 30_000);

        // The list a type is picked from is alphabetical by what each is
        // called; the table has the order of Compare's By damage type.
        Assert.Equal(["magic", "melee", "pet", "ws"], c.Types.Select(t => t.Name));
        Assert.Equal([new TargetTotal("melee", 160), new TargetTotal("ws", 400), new TargetTotal("magic", 300),
                      new TargetTotal("pet", 130)], DamageTypes.Listed(c.Types));
        Assert.Equal(c.AllTypes, DamageTypes.Listed(c.Types).Sum(t => t.Total));
        // The very order of the sheet's own table of the same fight.
        var sheet = CompareSheet.Of(Fight(), Fight(), false, true, false);
        Assert.Equal(sheet.Kinds.Select(k => k.Key), DamageTypes.Listed(c.Types).Select(t => t.Name));
    }

    [Fact]
    public void A_type_the_table_does_not_know_is_listed_after_the_ones_it_does()
    {
        var lines = DamageTypes.Listed([new TargetTotal("zeta", 900), new TargetTotal("magic", 5), new TargetTotal("Alpha", 1),
                                        new TargetTotal("melee", 7)]);

        Assert.Equal(["melee", "magic", "Alpha", "zeta"], lines.Select(t => t.Name));
    }

    [Fact]
    public void A_type_that_is_picked_and_came_to_nothing_keeps_its_place_in_the_table()
    {
        // Skillchains picked, then Include Skillchains switched off: the
        // line stays where it stands, with no total, to be let go.
        var lines = DamageTypes.Listed([new TargetTotal("melee", 160), new TargetTotal("magic", 300)], ["skillchain", "melee"]);

        Assert.Equal(["melee", "skillchain", "magic"], lines.Select(t => t.Name));
        Assert.True(double.IsNaN(lines[1].Total));
        Assert.Equal(160, lines[0].Total);
        // A type with no key is no line, and what it came to is said apart.
        var odd = new[] { new TargetTotal("", 30), new TargetTotal("melee", 160) };
        Assert.Equal(["melee"], DamageTypes.Listed(odd, [""]).Select(t => t.Name));
        Assert.Equal(30, Targets.Unnamed(odd, 190));
    }

    // -------------------------------------------------------------- the sheet

    [Fact]
    public void The_compare_sheet_says_which_types_are_isolated_and_keeps_its_rates()
    {
        var s = CompareSheet.Of(Fight(), Fight(2), byJob: false, skillchains: true, hideNames: false, types: Only("melee"));
        Assert.Equal(["melee"], s.IsolatedTypes);

        var total = s.DamageTiles[0];
        Assert.Equal(("Melee damage", "160", "320"), (total.Label, total.A, total.B));
        Assert.Equal("Only Melee damage", total.Tip);

        var dps = s.DamageTiles.Single(t => t.Label == "Party DPS");
        Assert.Equal(("8.0", "16.0"), (dps.A, dps.B));
        Assert.Null(dps.Tip);
        Assert.Equal(new AB("8.0", "16.0"), s.Actors.Single().Dps);

        // By damage type lists them all, in the table's own order, each with
        // its share of every type's damage; the list to pick from is by name.
        Assert.Equal(["Melee", "Weaponskills", "Magic", "Pet"], s.Kinds.Select(k => k.Label));
        Assert.Equal(["melee", "ws", "magic", "pet"], s.Kinds.Select(k => k.Key));
        Assert.Equal(new AB("16.2%", "16.2%"), s.Kinds[0].Share);
        Assert.Equal(new AB("40.4%", "40.4%"), s.Kinds[1].Share);
        Assert.Equal([new PairTotal("magic", 300, 600), new PairTotal("melee", 160, 320), new PairTotal("pet", 130, 260),
                      new PairTotal("ws", 400, 800)], s.TypeList);
    }

    [Fact]
    public void A_total_says_which_types_and_which_targets_it_is_of()
    {
        Assert.Equal("Total damage", DamageTypes.Heading([], []));
        Assert.Equal("Damage to Kirin", DamageTypes.Heading([], ["Kirin"]));
        Assert.Equal("Melee damage", DamageTypes.Heading(["melee"], []));
        Assert.Equal("Damage of 2 types", DamageTypes.Heading(["melee", "ws"], []));
        Assert.Equal("Weaponskills damage to Kirin", DamageTypes.Heading(["ws"], ["Kirin"]));
        Assert.Equal("Damage of 2 types to 3 targets", DamageTypes.Heading(["melee", "ws"], ["Kirin", "Genbu", "Suzaku"]));

        Assert.Null(DamageTypes.Told([], []));
        Assert.Equal("Only the damage dealt to Genbu, Kirin", DamageTypes.Told([], ["Kirin", "Genbu"]));
        Assert.Equal("Only Melee, Weaponskills damage dealt to Kirin", DamageTypes.Told(["ws", "melee"], ["Kirin"]));
    }

    [Fact]
    public void With_both_isolated_the_sheet_says_both_and_gives_no_rate()
    {
        var s = CompareSheet.Of(Fight(), Fight(2), byJob: false, skillchains: true, hideNames: false,
                                targets: Only("Kirin"), types: Only("ws"));
        Assert.Equal(("Weaponskills damage to Kirin", "400", "800"), (s.DamageTiles[0].Label, s.DamageTiles[0].A, s.DamageTiles[0].B));
        Assert.Equal(CompareSheet.Dash, s.DamageTiles.Single(t => t.Label == "Party DPS").A);
        // The targets are of weaponskills alone, and the types of Kirin alone.
        Assert.Equal(["Kirin"], s.Targets.Select(t => t.Name));
        Assert.Equal(["Melee", "Weaponskills", "Pet"], s.Kinds.Select(k => k.Label));
    }
}
