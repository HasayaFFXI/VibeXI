using System.Text;

namespace Zerg.Core;

/// <summary>The lines a poll returns, and where the next poll starts.</summary>
public sealed record Tail(IReadOnlyList<string> Lines, long NextOffset, long Size, bool Truncated);

/// <summary>
/// Finds and tails the event files the VibeXI addon writes. A port of
/// <c>newest_events</c>, <c>_open_shared</c> and <c>read_tail</c> in
/// <c>apps/damage-meter/damage-meter.py</c>, which remains the reference: the
/// parity tests run that file's own code against the same bytes on disk.
///
/// This class stays dumb on purpose, like the Python server. It returns raw
/// lines; interpreting them is <c>web/lib/source.js</c>'s job.
/// </summary>
public static class EventFiles
{
    /// <summary>One poll never reads more than this.</summary>
    public const int MaxTail = 8 * 1024 * 1024;

    /// <summary>The most recently modified *.jsonl in <paramref name="directory"/>,
    /// or null if there is none or the directory does not exist (yet).</summary>
    public static FileInfo? Newest(string directory)
    {
        FileInfo? best = null;
        try
        {
            // Filtered by hand rather than with a "*.jsonl" search pattern, so
            // there is no question of Win32 pattern quirks: exactly the files
            // whose name ends in .jsonl, compared without regard to case, as
            // Python's glob does on Windows.
            foreach (var f in new DirectoryInfo(directory).EnumerateFiles())
            {
                if (!f.Name.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)) continue;
                // Strictly greater: on a tie the first in directory order wins,
                // which is what Python's max() does too.
                if (best is null || f.LastWriteTimeUtc > best.LastWriteTimeUtc) best = f;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
        return best;
    }

    /// <summary>
    /// Opens for reading while sharing Read, Write AND Delete. The addon appends
    /// to this file from inside the game process, on the game's thread, and must
    /// never be blocked by this tool. In .NET this is just a FileShare flag; the
    /// Python server has to go through CreateFileW to get the same thing.
    /// </summary>
    public static FileStream OpenShared(string path) =>
        new(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, bufferSize: 1);

    /// <summary>
    /// Whole lines from <paramref name="path"/> starting at byte <paramref name="offset"/>.
    /// A partial trailing line (the addon is still writing it) is left for the
    /// next poll, which is why the caller must carry NextOffset forward rather
    /// than seeking to EOF: half a JSON object is not parseable.
    /// </summary>
    public static Tail ReadTail(string path, long offset)
    {
        using var fs = OpenShared(path);
        long length = fs.Length;
        bool truncated = false;

        // File shrank -> it was rotated or rewritten under us; restart from zero.
        // A negative offset is treated the same way. Python raises on the seek
        // instead and answers 500 -- the page never sends one, so this is a
        // deliberate divergence, not a parity bug.
        if (offset > length || offset < 0)
        {
            offset = 0;
            truncated = true;
        }
        if (offset >= length) return new Tail([], length, length, truncated);

        fs.Position = offset;
        int want = (int)Math.Min(length - offset, MaxTail);
        var buf = new byte[want];
        int got = fs.ReadAtLeast(buf, want, throwOnEndOfStream: false);
        var span = buf.AsSpan(0, got);

        // Trim back to the last newline so we never emit a half-written line.
        int last = span.LastIndexOf((byte)'\n');
        if (last < 0) return new Tail([], offset, length, truncated);

        // The addon escapes every non-ASCII byte as \uXXXX, so this is ASCII in
        // practice; UTF-8 with replacement is the safe superset, and matches
        // Python's decode('utf-8', errors='replace') sequence for sequence.
        var text = Encoding.UTF8.GetString(span[..(last + 1)]);
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        // Split leaves a trailing empty element after the final newline.
        if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);

        return new Tail(lines, offset + last + 1, length, truncated);
    }
}
