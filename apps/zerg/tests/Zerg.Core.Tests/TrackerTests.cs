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
        Assert.Equal(10_000, held.Elapsed);
        t.Second(60_000);                                      // resume
        t.Feed([Lines.Hit(65, "Hasaya", 50)]);
        var c = t.Count(Nobody, true, 70_000);
        Assert.Equal(150, c.Totals.Total);
        Assert.Equal(20_000, c.Elapsed);
        Assert.Equal(15_000, c.Events[^1].T);                  // the pause is subtracted
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
        t.Feed([Lines.Hit(20, "Hasaya", 100), Lines.Heal(21, "Sylviane", 300)]);
        t.Count(Nobody, true, 22_000);
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
    public void An_imported_parse_locks_both_buttons()
    {
        var v = SessionView.Of(Session.Arm(5000).Start(6000).Pause(9000), imported: true);
        Assert.False(v.StartEnabled);
        Assert.False(v.SecondEnabled);
        Assert.Equal("Start", v.StartText);
        Assert.Equal(StartLook.Locked, v.StartLook);
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
