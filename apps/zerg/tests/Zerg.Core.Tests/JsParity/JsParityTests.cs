using Xunit.Abstractions;

namespace Zerg.Core.Tests.JsParity;

/// <summary>
/// Zerg.Core's counting logic against the real JS it was ported from, on the
/// generated fixture (several seeds), the real 18-character alliance export
/// and randomised sessions and filters. Every exported function is compared
/// field by field; see <see cref="Suite"/>.
///
/// Scaffolding: deleted at N9 with <c>apps/damage-meter</c>.
/// </summary>
public class JsParityTests(ITestOutputHelper log)
{
    const string Owner = "Hasaya";

    void Pass(Suite s)
    {
        log.WriteLine($"{s.Checks} comparisons");
        foreach (var m in s.Mismatches) log.WriteLine(m);
        Assert.True(s.Mismatches.Count == 0, $"{s.Mismatches.Count} mismatches; first: {s.Mismatches.FirstOrDefault()}");
    }

    [JsParityFact]
    public void Reading_lines()
    {
        var s = new Suite(new JsReference());
        s.Source("fixture", Fixtures.Events(), Owner);
        s.Source("fixture seed 7", Fixtures.Events(7), Owner);
        s.Source("fixture with edges", Fixtures.EventsWithEdges(), Owner);
        s.Source("odd lines", Suite.OddLines, null);
        s.Source("odd lines after the fixture", [.. Fixtures.Events().Take(40), .. Suite.OddLines], "");
        s.Filenames();
        Pass(s);
    }

    [JsParityFact]
    public void Sessions_and_counting_on_the_fixture()
    {
        var s = new Suite(new JsReference());
        foreach (var seed in new int?[] { null, 7, 11 })
        {
            var (cs, v) = s.Reader("fixture " + seed, Fixtures.Events(seed), Owner);
            for (int i = 0; i < 25; i++) s.Scenario("fixture " + seed, cs, v, 1000 * (seed ?? 1) + i, deep: i < 3);
        }
        var (ec, ev) = s.Reader("edges", Fixtures.EventsWithEdges(), Owner);
        for (int i = 0; i < 25; i++) s.Scenario("edges", ec, ev, 7000 + i, deep: i < 3);
        Pass(s);
    }

    [JsParityFact]
    public void Sessions_and_counting_on_the_alliance_parse()
    {
        var s = new Suite(new JsReference());
        var imp = s.Import("alliance", Fixtures.Alliance(), "KIRIN");
        Assert.NotNull(imp);
        for (int i = 0; i < 25; i++) s.Scenario("alliance", imp.Source, "KIRIN.source", 500 + i, deep: i < 2);
        s.Quantiles(3);
        Pass(s);
    }

    [JsParityFact]
    public void Export_and_import()
    {
        var s = new Suite(new JsReference());
        s.Import("generated export", Fixtures.Export(1, "2026-07-30"), "G1");
        s.Import("edges export", Fixtures.ExportWithEdges(), "G2");
        s.Import("alliance", Fixtures.Alliance(), "K");
        s.ImportOddities(Fixtures.Export(1, "2026-07-30"), Fixtures.Events());
        s.ImportOddities(Fixtures.Alliance(), Fixtures.Events());
        Pass(s);
    }

    [JsParityFact]
    public void Compare_runs()
    {
        var s = new Suite(new JsReference());
        var a = Fixtures.Export(1, "2026-07-30");
        var b = Fixtures.Export(7, "2026-07-31");
        var c = Fixtures.Export(11, "2026-08-01");
        var k = Fixtures.Alliance();
        s.CompareRuns("1 vs 7", a, b, 1);
        s.CompareRuns("7 vs 1", b, a, 2);
        s.CompareRuns("11 vs alliance", c, k, 3);
        s.CompareRuns("alliance vs itself", k, k, 4);
        s.CompareRuns("edges vs 1", Fixtures.ExportWithEdges(), a, 6);
        s.CompareRuns("alliance vs edges", k, Fixtures.ExportWithEdges(), 7);
        s.Deltas(5);
        Pass(s);
    }

    [JsParityFact]
    public void Charts()
    {
        var s = new Suite(new JsReference());
        s.NiceTicks(4);
        s.ChartEdges(20);
        var edges = ParseFile.Import(Fixtures.ExportWithEdges());
        s.Charts("edges", edges.Source, edges.Session, 1);
        var seven = ParseFile.Import(Fixtures.Export(7, "2026-07-31"));
        s.Charts("seed 7", seven.Source, seven.Session, 300);
        var alliance = ParseFile.Import(Fixtures.Alliance());
        s.Charts("alliance", alliance.Source, alliance.Session, 600);
        s.PaceChart("edges vs alliance", Fixtures.ExportWithEdges(), Fixtures.Alliance(), 900);
        s.PaceChart("7 vs 1", Fixtures.Export(7, "2026-07-31"), Fixtures.Export(1, "2026-07-30"), 950);
        Pass(s);
    }

    [JsParityFact]
    public void Formatting()
    {
        var s = new Suite(new JsReference());
        s.Formatting(9);
        Pass(s);
    }
}
