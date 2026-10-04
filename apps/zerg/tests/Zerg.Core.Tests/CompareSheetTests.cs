namespace Zerg.Core.Tests;

/// <summary>What the Compare section prints for a pair of runs.</summary>
public class CompareSheetTests
{
    static readonly HashSet<string> Nobody = [];

    /// <summary>
    /// A run made the way Use current makes one: lines fed to a tracker
    /// armed at 0, its clock frozen at <paramref name="end"/> seconds, and
    /// the lot read back through the export format.
    /// </summary>
    static ImportedParse Run(double end, params string[] lines)
    {
        var t = new Tracker();
        t.Follow("Hasaya_2026.07.30.jsonl");
        t.Start(0);
        t.Feed(lines);
        t.Count(Nobody, true, end * 1000);
        return ParseFile.Import(CompareSheet.Snapshot(t.Reader, t.Session, t.File, end * 1000)!);
    }

    static CompareSheet Sheet(ImportedParse a, ImportedParse b, bool byJob = false, bool skillchains = true,
                              bool hideNames = false) => CompareSheet.Of(a, b, byJob, skillchains, hideNames);

    // ----------------------------------------------------------- a change

    [Fact]
    public void A_change_carries_its_sign_in_the_text()
    {
        Assert.Equal(new Change("+50.0%", "+50", ChangeTone.Better), Change.Of(100, 150, ChangeKind.Percent, Format.Int, 1));
        Assert.Equal(new Change("−25.0%", "−50", ChangeTone.Worse), Change.Of(200, 150, ChangeKind.Percent, Format.Int, 1));
        Assert.Equal(new Change("±0.0%", "±0", ChangeTone.Flat), Change.Of(5, 5, ChangeKind.Percent, Format.Int, 1));
    }

    [Fact]
    public void Less_can_be_the_better_way()
    {
        Assert.Equal(ChangeTone.Better, Change.Of(200, 150, ChangeKind.Percent, Format.Int, -1).Tone);
        Assert.Equal(ChangeTone.Worse, Change.Of(100, 150, ChangeKind.Percent, Format.Int, -1).Tone);
    }

    [Fact]
    public void A_change_that_is_neither_better_nor_worse_is_flat_but_still_signed()
    {
        Assert.Equal(new Change("+4m 07s", "", ChangeTone.Flat), Change.Of(60, 307, ChangeKind.Time, Format.Int, 0));
    }

    [Fact]
    public void Over_a_baseline_of_nothing_a_figure_is_new()
    {
        Assert.Equal(new Change("new", "+7", ChangeTone.Better), Change.Of(0, 7, ChangeKind.Percent, Format.Int, 1));
        Assert.Equal(new Change("±0", "±0", ChangeTone.Flat), Change.Of(0, 0, ChangeKind.Percent, Format.Int, 1));
    }

    [Fact]
    public void A_percentage_changes_in_points()
    {
        Assert.Equal(new Change("+2.1 pt", "", ChangeTone.Better), Change.Of(0.80, 0.821, ChangeKind.Points, Format.Int, 1));
        Assert.Equal(new Change("−10.0 pt", "", ChangeTone.Worse), Change.Of(0.9, 0.8, ChangeKind.Points, Format.Int, 1));
    }

    [Fact]
    public void With_a_side_missing_there_is_no_change_to_state()
    {
        Assert.Equal(Change.Nothing, Change.Of(null, 5, ChangeKind.Percent, Format.Int, 1));
        Assert.Equal(Change.Nothing, Change.Of(5, null, ChangeKind.Points, Format.Int, 1));
        Assert.Equal(ChangeTone.None, Change.Nothing.Tone);
    }

    // -------------------------------------------------------------- tiles

    [Fact]
    public void The_damage_tiles_read_B_against_A()
    {
        // A: 300 damage over 20 s. B: 600 over 40 s, with a weaponskill and a miss.
        var a = Run(30, Lines.Hit(10, "Hasaya", 100), Lines.Hit(20, "Hasaya", 200));
        var b = Run(50, Lines.Hit(10, "Hasaya", 100), Lines.Hit(20, "Hasaya", 0, hit: false),
                        Lines.Hit(30, "Hasaya", 500, kind: "ws", action: "Tachi: Gekko"));
        var tiles = Sheet(a, b).DamageTiles;

        Assert.Equal(["Total damage", "Length", "Party DPS", "Accuracy", "WS damage", "Skillchain damage"],
                     tiles.Select(t => t.Label));
        Assert.Equal(("300", "600", "+100.0%", "+300"), (tiles[0].A, tiles[0].B, tiles[0].Change.Main, tiles[0].Change.Sub));
        Assert.True(tiles[0].Hero);
        // Length is neutral: shorter is not obviously better.
        Assert.Equal(("00:20", "00:40", "+20s", ChangeTone.Flat), (tiles[1].A, tiles[1].B, tiles[1].Change.Main, tiles[1].Change.Tone));
        Assert.Equal(("15.0", "15.0", "±0.0%"), (tiles[2].A, tiles[2].B, tiles[2].Change.Main));
        Assert.Equal(("100.0%", "50.0%", "−50.0 pt", ChangeTone.Worse),
                     (tiles[3].A, tiles[3].B, tiles[3].Change.Main, tiles[3].Change.Tone));
        // A had no weaponskill: a dash, not 0, and nothing to compare.
        Assert.Equal(("—", "500", "—", "0 WS · avg —", "1 WS · avg 500"),
                     (tiles[4].A, tiles[4].B, tiles[4].Change.Main, tiles[4].SubA, tiles[4].SubB));
        Assert.Equal(("—", "—", "0 chains"), (tiles[5].A, tiles[5].B, tiles[5].SubA));
        Assert.Equal("switched off", Sheet(a, b, skillchains: false).DamageTiles[5].SubA);
    }

    [Fact]
    public void A_parse_with_no_heal_lines_reads_as_dashes_not_zeroes()
    {
        var a = Run(30, Lines.Hit(10, "Hasaya", 100));
        var b = Run(30, Lines.Hit(10, "Hasaya", 100), Lines.Heal(12, "Mage", 300, use: 900), Lines.Heal(14, "Mage", 100, use: 901));
        var s = Sheet(a, b);

        Assert.False(s.A.HasHeals);
        Assert.Equal(("—", "400", "—"), (s.HealTiles[0].A, s.HealTiles[0].B, s.HealTiles[0].Change.Main));
        Assert.Equal(("—", "20.0"), (s.HealTiles[2].A, s.HealTiles[2].B));
        Assert.Equal(("—", "2"), (s.HealTiles[3].A, s.HealTiles[3].B));
        Assert.Equal(("—", "200"), (s.HealTiles[4].A, s.HealTiles[4].B));
        Assert.Equal(("—", "300"), (s.HealTiles[5].A, s.HealTiles[5].B));

        Assert.Equal("Parse A has no healing lines — healing is recorded by the VibeXI addon 0.3.0 and later, " +
                     "so its side reads as a dash.", s.HealNote);
        var mage = Assert.Single(s.Healers);
        Assert.Equal(new AB("—", "400"), new AB(mage.Healing.TextA, mage.Healing.TextB));
        Assert.Equal((0d, 1d), (mage.Healing.A, mage.Healing.B));
        Assert.Equal(Change.Nothing, mage.HealingChange);
        Assert.Equal(new AB("—", "2"), mage.Casts);
        Assert.Equal(new AB("—", "400"), new AB(s.Party.Healing.TextA, s.Party.Healing.TextB));
        Assert.Equal(new AB("—", "400"), Assert.Single(mage.Heals).Total);
        Assert.Equal(new AB("—", "400"), new AB(s.HealSpells[0].Amount.TextA, s.HealSpells[0].Amount.TextB));

        // No line at all for the side that could not be measured.
        Assert.All(s.HealPace.A, v => Assert.True(double.IsNaN(v)));
        Assert.Equal(400, s.HealPace.B[^1]);

        Assert.Equal("Neither parse has healing lines — healing is recorded by the VibeXI addon 0.3.0 and later.",
                     Sheet(a, a).HealNote);
    }

    [Fact]
    public void A_healer_missing_from_a_measured_run_healed_nothing_there()
    {
        var a = Run(30, Lines.Hit(10, "Hasaya", 100), Lines.Heal(12, "Mage", 300));
        var b = Run(30, Lines.Hit(10, "Hasaya", 100), Lines.Heal(12, "Priest", 500));
        var s = Sheet(a, b);

        Assert.Equal("", s.HealNote);
        Assert.Equal(["Priest", "Mage"], s.Healers.Select(h => h.Key));
        var priest = s.Healers[0];
        Assert.Equal(new AB("0", "500"), new AB(priest.Healing.TextA, priest.Healing.TextB));
        Assert.Equal("new", priest.HealingChange.Main);
        // Nothing cast is no average, not an average of 0.
        Assert.Equal(new AB("—", "500"), priest.Avg);
        Assert.Equal(Change.Nothing, priest.AvgChange);
        Assert.Equal(new AB("300", "500"), new AB(s.Party.Healing.TextA, s.Party.Healing.TextB));
    }

    [Fact]
    public void Two_runs_without_a_heal_between_them_say_so()
    {
        // The only heal in the file is a monster's, which is nobody's healing.
        var a = Run(30, Lines.Hit(10, "Hasaya", 100), Lines.Hit(11, "Goblin", 5, actorKind: "mob", target: "Hasaya",
                                                                targetKind: "player"),
                        Lines.Heal(12, "Goblin", 300, target: "Goblin"));
        var s = Sheet(a, a);
        Assert.True(s.A.HasHeals);
        Assert.Empty(s.Healers);
        Assert.Equal("No healing in either run.", s.HealNote);
    }

    // --------------------------------------------------------------- rows

    [Fact]
    public void A_character_in_one_run_only_has_dashes_on_the_other_side_and_reads_new()
    {
        var a = Run(30, Lines.Hit(10, "Hasaya", 400));
        var b = Run(30, Lines.Hit(10, "Hasaya", 300), Lines.Hit(12, "Tank", 100));
        var s = Sheet(a, b);

        Assert.Equal(["Hasaya", "Tank"], s.Actors.Select(r => r.Key));
        var tank = s.Actors[1];
        Assert.Equal((false, true), (tank.InA, tank.InB));
        Assert.Equal(new AB("—", "100"), new AB(tank.Damage.TextA, tank.Damage.TextB));
        Assert.Equal(new Change("new", "+100", ChangeTone.Better), tank.DamageChange);
        Assert.Equal(new AB("—", "5.0"), tank.Dps);
        Assert.Equal(Change.Nothing, tank.DpsChange);
        Assert.Equal(new AB("—", "25.0%"), tank.Share);

        // Bars are out of the largest figure in the table, either run's.
        Assert.Equal((1d, 0.75), (s.Actors[0].Damage.A, s.Actors[0].Damage.B));
        Assert.Equal((0d, 0.25), (tank.Damage.A, tank.Damage.B));
        Assert.Equal(new Change("−25.0%", "−100", ChangeTone.Worse), s.Actors[0].DamageChange);
    }

    [Fact]
    public void An_actions_list_pairs_each_action_across_the_runs()
    {
        var a = Run(30, Lines.Hit(10, "Hasaya", 100), Lines.Hit(11, "Hasaya", 0, hit: false));
        var b = Run(30, Lines.Hit(10, "Hasaya", 300), Lines.Hit(12, "Hasaya", 900, kind: "ws", action: "Tachi: Gekko"));
        var acts = Sheet(a, b).Actors[0].Actions;

        Assert.Equal(["Tachi: Gekko", "Attack"], acts.Select(x => x.Name));
        var ws = acts[0];
        Assert.Equal(new AB("—", "1"), ws.Uses);
        Assert.Equal(new AB("—", "900"), ws.Total);
        Assert.Equal("new", ws.TotalChange.Main);
        var attack = acts[1];
        Assert.Equal(new AB("2", "1"), attack.Uses);
        Assert.Equal(new AB("50.0%", "100.0%"), attack.Accuracy);
        Assert.Equal("+50.0 pt", attack.AccuracyChange.Main);
        Assert.Equal(new AB("100", "300"), attack.Avg);
        Assert.Equal(new Change("+200.0%", "+200", ChangeTone.Better), attack.TotalChange);
    }

    [Fact]
    public void Damage_types_and_targets_are_listed_with_each_runs_share()
    {
        var a = Run(30, Lines.Hit(10, "Hasaya", 100), Lines.Hit(12, "Hasaya", 300, kind: "ws", action: "Tachi: Gekko"));
        var b = Run(30, Lines.Hit(10, "Hasaya", 200, target: "Orc"));
        var s = Sheet(a, b);

        Assert.Equal(["Melee", "Weaponskills"], s.Kinds.Select(k => k.Label));
        Assert.Equal(new AB("25.0%", "100.0%"), s.Kinds[0].Share);
        Assert.Equal(new AB("300", "0"), new AB(s.Kinds[1].Damage.TextA, s.Kinds[1].Damage.TextB));
        Assert.Equal("−100.0%", s.Kinds[1].Change.Main);
        Assert.Equal(["Goblin", "Orc"], s.Targets.Select(t => t.Name));
        Assert.Equal("new", s.Targets[1].Change.Main);
    }

    // -------------------------------------------------------------- names

    [Fact]
    public void A_row_takes_its_job_from_the_newer_run_first()
    {
        var a = Run(30, Lines.Job("Hasaya", "SAM", "WAR"), Lines.Hit(10, "Hasaya", 100));
        var b = Run(30, Lines.Job("Hasaya", "DRG", "WAR"), Lines.Hit(10, "Hasaya", 100), Lines.Hit(11, "Tank", 50));
        var ids = Sheet(a, b).Ids;

        Assert.Equal(("SAM/WAR", "DRG/WAR", "DRG", 0), (ids["Hasaya"].JobA, ids["Hasaya"].JobB, ids["Hasaya"].ColorJob, ids["Hasaya"].Slot));
        // Never reported: no job, and the fallback colour of the row's place.
        Assert.Equal(("", "", "", 1), (ids["Tank"].JobA, ids["Tank"].JobB, ids["Tank"].ColorJob, ids["Tank"].Slot));
    }

    [Fact]
    public void Hiding_names_hides_every_name()
    {
        var lines = new[]
        {
            Lines.Job("Hasaya", "SAM", "WAR"), Lines.Job("Other", "SAM", "WAR"), Lines.Job("Mage", "WHM"),
            Lines.Hit(10, "Hasaya", 300), Lines.Hit(11, "Other", 200), Lines.Hit(12, "Stranger", 100),
            Lines.Heal(13, "Mage", 50, target: "Hasaya"), Lines.Heal(14, "Mage", 40, target: "Bystander"),
        };
        var run = Run(30, lines);
        var s = Sheet(run, run, hideNames: true);

        // The file's owner too: the two runs may be two people's.
        Assert.Equal("SAM/WAR", s.Ids["Hasaya"].Label);
        Assert.Equal("SAM/WAR 2", s.Ids["Other"].Label);
        Assert.Equal("Unknown job", s.Ids["Stranger"].Label);
        Assert.Equal("WHM", s.Ids["Mage"].Label);
        Assert.All(s.Ids.Values, id => Assert.True(id.Hidden));
        // Someone a heal landed on, with no row of their own, is not let through by name.
        Assert.Equal(["SAM/WAR", "Unknown job 2"], s.HealTargets.Select(t => t.Name));

        var shown = Sheet(run, run);
        Assert.Equal("Hasaya", shown.Ids["Hasaya"].Label);
        Assert.Equal(["Hasaya", "Bystander"], shown.HealTargets.Select(t => t.Name));
    }

    [Fact]
    public void By_job_combines_everyone_on_a_job_and_borrows_no_job_from_the_other_run()
    {
        var a = Run(30, Lines.Job("Hasaya", "SAM", "WAR"), Lines.Job("Other", "SAM", "NIN"), Lines.Job("Pestii", "PUP"),
                        Lines.Hit(10, "Hasaya", 300), Lines.Hit(11, "Other", 200), Lines.Hit(12, "Pestii", 100),
                        Lines.Heal(13, "Pestii", 70, target: "Hasaya"));
        // Pestii is in B, with no job reported there.
        var b = Run(30, Lines.Job("Hasaya", "SAM", "WAR"), Lines.Hit(10, "Hasaya", 300), Lines.Hit(12, "Pestii", 100));
        var s = Sheet(a, b, byJob: true);

        Assert.Equal(["SAM", "PUP", "Unknown job"], s.Actors.Select(r => r.Key));
        Assert.Equal(new AB("500", "300"), new AB(s.Actors[0].Damage.TextA, s.Actors[0].Damage.TextB));
        Assert.Equal("Hasaya, Other", s.Ids["SAM"].Members);
        Assert.Equal(("SAM", "SAM"), (s.Ids["SAM"].Label, s.Ids["SAM"].ColorJob));
        Assert.Equal(new AB("100", "—"), new AB(s.Actors[1].Damage.TextA, s.Actors[1].Damage.TextB));
        Assert.Equal(new AB("—", "100"), new AB(s.Actors[2].Damage.TextA, s.Actors[2].Damage.TextB));
        Assert.Equal(("Pestii", ""), (s.Ids["Unknown job"].Members, s.Ids["Unknown job"].ColorJob));

        var hidden = Sheet(a, b, byJob: true, hideNames: true);
        Assert.Equal("2 characters", hidden.Ids["SAM"].Members);
        Assert.Equal("1 character", hidden.Ids["PUP"].Members);
        // The rows are jobs, so every heal target is a name with no row: still hidden.
        Assert.Equal("SAM/WAR", Assert.Single(hidden.HealTargets).Name);
    }

    // -------------------------------------------------------------- chart

    [Fact]
    public void The_shorter_runs_line_ends_and_its_table_cells_are_dashes()
    {
        var a = Run(15, Lines.Hit(10, "Hasaya", 100), Lines.Hit(12, "Hasaya", 50));      // 5 s
        var b = Run(30, Lines.Hit(10, "Hasaya", 100), Lines.Hit(25, "Hasaya", 400));     // 20 s
        var s = Sheet(a, b);
        var table = CompareSheet.Table(s.DamagePace);

        Assert.Equal(new PaceLine("0:00", "100", "100", "+0"), table[0]);
        Assert.Equal(new PaceLine("0:04", "150", "100", "−50"), table.Single(r => r.Time == "0:04"));
        // Past its own end the shorter run has no figure, and so no difference.
        Assert.Equal(new PaceLine("0:06", "—", "100", "—"), table.Single(r => r.Time == "0:06"));
        Assert.Equal(new PaceLine("0:20", "—", "500", "—"), table[^1]);
        Assert.True(double.IsNaN(s.DamagePace.A[^1]));
    }

    // -------------------------------------------------------------- slots

    [Fact]
    public void A_slot_says_when_a_run_began_how_long_it_ran_and_who_was_in_it()
    {
        var run = Run(75, Lines.Hit(10, "Hasaya", 100), Lines.Hit(11, "Tank", 50), Lines.Hit(12, "Goblin", 5, actorKind: "mob"));
        var info = CompareSheet.Describe(run);
        var began = DateTimeOffset.FromUnixTimeMilliseconds(10_000).ToLocalTime();

        Assert.Equal(began.ToString("yyyy-MM-dd HH:mm:ss"), info.Started);
        Assert.Equal("01:05", info.Length);
        Assert.Equal("2 characters", info.Party);
        Assert.Equal("", info.Skipped);
    }

    [Fact]
    public void The_session_on_screen_can_be_taken_as_a_run_without_touching_it()
    {
        var t = new Tracker();
        t.Follow("Hasaya_2026.07.30.jsonl");
        // Nothing to take before the clock has started.
        Assert.Null(CompareSheet.Snapshot(t.Reader, t.Session, t.File, 5_000));
        t.Start(8_000);
        t.Feed([Lines.Hit(5, "Hasaya", 999), Lines.Hit(10, "Hasaya", 100), Lines.Hit(20, "Hasaya", 200)]);
        Assert.Null(CompareSheet.Snapshot(t.Reader, t.Session, t.File, 9_000));
        t.Count(Nobody, true, 25_000);

        var run = ParseFile.Import(CompareSheet.Snapshot(t.Reader, t.Session, t.File, 25_000)!);

        // The live clock was not paused by it.
        Assert.True(t.Session.Running);
        // The copy's is, at the moment it was taken, and it holds only what the session covers.
        Assert.Equal(25_000, run.Session.PausedAt);
        Assert.Equal(2, run.Source.Events.Count);
        Assert.Equal("Hasaya_2026.07.30.jsonl", run.File);
        var m = Compare.Measure(run);
        Assert.Equal((300d, 15d, 20d), (m.Total, m.Duration, m.Dps));

        // Later rows reach the session, not the copy.
        t.Feed([Lines.Hit(30, "Hasaya", 50)]);
        Assert.Equal(350, t.Count(Nobody, true, 40_000).Totals.Total);
        Assert.Equal(300, Compare.Measure(run).Total);
    }

    [Fact]
    public void A_paused_session_is_taken_with_the_clock_it_was_paused_on()
    {
        var t = new Tracker();
        t.Follow("Hasaya_2026.07.30.jsonl");
        t.Start(0);
        t.Feed([Lines.Hit(10, "Hasaya", 100)]);
        t.Count(Nobody, true, 20_000);
        t.TogglePause(20_000);

        var run = ParseFile.Import(CompareSheet.Snapshot(t.Reader, t.Session, t.File, 90_000)!);
        Assert.Equal(20_000, run.Session.PausedAt);
        Assert.Equal(10, Compare.Measure(run).Duration);
    }
}
