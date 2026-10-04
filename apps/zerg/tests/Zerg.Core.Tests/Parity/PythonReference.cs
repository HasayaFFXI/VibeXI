using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Zerg.Core.Tests.Parity;

/// <summary>
/// Runs damage-meter.py's own /api/events code (via reference.py) so Zerg's port
/// can be compared against the real thing on the same bytes on disk. Live rather
/// than a committed golden file: no drift, and no exposure to git rewriting the
/// line endings of fixture files (this repo has autocrlf=true and text=auto).
/// </summary>
static class PythonReference
{
    public static readonly string? RepoRoot = FindRepoRoot();
    public static string ServerPy => Path.Combine(RepoRoot!, "apps", "damage-meter", "damage-meter.py");
    public static string GeneratorPy => Path.Combine(RepoRoot!, "apps", "damage-meter", "tools", "gen-test-events.py");
    static string ReferencePy => Path.Combine(RepoRoot!, "apps", "zerg", "tests", "Zerg.Core.Tests", "Parity", "reference.py");

    static readonly Lazy<string?> Exe = new(FindPython);
    public static string? Python => Exe.Value;

    /// <summary>Why the parity tests cannot run here, or null if they can.</summary>
    public static string? Unavailable =>
        RepoRoot is null ? "repo root (apps/damage-meter/damage-meter.py) not found above the test binaries"
        : Python is null ? "no Python 3 on PATH"
        : null;

    public sealed record Answer(string Query, JsonObject? Payload, string? Error);

    public static List<Answer> Ask(string eventsDir, IEnumerable<string> queries)
    {
        var request = JsonSerializer.Serialize(new { server = ServerPy, dir = eventsDir, queries = queries.ToArray() });
        var stdout = Run(Python!, [ReferencePy], request);
        return JsonNode.Parse(stdout)!.AsArray().Select(n =>
        {
            var o = n!.AsObject();
            return new Answer(o["query"]!.GetValue<string>(),
                              o["payload"]?.AsObject(),
                              o["error"]?.GetValue<string>());
        }).ToList();
    }

    /// <summary>Writes the synthetic fixture with the damage meter's own generator.</summary>
    public static void Generate(string outPath) => Run(Python!, [GeneratorPy, "--out", outPath], null);

    static string Run(string exe, string[] args, string? stdin)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["PYTHONDONTWRITEBYTECODE"] = "1";   // no __pycache__ litter in the repo

        using var p = Process.Start(psi)!;
        var outTask = p.StandardOutput.ReadToEndAsync();
        var errTask = p.StandardError.ReadToEndAsync();
        if (stdin is not null) p.StandardInput.Write(stdin);
        p.StandardInput.Close();
        if (!p.WaitForExit(120_000)) { p.Kill(); throw new TimeoutException($"{exe} {string.Join(' ', args)}"); }
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"{Path.GetFileName(args[0])} exited {p.ExitCode}:\n{errTask.Result}");
        return outTask.Result;
    }

    static string? FindRepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "apps", "damage-meter", "damage-meter.py")))
                return d.FullName;
        return null;
    }

    static string? FindPython()
    {
        // `python` first; `py -3` is not tried because ArgumentList would need
        // the extra argument threaded through everywhere. The Microsoft Store
        // stub also answers to `python`, but it fails --version, so it is caught.
        try
        {
            var psi = new ProcessStartInfo("python", "--version")
            {
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true,
            };
            using var p = Process.Start(psi)!;
            var v = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(10_000);
            return p.ExitCode == 0 && v.StartsWith("Python 3", StringComparison.Ordinal) ? "python" : null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }
}

/// <summary>A Fact that reports itself skipped, not passed, when Python or the
/// damage-meter source is missing.</summary>
sealed class ParityFactAttribute : FactAttribute
{
    public ParityFactAttribute()
    {
        if (PythonReference.Unavailable is { } why) Skip = "Parity needs " + why;
    }
}
