using System.Text.RegularExpressions;

namespace Zerg.Core.Tests;

/// <summary>Saving a paused parse, and showing one that was saved.</summary>
public class ImportExportTests
{
    static readonly HashSet<string> Nobody = [];

    /// <summary>A session armed at 8 s whose first counted hit is at 10 s,
    /// with one row before the zero and one inside a pause.</summary>
    static Tracker Paused()
    {
        var t = new Tracker();
        t.Follow("Hasaya_2026.07.30.jsonl");
        t.Feed([Lines.Job("Hasaya", "SAM", "WAR"), Lines.Hit(5, "Hasaya", 999)]);
        t.Start(8_000);
        t.Feed([Lines.Job("Tank", "PLD", "WAR"), Lines.Hit(10, "Tank", 100), Lines.Hit(12, "Hasaya", 300),
                Lines.Heal(13, "Mage", 80)]);
        t.Count(Nobody, true, 20_000);
        t.TogglePause(20_000);
        t.Feed([Lines.Hit(25, "Hasaya", 777)]);
        t.TogglePause(30_000);
        t.Feed([Lines.Hit(32, "Hasaya", 50, kind: "ws", action: "Tachi: Gekko")]);
        t.Count(Nobody, true, 40_000);
        t.TogglePause(40_000);
        return t;
    }

    // -------------------------------------------------------------- export

    [Fact]
    public void A_parse_is_exported_only_once_its_clock_has_stopped()
    {
        var t = new Tracker();
        t.Follow("Hasaya_2026.07.30.jsonl");
        Assert.False(t.CanExport);
        Assert.Null(t.Export(1_000));

        t.Start(8_000);
        Assert.False(t.CanExport);                 // armed: no clock yet
        t.Feed([Lines.Hit(10, "Hasaya", 100)]);
        t.Count(Nobody, true, 12_000);
        Assert.False(t.CanExport);                 // running
        Assert.Null(t.Export(12_000));

        t.TogglePause(15_000);
        Assert.True(t.CanExport);
        Assert.NotNull(t.Export(15_000));

        t.TogglePause(16_000);
        Assert.False(t.CanExport);                 // resumed
    }

    [Fact]
    public void An_export_holds_only_what_the_session_can_count()
    {
        var parse = ParseFile.Import(Paused().Export(40_000)!);

        // Not the row before the zero, nor the one that landed in the pause.
        Assert.Equal([100d, 300d, 50d], parse.Source.Events.Select(e => e.Dmg));
        Assert.Single(parse.Source.Heals);
        Assert.Equal((10_000d, 40_000d), (parse.Session.StartedAt, parse.Session.PausedAt));
        Assert.Equal([new PauseSpan(20_000, 30_000)], parse.Session.Spans);
        Assert.Equal("Hasaya_2026.07.30.jsonl", parse.File);
        // The jobs travel in the roster: one of them was written before Start.
        Assert.Equal("SAM/WAR", parse.Source.Roster.JobLabel("Hasaya"));
        Assert.Equal("PLD/WAR", parse.Source.Roster.JobLabel("Tank"));
    }

    [Fact]
    public void An_export_is_named_for_its_owner_and_when_the_pull_began()
    {
        var began = new DateTimeOffset(2026, 7, 30, 21, 30, 45, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 7, 30, 21, 30, 45)));
        double ms = began.ToUnixTimeMilliseconds();

        Assert.Equal("Hasaya_parse_2026.07.30_2130.zerg", ParseFile.SuggestedName("Hasaya", ms));
        // Nothing a file name cannot hold, and nothing at all for no owner.
        Assert.Equal("Has-a_ya_parse_2026.07.30_2130.zerg", ParseFile.SuggestedName("Has-a_ya/..\\:*?", ms));
        Assert.Equal("parse_2026.07.30_2130.zerg", ParseFile.SuggestedName(null, ms));
        Assert.Equal("parse_2026.07.30_2130.zerg", ParseFile.SuggestedName("", ms));
    }

    [Fact]
    public void An_export_is_never_saved_as_an_event_file()
    {
        Assert.True(ParseFile.CanSaveAs(@"C:\parses\Hasaya_parse_2026.07.30_2130.zerg"));
        Assert.True(ParseFile.CanSaveAs(@"C:\parses\old.json"));
        Assert.False(ParseFile.CanSaveAs(@"C:\events\Hasaya_2026.07.30.jsonl"));
        Assert.False(ParseFile.CanSaveAs(@"C:\events\SNEAKY.JSONL"));
    }

    // -------------------------------------------------------------- import

    [Fact]
    public void An_import_counts_exactly_as_the_session_it_was_exported_from()
    {
        var live = Paused();
        var shown = Tracker.Of(ParseFile.Import(live.Export(40_000)!));

        // Counted long afterwards: its clock is the export's, not today's.
        var a = live.Count(Nobody, true, 40_000);
        var b = shown.Count(Nobody, true, 9_999_999);

        Assert.True(shown.Imported);
        Assert.Equal(20_000, b.Elapsed);
        Assert.Equal((a.Totals.Total, a.Elapsed, a.Healing.Total), (b.Totals.Total, b.Elapsed, b.Healing.Total));
        Assert.Equal(a.Totals.Actors.Select(x => (x.Name, x.Total, x.Dps)), b.Totals.Actors.Select(x => (x.Name, x.Total, x.Dps)));
        Assert.Equal(a.Events.Select(e => (e.T, e.Actor, e.Dmg)), b.Events.Select(e => (e.T, e.Actor, e.Dmg)));
        // The viewer's own filters apply to it.
        Assert.Equal(100, shown.Count(new HashSet<string> { "Hasaya" }, true).Totals.Total);
        Assert.Equal(450, shown.Count(Nobody, true).Totals.Total);
    }

    [Fact]
    public void An_imports_owner_is_the_exporters()
    {
        var shown = Tracker.Of(ParseFile.Import(Paused().Export(40_000)!));
        shown.Count(Nobody, true);

        Assert.Equal("Hasaya", shown.Reader.Roster.Owner);
        // Slot 0, though Tank swung first.
        Assert.Equal(0, shown.Cast.Slots["Hasaya"]);
        Assert.Equal(1, shown.Cast.Slots["Tank"]);
    }

    [Fact]
    public void An_import_is_a_finished_recording()
    {
        var shown = Tracker.Of(ParseFile.Import(Paused().Export(40_000)!));
        var before = shown.Count(Nobody, true);

        shown.Start(50_000);
        shown.Second(60_000);
        shown.TogglePause(70_000);
        shown.Cancel();
        shown.Follow("Other_2026.08.01.jsonl");
        shown.Feed([Lines.Hit(33, "Hasaya", 5000)]);

        var after = shown.Count(Nobody, true, 99_000);
        Assert.Equal((before.Totals.Total, before.Elapsed), (after.Totals.Total, after.Elapsed));
        Assert.Equal(40_000, shown.Session.PausedAt);
        Assert.Equal("Hasaya_2026.07.30.jsonl", shown.File);
        Assert.Equal(0, shown.Lines);
    }

    [Fact]
    public void An_import_exports_again_as_the_file_it_came_from()
    {
        var original = Paused().Export(40_000)!;
        var shown = Tracker.Of(ParseFile.Import(original));
        shown.Count(Nobody, true);

        Assert.True(shown.CanExport);
        var again = shown.Export(77_000)!;

        // The same document, but for when it was written.
        static string Undated(string text) => Regex.Replace(text, "\"exported\": \"[^\"]*\"", "\"exported\": \"\"");
        Assert.NotEqual(original, again);
        Assert.Equal(Undated(original), Undated(again));
    }

    // ------------------------------------------------------------- wording

    [Fact]
    public void An_import_says_which_day_it_began_and_holds_both_buttons_off()
    {
        var shown = Tracker.Of(ParseFile.Import(Paused().Export(40_000)!));
        var began = DateTimeOffset.FromUnixTimeMilliseconds(10_000).ToLocalTime();

        Assert.Equal("started " + began.ToString("yyyy-MM-dd HH:mm:ss"), SessionText.Status(shown.Session, imported: true));
        Assert.Equal("started " + began.ToString("HH:mm:ss"), SessionText.Status(shown.Session));

        var view = SessionView.Of(shown.Session, imported: true);
        Assert.False(view.StartEnabled);
        Assert.False(view.SecondEnabled);
        Assert.Equal(StartLook.Locked, view.StartLook);
        Assert.Contains("Switch to Damage or Healing", view.StartTip);
        Assert.Equal("saved parse — read only",SessionText.TotalNote(shown.Session, anyone: true, imported: true));
        Assert.Equal("Nothing to show in this parse", SessionText.Empty(shown.Session, imported: true));
    }
}
