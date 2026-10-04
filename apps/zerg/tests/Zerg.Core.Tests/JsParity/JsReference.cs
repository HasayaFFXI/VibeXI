using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jint;
using Zerg.Core.Tests.Parity;

namespace Zerg.Core.Tests.JsParity;

/// <summary>
/// The real <c>source.js</c>, <c>stats.js</c>, <c>compare.js</c> and <c>chart.js</c> from
/// <c>apps/damage-meter/web/lib</c>, running under Jint, so the C# port can be
/// compared with them on the same inputs. Read from disk on every run: no
/// copy that could drift.
///
/// Transitional, like the code it checks against: deleted at N9 with
/// <c>apps/damage-meter</c>.
/// </summary>
sealed class JsReference
{
    public static readonly string[] Files = ["source.js", "stats.js", "compare.js", "chart.js"];

    static string LibDir => Path.Combine(PythonReference.RepoRoot!, "apps", "damage-meter", "web", "lib");

    /// <summary>Why the reference cannot run here, or null if it can.</summary>
    public static string? Unavailable =>
        PythonReference.Unavailable
        ?? (Files.All(f => File.Exists(Path.Combine(LibDir, f))) ? null : "the JS files under " + LibDir);

    public static string Source(string file) => File.ReadAllText(Path.Combine(LibDir, file));

    // Turns any value into the plain shape Canon builds on the C# side.
    const string Prelude = """
        var window = globalThis;
        function __canon(v) {
          if (v === undefined || typeof v === 'function') return undefined;
          if (v === null) return null;
          if (typeof v === 'number') return isFinite(v) ? v : { $n: String(v) };
          if (typeof v === 'string' || typeof v === 'boolean') return v;
          if (v instanceof Date) return { $date: v.getFullYear() + '-' + (v.getMonth() + 1) + '-' + v.getDate() };
          if (Array.isArray(v) || ArrayBuffer.isView(v)) {
            var a = [];
            for (var i = 0; i < v.length; i++) { var c = __canon(v[i]); a.push(c === undefined ? null : c); }
            return a;
          }
          var o = {};
          for (var k in v) if (Object.prototype.hasOwnProperty.call(v, k)) {
            var x = __canon(v[k]);
            if (x !== undefined && x !== null) o[k] = x;
          }
          return o;
        }
        function __out(v) { var c = __canon(v); return c === undefined ? 'null' : JSON.stringify(c); }
        """;

    readonly Engine engine = new(o => o
        .LocalTimeZone(TimeZoneInfo.Local)
        .Culture(CultureInfo.GetCultureInfo("en-US"))
        .LimitRecursion(10_000));

    /// <param name="mutate">Rewrites a file's text before it runs (file name,
    /// text → text): the mutation check's way of breaking a rule on purpose.</param>
    public JsReference(Func<string, string, string>? mutate = null)
    {
        engine.Execute(Prelude);
        foreach (var f in Files)
        {
            var text = Source(f);
            if (mutate != null) text = mutate(f, text);
            engine.Execute(text, f);
        }
    }

    /// <summary>Declares a global from JSON text.</summary>
    public void SetJson(string name, string json)
    {
        engine.SetValue("__arg", json);
        engine.Execute($"var {name} = JSON.parse(__arg); __arg = undefined;");
    }

    public void Set(string name, object? value) => SetJson(name, JsonSerializer.Serialize(value));

    /// <summary>Declares a global string.</summary>
    public void SetString(string name, string value)
    {
        engine.SetValue("__arg", value);
        engine.Execute($"var {name} = __arg; __arg = undefined;");
    }

    public void Run(string code) => engine.Execute(code);

    /// <summary>The value of an expression, in the plain shape.</summary>
    public JsonNode? Eval(string expression)
    {
        var text = engine.Evaluate($"__out({expression})").AsString();
        return JsonNode.Parse(text);
    }

    /// <summary>A string-valued expression, raw (for text compared byte for byte).</summary>
    public string EvalString(string expression) => engine.Evaluate(expression).AsString();
}

/// <summary>Inputs shared by the parity tests, built once per run.</summary>
static class Fixtures
{
    static readonly Dictionary<string, string[]> Generated = [];
    static readonly Dictionary<string, string> Exports = [];
    static readonly Lock Gate = new();

    /// <summary>The synthetic event file, from the generator, as lines.</summary>
    public static string[] Events(int? seed = null)
    {
        var key = seed?.ToString(CultureInfo.InvariantCulture) ?? "default";
        lock (Gate)
        {
            if (Generated.TryGetValue(key, out var hit)) return hit;
            using var t = new TempDir();
            var path = Path.Combine(t.Dir, "Hasaya_2026.07.30.jsonl");
            var args = new List<string> { PythonReference.GeneratorPy, "--out", path };
            if (seed != null) args.AddRange(["--seed", key]);
            Python(args);
            return Generated[key] = File.ReadAllText(path).Split('\n');
        }
    }

    /// <summary>An import-ready parse from the generator.</summary>
    public static string Export(int seed, string date)
    {
        var key = seed + "@" + date;
        lock (Gate)
        {
            if (Exports.TryGetValue(key, out var hit)) return hit;
            using var t = new TempDir();
            var path = Path.Combine(t.Dir, "Run.json");
            Python([PythonReference.GeneratorPy, "--export", path, "--seed", seed.ToString(CultureInfo.InvariantCulture),
                    "--date", date]);
            return Exports[key] = File.ReadAllText(path);
        }
    }

    /// <summary>
    /// The generated fixture plus rows it has no reason to contain but the
    /// rules care about, timed just after its first record:
    /// <list type="bullet">
    /// <item>a weaponskill flagged a hit that did 0 (it does not connect);</item>
    /// <item>a two-target use whose second row is earlier, and whose first row
    /// misses (the use is one hit, at the earlier time);</item>
    /// <item>an unresolved "other" sighting of a known player (it must not
    /// make them a stranger), and a name first seen unresolved that later acts
    /// as a player (the real answer replaces "other");</item>
    /// <item>two characters tied on damage, the later-sorting name seen first
    /// (ties keep their order);</item>
    /// <item>a zero-damage action (dropped from Compare's action rows);</item>
    /// <item>a pet's heals, one cast on two targets (credited to the owner,
    /// kept out of the Healing column);</item>
    /// <item>a job line with no main job (an unknown job in Compare by Job).</item>
    /// </list>
    /// </summary>
    public static string[] EventsWithEdges(int? seed = null)
    {
        var lines = Events(seed);
        double t0 = double.MaxValue;
        foreach (var l in lines)
            if (Zerg.Core.Js.TryParse(l, out var n) && n is System.Text.Json.Nodes.JsonObject o && Zerg.Core.Js.AsNumber(o["t"]) is double t)
                t0 = Math.Min(t0, t);
        string T(int dt) => (t0 + dt).ToString(CultureInfo.InvariantCulture);
        string[] edges =
        [
            $$"""{"t":{{T(30)}},"seq":1,"use":900001,"kind":"ws","actor":"Hasaya","actorKind":"player","action":"Tachi: Fudo","actionId":152,"target":"Goblin Edge","targetKind":"mob","dmg":0,"hit":true,"crit":false,"burst":false,"msg":185}""",
            $$"""{"t":{{T(41)}},"seq":1,"use":900002,"kind":"magic","actor":"Hasaya","actorKind":"player","action":"Edge Wave","actionId":1,"target":"Goblin Edge","targetKind":"mob","dmg":0,"hit":false,"crit":false,"burst":false,"msg":85}""",
            $$"""{"t":{{T(40)}},"seq":2,"use":900002,"kind":"magic","actor":"Hasaya","actorKind":"player","action":"Edge Wave","actionId":1,"target":"Goblin Edge II","targetKind":"mob","dmg":321,"hit":true,"crit":true,"burst":false,"msg":2}""",
            $$"""{"t":{{T(42)}},"seq":1,"use":900003,"kind":"melee","actor":"Goblin Edge","actorKind":"mob","action":"Attack","actionId":1,"target":"Hasaya","targetKind":"other","dmg":10,"hit":true,"crit":false,"burst":false,"msg":1}""",
            $$"""{"t":{{T(43)}},"seq":1,"use":900009,"kind":"melee","actor":"Goblin Edge","actorKind":"mob","action":"Attack","actionId":1,"target":"Late Arrival","targetKind":"other","dmg":5,"hit":true,"crit":false,"burst":false,"msg":1}""",
            $$"""{"t":{{T(44)}},"seq":1,"use":900010,"kind":"melee","actor":"Late Arrival","actorKind":"player","action":"Attack","actionId":1,"target":"Goblin Edge","targetKind":"mob","dmg":77,"hit":true,"crit":false,"burst":false,"msg":1}""",
            $$"""{"t":{{T(50)}},"seq":1,"use":900004,"kind":"melee","actor":"Zed","actorKind":"player","action":"Attack","actionId":1,"target":"Goblin Edge","targetKind":"mob","dmg":100,"hit":true,"crit":false,"burst":false,"msg":1}""",
            $$"""{"t":{{T(51)}},"seq":1,"use":900005,"kind":"melee","actor":"Amy","actorKind":"player","action":"Attack","actionId":1,"target":"Goblin Edge","targetKind":"mob","dmg":100,"hit":true,"crit":false,"burst":false,"msg":1}""",
            $$"""{"t":{{T(52)}},"seq":1,"use":900006,"kind":"magic","actor":"Amy","actorKind":"player","action":"Stun","actionId":252,"target":"Goblin Edge","targetKind":"mob","dmg":0,"hit":false,"crit":false,"burst":false,"msg":85}""",
            $$"""{"kind":"heal","t":{{T(60)}},"seq":1,"use":900007,"via":"pet","actor":"Carbuncle","actorKind":"pet","action":"Healing Ruby II","actionId":2,"target":"Hasaya","targetKind":"player","hp":140,"msg":318,"owner":"Amy","pet":"Carbuncle"}""",
            $$"""{"kind":"heal","t":{{T(60)}},"seq":2,"use":900007,"via":"pet","actor":"Carbuncle","actorKind":"pet","action":"Healing Ruby II","actionId":2,"target":"Zed","targetKind":"player","hp":0,"msg":318,"owner":"Amy","pet":"Carbuncle"}""",
            $$"""{"kind":"heal","t":{{T(61)}},"seq":1,"use":900008,"via":"magic","actor":"Amy","actorKind":"player","action":"Cure","actionId":1,"target":"Zed","targetKind":"player","hp":45,"msg":7}""",
            $$"""{"kind":"job","t":{{T(62)}},"actor":"Zed","main":"NON","mainId":0,"mainLvl":0}""",
        ];
        return [.. lines.Where(l => l.Trim().Length > 0), .. edges];
    }

    /// <summary>The edged fixture as an export: armed before its first record,
    /// paused after its last.</summary>
    public static string ExportWithEdges()
    {
        var r = EventReader.ParseAll(EventsWithEdges(), "Hasaya");
        var sn = Session.Arm(r.Events.Min(e => e.T) - 1000);
        sn.Start(Counting.FirstCounted(r.Events, sn, new FilterOptions { Roster = r.Roster })!.Value);
        sn.Pause(Math.Max(r.Events.Max(e => e.T), r.Heals.Max(h => h.T)) + 5000);
        return ParseFile.Stringify(ParseFile.Export(r, sn, t => sn.At(t) != null, "Hasaya_2026.07.30.jsonl", 1785500000000));
    }

    /// <summary>The real 18-character alliance parse.</summary>
    public static string Alliance() => File.ReadAllText(Path.Combine(PythonReference.RepoRoot!, "apps", "damage-meter",
        "tools", "exports", "Paradox_Kirin_4.json"));

    static void Python(List<string> args)
    {
        var psi = new ProcessStartInfo(PythonReference.Python!)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync();
        p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException("generator failed: " + err.Result);
    }
}

/// <summary>A test that needs the JS reference (and Python, for the fixture generator).</summary>
sealed class JsParityFactAttribute : FactAttribute
{
    public JsParityFactAttribute()
    {
        if (JsReference.Unavailable is { } why) Skip = "JS parity needs " + why;
    }
}
