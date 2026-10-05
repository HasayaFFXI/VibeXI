namespace Zerg.Core;

/// <summary>What one poll found.</summary>
/// <param name="File">The newest event file's name (no folder), or null while
/// there is none.</param>
/// <param name="Reset">True when everything read before must be dropped and the
/// file replayed from the top: a different file is newest now (the addon was
/// loaded again, a character switch), or the file shrank under us.</param>
/// <param name="Lines">Whole lines new since the last poll.</param>
/// <param name="Size">The file's length in bytes when it was read.</param>
/// <param name="Modified">When the file was last written (UTC), as found
/// before reading it.</param>
public sealed record TailUpdate(string? File, bool Reset, IReadOnlyList<string> Lines, long Size,
                                DateTime Modified = default);

/// <summary>
/// Follows the newest event file in a folder, one poll at a time: the file
/// name and byte offset carried from poll to poll, and the rules for when to
/// start over. Not thread-safe; one caller polls at a time.
/// </summary>
public sealed class EventTail(string directory)
{
    public string Directory { get; } = directory;

    /// <summary>The file being followed (name only), kept while no file is
    /// found, so the same file reappearing carries on where it was.</summary>
    public string? File { get; private set; }

    /// <summary>Where the next poll starts reading.</summary>
    public long Offset { get; private set; }

    /// <summary>
    /// Reads whatever is new. An I/O error reading the file propagates and
    /// leaves the offset where it was, so the next poll retries the same bytes.
    /// </summary>
    public TailUpdate Poll()
    {
        var newest = EventFiles.Newest(Directory);
        if (newest is null) return new TailUpdate(null, false, [], 0);

        bool reset = false;
        long offset = Offset;
        // Compared by name, case and all: the folder is fixed, and a file that
        // was renamed is a different file to whoever is reading it.
        if (newest.Name != File)
        {
            reset = true;
            offset = 0;
        }

        var tail = EventFiles.ReadTail(newest.FullName, offset);
        if (tail.Truncated) reset = true;

        File = newest.Name;
        Offset = tail.NextOffset;
        return new TailUpdate(newest.Name, reset, tail.Lines, tail.Size, newest.LastWriteTimeUtc);
    }
}
