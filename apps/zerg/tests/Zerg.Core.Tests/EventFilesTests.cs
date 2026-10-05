using System.Text;

namespace Zerg.Core.Tests;

public class EventFilesTests
{
    // ------------------------------------------------------------- Newest

    [Fact]
    public void Newest_is_null_for_a_missing_directory()
    {
        Assert.Null(EventFiles.Newest(Path.Combine(Path.GetTempPath(), "zerg-tests", "does-not-exist")));
    }

    [Fact]
    public void Newest_is_null_when_there_are_no_jsonl_files()
    {
        using var t = new TempDir();
        t.Write("notes.txt", "x\n");
        t.Write("old.jsonl.bak", "x\n");
        Assert.Null(EventFiles.Newest(t.Dir));
    }

    [Fact]
    public void Newest_picks_by_modified_time_not_by_name()
    {
        using var t = new TempDir();
        var a = t.Write("A_2026.01.01.jsonl", "a\n");
        var b = t.Write("B_2026.01.01.jsonl", "b\n");
        var c = t.Write("C_2026.01.01.jsonl", "c\n");
        var now = DateTime.UtcNow;
        File.SetLastWriteTimeUtc(a, now.AddMinutes(-3));
        File.SetLastWriteTimeUtc(b, now.AddMinutes(-1));
        File.SetLastWriteTimeUtc(c, now.AddMinutes(-2));

        Assert.Equal("B_2026.01.01.jsonl", EventFiles.Newest(t.Dir)!.Name);
    }

    [Fact]
    public void Newest_ignores_a_directory_named_like_an_event_file()
    {
        using var t = new TempDir();
        t.Write("Real_2026.01.01.jsonl", "a\n");
        Directory.CreateDirectory(Path.Combine(t.Dir, "Fake_2099.01.01.jsonl"));
        Assert.Equal("Real_2026.01.01.jsonl", EventFiles.Newest(t.Dir)!.Name);
    }

    [Fact]
    public void Newest_matches_the_extension_without_regard_to_case()
    {
        using var t = new TempDir();
        t.Write("Upper_2026.01.01.JSONL", "a\n");
        Assert.Equal("Upper_2026.01.01.JSONL", EventFiles.Newest(t.Dir)!.Name);
    }

    // ----------------------------------------------------------- ReadTail

    [Fact]
    public void Reads_every_complete_line_from_zero()
    {
        using var t = new TempDir();
        var p = t.Write("x.jsonl", "{\"a\":1}\n{\"b\":2}\n");
        var tail = EventFiles.ReadTail(p, 0);
        Assert.Equal(["{\"a\":1}", "{\"b\":2}"], tail.Lines);
        Assert.Equal(16, tail.NextOffset);
        Assert.Equal(16, tail.Size);
        Assert.False(tail.Truncated);
    }

    [Fact]
    public void Holds_back_a_half_written_line_until_it_is_finished()
    {
        using var t = new TempDir();
        var p = t.Write("x.jsonl", "{\"a\":1}\n{\"b\":");

        var first = EventFiles.ReadTail(p, 0);
        Assert.Equal(["{\"a\":1}"], first.Lines);
        Assert.Equal(8, first.NextOffset);

        File.AppendAllText(p, "2}\n");
        var second = EventFiles.ReadTail(p, first.NextOffset);
        Assert.Equal(["{\"b\":2}"], second.Lines);
        Assert.Equal(16, second.NextOffset);
    }

    [Fact]
    public void A_file_with_no_newline_yet_returns_nothing_and_stays_put()
    {
        using var t = new TempDir();
        var p = t.Write("x.jsonl", "{\"a\":");
        var tail = EventFiles.ReadTail(p, 0);
        Assert.Empty(tail.Lines);
        Assert.Equal(0, tail.NextOffset);
    }

    [Fact]
    public void Offset_at_end_of_file_returns_nothing()
    {
        using var t = new TempDir();
        var p = t.Write("x.jsonl", "abc\n");
        var tail = EventFiles.ReadTail(p, 4);
        Assert.Empty(tail.Lines);
        Assert.Equal(4, tail.NextOffset);
        Assert.False(tail.Truncated);
    }

    [Fact]
    public void A_file_that_shrank_is_read_again_from_zero()
    {
        using var t = new TempDir();
        var p = t.Write("x.jsonl", "abc\n");
        var tail = EventFiles.ReadTail(p, 100);
        Assert.True(tail.Truncated);
        Assert.Equal(["abc"], tail.Lines);
        Assert.Equal(4, tail.NextOffset);
    }

    [Fact]
    public void A_negative_offset_is_treated_as_a_shrink_rather_than_an_error()
    {
        // Starting over is safer than throwing in the middle of a poll.
        using var t = new TempDir();
        var p = t.Write("x.jsonl", "abc\n");
        var tail = EventFiles.ReadTail(p, -5);
        Assert.True(tail.Truncated);
        Assert.Equal(["abc"], tail.Lines);
    }

    [Fact]
    public void Crlf_is_normalised_and_empty_lines_are_kept()
    {
        using var t = new TempDir();
        var p = t.Write("x.jsonl", "a\r\n\r\nb\rc\n\n");
        Assert.Equal(["a", "", "b\rc", ""], EventFiles.ReadTail(p, 0).Lines);
    }

    [Fact]
    public void Invalid_utf8_becomes_replacement_characters()
    {
        using var t = new TempDir();
        var p = t.Write("x.jsonl", [(byte)'a', 0xFF, (byte)'b', (byte)'\n']);
        Assert.Equal(["a�b"], EventFiles.ReadTail(p, 0).Lines);
    }

    [Fact]
    public void One_poll_reads_at_most_the_cap_and_the_next_carries_on()
    {
        using var t = new TempDir();
        var line = new string('x', 99) + "\n";                 // 100 bytes
        int count = EventFiles.MaxTail / 100 + 500;            // a little over the cap
        var p = t.Write("x.jsonl", string.Concat(Enumerable.Repeat(line, count)));

        var first = EventFiles.ReadTail(p, 0);
        Assert.Equal(EventFiles.MaxTail / 100, first.Lines.Count);
        Assert.True(first.NextOffset <= EventFiles.MaxTail);

        var second = EventFiles.ReadTail(p, first.NextOffset);
        Assert.Equal(count - first.Lines.Count, second.Lines.Count);
        Assert.Equal(new FileInfo(p).Length, second.NextOffset);
    }

    // ------------------------------------------- never block the game's writer

    [Fact]
    public void The_writer_can_append_while_the_file_is_open_for_reading()
    {
        using var t = new TempDir();
        var p = t.Write("x.jsonl", "a\n");
        using var reader = EventFiles.OpenShared(p);
        using (var writer = new FileStream(p, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            writer.Write(Encoding.ASCII.GetBytes("b\n"));
        Assert.Equal(4, new FileInfo(p).Length);
    }

    [Fact]
    public void The_file_can_be_deleted_while_it_is_open_for_reading()
    {
        // FileShare.Delete: the addon may rotate or remove the file while it
        // is open here.
        using var t = new TempDir();
        var p = t.Write("x.jsonl", "a\n");
        using var reader = EventFiles.OpenShared(p);
        File.Delete(p);
    }
}
