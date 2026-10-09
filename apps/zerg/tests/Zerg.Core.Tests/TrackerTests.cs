using System.Globalization;
using Zerg.Core.Charts;

namespace Zerg.Core.Tests;

/// <summary>Event-file lines made by hand, for the rules that sit above the counting.</summary>
static class Lines
{
    static int use;

    /// <summary>A damage row. <paramref name="t"/> is in seconds, as the file's is.</summary>
    public static string Hit(double t, string actor, double dmg, string kind = "melee", string action = "Attack",
                             string actorKind = "player", string target = "Goblin", bool hit = true,
                             string? owner = null, string targetKind = "mob", bool crit = false)
    {
        var pet = owner != null ? $",\"owner\":\"{owner}\",\"pet\":\"{actor}\"" : "";
        return string.Create(CultureInfo.InvariantCulture,
            $"{{\"t\":{t},\"seq\":1,\"use\":{++use},\"kind\":\"{kind}\",\"actor\":\"{actor}\",\"actorKind\":\"{actorKind}\"," +
            $"\"action\":\"{action}\",\"actionId\":1,\"target\":\"{target}\",\"targetKind\":\"{targetKind}\",\"dmg\":{dmg}," +
            $"\"hit\":{(hit ? "true" : "false")},\"crit\":{(crit ? "true" : "false")},\"burst\":false,\"msg\":1{pet}}}");
    }

    /// <summary>One target of a heal. A pet's names its <paramref name="owner"/>.</summary>
    public static string Heal(double t, string actor, double hp, string action = "Cure IV", string target = "Hasaya",
                              int? use = null, string? owner = null)
    {
        var pet = owner != null ? $",\"owner\":\"{owner}\",\"pet\":\"{actor}\"" : "";
        return string.Create(CultureInfo.InvariantCulture,
            $"{{\"kind\":\"heal\",\"t\":{t},\"seq\":1,\"use\":{use ?? ++Lines.use},\"via\":\"{(owner != null ? "pet" : "magic")}\"," +
            $"\"actor\":\"{actor}\",\"actorKind\":\"{(owner != null ? "pet" : "player")}\",\"action\":\"{action}\",\"actionId\":4," +
            $"\"target\":\"{target}\",\"targetKind\":\"player\",\"hp\":{hp},\"msg\":7{pet}}}");
    }

    public static string Job(string actor, string main, string? sub = null)
    {
        var s = sub != null ? $",\"sub\":\"{sub}\",\"subId\":1,\"subLvl\":37" : "";
        return $"{{\"kind\":\"job\",\"t\":1,\"actor\":\"{actor}\",\"main\":\"{main}\",\"mainId\":1,\"mainLvl\":75{s}}}";
    }
}

public class CastTests
{
    static readonly HashSet<string> Nobody = [];

    static Tracker Tracking(params string[] lines)
    {
        var t = new Tracker();
        t.Follow("Hasaya_2026.07.30.jsonl");
        t.Feed(lines);
        t.Cast.Assign(t.Reader.Events, t.Reader.Roster);
        return t;
    }

    [Fact]
    public void The_owner_takes_slot_zero_whoever_swung_first()
    {
        var t = Tracking(Lines.Hit(10, "Tank", 50), Lines.Hit(11, "Hasaya", 60), Lines.Hit(12, "Mage", 70));
        Assert.Equal(0, t.Cast.Slots["Hasaya"]);
        Assert.Equal(1, t.Cast.Slots["Tank"]);
        Assert.Equal(2, t.Cast.Slots["Mage"]);
    }

    [Fact]
    public void Monsters_get_no_slot_and_a_pet_shares_its_owners()
    {
        var t = Tracking(
            Lines.Job("Beast", "BST", "WHM"),
            Lines.Hit(10, "Goblin", 50, actorKind: "mob", target: "Hasaya", targetKind: "player"),
            Lines.Hit(11, "Fluffikins", 60, actorKind: "pet", owner: "Beast"),
            Lines.Hit(12, "Hasaya", 70));
        Assert.Equal(["Hasaya", "Beast"], t.Cast.Slots.OrderBy(p => p.Value).Select(p => p.Key));
    }

    [Fact]
    public void An_owner_known_only_through_their_pet_takes_a_slot_when_first_drawn()
    {
        // Nothing but the pet's rows names them, so nothing says they are a player.
        var t = Tracking(Lines.Hit(11, "Fluffikins", 60, actorKind: "pet", owner: "Beast"), Lines.Hit(12, "Tank", 70));
        Assert.Equal(["Tank"], t.Cast.Slots.Keys);
        Assert.Equal(1, t.Cast.SlotOf("Beast"));
        Assert.Equal(1, t.Cast.SlotOf("Beast"));
    }

    [Fact]
    public void An_owner_who_is_not_one_of_ours_yet_is_not_slotted_first()
    {
        // Nothing has said the owner is a player, so they wait their turn.
        var t = Tracking(Lines.Hit(10, "Tank", 50));
        Assert.Equal(0, t.Cast.Slots["Tank"]);
        Assert.False(t.Cast.Slots.ContainsKey("Hasaya"));
    }

    [Fact]
    public void Slots_survive_a_restart_and_new_names_take_the_next()
    {
        var t = Tracking(Lines.Hit(10, "Tank", 50), Lines.Hit(11, "Hasaya", 60));
        t.Start(20_000);
        t.Feed([Lines.Hit(21, "Mage", 70), Lines.Hit(22, "Tank", 50)]);
        t.Cast.Assign(t.Reader.Events, t.Reader.Roster);
        Assert.Equal(0, t.Cast.Slots["Hasaya"]);
        Assert.Equal(1, t.Cast.Slots["Tank"]);
        Assert.Equal(2, t.Cast.Slots["Mage"]);
    }

    [Fact]
    public void Characters_sharing_a_job_are_shaded_in_slot_order()
    {
        var t = Tracking(
            Lines.Job("Hasaya", "SAM", "WAR"), Lines.Job("Rhyllis", "SAM", "WAR"), Lines.Job("Tank", "PLD", "WAR"),
            Lines.Job("Nobody", "NON"),
            Lines.Hit(10, "Rhyllis", 50), Lines.Hit(11, "Tank", 50), Lines.Hit(12, "Hasaya", 60), Lines.Hit(13, "Nobody", 5));
        Assert.Equal(0, t.Cast.VariantOf("Hasaya"));
        Assert.Equal(1, t.Cast.VariantOf("Rhyllis"));
        Assert.Equal(0, t.Cast.VariantOf("Tank"));
        Assert.Equal(0, t.Cast.VariantOf("Nobody"));
    }

    [Fact]
    public void Hidden_names_are_jobs_numbered_in_slot_order_and_the_owner_keeps_theirs()
    {
        var t = Tracking(
            Lines.Job("Hasaya", "SAM", "WAR"), Lines.Job("Rhyllis", "SAM", "WAR"), Lines.Job("Other", "SAM", "WAR"),
            Lines.Job("Vermillion", "BLM"), Lines.Job("Sylviane", "WHM", "BLM"),
            Lines.Hit(10, "Rhyllis", 50), Lines.Hit(11, "Hasaya", 60), Lines.Hit(12, "Other", 60),
            Lines.Hit(13, "Vermillion", 60), Lines.Hit(14, "Trust", 5));
        Assert.Null(t.Cast.AliasOf("Hasaya"));
        Assert.Equal("SAM/WAR", t.Cast.AliasOf("Rhyllis"));
        Assert.Equal("SAM/WAR 2", t.Cast.AliasOf("Other"));
        Assert.Equal("BLM", t.Cast.AliasOf("Vermillion"));
        Assert.Equal(Cast.UnknownJob, t.Cast.AliasOf("Trust"));
        // Never acted, so never slotted, but her job line makes her a known player.
        Assert.Equal("WHM/BLM", t.Cast.AliasOf("Sylviane"));
        Assert.Null(t.Cast.AliasOf("Goblin"));
    }

    [Fact]
    public void A_shade_steps_away_from_the_page_first()
    {
        var c = ((byte)200, (byte)100, (byte)0);
        Assert.Equal(c, Shades.Step(c, 0, lightTheme: false));
        // Dark theme: lighter, then darker, then lighter again by twice as much.
        Assert.Equal(((byte)217, (byte)147, (byte)77), Shades.Step(c, 1, lightTheme: false));
        Assert.Equal(((byte)140, (byte)70, (byte)0), Shades.Step(c, 2, lightTheme: false));
        Assert.Equal(((byte)233, (byte)193, (byte)153), Shades.Step(c, 3, lightTheme: false));
        // Light theme: the other way round.
        Assert.Equal(((byte)140, (byte)70, (byte)0), Shades.Step(c, 1, lightTheme: true));
        Assert.Equal(((byte)217, (byte)147, (byte)77), Shades.Step(c, 2, lightTheme: true));
        // Never all the way to white or black.
        Assert.Equal(((byte)50, (byte)25, (byte)0), Shades.Step(c, 9, lightTheme: true));
    }

    [Fact]
    public void The_chart_table_shows_about_sixteen_rows_and_always_the_last()
    {
        Assert.Empty(Sampling.Rows(0));
        Assert.Equal([0, 1, 2], Sampling.Rows(3));
        Assert.Equal(16, Sampling.Rows(16).Count);
        var rows = Sampling.Rows(100);   // every seventh, and the end
        Assert.Equal([0, 7, 14], rows.Take(3));
        Assert.Equal(99, rows[^1]);
        Assert.Equal(16, rows.Count);
    }
}

public class TrackerTests
{
    static readonly HashSet<string> Nobody = [];

    static Tracker Following()
    {
        var t = new Tracker();
        t.Follow("Hasaya_2026.07.30.jsonl");
        return t;
    }

    [Fact]
    public void Nothing_is_counted_before_start()
    {
        var t = Following();
        t.Feed([Lines.Hit(10, "Hasaya", 100)]);
        var c = t.Count(Nobody, true, 15_000);
        Assert.Empty(c.Events);
        Assert.Equal(0, c.Totals.Total);
        Assert.Empty(c.Listed);
        Assert.Equal(0, c.Elapsed);
        Assert.Equal(1, t.Lines);
    }

    [Fact]
    public void The_first_counted_row_after_start_is_the_zero()
    {
        var t = Following();
        t.Feed([Lines.Hit(10, "Hasaya", 100)]);
        t.Start(20_400);
        Assert.Empty(t.Reader.Events);                       // dropped by Start
        Assert.False(t.Count(Nobody, true, 21_000).Latched);
        Assert.True(t.Session.Armed);

        // A monster's swing does not start the clock; the party's does.
        t.Feed([Lines.Hit(22, "Goblin", 9, actorKind: "mob", target: "Hasaya", targetKind: "player"), Lines.Hit(25, "Hasaya", 100)]);
        var c = t.Count(Nobody, true, 35_000);
        Assert.True(c.Latched);
        Assert.Equal(25_000, t.Session.StartedAt);
        Assert.Equal(10_000, c.Elapsed);
        Assert.Equal(100, c.Totals.Total);
        Assert.Equal(10, c.Totals.Actors[0].Dps);
        Assert.False(t.Count(Nobody, true, 36_000).Latched);  // once
    }

    [Fact]
    public void A_swing_in_the_second_start_was_pressed_counts()
    {
        var t = Following();
        t.Start(20_900);
        t.Feed([Lines.Hit(20, "Hasaya", 100)]);
        Assert.Equal(100, t.Count(Nobody, true, 21_000).Totals.Total);
    }

    [Fact]
    public void An_excluded_character_does_not_start_the_clock_until_included()
    {
        var t = Following();
        t.Start(20_000);
        t.Feed([Lines.Hit(21, "Hasaya", 100), Lines.Hit(24, "Tank", 50)]);
        var c = t.Count(new HashSet<string> { "Hasaya" }, true, 30_000);
        Assert.Equal(24_000, t.Session.StartedAt);
        Assert.Equal(50, c.Totals.Total);
        // The zero is set once: including them later does not move it.
        c = t.Count(Nobody, true, 30_000);
        Assert.Equal(24_000, t.Session.StartedAt);
        Assert.Equal(50, c.Totals.Total);
    }

    [Fact]
    public void An_excluded_character_keeps_their_chip()
    {
        var t = Following();
        t.Start(20_000);
        t.Feed([Lines.Hit(21, "Hasaya", 100), Lines.Hit(22, "Tank", 150)]);
        var c = t.Count(new HashSet<string> { "Tank" }, true, 30_000);
        Assert.Equal(["Hasaya"], c.Totals.Actors.Select(a => a.Name));
        Assert.Equal(["Tank", "Hasaya"], c.Listed.Select(a => a.Name));
        Assert.Equal(100, c.Totals.Total);
    }

    [Fact]
    public void Skillchains_off_drops_them_and_their_column()
    {
        var t = Following();
        t.Start(20_000);
        t.Feed([Lines.Hit(21, "Hasaya", 100, kind: "ws", action: "Tachi: Jinpu"),
                Lines.Hit(21, "Hasaya", 40, kind: "skillchain", action: "Skillchain: Fusion")]);
        Assert.Equal(140, t.Count(Nobody, true, 30_000).Totals.Total);
        var off = t.Count(Nobody, false, 30_000);
        Assert.Equal(100, off.Totals.Total);
        Assert.Null(off.Totals.Actors[0].ScTotal);
    }

    [Fact]
    public void Pause_freezes_the_clock_and_drops_what_lands_in_it()
    {
        var t = Following();
        t.Start(20_000);
        t.Feed([Lines.Hit(20, "Hasaya", 100)]);
        t.Count(Nobody, true, 21_000);
        t.Second(30_000);                                      // pause
        Assert.NotNull(t.Session.PausedAt);
        t.Feed([Lines.Hit(40, "Hasaya", 999)]);
        var held = t.Count(Nobody, true, 50_000);
        Assert.Equal(100, held.Totals.Total);
        Assert.Equal(1_000, held.Elapsed);                     // read at the last swing, not at the press
        t.Second(60_000);                                      // resume
        t.Feed([Lines.Hit(65, "Hasaya", 50)]);
        var c = t.Count(Nobody, true, 70_000);
        Assert.Equal(150, c.Totals.Total);
        Assert.Equal(20_000, c.Elapsed);
        Assert.Equal(15_000, c.Events[^1].T);                  // the pause is subtracted
    }

    [Fact]
    public void A_paused_clock_ends_with_the_last_party_damage_row()
    {
        var t = Following();
        t.Start(20_000);
        t.Feed([Lines.Hit(20, "Tank", 120), Lines.Hit(24, "Hasaya", 300), Lines.Hit(25, "Hasaya", 0, hit: false),
                // Neither of these holds the clock open: a monster's swing, and a heal.
                Lines.Hit(27, "Goblin", 9, actorKind: "mob", target: "Hasaya", targetKind: "player"),
                Lines.Heal(28, "Sylviane", 300)]);
        t.Count(Nobody, true, 29_000);
        t.Second(60_000);                                      // pause, 35 s after the last swing

        // To the end of the second that swing landed in, a miss though it was.
        var held = t.Count(Nobody, true, 90_000);
        Assert.Equal((60_000d, 26_000d), (t.Session.PausedAt, t.Session.EndedAt));
        Assert.Equal(6_000, held.Elapsed);
        Assert.Equal([("Hasaya", 50d), ("Tank", 20d)], held.Totals.Actors.Select(a => (a.Name, a.Dps)));
        // What came after it is outside the session, the heal included.
        Assert.Equal(0, held.Healing.Total);

        // The end is the session's, not the viewer's: no switch moves it.
        Assert.Equal(6_000, t.Count(new HashSet<string> { "Hasaya" }, true, 90_000).Elapsed);
        Assert.Equal(6_000, t.Count(Nobody, false, 90_000).Elapsed);
    }

    [Fact]
    public void A_row_from_before_the_press_read_after_it_still_ends_the_clock()
    {
        var t = Following();
        t.Start(20_000);
        t.Feed([Lines.Hit(20, "Hasaya", 100)]);
        t.Count(Nobody, true, 21_000);
        t.Second(30_400);
        Assert.Equal(21_000, t.Session.EndedAt);

        // Written at 30 s, and polled after the press at 30.4 s.
        t.Feed([Lines.Hit(30, "Hasaya", 50), Lines.Hit(31, "Hasaya", 999)]);
        var held = t.Count(Nobody, true, 50_000);
        Assert.Equal(150, held.Totals.Total);
        // Its second runs past the press, and the clock never ends later than it stopped.
        Assert.Equal(30_400, t.Session.EndedAt);
        Assert.Equal(10_400, held.Elapsed);
    }

    [Fact]
    public void A_pause_with_nothing_counted_since_the_last_one_ends_before_it()
    {
        var t = Following();
        t.Start(20_000);
        t.Feed([Lines.Hit(20, "Hasaya", 100), Lines.Hit(25, "Hasaya", 200)]);
        t.Count(Nobody, true, 26_000);
        t.Second(40_000);                                      // pause
        Assert.Equal(6_000, t.Count(Nobody, true, 45_000).Elapsed);
        t.Second(50_000);                                      // resume: the wait is back on the clock
        Assert.Null(t.Session.EndedAt);
        Assert.Equal(30_000, t.Count(Nobody, true, 60_000).Elapsed);
        t.Second(70_000);                                      // paused again, and nobody swung
        Assert.Equal(6_000, t.Count(Nobody, true, 99_000).Elapsed);

        // The file is one that was paused there: the later pause is inside that one.
        var parse = ParseFile.Import(t.Export(99_000)!);
        Assert.Equal(26_000, parse.Session.PausedAt);
        Assert.Empty(parse.Session.Spans);
        Assert.Equal(6_000, Tracker.Of(parse).Count(Nobody, true).Elapsed);
    }

    [Fact]
    public void A_snapped_clock_never_runs_into_an_earlier_pause()
    {
        // Paused 300 ms into the last swing's second, resumed, and paused again.
        var s = Session.Arm(20_000).Start(20_000).Pause(25_300).Resume(40_000).Pause(50_000);
        Assert.Equal(15_300, s.Elapsed(99_000));
        Assert.Equal(25_300, s.Snap(25_000).EndedAt);
        Assert.Equal(5_300, s.Elapsed(99_000));
        Assert.Null(s.At(25_300));

        // Nothing to snap to, or a session that is running: read as before.
        Assert.Null(s.Snap(null).EndedAt);
        Assert.Equal(15_300, s.Elapsed(99_000));
        Assert.Null(s.Resume(60_000).Snap(25_000).EndedAt);
    }

    [Fact]
    public void The_second_button_cancels_while_armed_and_keeps_the_rows()
    {
        var t = Following();
        t.Start(20_000);
        t.Feed([Lines.Hit(10, "Hasaya", 100)]);                // before the press: does not latch
        t.Count(Nobody, true, 21_000);
        Assert.True(t.Session.Armed);
        t.Second(22_000);
        Assert.Null(t.Session.ArmedAt);
        Assert.Single(t.Reader.Events);
        t.TogglePause(23_000);                                 // nothing to hold
        Assert.Null(t.Session.PausedAt);
    }

    [Fact]
    public void Start_during_a_session_rearms_and_keeps_the_roster()
    {
        var t = Following();
        t.Feed([Lines.Job("Hasaya", "SAM", "WAR")]);
        t.Start(20_000);
        t.Feed([Lines.Hit(21, "Hasaya", 100)]);
        t.Count(Nobody, true, 30_000);
        long lines = t.Lines;

        t.Start(40_000);
        Assert.True(t.Session.Armed);
        Assert.Empty(t.Reader.Events);
        Assert.Equal(lines, t.Lines);
        Assert.Equal("SAM/WAR", t.Reader.Roster.JobLabel("Hasaya"));
        Assert.Equal(0, t.Count(Nobody, true, 41_000).Totals.Total);
    }

    [Fact]
    public void The_first_button_arms_at_once_while_nothing_is_measured()
    {
        var t = Following();
        Assert.True(t.First(20_000));                          // idle
        Assert.True(t.Session.Armed);
        Assert.True(t.First(25_000));                          // armed: nothing to lose yet
        Assert.Equal(25_000, t.Session.ArmedAt);
        Assert.False(t.Asking);
    }

    [Fact]
    public void Over_a_measurement_the_first_button_asks_and_the_next_press_confirms()
    {
        var t = Following();
        t.Start(20_000);
        t.Feed([Lines.Hit(21, "Hasaya", 100)]);
        t.Count(Nobody, true, 30_000);

        Assert.False(t.First(40_000));
        Assert.True(t.Asking);
        // Asked about, not done: the pull is measured as before.
        Assert.Equal(21_000, t.Session.StartedAt);
        Assert.Equal(100, t.Count(Nobody, true, 40_100).Totals.Total);

        // The second half of a double-click is not an answer.
        Assert.False(t.First(40_000 + Tracker.Settle - 1));
        Assert.True(t.Asking);
        Assert.Single(t.Reader.Events);

        Assert.True(t.First(40_000 + Tracker.Settle));
        Assert.False(t.Asking);
        Assert.True(t.Session.Armed);
        Assert.Empty(t.Reader.Events);
    }

    [Fact]
    public void The_second_button_calls_off_a_restart_and_holds_nothing()
    {
        var t = Following();
        t.Start(20_000);
        t.Feed([Lines.Hit(21, "Hasaya", 100)]);
        t.Count(Nobody, true, 30_000);
        t.First(40_000);

        t.Second(41_000);
        Assert.False(t.Asking);
        Assert.Null(t.Session.PausedAt);                       // Cancel, not Pause
        Assert.Equal(100, t.Count(Nobody, true, 42_000).Totals.Total);

        t.Second(43_000);                                      // Pause again
        Assert.Equal(43_000, t.Session.PausedAt);
        // Asked again, the first press asks again.
        Assert.False(t.First(50_000));
        Assert.True(t.Asking);
    }

    [Fact]
    public void A_restart_nobody_answered_or_a_new_file_ends_the_question()
    {
        var t = Following();
        t.Start(20_000);
        t.Feed([Lines.Hit(21, "Hasaya", 100)]);
        t.Count(Nobody, true, 30_000);

        t.First(40_000);
        t.Withdraw();
        Assert.False(t.Asking);
        Assert.Single(t.Reader.Events);

        t.First(50_000);
        t.Follow("Hasaya_2026.07.31.jsonl");
        Assert.False(t.Asking);
        // Nothing is measured in the new file, so Start is Start.
        Assert.True(t.First(60_000));
    }

    [Fact]
    public void A_new_file_starts_from_nothing()
    {
        var t = Following();
        t.Start(20_000);
        t.Feed([Lines.Hit(21, "Hasaya", 100)]);
        t.Count(Nobody, true, 30_000);

        t.Follow("Rhyllis_2026.07.31.jsonl");
        Assert.Null(t.Session.ArmedAt);
        Assert.Null(t.Session.StartedAt);
        Assert.Empty(t.Reader.Events);
        Assert.Empty(t.Cast.Slots);
        Assert.Equal("Rhyllis", t.Reader.Roster.Owner);
        Assert.Equal(0, t.Lines);
    }

    [Theory]
    [InlineData("Rhyllis-20260731213045.jsonl", "Rhyllis")]      // one per load: what the addon writes
    [InlineData("Rhyllis-20260731000000.JSONL", "Rhyllis")]
    [InlineData("Rhyllis_2026.07.31.jsonl", "Rhyllis")]          // one per day: what it wrote before
    [InlineData("Rhyllis-20260731213045 (2).jsonl", "Rhyllis")]  // a copy still says whose it is
    [InlineData("-20260731213045.jsonl", null)]
    [InlineData("20260731213045.jsonl", null)]
    public void The_owner_is_read_from_the_file_name(string file, string? owner)
    {
        var t = Following();
        t.Follow(file);
        Assert.Equal(owner, t.Reader.Roster.Owner);
        Assert.Equal(owner, EventReader.ParseFilename(file).Owner);
    }

    [Fact]
    public void The_date_is_read_from_the_file_name()
    {
        Assert.Equal(new DateOnly(2026, 7, 31), EventReader.ParseFilename("Rhyllis-20260731213045.jsonl").Date);
        Assert.Equal(new DateOnly(2026, 7, 31), EventReader.ParseFilename("Rhyllis_2026.07.31.jsonl").Date);
        Assert.Null(EventReader.ParseFilename("Rhyllis-213045.jsonl").Date);
    }

    [Fact]
    public void Another_folder_leaves_no_file_and_no_session()
    {
        var t = Following();
        t.Start(20_000);
        t.Feed([Lines.Hit(21, "Hasaya", 100)]);
        t.Count(Nobody, true, 30_000);

        t.Unfollow();
        Assert.Null(t.File);
        Assert.Null(t.Session.ArmedAt);
        Assert.Null(t.Session.StartedAt);
        Assert.Empty(t.Reader.Events);
        Assert.Empty(t.Cast.Slots);
        Assert.Equal(0, t.Lines);
        Assert.Equal(0, t.Count(Nobody, true, 31_000).Totals.Total);
    }

    [Fact]
    public void The_biggest_hit_names_who_and_what()
    {
        var t = Following();
        t.Start(20_000);
        t.Feed([Lines.Hit(21, "Hasaya", 100), Lines.Hit(22, "Tank", 700, kind: "ws", action: "Savage Blade"),
                Lines.Hit(23, "Hasaya", 700, kind: "ws", action: "Tachi: Jinpu")]);
        var best = t.Count(Nobody, true, 30_000).Best;
        Assert.NotNull(best);
        Assert.Equal(700, best.Max);
        Assert.Equal("Hasaya", best.Who);                       // Hasaya leads, so theirs is found first
        Assert.Equal("Tachi: Jinpu", best.What);
        Assert.Null(Following().Count(Nobody, true, 1).Best);
    }

    // ------------------------------------------------------- the Party line

    [Fact]
    public void The_party_line_is_counted_from_everyones_swings_together()
    {
        var t = Following();
        t.Start(20_000);
        t.Feed([
            // Hasaya: four swings, three land; two weaponskills, one lands; closes a chain.
            Lines.Hit(21, "Hasaya", 100), Lines.Hit(22, "Hasaya", 100), Lines.Hit(23, "Hasaya", 100),
            Lines.Hit(24, "Hasaya", 0, hit: false),
            Lines.Hit(25, "Hasaya", 400, kind: "ws", action: "Tachi: Jinpu"),
            Lines.Hit(26, "Hasaya", 0, kind: "ws", action: "Tachi: Jinpu", hit: false),
            Lines.Hit(25, "Hasaya", 40, kind: "skillchain", action: "Skillchain: Fusion"),
            // Tank: two swings, neither lands; one weaponskill; a pet that swings twice and lands once.
            Lines.Hit(27, "Tank", 0, hit: false), Lines.Hit(28, "Tank", 0, hit: false),
            Lines.Hit(29, "Tank", 200, kind: "ws", action: "Savage Blade"),
            Lines.Hit(30, "Fluffikins", 60, actorKind: "pet", owner: "Tank"),
            Lines.Hit(31, "Fluffikins", 0, actorKind: "pet", owner: "Tank", hit: false),
        ]);
        var totals = t.Count(Nobody, true, 40_000).Totals;
        var party = totals.Party;
        Assert.Equal(1000, totals.Total);

        // Three of six swings: not the mean of the rows' 75% and 0%.
        Assert.Equal(0.75, totals.Actors[0].AutoAcc);
        Assert.Equal(0, totals.Actors[1].AutoAcc);
        Assert.Equal(0.5, party.AutoAcc);

        Assert.Equal(600, party.WsTotal);
        Assert.Equal(300, party.WsAvg);                         // over the two that dealt damage
        Assert.Equal(0.6, party.WsShare);
        Assert.Equal(2.0 / 3, party.WsAcc);                     // two of three used
        Assert.Equal(40, party.ScTotal);
        Assert.Equal(0.04, party.ScShare);
        Assert.Equal(60, party.PetTotal);
        Assert.Equal(0.5, party.PetAcc);

        // The sums are the rows' own counts added up, and each row is as it was.
        Assert.Equal(totals.Actors.Sum(a => a.Split.AutoTries), totals.Split.AutoTries);
        Assert.Equal(totals.Actors.Sum(a => a.Split.WsTotal), totals.Split.WsTotal);
        Assert.Equal(totals.Actors.Sum(a => a.Split.PetTotal), totals.Split.PetTotal);
        Assert.Equal(400, totals.Actors[0].WsTotal);
        Assert.Equal(0.5, totals.Actors[0].WsAcc);
        Assert.Null(totals.Actors[0].PetTotal);
        Assert.Equal(60, totals.Actors[1].PetTotal);
    }

    [Fact]
    public void A_party_with_nothing_to_measure_prints_dashes_on_its_line()
    {
        var t = Following();
        t.Start(20_000);
        // Nobody weaponskilled, closed a chain or had a pet.
        t.Feed([Lines.Hit(21, "Hasaya", 100), Lines.Hit(22, "Tank", 50), Lines.Hit(23, "Tank", 0, hit: false)]);
        var party = t.Count(Nobody, true, 30_000).Totals.Party;
        Assert.Equal(2.0 / 3, party.AutoAcc);
        Assert.Null(party.WsTotal);
        Assert.Null(party.WsAvg);
        Assert.Null(party.WsShare);
        Assert.Null(party.WsAcc);
        Assert.Null(party.ScTotal);
        Assert.Null(party.ScShare);
        Assert.Null(party.PetTotal);
        Assert.Null(party.PetAcc);

        // A party of mages: no swings either. And nobody at all.
        var mages = Following();
        mages.Start(20_000);
        mages.Feed([Lines.Hit(21, "Mage", 300, kind: "magic", action: "Thunder III")]);
        Assert.Null(mages.Count(Nobody, true, 30_000).Totals.Party.AutoAcc);
        Assert.Equal(default, Following().Count(Nobody, true, 1).Totals.Party);
    }

    [Fact]
    public void The_party_line_follows_the_filters_as_the_rows_do()
    {
        var t = Following();
        t.Start(20_000);
        t.Feed([Lines.Hit(21, "Hasaya", 100, kind: "ws", action: "Tachi: Jinpu"),
                Lines.Hit(21, "Hasaya", 40, kind: "skillchain", action: "Skillchain: Fusion"),
                Lines.Hit(22, "Tank", 0, hit: false)]);
        // One character alone: the party's line is that character's row.
        var alone = t.Count(new HashSet<string> { "Tank" }, true, 30_000).Totals;
        var row = alone.Actors[0];
        Assert.Equal(new SplitFigures(row.AutoAcc, row.WsTotal, row.WsAvg, row.WsShare, row.WsAcc,
                                      row.ScTotal, row.ScShare, row.PetTotal, row.PetAcc), alone.Party);
        Assert.Null(alone.Party.AutoAcc);                        // the excluded character's miss is not in it

        // Skillchains off: a dash, as in the rows.
        var off = t.Count(Nobody, false, 30_000).Totals.Party;
        Assert.Null(off.ScTotal);
        Assert.Equal(0, off.AutoAcc);                            // Tank's one swing, which missed
        Assert.Equal(1, off.WsShare);
    }

    // ---------------------------------------------------------------- heals

    [Fact]
    public void Heals_are_counted_beside_the_damage_and_never_in_it()
    {
        var t = Following();
        t.Start(20_000);
        t.Feed([Lines.Hit(21, "Hasaya", 100), Lines.Heal(22, "Sylviane", 300), Lines.Heal(23, "Sylviane", 200, "Cure III")]);
        var c = t.Count(Nobody, true, 31_000);
        Assert.Equal(100, c.Totals.Total);
        Assert.Equal(["Hasaya"], c.Listed.Select(a => a.Name));
        Assert.Equal(500, c.Healing.Total);
        Assert.Equal(2, c.Healing.Casts);
        Assert.Equal(["Sylviane"], c.Healers.Select(a => a.Name));
        Assert.Equal([1000d, 2000d], c.Heals.Select(h => h.T));   // on the session clock
    }

    [Fact]
    public void A_heal_does_not_start_the_clock()
    {
        var t = Following();
        t.Start(20_000);
        t.Feed([Lines.Heal(21, "Sylviane", 300)]);
        var c = t.Count(Nobody, true, 25_000);
        Assert.False(c.Latched);
        Assert.True(t.Session.Armed);
        Assert.Empty(c.Heals);
        Assert.Equal(0, c.Healing.Total);

        // The first hit is the zero, and the cure before it falls before it.
        t.Feed([Lines.Hit(26, "Hasaya", 100), Lines.Heal(27, "Sylviane", 150)]);
        c = t.Count(Nobody, true, 30_000);
        Assert.True(c.Latched);
        Assert.Equal(150, c.Healing.Total);
    }

    [Fact]
    public void An_excluded_healer_is_left_out_and_keeps_their_chip()
    {
        var t = Following();
        t.Start(20_000);
        t.Feed([Lines.Hit(21, "Hasaya", 100), Lines.Heal(22, "Sylviane", 300), Lines.Heal(23, "Rhyllis", 90, "Curing Waltz")]);
        var c = t.Count(new HashSet<string> { "Sylviane" }, true, 30_000);
        Assert.Equal(["Rhyllis"], c.Healing.Actors.Select(a => a.Name));
        Assert.Equal(90, c.Healing.Total);
        Assert.Equal(["Sylviane", "Rhyllis"], c.Healers.Select(a => a.Name));
        // One list for both sections: the damage side is untouched by a healer's exclusion.
        Assert.Equal(100, c.Totals.Total);
    }

    [Fact]
    public void A_pets_heal_is_its_owners_and_in_a_column_of_its_own()
    {
        var t = Following();
        t.Start(20_000);
        t.Feed([Lines.Hit(21, "Hasaya", 100), Lines.Heal(22, "Sylviane", 300),
                Lines.Heal(23, "Wyvern", 420, "Healing Breath III", owner: "Parabellum")]);
        var c = t.Count(Nobody, true, 30_000);
        Assert.Equal(300, c.Healing.Total);
        Assert.Equal(420, c.Healing.PetTotal);
        // Listed by everything they healed, pet included; led by healing alone.
        Assert.Equal(["Parabellum", "Sylviane"], c.Healing.Actors.Select(a => a.Name));
        Assert.Equal("Sylviane", c.TopHealer?.Name);
        var best = c.BestHeal;
        Assert.NotNull(best);
        Assert.Equal(420, best.Max);
        Assert.Equal("Parabellum", best.Who);
        Assert.Equal("Wyvern: Healing Breath III", best.What);
        // Excluding the owner takes the pet's heals with them.
        c = t.Count(new HashSet<string> { "Parabellum" }, true, 30_000);
        Assert.Equal(0, c.Healing.PetTotal);
        Assert.Equal(["Parabellum", "Sylviane"], c.Healers.Select(a => a.Name));
    }

    [Fact]
    public void Nobody_leads_and_nothing_is_biggest_with_no_healing()
    {
        var t = Following();
        t.Start(20_000);
        t.Feed([Lines.Hit(21, "Hasaya", 100)]);
        var c = t.Count(Nobody, true, 30_000);
        Assert.Null(c.TopHealer);
        Assert.Null(c.BestHeal);
        // A cure on someone at full health is a cast that healed nothing: it
        // is the biggest heal there is, and still nobody leads.
        t.Feed([Lines.Heal(22, "Sylviane", 0)]);
        c = t.Count(Nobody, true, 30_000);
        Assert.Null(c.TopHealer);
        Assert.Equal(0, c.BestHeal?.Max);
        Assert.Equal(1, c.Healing.Casts);
    }

    [Fact]
    public void Heals_that_land_in_a_pause_are_dropped_and_a_restart_drops_them_all()
    {
        var t = Following();
        t.Start(20_000);
        t.Feed([Lines.Hit(20, "Hasaya", 100), Lines.Heal(21, "Sylviane", 300), Lines.Hit(22, "Hasaya", 100)]);
        t.Count(Nobody, true, 23_000);
        t.Second(30_000);                                      // pause
        t.Feed([Lines.Heal(40, "Sylviane", 999)]);
        Assert.Equal(300, t.Count(Nobody, true, 50_000).Healing.Total);
        t.Start(60_000);
        Assert.Empty(t.Reader.Heals);
        Assert.Equal(0, t.Count(Nobody, true, 61_000).Healing.Total);
    }

    [Fact]
    public void Heals_read_as_rows_fold_a_cast_and_never_miss()
    {
        var t = Following();
        t.Start(20_000);
        t.Feed([Lines.Hit(20, "Hasaya", 100),
                Lines.Heal(21, "Sylviane", 250, "Curaga II", "Hasaya", use: 5001),
                Lines.Heal(21, "Sylviane", 0, "Curaga II", "Rhyllis", use: 5001),
                Lines.Heal(25, "Sylviane", 0, "Curaga II", "Hasaya", use: 5002)]);
        var c = t.Count(Nobody, true, 30_000);
        var rows = Healing.AsEvents(c.Heals);
        Assert.All(rows, r => Assert.True(r.Hit));
        var d = Counting.Distribution(rows, "Sylviane", "Curaga II");
        Assert.Equal(2, d.Count);                              // per cast, not per target
        Assert.Equal(0, d.Misses);
        Assert.Equal(250, d.Total);
        Assert.Equal("2 targets", d.Events[0].Target);
        var line = Counting.Cumulative(rows, ["Sylviane"], 0, c.Elapsed);
        Assert.Equal(250, line.Series[0].Values[^1]);
    }

    // ------------------------------------------------------ the two buttons

    [Fact]
    public void The_buttons_before_start()
    {
        var v = SessionView.Of(Session.Idle());
        Assert.Equal("Start", v.StartText);
        Assert.Equal(StartLook.Idle, v.StartLook);
        Assert.True(v.StartEnabled);
        Assert.Equal("Pause", v.SecondText);
        Assert.False(v.SecondEnabled);
        Assert.Equal(SecondLook.Off, v.SecondLook);
        Assert.Equal(SessionLight.Idle, v.Light);
    }

    [Fact]
    public void The_buttons_while_armed()
    {
        var v = SessionView.Of(Session.Arm(5000));
        Assert.Equal("Restart", v.StartText);
        Assert.Equal(StartLook.Armed, v.StartLook);
        Assert.Equal("Cancel", v.SecondText);
        Assert.True(v.SecondEnabled);
        Assert.False(v.SecondIsToggle);
        Assert.Equal(SecondLook.Cancel, v.SecondLook);
        Assert.Equal(SessionLight.Armed, v.Light);
    }

    [Fact]
    public void The_buttons_while_running_and_held()
    {
        var s = Session.Arm(5000).Start(6000);
        var v = SessionView.Of(s);
        Assert.Equal("Restart", v.StartText);
        Assert.Equal(StartLook.Running, v.StartLook);
        Assert.Equal("Pause", v.SecondText);
        Assert.True(v.SecondIsToggle);
        Assert.False(v.SecondPressed);
        Assert.Equal(SecondLook.Live, v.SecondLook);
        Assert.Equal(SessionLight.Live, v.Light);

        v = SessionView.Of(s.Pause(9000));
        Assert.Equal("Resume", v.SecondText);
        Assert.True(v.SecondPressed);
        Assert.Equal(SecondLook.Held, v.SecondLook);
        Assert.Equal(SessionLight.Held, v.Light);
    }

    [Fact]
    public void The_buttons_while_a_restart_is_asked_about()
    {
        var s = Session.Arm(5000).Start(6000);
        Assert.True(SessionView.Of(s).StartAsks);

        var v = SessionView.Of(s, asking: true);
        Assert.Equal("Confirm", v.StartText);
        Assert.Equal(StartLook.Confirm, v.StartLook);
        Assert.False(v.StartAsks);
        Assert.Equal("Cancel", v.SecondText);
        Assert.True(v.SecondEnabled);
        Assert.False(v.SecondIsToggle);
        Assert.Equal(SecondLook.Cancel, v.SecondLook);
        // Still being measured, and the light says so.
        Assert.Equal(SessionLight.Live, v.Light);
        Assert.Equal(SessionLight.Held, SessionView.Of(s.Pause(9000), asking: true).Light);

        // There is a question only over a measurement.
        Assert.False(SessionView.Of(Session.Idle()).StartAsks);
        Assert.False(SessionView.Of(Session.Arm(5000)).StartAsks);
        Assert.Equal("Restart", SessionView.Of(Session.Arm(5000), asking: true).StartText);
    }

    [Fact]
    public void An_imported_parse_locks_both_buttons()
    {
        var v = SessionView.Of(Session.Arm(5000).Start(6000).Pause(9000), imported: true);
        Assert.False(v.StartEnabled);
        Assert.False(v.SecondEnabled);
        Assert.Equal("Start", v.StartText);
        Assert.Equal(StartLook.Locked, v.StartLook);
    }

    [Fact]
    public void The_state_tag_is_the_sessions_state_or_saved_over_an_imported_parse()
    {
        // A session is changed in place, so each state is made afresh.
        static Session Running() => Session.Arm(5000).Start(6000);
        Assert.Equal(SessionTag.Idle, SessionView.Of(Session.Idle()).Tag);
        Assert.Equal(SessionTag.Armed, SessionView.Of(Session.Arm(5000)).Tag);
        Assert.Equal(SessionTag.Live, SessionView.Of(Running()).Tag);
        Assert.Equal(SessionTag.Held, SessionView.Of(Running().Pause(9000)).Tag);

        // A question about a restart changes the pair, not the state.
        Assert.Equal(SessionTag.Live, SessionView.Of(Running(), asking: true).Tag);
        Assert.Equal(SessionTag.Held, SessionView.Of(Running().Pause(9000), asking: true).Tag);
        Assert.Equal(SessionTag.Armed, SessionView.Of(Session.Arm(5000), asking: true).Tag);

        // A saved parse's clock is stopped, so its light is Held; nobody is
        // holding it, and the tag says what it is.
        var saved = SessionView.Of(Running().Pause(9000), imported: true);
        Assert.Equal(SessionLight.Held, saved.Light);
        Assert.Equal(SessionTag.Saved, saved.Tag);
    }

    [Fact]
    public void The_tray_menus_heading_is_there_only_while_a_session_is_armed_or_counting()
    {
        static Session Running() => Session.Arm(5000).Start(6000);
        Assert.False(SessionView.Of(Session.Idle()).Underway);
        Assert.True(SessionView.Of(Session.Arm(5000)).Underway);
        Assert.True(SessionView.Of(Running()).Underway);
        Assert.False(SessionView.Of(Running().Pause(9000)).Underway);

        // A restart being asked about leaves it as it was: there over a
        // session that is counting, not there over one that is held.
        Assert.True(SessionView.Of(Running(), asking: true).Underway);
        Assert.False(SessionView.Of(Running().Pause(9000), asking: true).Underway);

        // Never over a saved parse, whatever its session was doing when it was saved.
        Assert.False(SessionView.Of(Running(), imported: true).Underway);
        Assert.False(SessionView.Of(Session.Arm(5000), imported: true).Underway);
    }

    [Fact]
    public void What_the_session_says_in_words()
    {
        var idle = Session.Idle();
        Assert.Equal("Press Start to begin measuring", SessionText.Empty(idle));
        Assert.Equal("not started — press Start", SessionText.TotalNote(idle, anyone: false));
        Assert.Equal("—", SessionText.ClockNote(idle));
        Assert.Equal("not started", SessionText.Status(idle));
        Assert.Equal("00:00", SessionText.Stopwatch(idle, 99_000));

        var armed = Session.Arm(5000);
        Assert.StartsWith("Armed", SessionText.Empty(armed));
        Assert.Equal("armed — starts on the first hit", SessionText.TotalNote(armed, anyone: false));
        Assert.Equal("waiting for the first hit", SessionText.ClockNote(armed));
        Assert.Equal("armed, waiting for the first hit", SessionText.Status(armed));

        var live = Session.Arm(5000).Start(6000);
        Assert.Equal("No damage yet", SessionText.Empty(live));
        Assert.Equal("No healing yet", SessionText.Empty(live, what: "healing"));
        Assert.Equal("no damage yet", SessionText.TotalNote(live, anyone: false));
        Assert.Equal("counting", SessionText.TotalNote(live, anyone: true));
        Assert.Equal("started " + Format.Clock(6000), SessionText.ClockNote(live));
        Assert.Equal("01:05", SessionText.Stopwatch(live, 71_000));

        var held = live.Pause(9000);
        Assert.Equal("Paused — nothing is being counted", SessionText.Empty(held));
        Assert.Equal("held — nothing counting", SessionText.TotalNote(held, anyone: true));
        Assert.Equal("started " + Format.Clock(6000) + " · held", SessionText.ClockNote(held));
        Assert.Equal("00:03", SessionText.Stopwatch(held, 71_000));
    }
}
