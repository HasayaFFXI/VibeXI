using System.Text;
using System.Text.Json.Nodes;
using Xunit.Abstractions;

namespace Zerg.Core.Tests.Parity;

/// <summary>
/// The same queries, against the same files, answered by damage-meter.py and by
/// Zerg.Core. Every field must match except mtime, which may differ by a
/// microsecond because Python goes through a float (it is compared to within
/// 10 µs, and the UTC offset exactly).
///
/// Negative offsets are deliberately absent: Python answers 500, Zerg treats
/// them as a shrink. EventFilesTests covers Zerg's side.
/// </summary>
public class ParityTests(ITestOutputHelper log)
{
    const string Name = "Hasaya_2026.07.30.jsonl";

    [ParityFact]
    public void Generated_fixture_at_line_boundaries_and_mid_line()
    {
        using var t = new TempDir();
        var path = Path.Combine(t.Dir, Name);
        PythonReference.Generate(path);
        var bytes = File.ReadAllBytes(path);

        var bounds = Boundaries(bytes);
        // Every boundary would make the reference's answer quadratic in the file
        // size; the first and last few plus every 25th covers the same ground.
        var picked = bounds.Where((_, i) => i < 4 || i > bounds.Count - 5 || i % 25 == 0).ToList();
        var queries = new List<string> { "" };
        foreach (var b in picked)
        {
            queries.Add($"file={Name}&offset={b}");
            queries.Add($"file={Name}&offset={b + 7}");             // mid-line
        }
        queries.Add($"file={Name}&offset={bytes.Length + 1}");      // "shrank"
        queries.Add($"file=Someone_Else_2026.07.30.jsonl&offset=100");

        Compare(t.Dir, queries);
    }

    [ParityFact]
    public void Awkward_bytes_at_every_offset()
    {
        // CRLF, a blank CRLF line, a lone CR, a BOM, raw multibyte UTF-8, every
        // flavour of invalid UTF-8 (lone lead, truncated sequence, encoded
        // surrogate, above U+10FFFF, overlong), and an unfinished last line.
        // Every byte offset, so reads start inside multibyte characters too --
        // which is where the two decoders' replacement rules would show.
        var b = new List<byte>();
        void S(string s) => b.AddRange(Encoding.UTF8.GetBytes(s));
        b.AddRange([0xEF, 0xBB, 0xBF]);
        S("{\"a\":1}\r\n\r\n{\"b\":2}\rX\n\n");
        S("café テ \U0001F600\n");
        b.AddRange([(byte)'<', 0xFF, 0xFE, (byte)'|', 0xC3, (byte)'|', 0xE3, 0x81, (byte)'|',
                    0xED, 0xA0, 0x80, (byte)'|', 0xF4, 0x90, 0x80, 0x80, (byte)'|', 0xC0, 0xAF, (byte)'>', (byte)'\n']);
        S("{\"unfinished\":");

        using var t = new TempDir();
        t.Write(Name, b.ToArray());
        var queries = new List<string> { "" };
        for (int i = 0; i <= b.Count + 1; i++) queries.Add($"file={Name}&offset={i}");
        Compare(t.Dir, queries);
    }

    [ParityFact]
    public void Query_string_oddities()
    {
        using var t = new TempDir();
        t.Write(Name, "{\"a\":1}\n{\"b\":2}\n{\"c\":3}\n");
        Compare(t.Dir,
        [
            "", "offset=8", $"file={Name}", $"file={Name}&offset=8",
            $"file={Name}&offset=abc", $"file={Name}&offset=8.0", $"file={Name}&offset=0x8",
            $"file={Name}&offset=+8", $"file={Name}&offset=%2B8", $"file={Name}&offset=%208%20",
            $"file={Name}&offset= 8 ", $"file={Name}&offset=8&offset=16", "file=&offset=8",
            "file&offset=8", $"&&file={Name}&&offset=16&&", $"offset=16&file={Name}",
            $"file={Name.Replace("_", "%5F")}&offset=8", $"file={Name.ToLowerInvariant()}&offset=8",
            $"file={Name}&offset=999999", $"file={Name}&offset=24",
        ]);
    }

    [ParityFact]
    public void Tiny_and_empty_files()
    {
        using (var t = new TempDir())
        {
            t.Write(Name, "no newline yet");
            Compare(t.Dir, ["", .. Enumerable.Range(0, 16).Select(i => $"file={Name}&offset={i}")]);
        }
        using (var t = new TempDir())
        {
            t.Write(Name, "");
            Compare(t.Dir, ["", $"file={Name}&offset=0", $"file={Name}&offset=5"]);
        }
    }

    [ParityFact]
    public void No_file_and_no_directory()
    {
        using (var t = new TempDir())
            Compare(t.Dir, ["", $"file={Name}&offset=10"]);
        Compare(Path.Combine(Path.GetTempPath(), "zerg-tests", "never-created"), ["", "offset=3"]);
    }

    [ParityFact]
    public void Newest_file_selection()
    {
        using var t = new TempDir();
        var now = DateTime.UtcNow;
        string[] names = ["A_2026.07.29.jsonl", "B_2026.07.30.jsonl", "C_2026.07.28.jsonl"];
        int[] ageMinutes = [3, 1, 2];
        for (int i = 0; i < names.Length; i++)
        {
            var p = t.Write(names[i], $"{{\"n\":\"{names[i]}\"}}\n");
            File.SetLastWriteTimeUtc(p, now.AddMinutes(-ageMinutes[i]));
        }
        t.Write("decoy.txt", "x\n");
        Compare(t.Dir, ["", .. names.Select(n => $"file={n}&offset=3")]);
    }

    [ParityFact]
    public void Over_the_read_cap()
    {
        using var t = new TempDir();
        var line = "{\"pad\":\"" + new string('x', 90) + "\"}\n";       // 101 bytes, so the cap lands mid-line
        int count = EventFiles.MaxTail / line.Length + 300;
        var bytes = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat(line, count)));
        t.Write(Name, bytes);

        int afterCap = Array.LastIndexOf(bytes, (byte)'\n', EventFiles.MaxTail - 1) + 1;
        Compare(t.Dir, [$"file={Name}&offset=0", $"file={Name}&offset={afterCap}",
                        $"file={Name}&offset={bytes.Length - 50}"]);
    }

    // ------------------------------------------------------------------ core

    void Compare(string dir, IReadOnlyList<string> queries)
    {
        var answers = PythonReference.Ask(dir, queries);
        Assert.Equal(queries.Count, answers.Count);
        log.WriteLine($"{Path.GetFileName(dir)}: {answers.Count} queries");

        foreach (var a in answers)
        {
            Assert.True(a.Error is null, $"damage-meter.py failed on '{a.Query}': {a.Error}");
            var py = a.Payload!;
            var cs = JsonNode.Parse(EventsApi.FromQuery(dir, a.Query).ToJson())!.AsObject();

            var keys = py.Select(p => p.Key).Order().ToList();
            Assert.Equal(keys, cs.Select(p => p.Key).Order().ToList());

            foreach (var key in keys.Where(k => k is not ("lines" or "mtime")))
                Assert.True(JsonNode.DeepEquals(py[key], cs[key]),
                    $"'{a.Query}': {key} python={py[key]?.ToJsonString()} zerg={cs[key]?.ToJsonString()}");

            var pyLines = py["lines"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
            var csLines = cs["lines"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
            Assert.True(pyLines.Count == csLines.Count,
                $"'{a.Query}': python returned {pyLines.Count} lines, zerg {csLines.Count}");
            for (int i = 0; i < pyLines.Count; i++)
                Assert.True(pyLines[i] == csLines[i],
                    $"'{a.Query}': line {i} differs\n python={Escape(pyLines[i])}\n zerg  ={Escape(csLines[i])}");

            if (py["mtime"] is { } pm)
            {
                var p = DateTimeOffset.Parse(pm.GetValue<string>());
                var c = DateTimeOffset.Parse(cs["mtime"]!.GetValue<string>());
                Assert.Equal(p.Offset, c.Offset);
                Assert.True(Math.Abs((p - c).Ticks) <= 100,
                    $"'{a.Query}': mtime python={pm} zerg={cs["mtime"]}");
            }
        }
    }

    static List<int> Boundaries(byte[] bytes)
    {
        var list = new List<int> { 0 };
        for (int i = 0; i < bytes.Length; i++) if (bytes[i] == '\n') list.Add(i + 1);
        return list;
    }

    static string Escape(string s) =>
        string.Concat(s.Select(ch => ch < 0x20 || ch > 0x7E ? $"\\u{(int)ch:X4}" : ch.ToString()));
}
