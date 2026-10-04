using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Zerg.Core.Tests.JsParity;

/// <summary>
/// Every exported function of the three JS files, run on both sides on the
/// same inputs, with every difference collected rather than thrown, so the
/// mutation check can run the same suite against a deliberately broken JS
/// and count what it catches.
/// </summary>
sealed partial class Suite(JsReference js)
{
    public List<string> Mismatches { get; } = [];
    public int Checks { get; private set; }
    int names;

    static string N(double d) => Zerg.Core.Js.NumberToString(d);
    static string Q(string? s) => s is null ? "null" : JsonSerializer.Serialize(s);

    void Check(string what, JsonNode? jsValue, object? cs)
    {
        Checks++;
        foreach (var d in Canon.Diff(jsValue, Canon.Of(cs), "$", 8))
            if (Mismatches.Count < 200) Mismatches.Add(what + "  " + d);
    }

    void Same(string what, string jsText, string csText)
    {
        Checks++;
        if (jsText == csText) return;
        int i = 0;
        while (i < Math.Min(jsText.Length, csText.Length) && jsText[i] == csText[i]) i++;
        Mismatches.Add($"{what}  text differs at {i}: js «{Around(jsText, i)}» c# «{Around(csText, i)}»");
    }

    static string Around(string s, int i) => s.Substring(Math.Max(0, i - 20), Math.Min(s.Length, i + 40) - Math.Max(0, i - 20));

    string Fresh(string stem) => stem + (names++).ToString(CultureInfo.InvariantCulture);

    // ------------------------------------------------------------- source.js

    /// <summary>A reader fed the same lines on both sides, compared line by line.
    /// Returns the C# reader and the JS reader's variable name.</summary>
    public (EventReader Cs, string Js) Reader(string label, IReadOnlyList<string> lines, string? owner)
    {
        var v = Fresh("R");
        js.Set("__lines", lines);
        js.Run($"var {v} = DPS.source.create({Q(owner)}); var {v}_fed = __lines.map(function (l) {{ return {v}.feed(l); }});");
        var cs = new EventReader(owner);
        var fed = lines.Select(l => cs.Feed(l)).ToList();
        Check(label + " feed", js.Eval(v + "_fed"), fed);
        CheckReader(label, v, cs);
        return (cs, v);
    }

    void CheckReader(string label, string v, EventReader cs)
    {
        Check(label + " events", js.Eval(v + ".events"), cs.Events);
        Check(label + " heals", js.Eval(v + ".heals"), cs.Heals);
        Check(label + " lineNo", js.Eval(v + ".state.lineNo"), cs.LineNo);
        Check(label + " roster", js.Eval($"{{owner: {v}.roster.owner, manual: {v}.roster.manual, kinds: {v}.roster.kinds, jobs: {v}.roster.jobs}}"),
              new { cs.Roster.Owner, cs.Roster.Manual, cs.Roster.Kinds, cs.Roster.Jobs });
        // Key order is what an export writes them in.
        Check(label + " roster order", js.Eval($"[Object.keys({v}.roster.kinds), Object.keys({v}.roster.manual), Object.keys({v}.roster.jobs)]"),
              new[] { Zerg.Core.Js.Keys(cs.Roster.Kinds.Keys.ToList()), Zerg.Core.Js.Keys(cs.Roster.Manual.Keys.ToList()),
                      Zerg.Core.Js.Keys(cs.Roster.Jobs.Keys.ToList()) });
        var all = cs.Roster.Kinds.Keys.Concat(cs.Roster.Jobs.Keys).Concat(["Nobody", ""]).Distinct().ToList();
        js.Set("__names", all);
        Check(label + " roster lookups",
              js.Eval($"__names.map(function (n) {{ var r = {v}.roster; return [r.kindOf(n), r.isMob(n), r.isAlly(n), r.jobLabel(n), r.jobTitle(n), r.jobOf(n)]; }})"),
              all.Select(n => new object?[] { cs.Roster.KindOf(n), cs.Roster.IsMob(n), cs.Roster.IsAlly(n),
                                              cs.Roster.JobLabel(n), cs.Roster.JobTitle(n), cs.Roster.JobOf(n) }));
    }

    static readonly string Bom = ((char)0xFEFF).ToString();

    /// <summary>Lines that are not what the addon writes: wrong types, missing
    /// fields, broken JSON. Both readers must shrug them off identically.</summary>
    public static readonly string[] OddLines =
    [
        "", " ", "not json", "{", "[1,2]", "null", "5", "\"str\"", "true", "{}",
        Bom + "{\"kind\":\"melee\",\"actor\":\"A\"}",
        "{\"kind\":\"meta\",\"actor\":\"A\"}",
        "{\"kind\":\"melee\"}",
        "{\"kind\":\"melee\",\"actor\":\"\"}",
        "{\"kind\":5,\"actor\":\"Num\",\"actorKind\":\"player\",\"dmg\":3,\"hit\":true}",
        "{\"kind\":0,\"actor\":\"Zero\"}",
        "{\"kind\":[],\"actor\":\"Arr\",\"actorKind\":\"player\"}",
        "{\"kind\":\"melee\",\"actor\":\"Typed\",\"actorKind\":\"player\",\"t\":\"1785000000\",\"seq\":\" 0x1F \",\"use\":\"12\",\"dmg\":\"1e3\",\"hit\":\"true\",\"crit\":1,\"burst\":true,\"msg\":[31],\"actionId\":[1,2],\"target\":7,\"targetKind\":\"mob\",\"owner\":0,\"pet\":\"\"}",
        "{\"kind\":\"melee\",\"actor\":\"Big\",\"actorKind\":\"player\",\"t\":1e400,\"dmg\":-1e400,\"use\":null,\"hit\":true}",
        "{\"kind\":\"melee\",\"actor\":\"Dup\",\"actor\":\"Dup2\",\"actorKind\":\"pet\",\"kind\":\"ws\",\"dmg\":5,\"hit\":true,\"owner\":\"Hasaya\",\"pet\":\"Carbuncle\",\"use\":0}",
        "{\"kind\":\"melee\",\"actor\":\"Neg\",\"actorKind\":\"player\",\"t\":-1.5,\"dmg\":-0,\"use\":-0,\"seq\":\"-\",\"msg\":\"Infinity\",\"hit\":true}",
        "{\"kind\":\"melee\",\"actor\":\"Obj\",\"actorKind\":{\"a\":1},\"action\":\"\",\"target\":\"Mob\",\"targetKind\":\"other\",\"dmg\":{},\"hit\":false}",
        "{\"kind\":\"melee\",\"actor\":\"Mob\",\"actorKind\":\"mob\",\"target\":\"Obj\",\"targetKind\":\"player\",\"dmg\":9,\"hit\":true}",
        "{\"kind\":\"job\",\"actor\":\"\"}",
        "{\"kind\":\"job\",\"actor\":\"Jobber\"}",
        "{\"kind\":\"job\",\"actor\":\"Jobber2\",\"main\":\"WAR\",\"mainLvl\":\"75\",\"sub\":\"NON\",\"subLvl\":37.5}",
        "{\"kind\":\"job\",\"actor\":\"Jobber2\",\"main\":\"RDM\",\"mainLvl\":0,\"sub\":\"BLM\",\"subLvl\":2.5}",
        "{\"kind\":\"heal\",\"actor\":\"\"}",
        "{\"kind\":\"heal\",\"actor\":\"Healer\",\"actorKind\":\"player\",\"target\":\"Obj\",\"targetKind\":\"player\",\"hp\":\"30\",\"use\":\"x\",\"owner\":\"\",\"pet\":5}",
        "{\"kind\":\"heal\",\"actor\":\"Carby\",\"actorKind\":\"pet\",\"target\":\"Healer\",\"hp\":12,\"owner\":\"Healer\",\"pet\":\"Carby\",\"via\":\"pet\",\"use\":4}",
        "{\"kind\":\"defeat\",\"actor\":\"Healer\",\"actorKind\":\"player\",\"target\":\"Unknown\",\"targetKind\":\"other\"}",
        "{\"kind\":\"melee\",\"actor\":\"Esc\",\"actorKind\":\"player\",\"action\":\"Tab\\tQuote\\\"Back\\\\slash\\u0001\\u001f\\ud83d\\ude00\\u00e9\\u2028\",\"dmg\":1,\"hit\":true}",
        "{\"kind\":\"melee\",\"actor\":\"123\",\"actorKind\":\"player\",\"target\":\"7\",\"targetKind\":\"mob\",\"dmg\":1,\"hit\":true,\"use\":1}",
        "{\"kind\":\"melee\",\"actor\":\"Deep\",\"x\":[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[1]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]}",
    ];

    public void Source(string label, IReadOnlyList<string> lines, string? owner)
    {
        var (cs, v) = Reader(label, lines, owner);
        // Reset keeps the roster and drops the rows; feeding again must agree.
        js.Run($"{v}.reset(); var {v}_again = __lines.map(function (l) {{ return {v}.feed(l); }});");
        cs.Reset();
        var again = lines.Select(l => cs.Feed(l)).ToList();
        Check(label + " after reset", js.Eval(v + "_again"), again);
        CheckReader(label + " after reset", v, cs);

        // Manual overrides, set and cleared, in an order that moves keys.
        var some = cs.Roster.Kinds.Keys.Take(4).ToList();
        var ops = new List<(string, string?)>();
        for (int i = 0; i < some.Count; i++) ops.Add((some[i], i % 2 == 0 ? "mob" : "ally"));
        if (some.Count > 1) { ops.Add((some[0], null)); ops.Add((some[0], "ally")); ops.Add((some[1], "")); }
        foreach (var (n, k) in ops)
        {
            js.Run($"{v}.roster.setManual({Q(n)}, {Q(k)});");
            cs.Roster.SetManual(n, k);
        }
        CheckReader(label + " with overrides", v, cs);
    }

    public static readonly string[] FileNames =
    [
        "Hasaya_2026.07.30.jsonl", "hasaya_2026.07.30.JSONL", "Two_Words_2026.12.31.jsonl", "_2026.01.01.jsonl",
        "Hasaya_2026.13.40.jsonl", "Hasaya_0099.00.00.jsonl", "Hasaya_2026.07.30.json", "Hasaya_parse_2026.09.10_2230.zerg",
        "Paradox_Kirin_4.json", "2026-07-30 log.txt", "x_2026_07_30.jsonl", "Abc9_2026.07.30.jsonl", "", "nothing",
        "A_B_2026.07.30.jsonl", "Ab_2026.07.30.jsonl.bak", "Naïve_2026.07.30.jsonl", "Hasaya_١٢٣٤.07.30.jsonl",
    ];

    public void Filenames()
    {
        js.Set("__files", FileNames);
        Check("parseFilename", js.Eval("__files.map(DPS.source.parseFilename)"),
              FileNames.Select(f => { var (o, d) = EventReader.ParseFilename(f); return new { owner = o, date = d }; }));
    }

    // -------------------------------------------------------------- stats.js

    static JsonObject SessionJson(Session s)
    {
        var spans = new JsonArray();
        foreach (var p in s.Spans) spans.Add(new JsonObject { ["from"] = p.From, ["to"] = p.To });
        return new JsonObject
        {
            ["armedAt"] = s.ArmedAt, ["startedAt"] = s.StartedAt, ["spans"] = spans, ["pausedAt"] = s.PausedAt,
        };
    }

    /// <summary>
    /// One randomised session and filter over a reader: the clock driven
    /// through arm, latch, pauses and resumes, then every counting function
    /// over what it selects.
    /// </summary>
    public void Scenario(string label, EventReader cs, string v, int seed, bool deep)
    {
        var r = new Random(seed);
        label += " #" + seed;
        var ts = cs.Events.Select(e => e.T).Distinct().Order().ToArray();
        if (ts.Length == 0) return;
        double lo = ts[0], hi = ts[^1];
        double Pick() => r.Next(3) == 0 ? ts[r.Next(ts.Length)] : lo - 20000 + r.NextDouble() * (hi - lo + 40000);

        // Filters: exclusions (some false, some true, one unknown), skillchains,
        // and manual overrides.
        var names = cs.Roster.Kinds.Keys.ToList();
        var actors = new Dictionary<string, bool>();
        if (r.Next(3) > 0)
            foreach (var n in names) if (r.Next(5) == 0) actors[n] = r.Next(3) == 0;
        actors["Nobody"] = false;
        bool chains = r.Next(3) > 0;
        var manual = new List<(string, string)>();
        for (int i = 0, k = r.Next(3); i < k && names.Count > 0; i++)
            manual.Add((names[r.Next(names.Count)], r.Next(2) == 0 ? "ally" : "mob"));
        foreach (var (n, k) in manual)
        {
            js.Run($"{v}.roster.setManual({Q(n)}, {Q(k)});");
            cs.Roster.SetManual(n, k);
        }
        js.Set("__actors", actors);
        var opts = new FilterOptions { Roster = cs.Roster, Actors = actors, Skillchains = chains };
        var jsOpts = $"{{roster: {v}.roster, actors: __actors, skillchains: {(chains ? "true" : "false")}}}";

        // The clock.
        var armAt = Pick();
        js.Run($"var SN = DPS.stats.arm({N(armAt)});");
        var sn = Session.Arm(armAt);
        Check(label + " arm", js.Eval("SN"), sn);
        Check(label + " armed", js.Eval("[DPS.stats.sessionArmed(SN), DPS.stats.sessionRunning(SN), DPS.stats.sessionAt(SN, " + N(hi) + "), DPS.stats.sessionElapsed(SN, " + N(hi) + ")]"),
              new object?[] { sn.Armed, sn.Running, sn.At(hi), sn.Elapsed(hi) });
        // Filtering an armed session selects nothing.
        Check(label + " filter armed", js.Eval($"DPS.stats.filter({v}.events, Object.assign({{session: SN}}, {jsOpts}))"),
              Counting.Filter(cs.Events, new FilterOptions { Session = sn, Roster = cs.Roster, Actors = actors, Skillchains = chains }));
        var first = Counting.FirstCounted(cs.Events, sn, opts);
        Check(label + " firstCounted", js.Eval($"DPS.stats.firstCounted({v}.events, SN, {jsOpts})"), first);
        var zero = first ?? (r.Next(2) == 0 ? Pick() : (double?)null);
        if (zero is double z)
        {
            js.Run($"DPS.stats.sessionStart(SN, {N(z)}); DPS.stats.sessionStart(SN, {N(z + 5000)});");
            sn.Start(z).Start(z + 5000);
            // Pauses and resumes at increasing times, some on an event's own time.
            double now = z;
            for (int i = 0, k = r.Next(4); i < k; i++)
            {
                now = r.Next(2) == 0 ? Math.Max(now, ts[r.Next(ts.Length)]) : now + r.NextDouble() * 60000;
                js.Run($"DPS.stats.sessionPause(SN, {N(now)}); DPS.stats.sessionPause(SN, {N(now + 1)});");
                sn.Pause(now).Pause(now + 1);
                now += r.Next(2) == 0 ? 0 : r.NextDouble() * 90000;
                js.Run($"DPS.stats.sessionResume(SN, {N(now)}); DPS.stats.sessionResume(SN, {N(now + 1)});");
                sn.Resume(now).Resume(now + 1);
            }
            if (r.Next(2) == 0)
            {
                now = Math.Max(now, hi - r.NextDouble() * (hi - lo) / 3);
                js.Run($"DPS.stats.sessionPause(SN, {N(now)});");
                sn.Pause(now);
            }
        }
        Check(label + " session", js.Eval("SN"), sn);

        var probes = ts.Concat(sn.Spans.SelectMany(s => new[] { s.From - 1, s.From, s.To - 1, s.To }))
                       .Concat(sn.PausedAt is double pa ? [pa - 1, pa, pa + 1] : [])
                       .Concat(sn.StartedAt is double sa ? [sa - 1, sa, sa + 0.5] : []).ToList();
        js.Set("__probes", probes);
        Check(label + " sessionAt", js.Eval("__probes.map(function (t) { return DPS.stats.sessionAt(SN, t); })"),
              probes.Select(t => sn.At(t)));
        Check(label + " sessionElapsed", js.Eval("__probes.map(function (t) { return DPS.stats.sessionElapsed(SN, t); })"),
              probes.Select(t => sn.Elapsed(t)));
        Check(label + " states", js.Eval("[DPS.stats.sessionArmed(SN), DPS.stats.sessionRunning(SN)]"),
              new[] { sn.Armed, sn.Running });

        // What the session selects.
        var endAt = sn.PausedAt ?? hi + r.NextDouble() * 30000;
        var elapsed = sn.Elapsed(endAt);
        js.Run($"var SC = DPS.stats.filter({v}.events, Object.assign({{session: SN}}, {jsOpts}));" +
               "var SC2 = DPS.stats.filter(SC, {actors: __actors});");
        var sc = Counting.Filter(cs.Events, new FilterOptions { Session = sn, Roster = cs.Roster, Actors = actors, Skillchains = chains });
        var sc2 = Counting.Filter(sc, new FilterOptions { Actors = actors });
        Check(label + " filter", js.Eval("SC"), sc);
        Check(label + " filter again", js.Eval("SC2"), sc2);
        // Without a session the clock is left alone, and without a roster the
        // monsters stay.
        Check(label + " filter wall", js.Eval($"DPS.stats.filter({v}.events, {{actors: __actors, skillchains: {(chains ? "true" : "false")}}})"),
              Counting.Filter(cs.Events, new FilterOptions { Actors = actors, Skillchains = chains }));

        Check(label + " counted", js.Eval($"{v}.events.map(function (e) {{ return DPS.stats.counted(e, {v}.roster, __actors, {(chains ? "true" : "false")}); }})"),
              cs.Events.Select(e => Counting.Counted(e, cs.Roster, actors, chains)));
        Check(label + " credit", js.Eval($"{v}.events.map(DPS.stats.credit)"), cs.Events.Select(Counting.Credit));

        var col = Counting.Collapse(sc);
        js.Run("var COL = DPS.stats.collapse(SC);");
        Check(label + " collapse", js.Eval("COL"), col);
        Check(label + " connects", js.Eval("COL.map(DPS.stats.connects).concat(SC.map(DPS.stats.connects))"),
              col.Select(Counting.Connects).Concat(sc.Select(Counting.Connects)));

        var secs = elapsed / 1000;
        js.Run($"var AG = DPS.stats.aggregate(SC, {{duration: {N(secs)}}});");
        var agg = Counting.Aggregate(sc, secs);
        Check(label + " aggregate", js.Eval("AG"), agg);
        Check(label + " aggregate bare", js.Eval("DPS.stats.aggregate(SC2)"), Counting.Aggregate(sc2));
        Check(label + " aggregate raw", js.Eval($"DPS.stats.aggregate({v}.events)"), Counting.Aggregate(cs.Events));

        // Cumulative, with the shapes the views ask for and a few they don't.
        var shown = agg.Actors.Where(a => a.Total > 0).Select(a => a.Name).ToList();
        var lineNames = shown.Concat(r.Next(2) == 0 ? ["Nobody"] : []).ToList();
        if (r.Next(4) == 0 && shown.Count > 0) lineNames.Add(shown[0]);   // a name twice
        js.Set("__names", lineNames);
        int maxPoints = r.Next(3) switch { 0 => 0, 1 => 400, _ => 1 + r.Next(60) };
        foreach (var (from, now) in new (double?, double?)[] { (0, elapsed), (null, null), (0, null), (-5000, elapsed / 2), (null, elapsed + 1) })
        {
            var o = $"{{maxPoints: {maxPoints}{(from is double f ? ", from: " + N(f) : "")}{(now is double nn ? ", now: " + N(nn) : "")}}}";
            Check($"{label} cumulative {o}", js.Eval($"DPS.stats.cumulative(SC, __names, {o})"),
                  Counting.Cumulative(sc, lineNames, from, now, maxPoints));
        }
        Check(label + " cumulative wall", js.Eval($"DPS.stats.cumulative({v}.events, __names, {{now: {N(hi + 3000)}}})"),
              Counting.Cumulative(cs.Events, lineNames, now: hi + 3000));

        // Drill-downs: every character's whole kit and a sample of actions.
        var drills = new List<(string Actor, string? Action)>();
        foreach (var a in agg.Actors)
        {
            drills.Add((a.Name, null));
            foreach (var act in a.ActionOrder)
                if (deep || r.Next(4) == 0) drills.Add((a.Name, act));
        }
        drills.Add(("Nobody", null));
        if (!deep) drills = drills.OrderBy(_ => r.Next()).Take(12).ToList();
        int maxBins = r.Next(2) == 0 ? 0 : 1 + r.Next(40);
        js.Set("__drills", drills.Select(d => new object?[] { d.Actor, d.Action }));
        Check(label + " distribution", js.Eval($"__drills.map(function (d) {{ return DPS.stats.distribution(SC, d[0], d[1], {{maxBins: {maxBins}}}); }})"),
              drills.Select(d => Counting.Distribution(sc, d.Actor, d.Action, maxBins)));

        // Healing over the same window.
        js.Run($"var HL = DPS.stats.filterHeals({v}.heals, {{session: SN, roster: {v}.roster, actors: __actors}});");
        var hl = Healing.Filter(cs.Heals, new FilterOptions { Session = sn, Roster = cs.Roster, Actors = actors });
        Check(label + " filterHeals", js.Eval("HL"), hl);
        Check(label + " filterHeals again", js.Eval("DPS.stats.filterHeals(HL, {actors: __actors})"),
              Healing.Filter(hl, new FilterOptions { Actors = actors }));
        Check(label + " filterHeals wall", js.Eval($"DPS.stats.filterHeals({v}.heals, {{}})"), Healing.Filter(cs.Heals));
        Check(label + " healing", js.Eval("DPS.stats.healing(HL)"), Healing.Totals(hl));
        Check(label + " healing raw", js.Eval($"DPS.stats.healing({v}.heals)"), Healing.Totals(cs.Heals));

        // The export of this exact state, and reading it back.
        if (sn.PausedAt != null) ExportRoundTrip(label, cs, v, sn, r);

        foreach (var (n, _) in manual)
        {
            js.Run($"{v}.roster.setManual({Q(n)}, null);");
            cs.Roster.SetManual(n, null);
        }
    }

    public void Quantiles(int seed)
    {
        var r = new Random(seed);
        var cases = new List<(double[] Sorted, double Q)>();
        for (int i = 0; i < 60; i++)
        {
            var arr = Enumerable.Range(0, r.Next(7)).Select(_ => Math.Round(r.NextDouble() * 1000, r.Next(3))).Order().ToArray();
            cases.Add((arr, r.Next(4) switch { 0 => 0, 1 => 1, 2 => 0.5, _ => r.NextDouble() }));
        }
        js.Set("__q", cases.Select(c => new object[] { c.Sorted, c.Q }));
        Check("quantile", js.Eval("__q.map(function (c) { return DPS.stats.quantile(c[0], c[1]); })"),
              cases.Select(c => Counting.Quantile(c.Sorted, c.Q)));
    }

    // ------------------------------------------------------------ formatting

    public static readonly double[] Numbers =
    [
        0, -0.0, 0.4, 0.5, 1.5, 2.5, -2.5, -0.5, -0.4, 1.25, 0.05, 0.15, 2.675, 1.005,
        999.95, 9999.4, 9999.5, 9999.95, 10000, 12345.678, 99999.95, 999949.9, 999950, 999999.95, 1e6, 1234567.89,
        -1234567.89, -15000, 123456789012, 1e15, 1e21, 1.5e21, 1e-7, 4.35, 0.035,
        double.NaN, double.PositiveInfinity, double.NegativeInfinity,
    ];

    /// <summary>The time formatters, and the number formatters on numbers of
    /// up to 15 significant digits: past that Jint's toLocaleString drops
    /// digits a browser keeps, and it rounds 0.49999999999999994 up.
    /// FormatTests pins those to Chrome's own output.</summary>
    public void Formatting(int seed)
    {
        var r = new Random(seed);
        var nums = Numbers.Concat(Enumerable.Range(0, 80).Select(_ =>
            (r.NextDouble() - 0.3) * Math.Pow(10, r.Next(0, 9)))).ToList();
        js.Set("__nums", nums.Select(d => double.IsFinite(d) ? (object)d : null).ToList());
        // JSON cannot carry NaN or the infinities; put them back by index.
        js.Run("__nums[" + Array.IndexOf(Numbers, double.NaN) + "] = NaN; __nums[" +
               Array.IndexOf(Numbers, double.PositiveInfinity) + "] = Infinity; __nums[" +
               Array.IndexOf(Numbers, double.NegativeInfinity) + "] = -Infinity; __nums[1] = -0;");
        var S = "DPS.stats";
        Check("fmtInt", js.Eval($"__nums.map(function (n) {{ return {S}.fmtInt(n); }})"), nums.Select(Format.Int));
        for (int dp = 0; dp <= 3; dp++)
            Check("fmtNum dp=" + dp, js.Eval($"__nums.map(function (n) {{ return {S}.fmtNum(n, {dp}); }})"), nums.Select(n => Format.Num(n, dp)));
        Check("fmtNum default", js.Eval($"__nums.map(function (n) {{ return {S}.fmtNum(n); }})"), nums.Select(n => Format.Num(n)));
        Check("fmtCompact", js.Eval($"__nums.map(function (n) {{ return {S}.fmtCompact(n); }})"), nums.Select(Format.Compact));
        Check("fmtDuration", js.Eval($"__nums.map(function (n) {{ return {S}.fmtDuration(n / 1000); }})"), nums.Select(n => Format.Duration(n / 1000)));
        Check("fmtElapsed", js.Eval($"__nums.map(function (n) {{ return {S}.fmtElapsed(n); }})"), nums.Select(Format.Elapsed));
        Check("fmtStopwatch", js.Eval($"__nums.map(function (n) {{ return {S}.fmtStopwatch(n); }})"), nums.Select(Format.Stopwatch));
        var clocks = new double[] { 0, 1785000000000, 1785000000999.9, 1785000059999, -1.5, 1789093732106, 1798761599000, double.NaN }
            .Concat(Enumerable.Range(0, 20).Select(_ => 1.7e12 + r.NextDouble() * 1e11)).ToList();
        js.Set("__clocks", clocks.Select(d => double.IsFinite(d) ? (object)d : null).ToList());
        js.Run("__clocks[7] = NaN;");
        Check("fmtClock", js.Eval($"__clocks.map(function (n) {{ return {S}.fmtClock(n); }})"), clocks.Select(Format.Clock));
    }

    // -------------------------------------------------------- export, import

    void ExportRoundTrip(string label, EventReader cs, string v, Session sn, Random r)
    {
        double now = 1789093732106 + r.Next(100000);
        var file = r.Next(3) == 0 ? null : "Hasaya_2026.07.30.jsonl";
        js.Run($"var DOC = DPS.source.exportParse({v}, SN, {{keep: function (e) {{ return DPS.stats.sessionAt(SN, e.t) != null; }}, file: {Q(file)}, now: {N(now)}}});" +
               "var TXT = DPS.source.stringifyParse(DOC);");
        var doc = ParseFile.Export(cs, sn, t => sn.At(t) != null, file, now);
        Check(label + " export", js.Eval("DOC"), doc);
        var text = ParseFile.Stringify(doc);
        Same(label + " export text", js.EvalString("TXT"), text);

        js.Run("var IMP = DPS.source.importParse(TXT);");
        var imp = ParseFile.Import(text);
        CheckImport(label + " import", "IMP", imp);
        // Exported again, it is the same file.
        js.Run("var TXT2 = DPS.source.stringifyParse(DPS.source.exportParse(IMP.source, IMP.session, {file: IMP.file, now: " + N(now) + "}));");
        Same(label + " re-export", js.EvalString("TXT2"), ParseFile.Stringify(ParseFile.Export(imp.Source, imp.Session, null, imp.File, now)));
        Same(label + " round trip", text, ParseFile.Stringify(ParseFile.Export(imp.Source, imp.Session, null, imp.File, now)));
    }

    void CheckImport(string label, string v, ImportedParse cs)
    {
        Check(label + " head", js.Eval($"{{session: {v}.session, skipped: {v}.skipped, file: {v}.file, exported: {v}.exported}}"),
              new { session = cs.Session, skipped = cs.Skipped, file = cs.File, exported = cs.Exported });
        CheckReader(label, v + ".source", cs.Source);
    }

    /// <summary>Text → both sides' import, or both sides' refusal, compared.
    /// Returns the C# import, or null when it was refused.</summary>
    public ImportedParse? Import(string label, string text, string v)
    {
        js.SetString("__text", text);
        js.Run($"var {v}; var {v}_err = null; try {{ {v} = DPS.source.importParse(__text); }} catch (e) {{ {v}_err = e.message; }}");
        var jsErr = js.EvalString($"String({v}_err)");
        ImportedParse? cs = null;
        string csErr = "null";
        try { cs = ParseFile.Import(text); }
        catch (ParseImportException e) { csErr = e.Reason.ToString(); }
        Checks++;
        var expected = jsErr == "null" ? "null" : ErrorCode(jsErr);
        if (expected != csErr) { Mismatches.Add($"{label}  js threw «{jsErr}», c# «{csErr}»"); return null; }
        if (cs != null) CheckImport(label, v, cs);
        return cs;
    }

    /// <summary>The JS messages, by what they say (they name the old product,
    /// which Zerg's own messages do not).</summary>
    static string ErrorCode(string message) =>
        message.Contains("addon event file") ? nameof(ImportError.EventFile)
        : message.Contains("newer version") ? nameof(ImportError.TooNew)
        : message.Contains("no complete session") ? nameof(ImportError.NoSession)
        : message.Contains("no event list") ? nameof(ImportError.NoEvents)
        : message.Contains("is not a") ? nameof(ImportError.NotAParse)
        : "unexpected: " + message;

    public void ImportOddities(string goodText, IReadOnlyList<string> eventLines)
    {
        var good = JsonNode.Parse(goodText)!.AsObject();
        string With(Action<JsonObject> change)
        {
            var d = good.DeepClone().AsObject();
            change(d);
            return d.ToJsonString();
        }
        var session = (JsonObject)good["session"]!;
        var cases = new List<string>
        {
            "", "nope", "{}", "[]", "null", "{\"kind\":\"melee\"}", "[1]",
            string.Join("\n", eventLines.Take(5)), eventLines[0], eventLines[0] + "\nnot json",
            Bom + goodText,goodText + "\n\n", goodText.Replace("\n", "\r\n"),
            With(d => d["format"] = "other"), With(d => d.Remove("format")), With(d => d["kind"] = "x"),
            With(d => d["version"] = 2), With(d => d["version"] = "2"), With(d => d["version"] = new JsonArray(2)),
            With(d => d["version"] = "x"), With(d => d.Remove("version")), With(d => d["version"] = 0.5),
            With(d => d.Remove("session")), With(d => d["session"] = "x"),
            With(d => ((JsonObject)d["session"]!).Remove("pausedAt")),
            With(d => d["session"]!["pausedAt"] = (double)session["startedAt"]! - 1),
            With(d => d["session"]!["startedAt"] = "1"),
            With(d => d["session"]!["armedAt"] = null),
            With(d => d["session"]!["spans"] = new JsonArray(new JsonObject { ["from"] = 1, ["to"] = 2 }, 5)),
            With(d => d["session"]!["spans"] = new JsonArray(new JsonObject { ["from"] = 3, ["to"] = 2 })),
            With(d => d["session"]!["spans"] = new JsonArray(new JsonObject { ["from"] = 1, ["to"] = 2 }, new JsonArray())),
            With(d => d["session"]!["spans"] = "none"),
            With(d => d.Remove("events")), With(d => d["events"] = new JsonObject()),
            With(d => ((JsonArray)d["events"]!).Add(5)),
            With(d => ((JsonArray)d["events"]!).Add(new JsonObject { ["kind"] = "job", ["actor"] = "Sneaky", ["main"] = "THF" })),
            With(d => ((JsonArray)d["events"]!).Add(new JsonObject { ["kind"] = "meta" })),
            With(d => ((JsonArray)d["events"]!).Add(new JsonObject { ["kind"] = "heal", ["actor"] = "Sneaky", ["hp"] = 5 })),
            With(d => ((JsonArray)d["events"]!).Add(new JsonObject { ["kind"] = "melee", ["actor"] = "" })),
            With(d => d["kinds"] = new JsonObject { ["9"] = "player", ["Zed"] = 5, ["Amy"] = "mob", ["1"] = "pet" }),
            With(d => d["kinds"] = new JsonArray("x")),
            With(d => d["manual"] = new JsonObject { ["Hasaya"] = "mob", ["Zed"] = "friend", ["Amy"] = "ally", ["10"] = "ally" }),
            With(d => d["jobs"] = new JsonArray(5, new JsonObject { ["kind"] = "job", ["actor"] = "Amy", ["main"] = "BLU" },
                                                 new JsonObject { ["kind"] = "nope", ["actor"] = "Zed", ["main"] = "WAR" }, new JsonArray())),
            With(d => d["jobs"] = "WAR"),
            With(d => d.Remove("heals")), With(d => d["heals"] = 5),
            With(d => d["heals"] = new JsonArray(new JsonObject { ["kind"] = "melee", ["actor"] = "X", ["dmg"] = 5 }, 1,
                                                  new JsonObject { ["kind"] = "heal", ["actor"] = "Hea", ["hp"] = 7 })),
            With(d => d["owner"] = 5), With(d => d["owner"] = ""), With(d => d["file"] = 5), With(d => d["exported"] = false),
        };
        for (int i = 0; i < cases.Count; i++) Import("import case " + i, cases[i], Fresh("I"));
    }

    // ------------------------------------------------------------ compare.js

    public void CompareRuns(string label, string textA, string textB, int seed)
    {
        var r = new Random(seed);
        var va = Fresh("A");
        var vb = Fresh("B");
        var a = Import(label + " A", textA, va);
        var b = Import(label + " B", textB, vb);
        if (a == null || b == null) { Mismatches.Add(label + "  a run did not import"); return; }

        foreach (var byJob in new[] { false, true })
            foreach (var chains in new[] { true, false })
            {
                var l = $"{label} by={(byJob ? "job" : "actor")} sc={chains}";
                var o = $"{{by: '{(byJob ? "job" : "actor")}', skillchains: {(chains ? "true" : "false")}}}";
                js.Run($"var MA = DPS.compare.measure({va}, {o}); var MB = DPS.compare.measure({vb}, {o}); var D = DPS.compare.diff(MA, MB);");
                var ma = Compare.Measure(a, chains, byJob);
                var mb = Compare.Measure(b, chains, byJob);
                foreach (var (side, m) in new[] { ("MA", ma), ("MB", mb) })
                {
                    // `run` is the import, checked already; heal points are
                    // bare {t, hit, dmg}.
                    js.Run($"var __m = Object.assign({{}}, {side}); delete __m.run; delete __m.healEvents;");
                    Check($"{l} measure {side}", js.Eval("__m"), MeasureShape(m));
                    Check($"{l} heal points {side}", js.Eval($"{side}.healEvents"),
                          m.HealEvents.Select(e => new { t = e.T, hit = e.Hit, dmg = e.Dmg }));
                }
                var d = Compare.Diff(ma, mb);
                Check(l + " diff", js.Eval("D"), d);
                Check(l + " actions", js.Eval("D.rows.map(DPS.compare.actions)"), d.Rows.Select(Compare.Actions));
                Check(l + " healActions", js.Eval("D.heals.map(DPS.compare.healActions)"), d.Heals.Select(Compare.HealActions));
                int mp = r.Next(3) switch { 0 => 0, 1 => 400, _ => 1 + r.Next(50) };
                Check(l + " pace", js.Eval($"DPS.compare.pace(MA, MB, {{maxPoints: {mp}}})"), Compare.Pace(ma, mb, false, mp));
                Check(l + " pace healing", js.Eval($"DPS.compare.pace(MA, MB, {{field: 'healEvents', maxPoints: {mp}}})"),
                      Compare.Pace(ma, mb, true, mp));
                Check(l + " pace reversed", js.Eval("DPS.compare.pace(MB, MA)"), Compare.Pace(mb, ma));
            }
    }

    static object MeasureShape(Measurement m) => new
    {
        m.By, m.Events, m.Agg, m.Members, m.Duration, m.Total, m.Dps, m.Accuracy, m.WsTotal, m.WsCount,
        m.WsAvg, m.WsAcc, m.ScTotal, m.ScCount, m.Characters, m.Best, m.Kinds, m.Targets, m.Heal,
        m.HealSpells, m.HealTargets, m.HasHeals,
    };

    public void Deltas(int seed)
    {
        var r = new Random(seed);
        var vals = new double?[] { null, 0, -0.0, 1, -1, 2.5, 1e6, -3e5, double.NaN };
        var pairs = vals.SelectMany(x => vals.Select(y => (x, y))).Concat(Enumerable.Range(0, 30).Select(_ =>
            (x: (double?)(r.NextDouble() * 2000 - 1000), y: (double?)(r.NextDouble() * 2000 - 1000)))).ToList();
        js.Set("__pairs", pairs.Select(p => new[] { Wire(p.x), Wire(p.y) }));
        js.Run("__pairs.forEach(function (p) { for (var i = 0; i < 2; i++) if (p[i] === 'NaN') p[i] = NaN; else if (p[i] === '-0') p[i] = -0; });");
        Check("delta", js.Eval("__pairs.map(function (p) { return DPS.compare.delta(p[0], p[1]); })"),
              pairs.Select(p => Compare.Delta(p.x, p.y)));
        Check("KINDS", js.Eval("[DPS.compare.KINDS, DPS.compare.UNKNOWN_JOB, DPS.source.ADDL_ACTION, DPS.source.PARSE_FORMAT]"),
              new object[] { Compare.KindLabels, Compare.UnknownJob, EventReader.AdditionalEffect, ParseFile.FormatName });
    }

    static object? Wire(double? d) => d switch
    {
        null => null,
        double x when double.IsNaN(x) => "NaN",
        double x when x == 0 && double.IsNegative(x) => "-0",
        double x => x,
    };
}
