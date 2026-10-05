using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Zerg.Core;

/// <summary>Why a file could not be imported.</summary>
public enum ImportError
{
    /// <summary>Not an exported parse at all.</summary>
    NotAParse,
    /// <summary>One of the addon's own event files, opened by mistake.</summary>
    EventFile,
    /// <summary>Written by a newer Zerg, in a shape this one does not know.</summary>
    TooNew,
    /// <summary>No complete, paused session, so no clock to measure it on.</summary>
    NoSession,
    /// <summary>No event list.</summary>
    NoEvents,
}

/// <summary>An import that failed, with a message written for the player.</summary>
public sealed class ParseImportException(ImportError reason, string message) : Exception(message)
{
    public ImportError Reason { get; } = reason;
}

/// <summary>A parse read back from a file.</summary>
/// <param name="Source">An ordinary reader, so nothing downstream can tell an
/// import from a live file.</param>
/// <param name="Session">The parse's clock, always paused.</param>
/// <param name="Skipped">Event records that did not read as damage rows.</param>
/// <param name="File">The event file the parse was read from, if recorded.</param>
/// <param name="Exported">When it was exported (ISO 8601), if recorded.</param>
public sealed record ImportedParse(EventReader Source, Session Session, int Skipped, string? File, string? Exported);

/// <summary>
/// A paused parse, saved for another copy of Zerg to open.
///
/// <para>The file is the addon's own records plus the three things a reader
/// cannot recover from them: the session clock, the roster (jobs are written
/// once, on change, so most of them come before the Start press and are not in
/// the event list), and the manual overrides. Events, heals and job lines are
/// stored in the wire format (seconds, <c>mainLvl</c>) and read back through
/// <see cref="EventReader.FeedRecord"/>, the same path a live line takes, so an
/// import is not a second source that could disagree with the first; it is the
/// first source, replayed.</para>
///
/// <para>Never saved as <c>.jsonl</c>: Zerg follows the newest <c>*.jsonl</c> in
/// the events folder, and an export saved there must not be mistaken for
/// the addon's live file.</para>
/// </summary>
public static class ParseFile
{
    public const string FormatName = "vibexi-parse";
    /// <summary>Bump when the document's shape changes; an older Zerg then
    /// refuses the file with a message rather than misreading it.</summary>
    public const int Version = 1;

    /// <summary>What an export is saved as. Exports made before Zerg had a
    /// type of its own are <c>.json</c>, and still open: a file is told by
    /// what is in it, not by its name.</summary>
    public const string Extension = ".zerg";

    /// <summary>
    /// The name an export is offered under: whose parse, and when the pull
    /// began, in local time: "Hasaya_parse_2026.07.30_2130.zerg".
    /// </summary>
    public static string SuggestedName(string? owner, double startedAt)
    {
        var who = new string((owner ?? "").Where(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-').ToArray());
        var d = DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Truncate(startedAt)).ToLocalTime();
        return (who.Length > 0 ? who + "_" : "") + "parse_" +
               d.ToString("yyyy'.'MM'.'dd'_'HHmm", CultureInfo.InvariantCulture) + Extension;
    }

    /// <summary>
    /// Whether an export may be saved under this name. Never as
    /// <c>.jsonl</c>: Zerg follows the newest file of that type in the
    /// events folder, and a parse saved there would be taken for the live
    /// event file.
    /// </summary>
    public static bool CanSaveAs(string path) => !path.TrimEnd().EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase);

    /// <summary>A row → the record the addon wrote. The inverse of reading it.</summary>
    public static JsonObject ToRecord(CombatEvent e)
    {
        var r = new JsonObject
        {
            ["t"] = e.T / 1000, ["seq"] = e.Seq, ["use"] = e.Use, ["kind"] = e.Kind,
            ["actor"] = e.Actor, ["actorKind"] = e.ActorKind,
            ["action"] = e.Action, ["actionId"] = e.ActionId,
            ["target"] = e.Target, ["targetKind"] = e.TargetKind,
            ["dmg"] = e.Dmg, ["hit"] = e.Hit, ["crit"] = e.Crit, ["burst"] = e.Burst, ["msg"] = e.Msg,
        };
        // As the addon writes them: only a pet's own rows carry these.
        if (!string.IsNullOrEmpty(e.Owner)) r["owner"] = e.Owner;
        if (!string.IsNullOrEmpty(e.Pet)) r["pet"] = e.Pet;
        return r;
    }

    /// <summary>A heal → the heal line the addon wrote.</summary>
    public static JsonObject HealRecord(HealEvent h)
    {
        var r = new JsonObject
        {
            ["kind"] = "heal", ["t"] = h.T / 1000, ["seq"] = h.Seq, ["use"] = h.Use, ["via"] = h.Via,
            ["actor"] = h.Actor, ["actorKind"] = h.ActorKind,
            ["action"] = h.Action, ["actionId"] = h.ActionId,
            ["target"] = h.Target, ["targetKind"] = h.TargetKind,
            ["hp"] = h.Hp, ["msg"] = h.Msg,
        };
        if (!string.IsNullOrEmpty(h.Owner)) r["owner"] = h.Owner;
        if (!string.IsNullOrEmpty(h.Pet)) r["pet"] = h.Pet;
        return r;
    }

    /// <summary>A roster job → the job line it came from; the sub-job trio is
    /// left out when there is no sub-job, as the addon writes it.</summary>
    public static JsonObject JobRecord(string name, JobInfo j)
    {
        var r = new JsonObject
        {
            ["kind"] = "job", ["actor"] = name, ["main"] = j.Main, ["mainId"] = j.MainId, ["mainLvl"] = j.MainLevel,
        };
        if (!string.IsNullOrEmpty(j.Sub) && j.Sub != "NON")
        {
            r["sub"] = j.Sub;
            r["subId"] = j.SubId;
            r["subLvl"] = j.SubLevel;
        }
        return r;
    }

    /// <summary>
    /// A reader and its session → the export document. <paramref name="keep"/>
    /// picks which rows and heals go in, by their wall-clock time (default: all);
    /// <paramref name="file"/> is the event file they came from, for the record;
    /// <paramref name="now"/> is the export time in Unix milliseconds.
    ///
    /// Everything is copied, so the document is a snapshot: a poll landing while
    /// a save dialog is open cannot change what gets written.
    /// </summary>
    public static JsonObject Export(EventReader reader, Session session, Func<double, bool>? keep = null,
                                    string? file = null, double? now = null)
    {
        keep ??= _ => true;
        var roster = reader.Roster;
        var kinds = new JsonObject();
        foreach (var k in Js.Keys(roster.Kinds.Keys.ToList())) kinds[k] = roster.Kinds[k];
        var manual = new JsonObject();
        foreach (var k in Js.Keys(roster.Manual.Keys.ToList())) manual[k] = roster.Manual[k];
        var jobs = new JsonArray();
        foreach (var k in Js.Keys(roster.Jobs.Keys.ToList())) jobs.Add(JobRecord(k, roster.Jobs[k]));
        // Where the clock is read is where the file says it was paused: a
        // snapped session (Session.Snap) is written as one paused there, in
        // the shape every reader already knows. A pause after that point is
        // inside the one the file ends on, and is not written.
        var paused = session.EndedAt ?? session.PausedAt;
        var spans = new JsonArray();
        foreach (var s in session.Spans)
            if (paused is not double end || s.From < end) spans.Add(new JsonObject { ["from"] = s.From, ["to"] = s.To });
        var events = new JsonArray();
        foreach (var e in reader.Events) if (keep(e.T)) events.Add(ToRecord(e));
        var heals = new JsonArray();
        foreach (var h in reader.Heals) if (keep(h.T)) heals.Add(HealRecord(h));

        return new JsonObject
        {
            ["format"] = FormatName,
            ["version"] = (double)Version,
            ["exported"] = IsoTime(now ?? Session.Now()),
            ["file"] = string.IsNullOrEmpty(file) ? null : file,
            ["owner"] = roster.Owner,
            ["session"] = new JsonObject
            {
                ["armedAt"] = session.ArmedAt,
                ["startedAt"] = session.StartedAt,
                ["spans"] = spans,
                ["pausedAt"] = paused,
            },
            ["kinds"] = kinds,
            ["manual"] = manual,
            ["jobs"] = jobs,
            ["events"] = events,
            // Optional: a reader that predates heals simply ignores the key.
            ["heals"] = heals,
        };
    }

    /// <summary>Unix milliseconds → "2026-10-02T21:07:45.123Z".</summary>
    static string IsoTime(double ms)
    {
        var d = DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Truncate(ms)).UtcDateTime;
        var s = d.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fff'Z'", CultureInfo.InvariantCulture);
        return s;
    }

    /// <summary>
    /// The document → text. The head is indented for reading; events and heals
    /// are one per line, the way the addon's own file has them, so a long parse
    /// stays something a person can scroll and a diff can line up.
    /// </summary>
    public static string Stringify(JsonObject doc)
    {
        var head = new JsonObject();
        foreach (var (k, v) in doc)
            if (k != "events" && k != "heals") head[k] = v?.DeepClone();
        var text = Js.Stringify(head, 2);
        var sb = new StringBuilder(text, 0, text.Length - 2, text.Length + 1024);
        List(sb, "events", doc["events"] as JsonArray ?? []);
        if (doc["heals"] is JsonArray heals) List(sb, "heals", heals);
        sb.Append("\n}\n");
        return sb.ToString();
    }

    static void List(StringBuilder sb, string name, JsonArray rows)
    {
        sb.Append(",\n  \"").Append(name).Append("\": [");
        if (rows.Count > 0)
        {
            sb.Append("\n    ");
            for (int i = 0; i < rows.Count; i++)
            {
                if (i > 0) sb.Append(",\n    ");
                sb.Append(Js.Stringify(rows[i]));
            }
            sb.Append("\n  ");
        }
        sb.Append(']');
    }

    /// <summary>
    /// A session read back from a file: shape-checked, and required to be
    /// paused. Export is only offered while paused, so an unpaused session is not
    /// one Zerg wrote, and it would be meaningless if it were: its clock would
    /// count on from the moment of import over a file with nothing new in it.
    /// </summary>
    static Session? ReadSession(JsonNode? node)
    {
        if (node is not JsonObject s) return null;
        if (Finite(s["startedAt"]) is not double started || Finite(s["pausedAt"]) is not double paused
            || paused < started) return null;
        var spans = new List<PauseSpan>();
        if (s["spans"] is JsonArray list)
        {
            foreach (var item in list)
            {
                // A span that is not an object reads every field as missing.
                if (!Js.Truthy(item)) return null;
                var p = item as JsonObject;
                if (Finite(p?["from"]) is not double from || Finite(p?["to"]) is not double to || to < from)
                    return null;
                spans.Add(new PauseSpan(from, to));
            }
        }
        return new Session
        {
            ArmedAt = Finite(s["armedAt"]) ?? started,
            StartedAt = started,
            Spans = spans,
            PausedAt = paused,
        };
    }

    static double? Finite(JsonNode? v) => Js.AsNumber(v) is double d && double.IsFinite(d) ? d : null;

    /// <summary>The first line is a record on its own: an addon event file.</summary>
    static bool LooksLikeEventFile(string text)
    {
        var nl = text.IndexOf('\n');
        return Js.TryParse(nl < 0 ? text : text[..nl], out var o) && o is JsonObject obj && Js.Truthy(obj["kind"]);
    }

    const string NotParse = "That file is not a Zerg parse export.";
    const string EventFileMessage = "That is an addon event file (.jsonl), not an exported parse. " +
                                    "Open a file made with Export.";

    /// <summary>
    /// Text → the parse it holds, or a <see cref="ParseImportException"/> whose
    /// message is written for the player. Detection is by content, whatever the
    /// file is called.
    /// </summary>
    public static ImportedParse Import(string text)
    {
        if (!Js.TryParse(text, out var parsed))
            throw LooksLikeEventFile(text)
                ? new ParseImportException(ImportError.EventFile, EventFileMessage)
                : new ParseImportException(ImportError.NotAParse, NotParse);
        if (parsed is not JsonObject doc || Js.AsString(doc["format"]) != FormatName)
            throw parsed is JsonObject o && Js.Truthy(o["kind"])
                ? new ParseImportException(ImportError.EventFile, EventFileMessage)
                : new ParseImportException(ImportError.NotAParse, NotParse);
        if (Js.ToNumber(doc["version"]) > Version)
            throw new ParseImportException(ImportError.TooNew,
                "That parse was exported by a newer version of Zerg. Update this copy to open it.");
        var session = ReadSession(doc["session"])
            ?? throw new ParseImportException(ImportError.NoSession,
                "That parse has no complete session, so there is no clock to measure it on.");
        if (doc["events"] is not JsonArray events)
            throw new ParseImportException(ImportError.NoEvents, "That parse has no event list.");

        var owner = Js.Str(doc["owner"]);
        var r = new EventReader(owner.Length > 0 ? owner : null);
        // Classifications first, so the events' own kinds meet the same
        // first-answer-wins rule they met when they were live.
        if (doc["kinds"] is JsonObject kinds)
            foreach (var k in Js.Keys(kinds.Select(p => p.Key).ToList())) r.Roster.Note(k, Js.Str(kinds[k]));
        if (doc["manual"] is JsonObject manual)
            foreach (var k in Js.Keys(manual.Select(p => p.Key).ToList()))
                if (Js.AsString(manual[k]) is "ally" or "mob") r.Roster.SetManual(k, Js.AsString(manual[k]));
        if (doc["jobs"] is JsonArray jobs)
            foreach (var j in jobs)
                if (j is JsonObject jo && Js.AsString(jo["kind"]) == "job") r.FeedRecord(jo);

        int skipped = 0;
        foreach (var ev in events)
        {
            // A job or meta record here would be filed, not counted; nothing
            // Export writes puts one in this list, so it is refused, not obeyed.
            if (ev is not JsonObject eo || Js.AsString(eo["kind"]) is "job" or "meta" || r.FeedRecord(eo) == null)
                skipped++;
        }

        // Heals, if the export has them; one from before the addon recorded
        // healing simply has no list.
        if (doc["heals"] is JsonArray heals)
            foreach (var h in heals)
                if (h is JsonObject ho && Js.AsString(ho["kind"]) == "heal") r.FeedRecord(ho);

        var fileName = Js.Str(doc["file"]);
        var exported = Js.Str(doc["exported"]);
        return new ImportedParse(r, session, skipped,
                                 fileName.Length > 0 ? fileName : null,
                                 exported.Length > 0 ? exported : null);
    }
}
