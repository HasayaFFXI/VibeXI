using System.Text.Json;

namespace Zerg.Core.Tests;

public class EventsApiTests
{
    [Fact]
    public void No_event_file_gives_the_waiting_shape()
    {
        using var t = new TempDir();
        var p = EventsApi.Get(t.Dir, null, 0);
        Assert.Null(p.File);
        Assert.True(p.Reset);
        Assert.Empty(p.Lines);
        Assert.Equal(t.Dir, p.Dir);
    }

    [Fact]
    public void First_poll_resets_and_reads_from_the_top_whatever_offset_it_sent()
    {
        using var t = new TempDir();
        t.Write("Hasaya_2026.01.01.jsonl", "a\nb\n");
        var p = EventsApi.Get(t.Dir, null, 2);
        Assert.Equal("Hasaya_2026.01.01.jsonl", p.File);
        Assert.True(p.Reset);
        Assert.Equal(0, p.Offset);
        Assert.Equal(["a", "b"], p.Lines);
    }

    [Fact]
    public void Same_file_continues_from_the_offset_without_a_reset()
    {
        using var t = new TempDir();
        t.Write("Hasaya_2026.01.01.jsonl", "a\nb\n");
        var p = EventsApi.Get(t.Dir, "Hasaya_2026.01.01.jsonl", 2);
        Assert.False(p.Reset);
        Assert.Equal(["b"], p.Lines);
        Assert.Equal(4, p.NextOffset);
    }

    [Fact]
    public void A_newer_file_resets_the_client()
    {
        using var t = new TempDir();
        var old = t.Write("Hasaya_2026.01.01.jsonl", "a\n");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddHours(-1));
        t.Write("Hasaya_2026.01.02.jsonl", "z\n");

        var p = EventsApi.Get(t.Dir, "Hasaya_2026.01.01.jsonl", 2);
        Assert.Equal("Hasaya_2026.01.02.jsonl", p.File);
        Assert.True(p.Reset);
        Assert.Equal(["z"], p.Lines);
    }

    [Fact]
    public void A_shrunk_file_resets_but_echoes_the_clients_offset()
    {
        // Matches the Python server; the page never reads `offset`.
        using var t = new TempDir();
        t.Write("Hasaya_2026.01.01.jsonl", "a\n");
        var p = EventsApi.Get(t.Dir, "Hasaya_2026.01.01.jsonl", 50);
        Assert.True(p.Reset);
        Assert.Equal(50, p.Offset);
        Assert.Equal(["a"], p.Lines);
    }

    [Fact]
    public void Json_is_ascii_only_and_round_trips()
    {
        using var t = new TempDir();
        t.Write("Hasaya_2026.01.01.jsonl", "{\"actor\":\"caf\u00e9\"}\n");
        var body = EventsApi.Get(t.Dir, null, 0).ToJson();

        Assert.All(body, b => Assert.True(b < 0x80));
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("{\"actor\":\"caf\u00e9\"}", doc.RootElement.GetProperty("lines")[0].GetString());
    }

    [Fact]
    public void Json_key_order_follows_the_python_server()
    {
        using var t = new TempDir();
        static string Keys(byte[] body) => string.Join(",",
            JsonDocument.Parse(body).RootElement.EnumerateObject().Select(p => p.Name));

        Assert.Equal("ok,file,reset,offset,nextOffset,size,lines,dir",
                     Keys(EventsApi.Get(t.Dir, null, 0).ToJson()));
        t.Write("Hasaya_2026.01.01.jsonl", "a\n");
        Assert.Equal("ok,file,dir,mtime,reset,offset,nextOffset,size,lines",
                     Keys(EventsApi.Get(t.Dir, null, 0).ToJson()));
    }

    [Fact]
    public void Mtime_looks_like_python_isoformat()
    {
        var whole = new DateTime(2026, 7, 30, 12, 0, 0, DateTimeKind.Utc);
        Assert.Matches(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d[+-]\d\d:\d\d$", EventsApi.IsoLocal(whole));
        Assert.Matches(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d{6}[+-]\d\d:\d\d$",
                       EventsApi.IsoLocal(whole.AddTicks(1234560)));
    }

    [Theory]
    [InlineData(@"C:\x\events\", @"C:\x\events")]
    [InlineData("C:/x/events", @"C:\x\events")]
    [InlineData(@"C:\", @"C:\")]
    public void Dir_is_shown_the_way_python_prints_a_path(string given, string shown)
    {
        Assert.Equal(shown, EventsApi.DisplayDir(given));
    }

    [Fact]
    public void FromQuery_reads_file_and_offset()
    {
        using var t = new TempDir();
        t.Write("Hasaya_2026.01.01.jsonl", "a\nb\n");
        var p = EventsApi.FromQuery(t.Dir, "?offset=2&file=Hasaya_2026.01.01.jsonl");
        Assert.False(p.Reset);
        Assert.Equal(["b"], p.Lines);
    }
}
