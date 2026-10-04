using Xunit.Abstractions;

namespace Zerg.Core.Tests.JsParity;

/// <summary>
/// Proof that the parity suite can fail: each case breaks one counting rule in
/// the real JS (one exact text edit), runs a shortened suite against it, and
/// requires at least one mismatch. A mutation that slips through means the
/// suite's inputs cannot see that rule, and the inputs need widening.
///
/// The edits name exact text in the JS; if that text changes, the case fails
/// loudly ("not found") rather than silently testing nothing.
///
/// About six and a half minutes; skip it day to day with
/// <c>--filter Category!=Mutation</c>.
/// </summary>
[Trait("Category", "Mutation")]
public class MutationTests(ITestOutputHelper log)
{
    public static TheoryData<string, string, string, string> Mutations => new()
    {
        // source.js
        // (Removing "if (kind === 'other' && this.kinds[name]) return;" changes
        // nothing: the line after it already keeps a known kind.)
        { "a real kind replaces other", "source.js", "if (!this.kinds[name] || this.kinds[name] === 'other') this.kinds[name] = kind;", "if (!this.kinds[name]) this.kinds[name] = kind;" },
        { "seconds to ms", "source.js", "t: num(raw.t) * 1000,\n        seq", "t: num(raw.t) * 1000 + 1,\n        seq" },
        { "job line classifies", "source.js", "roster.note(who, 'player');", "" },
        { "last job wins", "source.js", "this.jobs[name] = rec;", "if (!this.jobs[name]) this.jobs[name] = rec;" },
        { "no-action name", "source.js", "action: str(raw.action) || ADDL_ACTION,", "action: str(raw.action),"},
        { "heal amount", "source.js", "hp: num(raw.hp),", "hp: num(raw.hp) + 1," },
        { "export keeps heals", "source.js", "heals: (reader.heals || []).filter(keep).map(healRecord)", "heals: (reader.heals || []).map(healRecord)" },
        { "export layout", "source.js", ".join(',\\n    ')", ".join(',\\n   ')" },
        { "import refuses meta", "source.js", "ev.kind === 'job' || ev.kind === 'meta' ||", "ev.kind === 'job' ||" },
        { "import needs a pause", "source.js", "s.pausedAt < s.startedAt) return null;", "s.pausedAt < s.startedAt - 1e15) return null;" },
        { "sub-job omitted", "source.js", "if (j.sub && j.sub !== 'NON') { r.sub", "if (j.sub) { r.sub" },
        // stats.js: the clock
        { "arm floors", "stats.js", "armedAt: Math.floor(now / 1000) * 1000,", "armedAt: now," },
        { "open pause counts", "stats.js", "if (sn.pausedAt != null && sn.pausedAt < t) ms += t - sn.pausedAt;", "" },
        { "pause end exclusive", "stats.js", "if (t >= sn.spans[i].from && t < sn.spans[i].to) return true;", "if (t >= sn.spans[i].from && t <= sn.spans[i].to) return true;" },
        { "latch is sticky", "stats.js", "if (sn && sn.startedAt == null) sn.startedAt = t;", "if (sn) sn.startedAt = t;" },
        { "first counted at arm", "stats.js", "if (e.t < sn.armedAt) continue;", "if (e.t <= sn.armedAt) continue;" },
        // stats.js: what counts
        { "pet credit prefix", "stats.js", "c.action = c.by + ': ' + e.action;", "c.action = e.action;" },
        { "skillchain switch", "stats.js", "if (!skillchains && e.kind === 'skillchain') return null;", "" },
        { "monsters dropped", "stats.js", "if (roster && roster.isMob(e.actor)) return null;", "" },
        { "exclusion by credited name", "stats.js", "if (actors && actors[e.actor] === false) return null;\n    return e;", "return e;" },
        { "collapse time", "stats.js", "if (e.t < u.t) u.t = e.t;", "" },
        { "collapse any-hit", "stats.js", "if (e.hit) u.hit = true;", "" },
        { "collapse target label", "stats.js", "u.target = u.targets.length + ' targets';", "" },
        { "shadows connect", "stats.js", "var MSG_SHADOWS = 31,", "var MSG_SHADOWS = 30," },
        { "dodge is no attempt", "stats.js", "if (e.kind === 'melee' && e.msg === MSG_DODGE) return null;", "" },
        { "ws connects on damage", "stats.js", "if (e.kind === 'ws') return e.dmg > 0;", "if (e.kind === 'ws') return !!e.hit;" },
        { "ws average", "stats.js", "a.wsAvg = s.wsHits ? s.wsTotal / s.wsHits : null;", "a.wsAvg = s.wsTries ? s.wsTotal / s.wsTries : null;" },
        { "pet accuracy melee only", "stats.js", "if (e.kind === 'melee' && o != null) { s.petTries++;", "if (o != null) { s.petTries++;" },
        { "null not zero", "stats.js", "a.scTotal = s.scRows ? s.scTotal : null;", "a.scTotal = s.scTotal;" },
        { "one denominator", "stats.js", "a.duration = fixed != null ? fixed : span;", "a.duration = span;" },
        { "stable actor order", "stats.js", "actors.sort(function (x, y) { return y.total - x.total; });", "actors.sort(function (x, y) { return y.total - x.total || (x.name < y.name ? -1 : 1); });" },
        { "live edge sample", "stats.js", "var n = nb + (live ? 1 : 0);", "var n = nb;" },
        { "histogram floor", "stats.js", "count = Math.max(6, ", "count = Math.max(5, " },
        { "quantile interpolates", "stats.js", "return sorted[lo] + (sorted[hi] - sorted[lo]) * (pos - lo);", "return sorted[lo];" },
        { "heal min above zero", "stats.js", "if (v > 0 && (act.min == null || v < act.min)) act.min = v;", "if (act.min == null || v < act.min) act.min = v;" },
        { "casts per use", "stats.js", "var newCast = h.use == null || !act.uses[h.use];", "var newCast = true;" },
        { "pet heals apart", "stats.js", "if (pet) {\n        a.petTotal += v;", "if (false) {\n        a.petTotal += v;" },
        // stats.js: formatting
        { "compact threshold", "stats.js", "if (a >= 1e4) return fmtNum(n / 1e3, 1) + 'K';", "if (a >= 1e5) return fmtNum(n / 1e3, 1) + 'K';" },
        { "elapsed unpadded", "stats.js", ": m + ':' + String(s).padStart(2, '0');", ": String(m).padStart(2, '0') + ':' + String(s).padStart(2, '0');" },
        { "duration rounds", "stats.js", "sec = Math.max(0, Math.round(sec));", "sec = Math.max(0, Math.floor(sec));" },
        // compare.js
        { "job never borrowed", "compare.js", "return j && j.main && j.main !== 'NON' ? j.main : UNKNOWN_JOB;", "return j && j.main ? j.main : UNKNOWN_JOB;" },
        { "pace NaN past end", "compare.js", "v[j] = times[j] <= stop + step - 1 ? run : NaN;", "v[j] = run;" },
        { "pace bins forward", "compare.js", "Math.ceil(e.t / step)", "Math.floor(e.t / step)" },
        { "pet is a kind", "compare.js", "var kk = e.owner ? 'pet' : e.kind;", "var kk = e.kind;" },
        { "zero rows dropped", "compare.js", "return size(x.a) > 0 || size(x.b) > 0;\n    }).sort", "return true;\n    }).sort" },
        { "delta over |A|", "compare.js", "pct: a ? (b - a) / Math.abs(a) : null", "pct: a ? (b - a) / a : null" },
        { "heal spell name", "compare.js", "hh.action.slice(hh.by.length + 2)", "hh.action" },
        // chart.js
        // ("var yTop = Math.max(vmax, yTicks[yTicks.length - 1]);" as "var yTop = vmax;"
        // changes nothing: the tick picker never returns a tick past vmax.)
        { "tick steps", "chart.js", "norm <= 2 ? 2", "norm <= 3 ? 3" },
        { "time steps", "chart.js", "var TIME_STEPS = [1, 5, 10, 15, 30,", "var TIME_STEPS = [1, 5, 10, 20, 30," },
        { "three gridlines when short", "chart.js", "plot.h < 200 ? 3 : 5", "plot.h < 100 ? 3 : 5" },
        { "at least three time ticks", "chart.js", "Math.max(3, Math.floor(plot.w / 90))", "Math.max(2, Math.floor(plot.w / 90))" },
        { "right margin fits labels", "chart.js", "pad.right = Math.ceil(widest) + 34;", "pad.right = Math.ceil(widest) + 30;" },
        { "name cut at 104", "chart.js", "clip(ctx, se.name, 104)", "clip(ctx, se.name, 204)" },
        { "label gap", "chart.js", "var gap = 13, floor", "var gap = 12, floor" },
        { "labels settle from the floor", "chart.js", "tags[tags.length - 1].y = floor;", "" },
        { "value labels need room", "chart.js", "labels[i].y - labels[i - 1].y < 14", "labels[i].y - labels[i - 1].y < 4" },
        { "four lines or fewer", "chart.js", "} else if (series.length <= 4) {", "} else if (series.length <= 5) {" },
        { "group is dashed", "chart.js", "ctx.setLineDash(ser.group ? [5, 4] : []);", "ctx.setLineDash([]);" },
        { "group ink", "chart.js", "function inkOf(se) { return se.group ? th.muted : se.color; }", "function inkOf(se) { return se.color; }" },
        { "pen lifts", "chart.js", "if (!isFinite(ser.values[j])) { pen = false; continue; }", "if (!isFinite(ser.values[j])) { continue; }" },
        { "marker at the last finite", "chart.js", "for (var i = values.length - 1; i >= 0; i--) if (isFinite(values[i])) return i;", "for (var i = values.length - 1; i >= 0; i--) return i;" },
        { "hover reach", "chart.js", "if (mx < plot.x - 8 ||", "if (mx < plot.x - 1 ||" },
        { "nearest grid time", "chart.js", "Math.round(frac * (times.length - 1))", "Math.floor(frac * (times.length - 1))" },
        { "largest first", "chart.js", "return b.values[idx] - a.values[idx];", "return a.values[idx] - b.values[idx];" },
        { "card flips left", "chart.js", "if (left + tw > wrapW - 4) left = x - tw - 14;", "" },
        { "card kept inside", "chart.js", "if (wrapH && top + tip.offsetHeight > wrapH - 4) top = wrapH - tip.offsetHeight - 4;", "" },
        { "bar thickness cap", "chart.js", "Math.min(24, Math.max(8, band - 12))", "Math.min(28, Math.max(8, band - 12))" },
        { "minimum bar", "chart.js", "var bw = Math.max(2, (r.value / max) * w);", "var bw = Math.max(0, (r.value / max) * w);" },
        { "bar row by band", "chart.js", "if (my >= geo[k].y0 && my < geo[k].y1) {", "if (my > geo[k].y0 + 3 && my < geo[k].y1) {" },
        { "columns fill their bin", "chart.js", "var bw = Math.max(1, slot - 2);", "var bw = Math.min(24, Math.max(1, slot - 2));" },
        { "histogram ends keep clear", "chart.js", "if (tx - plot.x < 34 || plot.x + plot.w - tx < 34) continue;", "" },
        { "mean label flips", "chart.js", "mx > plot.x + plot.w - 46 ? 'right' : 'left'", "mx > plot.x + plot.w - 6 ? 'right' : 'left'" },
        { "mean inside the plot", "chart.js", "if (mx >= plot.x && mx <= plot.x + plot.w) {", "if (mx >= plot.x) {" },
        { "share to one decimal", "chart.js", "F.fmtNum(dist.count ? b.count / dist.count * 100 : 0, 1)", "F.fmtNum(dist.count ? b.count / dist.count * 100 : 0, 0)" },
    };

    [Theory]
    [MemberData(nameof(Mutations))]
    public void A_broken_rule_is_caught(string name, string file, string find, string replace)
    {
        if (JsReference.Unavailable is { } why) { log.WriteLine("skipped: " + why); return; }
        int hits = 0;
        var js = new JsReference((f, text) =>
        {
            if (f != file) return text;
            text = text.Replace("\r\n", "\n");
            int at = text.IndexOf(find, StringComparison.Ordinal);
            if (at < 0 || text.IndexOf(find, at + 1, StringComparison.Ordinal) >= 0) return text;
            hits++;
            return text[..at] + replace + text[(at + find.Length)..];
        });
        Assert.True(hits == 1, $"'{name}': the text to change is not found exactly once in {file}");

        var s = new Suite(js);
        try { Run(s); }
        catch (Jint.Runtime.JavaScriptException e)
        {
            // The broken JS threw where the real one does not: caught too.
            s.Mismatches.Add("the JS threw: " + e.Message);
        }
        log.WriteLine($"{name}: {s.Mismatches.Count} mismatches in {s.Checks} comparisons; first: {s.Mismatches.FirstOrDefault()}");
        Assert.True(s.Mismatches.Count > 0, $"'{name}' was not caught by {s.Checks} comparisons");
    }

    /// <summary>A shorter pass of every part of the suite.</summary>
    static void Run(Suite s)
    {
        var events = Fixtures.EventsWithEdges();
        s.Source("fixture", events, "Hasaya");
        s.Source("odd lines", Suite.OddLines, null);
        var (cs, v) = s.Reader("fixture", events, "Hasaya");
        for (int i = 0; i < 8; i++) s.Scenario("fixture", cs, v, 1000 + i, deep: i == 0);
        s.Quantiles(3);
        s.Formatting(9);
        s.ImportOddities(Fixtures.Export(1, "2026-07-30"), events);
        s.CompareRuns("edges vs 7", Fixtures.ExportWithEdges(), Fixtures.Export(7, "2026-07-31"), 1);
        s.Deltas(5);
        s.NiceTicks(4);
        s.ChartEdges(20);
        var edges = ParseFile.Import(Fixtures.ExportWithEdges());
        s.Charts("edges", edges.Source, edges.Session, 1, shapes: 4);
        s.PaceChart("edges vs 7", Fixtures.ExportWithEdges(), Fixtures.Export(7, "2026-07-31"), 900);
    }
}
