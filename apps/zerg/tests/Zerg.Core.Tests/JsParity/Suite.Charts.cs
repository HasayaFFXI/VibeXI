using System.Globalization;
using System.Text.Json.Nodes;
using Zerg.Core.Charts;

namespace Zerg.Core.Tests.JsParity;

/// <summary>
/// The chart layout against the real <c>chart.js</c>. The JS draws onto a
/// canvas that records every call instead of painting; the C# layout is turned
/// into the calls it stands for, and the two lists are compared: every label,
/// gridline, marker, bar and column, then the hover card's text and position
/// at a spread of pointer positions. Text is measured by one made-up rule on
/// both sides, so widths agree exactly.
///
/// Scaffolding: deleted at N9 with <c>apps/damage-meter</c>.
/// </summary>
sealed partial class Suite
{
    bool chartsReady;

    // A canvas, its 2D context and the hover card's element, as far as
    // chart.js uses them. The inks are one-letter names so a wrong one shows.
    const string ChartStub = """
        var FFXITheme = {
          chart: function () {
            return { surface: 'S', surface2: 'S2', grid: 'G', axis: 'A', axisAlt: 'AA', ink: 'I', ink2: 'I2', muted: 'M', font: 'F' };
          },
          series: function (i) { return 'series' + i; }
        };
        function __measure(t) {
          var w = 0;
          for (var i = 0; i < t.length; i++) w += 4 + (t.charCodeAt(i) % 7) * 0.5;
          return w;
        }
        function __canvas(w, h, tipW, tipH) {
          var ops = [];
          var tip = { style: {}, innerHTML: '', offsetWidth: tipW, offsetHeight: tipH, className: '', parentNode: null,
                      setAttribute: function () {} };
          var wrap = { clientWidth: w, clientHeight: h, tip: null,
                       querySelector: function () { return this.tip; },
                       appendChild: function (t) { this.tip = t; t.parentNode = this; } };
          var ctx = {
            font: '', fillStyle: '', strokeStyle: '', lineWidth: 1, textAlign: 'start', textBaseline: 'alphabetic',
            path: [], dash: [],
            setTransform: function () {}, clearRect: function () {}, save: function () {}, restore: function () {},
            getImageData: function () { return {}; }, putImageData: function () {},
            setLineDash: function (d) { this.dash = d.slice(); },
            beginPath: function () { this.path = []; },
            moveTo: function (x, y) { this.path.push(['M', x, y]); },
            lineTo: function (x, y) { this.path.push(['L', x, y]); },
            quadraticCurveTo: function (a, b, x, y) { this.path.push(['Q', a, b, x, y]); },
            closePath: function () { this.path.push(['Z']); },
            arc: function (x, y, r) { this.path.push(['A', x, y, r]); },
            stroke: function () { ops.push({ op: 'stroke', style: this.strokeStyle, width: this.lineWidth, dash: this.dash.slice(), path: this.path.slice() }); },
            fill: function () { ops.push({ op: 'fill', style: this.fillStyle, path: this.path.slice() }); },
            fillRect: function (x, y, w, h) { ops.push({ op: 'rect', style: this.fillStyle, x: x, y: y, w: w, h: h }); },
            fillText: function (t, x, y) { ops.push({ op: 'text', text: String(t), x: x, y: y, style: this.fillStyle, font: this.font, align: this.textAlign, base: this.textBaseline }); },
            measureText: function (t) { return { width: __measure(String(t)) }; }
          };
          var canvas = {
            ownerDocument: { defaultView: { devicePixelRatio: 1 }, createElement: function () { return tip; } },
            parentNode: wrap, width: 0, height: 0, onmousemove: null, onmouseleave: null,
            getBoundingClientRect: function () { return { left: 0, top: 0, width: w, height: h }; },
            getContext: function () { return ctx; }
          };
          return { canvas: canvas, ops: ops, tip: tip };
        }
        function __hover(c, points) {
          return points.map(function (p) {
            c.ops.length = 0;
            if (!c.canvas.onmousemove) return { shown: false };
            c.canvas.onmousemove({ clientX: p[0], clientY: p[1] });
            if (c.tip.style.visibility !== 'visible') return { shown: false, ops: c.ops.slice() };
            return { shown: true, html: c.tip.innerHTML, left: c.tip.style.left, top: c.tip.style.top, ops: c.ops.slice() };
          });
        }
        function __nan(a) { return a.map(function (v) { return v === null ? NaN : v; }); }
        function __line(w, h, times, series, opts, points, tipW, tipH) {
          var c = __canvas(w, h, tipW, tipH);
          series.forEach(function (s, i) { s.values = __nan(s.values); s.color = 'c' + i; });
          DPS.chart.line(c.canvas, { times: times, series: series }, opts);
          return { ops: c.ops.slice(), hovers: __hover(c, points) };
        }
        function __bars(w, h, rows, opts, points, tipW, tipH) {
          var c = __canvas(w, h, tipW, tipH);
          rows.forEach(function (r, i) { r.color = 'c' + i; });
          DPS.chart.bars(c.canvas, rows, opts);
          return { ops: c.ops.slice(), hovers: __hover(c, points) };
        }
        function __hist(w, h, dist, points, tipW, tipH) {
          var c = __canvas(w, h, tipW, tipH);
          DPS.chart.histogram(c.canvas, dist, { color: 'col' });
          return { ops: c.ops.slice(), hovers: __hover(c, points) };
        }
        """;

    void ChartsReady()
    {
        if (chartsReady) return;
        js.Run(ChartStub);
        chartsReady = true;
    }

    // One width for every weight. The JS cuts an end label's name by its
    // width in regular type and then draws it semibold; Zerg measures in the
    // weight it draws. A weight-blind ruler keeps that difference out of the
    // comparison.
    static double Measure(string t)
    {
        double w = 0;
        foreach (var ch in t) w += 4 + ch % 7 * 0.5;
        return w;
    }

    static string Esc(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    // --------------------------------------------------- the canvas, in C#

    sealed class Ops
    {
        public JsonArray List { get; } = [];

        static JsonArray P(params object[] xs)
        {
            var a = new JsonArray();
            foreach (var x in xs) a.Add(x is string s ? JsonValue.Create(s) : JsonValue.Create(Convert.ToDouble(x, CultureInfo.InvariantCulture)));
            return a;
        }

        public static JsonArray Line(double x0, double y0, double x1, double y1) => [P("M", x0, y0), P("L", x1, y1)];
        public static JsonArray Dot(double x, double y, double r) => [P("A", x, y, r)];

        public void Stroke(string style, double width, JsonArray path, bool dashed = false) => List.Add(new JsonObject
        {
            ["op"] = "stroke", ["style"] = style, ["width"] = width,
            ["dash"] = dashed ? new JsonArray(5, 4) : new JsonArray(), ["path"] = path,
        });

        public void Fill(string style, JsonArray path) =>
            List.Add(new JsonObject { ["op"] = "fill", ["style"] = style, ["path"] = path });

        public void Rect(string style, double x, double y, double w, double h) =>
            List.Add(new JsonObject { ["op"] = "rect", ["style"] = style, ["x"] = x, ["y"] = y, ["w"] = w, ["h"] = h });

        public void Text(string text, double x, double y, string style, string font, string align, string? @base) =>
            List.Add(new JsonObject
            {
                ["op"] = "text", ["text"] = text, ["x"] = x, ["y"] = y, ["style"] = style, ["font"] = font,
                ["align"] = align, ["base"] = @base,
            });

        /// <summary>A marker: the dot, then its surface ring.</summary>
        public void Marker(string ink, double x, double y)
        {
            Fill(ink, Dot(x, y, LineLayout.MarkerRadius));
            Stroke("S", 2, Dot(x, y, LineLayout.MarkerRadius));
        }

        public static JsonArray Polyline(LineLayout l, double[] times, double[] values)
        {
            var path = new JsonArray();
            bool pen = false;
            for (int j = 0; j < times.Length; j++)
            {
                if (!double.IsFinite(values[j])) { pen = false; continue; }
                path.Add(P(pen ? "L" : "M", l.Px(times[j]), l.Py(values[j])));
                pen = true;
            }
            return path;
        }

        /// <summary>A bar: square on the left, rounded on the right.</summary>
        public static JsonArray BarPath(double x, double y, double w, double h, double r)
        {
            r = Math.Max(0, Math.Min(r, Math.Min(w, h / 2)));
            return [P("M", x, y), P("L", x + w - r, y), P("Q", x + w, y, x + w, y + r), P("L", x + w, y + h - r),
                    P("Q", x + w, y + h, x + w - r, y + h), P("L", x, y + h), P("Z")];
        }

        /// <summary>A column: square at the foot, rounded at the top.</summary>
        public static JsonArray ColumnPath(double x, double y, double w, double h, double r) =>
            [P("M", x, y + h), P("L", x, y + r), P("Q", x, y, x + r, y), P("L", x + w - r, y),
             P("Q", x + w, y, x + w, y + r), P("L", x + w, y + h), P("Z")];
    }

    static JsonObject Hidden(JsonArray? ops = null)
    {
        var o = new JsonObject { ["shown"] = false };
        if (ops != null) o["ops"] = ops;
        return o;
    }

    static JsonObject Shown(string html, double x, double y, double tipW, double tipH, double w, double h, JsonArray ops)
    {
        var (left, top) = HoverCard.Place(x, y, tipW, tipH, w, h);
        return new JsonObject
        {
            ["shown"] = true, ["html"] = html,
            ["left"] = Zerg.Core.Js.NumberToString(left) + "px", ["top"] = Zerg.Core.Js.NumberToString(top) + "px",
            ["ops"] = ops,
        };
    }

    static List<double[]> Points(Random r, double w, double h, int n)
    {
        var pts = new List<double[]>
        {
            new[] { -20.0, h / 2 }, new[] { w + 20, h / 2 }, new[] { w / 2, -20.0 }, new[] { w / 2, h + 20 },
            new[] { 0.0, 0.0 }, new[] { w, h }, new[] { w / 2, h / 2 }, new[] { w - 1, 1.0 }, new[] { 60.0, h - 30 },
        };
        for (int i = 0; i < n; i++) pts.Add([Math.Round(r.NextDouble() * (w + 30) - 15, 1), Math.Round(r.NextDouble() * (h + 30) - 15, 1)]);
        return pts;
    }

    static JsonArray Nums(IEnumerable<double> values)
    {
        var a = new JsonArray();
        foreach (var v in values) a.Add(double.IsFinite(v) ? JsonValue.Create(v) : null);
        return a;
    }

    // ----------------------------------------------------------------- line

    sealed record TestSeries(string Name, double[] Values, bool Group = false) : ILineSeries;

    void Line(string label, double w, double h, double[] times, IReadOnlyList<TestSeries> series,
              bool compact, bool endLabels, string? empty, int seed)
    {
        ChartsReady();
        var r = new Random(seed);
        var points = Points(r, w, h, 14);
        double tipW = 90 + r.Next(120), tipH = 30 + r.Next(260);

        var jsSeries = new JsonArray();
        foreach (var s in series)
            jsSeries.Add(new JsonObject { ["name"] = s.Name, ["values"] = Nums(s.Values), ["group"] = s.Group });
        js.SetJson("__times", Nums(times).ToJsonString());
        js.SetJson("__series", jsSeries.ToJsonString());
        js.Set("__points", points);
        var opts = $"{{compact: {(compact ? "true" : "false")}, endLabels: {(endLabels ? "true" : "false")}, empty: {Q(empty)}}}";
        var got = js.Eval($"__line({N(w)}, {N(h)}, __times, __series, {opts}, __points, {N(tipW)}, {N(tipH)})");

        var l = LineLayout.Compute(w, h, times, series, compact, endLabels, Measure);
        string Ink(int i) => series[i].Group ? "M" : "c" + i;

        var ops = new Ops();
        var hovers = new JsonArray();
        if (l.Empty)
        {
            ops.Text(empty ?? "No damage yet", w / 2, h / 2, "M", "13px F", "center", "alphabetic");
            foreach (var _ in points) hovers.Add(Hidden());
        }
        else
        {
            var plot = l.Plot;
            foreach (var t in l.YTicks)
            {
                ops.Stroke("G", 1, Ops.Line(plot.X, t.Pos, plot.X + plot.W, t.Pos));
                ops.Text(t.Label, plot.X - LineLayout.LabelGap, t.Pos, "M", "11px F", "right", "middle");
            }
            foreach (var t in l.XTicks)
                ops.Text(t.Label, t.Pos, plot.Y + plot.H + LineLayout.LabelGap, "M", "11px F", "center", "top");
            ops.Stroke("A", 1, Ops.Line(plot.X, l.AxisY, plot.X + plot.W, l.AxisY));
            for (int i = 0; i < series.Count; i++)
                ops.Stroke(Ink(i), 2, Ops.Polyline(l, times, series[i].Values), series[i].Group);
            foreach (var m in l.Markers) ops.Marker(Ink(m.Series), m.X, m.Y);
            foreach (var t in l.Labels)
            {
                if (t.Swatch)
                {
                    ops.Rect(Ink(t.Series), t.X, Zerg.Core.Js.Round(t.Y) - 1.5, LineLayout.SwatchWidth, LineLayout.SwatchHeight);
                    ops.Text(t.Text, t.X + LineLayout.SwatchToText, t.Y, "I2", "600 11px F", "left", "middle");
                }
                else ops.Text(t.Text, t.X, t.Y, "I2", "600 11px F", "left", "middle");
            }

            foreach (var p in points)
            {
                var hv = l.Hover(p[0], p[1]);
                if (hv == null) { hovers.Add(Hidden([])); continue; }
                var cross = new Ops();
                cross.Stroke("A", 1, Ops.Line(hv.X, plot.Y, hv.X, plot.Y + plot.H));
                foreach (var d in hv.Dots) cross.Marker(Ink(d.Series), d.X, d.Y);
                var html = "<div class=\"chart-tip-head\">" + hv.Head + "</div><table>" +
                    string.Concat(hv.Rows.Select(x => "<tr><td><i style=\"background:" + Ink(x.Series) + "\"></i>" +
                                                      Esc(x.Label) + "</td><td>" + x.Value + "</td></tr>")) + "</table>";
                hovers.Add(Shown(html, hv.AnchorX, p[1], tipW, tipH, w, h, cross.List));
            }
        }
        Check($"{label} line {w}x{h}{(compact ? " compact" : "")}{(endLabels ? " named" : "")}", got,
              new JsonObject { ["ops"] = ops.List, ["hovers"] = hovers });
    }

    static readonly (double W, double H, bool Compact, bool EndLabels)[] LineShapes =
    [
        (1100, 320, false, false), (1419, 320, false, true), (460, 264, true, true), (524, 180, true, true),
        (700, 220, false, true), (300, 150, true, false), (60, 40, false, false), (333, 201, false, false),
    ];

    void Lines(string label, double[] times, IReadOnlyList<TestSeries> series, int seed, int shapes = int.MaxValue)
    {
        for (int i = 0; i < Math.Min(shapes, LineShapes.Length); i++)
        {
            var s = LineShapes[i];
            Line(label, s.W, s.H, times, series, s.Compact, s.EndLabels, i % 2 == 0 ? null : "Nothing here", seed + i);
        }
    }

    /// <summary>A parse as the cumulative chart draws it: one line per
    /// character with damage, smallest total first, on a grid that ends at the
    /// clock; and the same with everyone under 5% folded into one line.</summary>
    static (double[] Times, List<TestSeries> All, List<TestSeries> Grouped, Aggregate Agg, List<CombatEvent> Events)
        Drawn(EventReader reader, Session sn, double? now = null)
    {
        var events = Counting.Filter(reader.Events, new FilterOptions { Session = sn, Roster = reader.Roster });
        var elapsed = sn.Elapsed(now ?? sn.PausedAt);
        var agg = Counting.Aggregate(events, elapsed / 1000);
        var names = agg.Actors.Where(a => a.Total > 0).Select(a => a.Name).ToList();
        var cum = Counting.Cumulative(events, names, 0, elapsed);

        var all = cum.Series.Select(s => new TestSeries(s.Name, s.Values)).ToList();
        var small = SmallLines.Pick(agg, reader.Roster.Owner);
        var grouped = all.Where(s => !small.Contains(s.Name)).ToList();
        if (small.Count > 0)
        {
            var sum = new double[cum.Times.Length];
            foreach (var s in all.Where(s => small.Contains(s.Name)))
                for (int i = 0; i < sum.Length; i++) sum[i] += s.Values[i];
            grouped.Add(new TestSeries(SmallLines.Name(small.Count), sum, Group: true));
        }
        double Last(TestSeries s) => s.Values.Length > 0 ? s.Values[^1] : 0;
        Zerg.Core.Js.StableSort(all, (a, b) => Last(a) - Last(b));
        Zerg.Core.Js.StableSort(grouped, (a, b) => Last(a) - Last(b));
        return (cum.Times, all, grouped, agg, events);
    }

    // ----------------------------------------------------------------- bars

    sealed record TestBar(string Label, double Value) : IBarRow;

    void Bars(string label, double w, double h, IReadOnlyList<TestBar> rows, double? labelWidth, int seed)
    {
        ChartsReady();
        var r = new Random(seed);
        var points = Points(r, w, h, 12);
        double tipW = 90 + r.Next(120), tipH = 30 + r.Next(120);

        js.Set("__rows", rows.Select(x => new { label = x.Label, value = x.Value }));
        js.Set("__points", points);
        var opts = labelWidth is double lw ? $"{{labelWidth: {N(lw)}}}" : "{}";
        var got = js.Eval($"__bars({N(w)}, {N(h)}, __rows, {opts}, __points, {N(tipW)}, {N(tipH)})");

        var l = labelWidth is double v
            ? BarsLayout.Compute(w, h, rows, Measure, v)
            : BarsLayout.Compute(w, h, rows, Measure);
        var ops = new Ops();
        var hovers = new JsonArray();
        if (l.Empty)
        {
            ops.Text("No data", w / 2, h / 2, "M", "13px F", "center", "alphabetic");
            foreach (var _ in points) hovers.Add(Hidden());
        }
        else
        {
            for (int i = 0; i < l.Rows.Count; i++)
            {
                var b = l.Rows[i];
                ops.Text(b.Label, l.LabelRight, b.Cy, "I2", "12px F", "right", "middle");
                ops.Fill("c" + i, Ops.BarPath(b.BarX, b.BarY, b.BarW, b.BarH, BarsLayout.Radius));
                ops.Text(b.ValueText, b.ValueX, b.Cy, "I", "600 12px F", "left", "middle");
            }
            foreach (var p in points)
            {
                int k = l.RowAt(p[1]);
                if (k < 0) { hovers.Add(Hidden([])); continue; }
                var html = "<div class=\"chart-tip-head\"><i style=\"background:c" + k + "\"></i>" + Esc(rows[k].Label) + "</div>" +
                           "<table><tr><td>Damage</td><td>" + Format.Int(rows[k].Value) + "</td></tr></table>";
                hovers.Add(Shown(html, p[0], p[1], tipW, tipH, w, h, []));
            }
        }
        Check($"{label} bars {w}x{h}", got, new JsonObject { ["ops"] = ops.List, ["hovers"] = hovers });
    }

    // ------------------------------------------------------------ histogram

    void Histogram(string label, double w, double h, IReadOnlyList<Bin>? bins, double avg, int count, int seed)
    {
        ChartsReady();
        var r = new Random(seed);
        var points = Points(r, w, h, 12);
        double tipW = 90 + r.Next(120), tipH = 30 + r.Next(120);

        if (bins == null) js.Run("var __dist = null;");
        else js.Set("__dist", new { bins = bins.Select(b => new { lo = b.Lo, hi = b.Hi, count = b.Count }), avg, count });
        js.Set("__points", points);
        var got = js.Eval($"__hist({N(w)}, {N(h)}, __dist, __points, {N(tipW)}, {N(tipH)})");

        var l = HistogramLayout.Compute(w, h, bins, avg, count);
        var ops = new Ops();
        var hovers = new JsonArray();
        if (l.Empty)
        {
            ops.Text("No hits recorded", w / 2, h / 2, "M", "13px F", "center", "alphabetic");
            foreach (var _ in points) hovers.Add(Hidden());
        }
        else
        {
            var plot = l.Plot;
            foreach (var t in l.YTicks)
            {
                ops.Stroke("G", 1, Ops.Line(plot.X, t.Pos, plot.X + plot.W, t.Pos));
                ops.Text(t.Label, plot.X - HistogramLayout.YLabelGap, t.Pos, "M", "11px F", "right", "middle");
            }
            foreach (var c in l.Columns) ops.Fill("col", Ops.ColumnPath(c.X, c.Y, c.W, c.H, c.Radius));
            ops.Stroke("A", 1, Ops.Line(plot.X, l.BaselineY, plot.X + plot.W, l.BaselineY));
            foreach (var x in l.XLabels)
                ops.Text(x.Text, x.X, plot.Y + plot.H + HistogramLayout.XLabelGap, "M", "11px F",
                         x.Align switch { LabelAlign.Left => "left", LabelAlign.Right => "right", _ => "center" }, "top");
            if (l.Mean is { } m)
            {
                ops.Stroke("I2", 1, Ops.Line(m.X, m.Top, m.X, m.Bottom));
                ops.Text(m.Label, m.LabelX, m.Top, "I2", "600 11px F", m.Align == LabelAlign.Right ? "right" : "left", "bottom");
            }
            var dash = ((char)0x2013).ToString();
            foreach (var p in points)
            {
                var hv = l.Hover(p[0], p[1]);
                if (hv == null) { hovers.Add(Hidden([])); continue; }
                var html = "<div class=\"chart-tip-head\">" + hv.Head.Replace(dash, "&ndash;") + "</div><table>" +
                    string.Concat(hv.Rows.Select(x => "<tr><td>" + x.Label + "</td><td>" + x.Value + "</td></tr>")) + "</table>";
                hovers.Add(Shown(html, p[0], p[1], tipW, tipH, w, h, []));
            }
        }
        Check($"{label} histogram {w}x{h}", got, new JsonObject { ["ops"] = ops.List, ["hovers"] = hovers });
    }

    // --------------------------------------------------------------- inputs

    /// <summary>Every chart over one parse: its cumulative lines (all, and with
    /// the small ones folded), its bars, and a histogram per character.</summary>
    public void Charts(string label, EventReader reader, Session sn, int seed, int shapes = int.MaxValue)
    {
        var (times, all, grouped, agg, events) = Drawn(reader, sn);
        Lines(label + " all", times, all, seed, shapes);
        Lines(label + " grouped", times, grouped, seed + 100, shapes);

        var rows = agg.Actors.Where(a => a.Total > 0).Select(a => new TestBar(a.Name, a.Total)).ToList();
        Bars(label, 1100, BarsLayout.HeightFor(rows.Count), rows, null, seed);
        Bars(label, 460, 150, rows, null, seed + 1);
        Bars(label + " narrow labels", 620, BarsLayout.HeightFor(rows.Count), rows, 60, seed + 2);

        foreach (var a in agg.Actors.Take(shapes == int.MaxValue ? 6 : 2))
        {
            var d = Counting.Distribution(events, a.Name, null);
            Histogram($"{label} {a.Name}", 1100, 210, d.Bins, d.Avg, d.Count, seed + 3);
            Histogram($"{label} {a.Name}", 430, 210, d.Bins, d.Avg, d.Count, seed + 4);
            if (a.ActionList.Count == 0) continue;
            var one = Counting.Distribution(events, a.Name, a.ActionList[0].Name);
            Histogram($"{label} {a.Name} / {a.ActionList[0].Name}", 700, 170, one.Bins, one.Avg, one.Count, seed + 5);
        }
    }

    /// <summary>Two runs' pace: the shorter run's line stops early, and both
    /// get their totals printed beside their own ends.</summary>
    public void PaceChart(string label, string textA, string textB, int seed)
    {
        var ma = Compare.Measure(ParseFile.Import(textA));
        var mb = Compare.Measure(ParseFile.Import(textB));
        var p = Compare.Pace(ma, mb);
        var series = new List<TestSeries> { new("A " + (char)0xB7 + " first", p.A), new("B " + (char)0xB7 + " second", p.B) };
        Lines(label + " pace", p.Times, series, seed, 4);
        // A side with nothing measured at all draws no line.
        var none = p.A.Select(_ => double.NaN).ToArray();
        Lines(label + " pace, one side empty", p.Times, [new("A", none), new("B", p.B)], seed + 50, 2);
    }

    /// <summary>Shapes no parse produces: nothing at all, one instant, all
    /// zero, colliding and separated end labels, awkward names and bins.</summary>
    public void ChartEdges(int seed)
    {
        Lines("no grid", [], [new("A", [])], seed, 2);
        Lines("no lines", [0, 1000], [], seed, 2);
        Lines("one instant", [5000], [new("Solo", [42])], seed, 3);
        Lines("all zero", [0, 1000, 2000], [new("A", [0, 0, 0]), new("B", [0, 0, 0])], seed, 3);
        Lines("apart", [0, 1000, 2000, 40000],
              [new("Low", [0, 10, 20, 100]), new("Mid", [0, 50, 900, 4000]), new("A <b> & \"q\"", [0, 500, 5000, 12345]),
               new("An extraordinarily long character name", [0, 5000, 50000, 123456])], seed);
        Lines("colliding", [0, 1000, 2000],
              [new("A", [0, 10, 1000]), new("B", [0, 20, 1030]), new("C", [0, 30, 1060]), new("D", [5, 5, 5])], seed);
        Lines("five lines", [0, 1000, 2000],
              [new("A", [0, 10, 1000]), new("B", [0, 20, 2000]), new("C", [0, 30, 3000]), new("D", [5, 5, 4000]),
               new("E", [1, 2, 5000])], seed);
        Lines("gaps", [0, 1000, 2000, 3000, 4000],
              [new("A", [0, double.NaN, 20, 30, double.NaN]), new("B", [double.NaN, 5, 6, double.NaN, double.NaN]),
               new("3 others", [1, 2, 3, 4, 5], Group: true)], seed);
        Lines("long", Enumerable.Range(0, 400).Select(i => i * 45000.0).ToArray(),
              [new("A", Enumerable.Range(0, 400).Select(i => i * 1234.5).ToArray())], seed, 4);

        Bars("none", 400, 120, [], null, seed);
        Bars("all zero", 400, 120, [new("A", 0), new("B", 0)], null, seed);
        Bars("odd", 500, 200, [new("An extraordinarily long character name", 123456), new("A <b> & \"q\"", 9999.5),
                               new("Tiny", 1), new("Zero", 0)], null, seed);

        Histogram("none", 400, 210, null, 0, 0, seed);
        Histogram("no bins", 400, 210, [], 0, 0, seed);
        Histogram("identical", 400, 210, [new Bin(500, 500) { Count = 3 }], 500, 3, seed);
        Histogram("mean at the right", 300, 150,
                  [new Bin(0, 10) { Count = 1 }, new Bin(10, 20), new Bin(20, 30) { Count = 40 }], 26, 41, seed);
        Histogram("mean outside", 500, 210, [new Bin(100, 200) { Count = 2 }, new Bin(200, 300) { Count = 1 }], 999, 3, seed);
        Histogram("wide", 1400, 260, Enumerable.Range(0, 28).Select(i => new Bin(1000 + i * 137.5, 1000 + (i + 1) * 137.5) { Count = i * 37 % 11 }).ToList(),
                  2900.4, 154, seed);
    }

    /// <summary>The tick picker on its own, over ranges of every size and sign.</summary>
    public void NiceTicks(int seed)
    {
        var r = new Random(seed);
        var cases = new List<double[]>
        {
            new[] { 0.0, 0, 5 }, new[] { 0.0, 1, 5 }, new[] { 0.0, 1000, 5 }, new[] { 0.0, 999, 3 }, new[] { 5.0, 5, 4 }, new[] { 10.0, 5, 4 },
            new[] { 0.0, 1e-7, 5 }, new[] { -50.0, 50, 4 }, new[] { 0.0, 3, 0 }, new[] { 0.0, 7, -2 }, new[] { 123.0, 124, 2 },
            new[] { 0.0, 1e15, 5 }, new[] { 0.0, 100, 4 }, new[] { 0.0, 10, 5 }, new[] { 0.0, 2, 4 },
        };
        for (int i = 0; i < 300; i++)
        {
            var span = Math.Pow(10, r.NextDouble() * 9 - 1);
            var lo = r.Next(3) == 0 ? 0 : Math.Round((r.NextDouble() - 0.3) * span * 3, r.Next(3));
            cases.Add([lo, lo + Math.Round(span, r.Next(3)), 2 + r.Next(7)]);
        }
        js.Set("__ticks", cases);
        Check("niceTicks", js.Eval("__ticks.map(function (c) { return DPS.chart.niceTicks(c[0], c[1], c[2]); })"),
              cases.Select(c => Ticks.Nice(c[0], c[1], c[2])));
        Check("niceTicks odd", js.Eval("[DPS.chart.niceTicks(NaN, 5, 3), DPS.chart.niceTicks(2, Infinity, 3), DPS.chart.niceTicks(-Infinity, 3, 3), DPS.chart.niceTicks(-0, -1, 3)]"),
              new[] { Ticks.Nice(double.NaN, 5, 3), Ticks.Nice(2, double.PositiveInfinity, 3), Ticks.Nice(double.NegativeInfinity, 3, 3), Ticks.Nice(-0.0, -1, 3) });
    }
}
