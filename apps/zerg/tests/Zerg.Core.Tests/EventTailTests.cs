namespace Zerg.Core.Tests;

public class EventTailTests
{
    static void Append(string path, string text) => File.AppendAllText(path, text);

    [Fact]
    public void No_file_yet_is_an_empty_update_without_a_reset()
    {
        using var t = new TempDir();
        var u = new EventTail(t.Dir).Poll();
        Assert.Null(u.File);
        Assert.False(u.Reset);
        Assert.Empty(u.Lines);
    }

    [Fact]
    public void The_first_poll_resets_and_reads_from_the_top()
    {
        using var t = new TempDir();
        t.Write("Hasaya_2026.10.02.jsonl", "a\nb\n");
        var tail = new EventTail(t.Dir);

        var u = tail.Poll();
        Assert.Equal("Hasaya_2026.10.02.jsonl", u.File);
        Assert.True(u.Reset);
        Assert.Equal(["a", "b"], u.Lines);
        Assert.Equal(4, tail.Offset);
    }

    [Fact]
    public void Later_polls_carry_the_offset_and_hold_back_a_partial_line()
    {
        using var t = new TempDir();
        var path = t.Write("Hasaya_2026.10.02.jsonl", "a\n");
        var tail = new EventTail(t.Dir);
        tail.Poll();

        Append(path, "b\nhalf");
        var u = tail.Poll();
        Assert.False(u.Reset);
        Assert.Equal(["b"], u.Lines);

        Assert.Empty(tail.Poll().Lines);

        Append(path, "-done\n");
        Assert.Equal(["half-done"], tail.Poll().Lines);
    }

    [Fact]
    public void A_newer_file_resets_and_is_read_from_zero()
    {
        using var t = new TempDir();
        var old = t.Write("Hasaya_2026.10.01.jsonl", "old1\nold2\n");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddMinutes(-5));
        var tail = new EventTail(t.Dir);
        tail.Poll();

        t.Write("Hasaya_2026.10.02.jsonl", "new\n");
        var u = tail.Poll();
        Assert.Equal("Hasaya_2026.10.02.jsonl", u.File);
        Assert.True(u.Reset);
        Assert.Equal(["new"], u.Lines);
    }

    [Fact]
    public void A_file_that_shrank_resets()
    {
        using var t = new TempDir();
        var path = t.Write("Hasaya_2026.10.02.jsonl", "aaaa\nbbbb\n");
        var tail = new EventTail(t.Dir);
        tail.Poll();

        t.Write("Hasaya_2026.10.02.jsonl", "c\n");
        var u = tail.Poll();
        Assert.True(u.Reset);
        Assert.Equal(["c"], u.Lines);
        Assert.Equal(2, tail.Offset);
    }

    [Fact]
    public void A_file_that_vanishes_and_returns_carries_on_where_it_was()
    {
        using var t = new TempDir();
        var path = t.Write("Hasaya_2026.10.02.jsonl", "a\n");
        var tail = new EventTail(t.Dir);
        tail.Poll();

        File.Move(path, path + ".away");
        var gone = tail.Poll();
        Assert.Null(gone.File);
        Assert.False(gone.Reset);

        File.Move(path + ".away", path);
        Append(path, "b\n");
        var back = tail.Poll();
        Assert.False(back.Reset);
        Assert.Equal(["b"], back.Lines);
    }
}
