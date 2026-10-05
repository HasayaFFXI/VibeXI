using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Zerg.Core;

/// <summary>
/// Reads the addon's event file one line at a time into damage rows, heals and
/// the roster.
///
/// The addon reads the game's own action packets and writes one JSON object
/// per line, so there is nothing to infer here: a line is validated, its clock
/// scaled from seconds to milliseconds, and it is filed. What the packet
/// states outright:
/// <list type="bullet">
/// <item><c>use</c>: one action is one use however many targets it reached;
/// the addon mints it from the packet's own target list.</item>
/// <item><c>actorKind</c> / <c>targetKind</c>: player, pet, mob, npc, from
/// the entity's spawn flags. The roster is a thin wrapper over these.</item>
/// <item><c>crit</c> and <c>hit</c>: real flags, so a miss, a parry, a shadow
/// and an evade are told apart and accuracy has an exact denominator.</item>
/// <item><c>owner</c>: a pet names its master.</item>
/// <item>job lines: a party member's main and sub job.</item>
/// </list>
///
/// The wire clock is whole seconds (the addon has no finer clock), with
/// <c>seq</c> ordering rows inside one second. Everything after this class is
/// in milliseconds; <c>t</c> is scaled here and never again.
/// </summary>
public sealed class EventReader
{
    /// <summary>The action name a row with none gets: an added effect riding on a swing.</summary>
    public const string AdditionalEffect = "Additional Effect";

    /// <param name="owner">The character the file belongs to, from its name.
    /// Only used to pin that character's colour; parsing ignores it.</param>
    public EventReader(string? owner = null)
    {
        Roster.Owner = string.IsNullOrEmpty(owner) ? null : owner;
    }

    public Roster Roster { get; } = new();

    /// <summary>Damage rows, in file order.</summary>
    public List<CombatEvent> Events { get; } = [];

    /// <summary>Heals, kept apart from <see cref="Events"/> so no damage count can see them.</summary>
    public List<HealEvent> Heals { get; } = [];

    /// <summary>How many records have been fed, blank and unreadable lines included.</summary>
    public int LineNo { get; private set; }

    /// <summary>One line of the file. Returns the damage row it made, or null
    /// (blank, not JSON, or a line that is not a damage row).</summary>
    public CombatEvent? Feed(string? line)
    {
        LineNo++;
        if (string.IsNullOrEmpty(line)) return null;
        return Js.TryParse(line, out var raw) ? Ingest(raw) : null;
    }

    /// <summary>
    /// One record that is already parsed. An imported export comes in through
    /// here, the same door <see cref="Feed"/> uses, so a record read back from
    /// an export cannot be read differently from the line it started as.
    /// </summary>
    public CombatEvent? FeedRecord(JsonNode? raw)
    {
        LineNo++;
        return Ingest(raw);
    }

    CombatEvent? Ingest(JsonNode? node)
    {
        if (node is not JsonObject raw) return null;
        var kind = raw["kind"];

        // The addon's own notices (its startup probe, and message ids it did
        // not recognise). Not events; they stay in the file for anyone searching it.
        if (Js.AsString(kind) == "meta") return null;

        // A job line is a fact about a character, not an event: no damage, no
        // target, no use. As a row it would put a zero-damage line into every
        // count, so it goes on the roster instead.
        if (Js.AsString(kind) == "job")
        {
            var who = Js.Str(raw["actor"]);
            if (who.Length > 0)
            {
                // It comes off a party slot, so it is also proof the name is
                // one of ours. For a member who never acts it is the only
                // proof there is; without it the healer reads as a stranger.
                Roster.Note(who, "player");
                Roster.NoteJob(who, new JobInfo(
                    Main: Or(Js.Str(raw["main"]), "NON"),
                    MainId: Js.Num(raw["mainId"]),
                    MainLevel: Js.Num(raw["mainLvl"]),
                    Sub: Or(Js.Str(raw["sub"]), "NON"),
                    SubId: Js.Num(raw["subId"]),
                    SubLevel: Js.Num(raw["subLvl"])));
            }
            return null;
        }

        // A heal is not a damage row and must not become one: every damage
        // count runs over Events, where a heal would be a miss and could start
        // the session clock. Which actions are heals, and the amount, are the
        // addon's decision; this only files the line.
        if (Js.AsString(kind) == "heal")
        {
            var healer = Js.Str(raw["actor"]);
            if (healer.Length == 0) return null;
            Roster.Note(healer, Js.Str(raw["actorKind"]));
            Roster.Note(Js.Str(raw["target"]), Js.Str(raw["targetKind"]));
            Heals.Add(new HealEvent
            {
                T = Js.Num(raw["t"]) * 1000,
                Seq = Js.Num(raw["seq"]),
                Use = raw["use"] is null ? null : Js.Num(raw["use"]),
                Via = Js.Str(raw["via"]),
                Actor = healer,
                ActorKind = Js.Str(raw["actorKind"]),
                Action = Js.Str(raw["action"]),
                ActionId = Js.Num(raw["actionId"]),
                Target = Js.Str(raw["target"]),
                TargetKind = Js.Str(raw["targetKind"]),
                Hp = Js.Num(raw["hp"]),
                Msg = Js.Num(raw["msg"]),
                Line = LineNo,
                Owner = Js.Truthy(raw["owner"]) ? Js.Str(raw["owner"]) : null,
                Pet = Js.Truthy(raw["pet"]) ? Js.Str(raw["pet"]) : null,
            });
            return null;
        }

        var actor = Js.Str(raw["actor"]);
        var target = Js.Str(raw["target"]);
        if (!Js.Truthy(kind) || actor.Length == 0) return null;

        Roster.Note(actor, Js.Str(raw["actorKind"]));
        Roster.Note(target, Js.Str(raw["targetKind"]));

        var e = new CombatEvent
        {
            T = Js.Num(raw["t"]) * 1000,
            Seq = Js.Num(raw["seq"]),
            Use = raw["use"] is null ? null : Js.Num(raw["use"]),
            Kind = Js.Str(kind),
            Actor = actor,
            ActorKind = Js.Str(raw["actorKind"]),
            Action = Or(Js.Str(raw["action"]), AdditionalEffect),
            ActionId = Js.Num(raw["actionId"]),
            Target = target,
            TargetKind = Js.Str(raw["targetKind"]),
            Dmg = Js.Num(raw["dmg"]),
            Hit = Js.AsBool(raw["hit"]),
            Crit = Js.AsBool(raw["crit"]),
            Burst = Js.AsBool(raw["burst"]),
            Msg = Js.Num(raw["msg"]),
            Line = LineNo,
            // Only on a pet's own rows, so they stay off every other row.
            Owner = Js.Truthy(raw["owner"]) ? Js.Str(raw["owner"]) : null,
            Pet = Js.Truthy(raw["pet"]) ? Js.Str(raw["pet"]) : null,
        };
        Events.Add(e);
        return e;
    }

    static string Or(string s, string fallback) => s.Length > 0 ? s : fallback;

    /// <summary>
    /// Drops the rows and keeps the roster. Which names are monsters does not
    /// stop being true because the session was restarted, and the jobs must
    /// survive: the addon only writes a job line when a job changes, so a
    /// cleared map would stay empty until somebody swapped.
    /// </summary>
    public void Reset()
    {
        Events.Clear();
        Heals.Clear();
        LineNo = 0;
    }

    /// <summary>Every line, in one call.</summary>
    public static EventReader ParseAll(IEnumerable<string?> lines, string? owner = null)
    {
        var r = new EventReader(owner);
        foreach (var l in lines) r.Feed(l);
        return r;
    }

    // ------------------------------------------------------------ file names

    // [0-9] rather than \d, which in .NET also matches other scripts' digits;
    // and "." never matches a line break of any kind (Zl and Zp are U+2028 and U+2029).
    static readonly Regex DailyName = new(
        @"^([^\n\r\p{Zl}\p{Zp}]*?)_([0-9]{4})\.([0-9]{2})\.([0-9]{2})\.jsonl$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    // What the addon writes now: one file per load, named for the local date
    // and time it was loaded (YYYYMMDDHHMMSS).
    static readonly Regex LoadName = new(
        @"^([^\n\r\p{Zl}\p{Zp}]*?)-([0-9]{4})([0-9]{2})([0-9]{2})[0-9]{6}\.jsonl$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    static readonly Regex AnyDate = new(@"([0-9]{4})[.\-_]([0-9]{2})[.\-_]([0-9]{2})", RegexOptions.CultureInvariant);
    static readonly Regex LeadingName = new(@"^([A-Za-z]+)[_\-]", RegexOptions.CultureInvariant);

    /// <summary>
    /// "Hasaya-20260730213045.jsonl" → ("Hasaya", 2026-07-30): the addon's
    /// file, one per load. "Hasaya_2026.07.30.jsonl" → the same: the file it
    /// wrote before, one per day, still read. Any other name is
    /// searched for a date and a leading name separately; either may be null.
    /// An out-of-range month or day rolls over into the next (month 13 is
    /// January of the next year), and a two-digit year is in the 1900s.
    /// </summary>
    public static (string? Owner, DateOnly? Date) ParseFilename(string? name)
    {
        if (string.IsNullOrEmpty(name)) return (null, null);
        var m = DailyName.Match(name);
        if (m.Success)
            return (m.Groups[1].Value.Length > 0 ? m.Groups[1].Value : null,
                    MakeDate(m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value));
        m = LoadName.Match(name);
        if (m.Success)
            return (m.Groups[1].Value.Length > 0 ? m.Groups[1].Value : null,
                    MakeDate(m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value));
        DateOnly? date = null;
        string? owner = null;
        var d = AnyDate.Match(name);
        if (d.Success) date = MakeDate(d.Groups[1].Value, d.Groups[2].Value, d.Groups[3].Value);
        var o = LeadingName.Match(name);
        if (o.Success) owner = o.Groups[1].Value;
        return (owner, date);
    }

    static DateOnly? MakeDate(string y, string m, string d)
    {
        int year = int.Parse(y), month = int.Parse(m), day = int.Parse(d);
        if (year is >= 0 and <= 99) year += 1900;
        try
        {
            return new DateOnly(year, 1, 1).AddMonths(month - 1).AddDays(day - 1);
        }
        catch (ArgumentOutOfRangeException) { return null; }   // past year 9999
    }
}
