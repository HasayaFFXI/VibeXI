using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Zerg.Core;

/// <summary>The <c>/api/events</c> response, field for field as damage-meter.py
/// sends it. The page only reads file, reset, nextOffset and lines; the rest is
/// kept so the two hosts stay interchangeable.</summary>
public sealed record EventsPayload(
    string? File,
    string Dir,
    string? Mtime,
    bool Reset,
    long Offset,
    long NextOffset,
    long Size,
    IReadOnlyList<string> Lines)
{
    static readonly JsonWriterOptions Options = new()
    {
        // Escapes every non-ASCII character, so the body is plain ASCII on the
        // wire whatever the file held -- matching json.dumps(ensure_ascii=True).
        // It also escapes < > & ' which Python does not; both parse identically.
        Encoder = JavaScriptEncoder.Default,
    };

    /// <summary>The response body. Key order follows the Python server's dicts.</summary>
    public byte[] ToJson()
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, Options))
        {
            w.WriteStartObject();
            w.WriteBoolean("ok", true);
            if (File is null)
            {
                w.WriteNull("file");
                WriteTail(w);
                w.WriteString("dir", Dir);
            }
            else
            {
                w.WriteString("file", File);
                w.WriteString("dir", Dir);
                w.WriteString("mtime", Mtime);
                WriteTail(w);
            }
            w.WriteEndObject();
        }
        return ms.ToArray();
    }

    void WriteTail(Utf8JsonWriter w)
    {
        w.WriteBoolean("reset", Reset);
        w.WriteNumber("offset", Offset);
        w.WriteNumber("nextOffset", NextOffset);
        w.WriteNumber("size", Size);
        w.WriteStartArray("lines");
        foreach (var line in Lines) w.WriteStringValue(line);
        w.WriteEndArray();
    }
}

/// <summary>A port of <c>events_payload</c> and the <c>/api/events</c> handler
/// in damage-meter.py.</summary>
public static class EventsApi
{
    /// <summary>Handles the raw query string, exactly as <c>_api_events</c> does.</summary>
    public static EventsPayload FromQuery(string eventsDir, string query)
    {
        var q = Query.Parse(query);
        return Get(eventsDir, Query.First(q, "file"), Query.Int(Query.First(q, "offset"), 0));
    }

    public static EventsPayload Get(string eventsDir, string? clientFile, long clientOffset)
    {
        var dir = DisplayDir(eventsDir);
        var src = EventFiles.Newest(eventsDir);
        if (src is null) return new EventsPayload(null, dir, null, true, 0, 0, 0, []);

        // A different file is newest now (day rollover, character switch, new
        // session) -> tell the client to drop its state and replay from the top.
        bool reset = false;
        long offset = clientOffset;
        if (!string.Equals(clientFile, src.Name, StringComparison.Ordinal))
        {
            reset = true;
            offset = 0;
        }

        var tail = EventFiles.ReadTail(src.FullName, offset);
        if (tail.Truncated) reset = true;

        // `offset` is echoed as the client's value even when the file shrank and
        // the read restarted from zero. That is what the Python server does, and
        // the page ignores this field, so it is kept rather than "fixed".
        src.Refresh();
        return new EventsPayload(src.Name, dir, IsoLocal(src.LastWriteTimeUtc), reset,
                                 offset, tail.NextOffset, tail.Size, tail.Lines);
    }

    /// <summary>What <c>str(Path(dir))</c> prints: backslashes, no trailing
    /// separator (except on a drive root).</summary>
    public static string DisplayDir(string dir)
    {
        var s = dir.Replace('/', '\\');
        return Path.TrimEndingDirectorySeparator(s);
    }

    /// <summary>
    /// <c>datetime.fromtimestamp(t).astimezone().isoformat()</c>: local time,
    /// microseconds only when non-zero, offset as +HH:MM.
    /// </summary>
    public static string IsoLocal(DateTime utc)
    {
        var local = new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToLocalTime();
        long micros = local.Ticks % TimeSpan.TicksPerSecond / 10;
        var off = local.Offset;
        var sign = off < TimeSpan.Zero ? '-' : '+';
        off = off.Duration();
        return local.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture) +
               (micros == 0 ? "" : "." + micros.ToString("D6", CultureInfo.InvariantCulture)) +
               sign + off.Hours.ToString("D2", CultureInfo.InvariantCulture) + ":" +
               off.Minutes.ToString("D2", CultureInfo.InvariantCulture);
    }
}
