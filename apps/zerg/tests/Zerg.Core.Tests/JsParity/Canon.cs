using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;

namespace Zerg.Core.Tests.JsParity;

/// <summary>
/// One plain shape for a JavaScript value and a C# object, so the two can be
/// compared field by field:
/// <list type="bullet">
/// <item>objects are maps of camelCase names; a null or missing field is
/// left out on both sides, so "explicitly null" and "absent" are the same;</item>
/// <item>arrays (and typed arrays) are arrays; null stays null inside them;</item>
/// <item>a finite number is a number, NaN and the infinities are
/// <c>{"$n": "NaN"}</c> and so on;</item>
/// <item>a date is <c>{"$date": "y-m-d"}</c>.</item>
/// </list>
/// The JavaScript half of this lives in <see cref="JsReference"/> as <c>__canon</c>.
/// </summary>
static class Canon
{
    /// <summary>Computed C# conveniences with no field of their own in the
    /// JavaScript shapes.</summary>
    static readonly HashSet<string> Skipped = ["IsPets", "Running", "Armed"];

    public static JsonNode? Of(object? v)
    {
        switch (v)
        {
            case null: return null;
            case string s: return JsonValue.Create(s);
            case bool b: return JsonValue.Create(b);
            case double d: return Num(d);
            case float f: return Num(f);
            case int i: return JsonValue.Create((double)i);
            case long l: return JsonValue.Create((double)l);
            case uint u: return JsonValue.Create((double)u);
            case DateOnly d: return new JsonObject { ["$date"] = $"{d.Year}-{d.Month}-{d.Day}" };
            case JsonNode n: return OfJson(n);
            case IDictionary dict:
            {
                var o = new JsonObject();
                foreach (DictionaryEntry e in dict)
                    if (Of(e.Value) is { } c) o[(string)e.Key] = c;
                return o;
            }
            case IEnumerable map when IsMap(map.GetType()):
            {
                var o = new JsonObject();
                foreach (var kv in map)
                {
                    var t = kv!.GetType();
                    var key = (string)t.GetProperty("Key")!.GetValue(kv)!;
                    if (Of(t.GetProperty("Value")!.GetValue(kv)) is { } c) o[key] = c;
                }
                return o;
            }
            case IEnumerable list:
            {
                var a = new JsonArray();
                foreach (var x in list) a.Add(Of(x));
                return a;
            }
        }
        var obj = new JsonObject();
        foreach (var p in v.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length > 0 || Skipped.Contains(p.Name)) continue;
            if (Of(p.GetValue(v)) is { } c) obj[char.ToLowerInvariant(p.Name[0]) + p.Name[1..]] = c;
        }
        return obj;
    }

    static bool IsMap(Type t) => t.GetInterfaces().Any(i =>
        i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)
        && i.GetGenericArguments()[0] == typeof(string));

    static JsonNode Num(double d) =>
        double.IsFinite(d) ? JsonValue.Create(d)
        : new JsonObject { ["$n"] = double.IsNaN(d) ? "NaN" : d > 0 ? "Infinity" : "-Infinity" };

    static JsonNode? OfJson(JsonNode? n) => n switch
    {
        null => null,
        JsonObject o => Obj(o),
        JsonArray a => new JsonArray(a.Select(OfJson).ToArray()),
        _ => Zerg.Core.Js.AsNumber(n) is double d ? Num(d) : n.DeepClone(),
    };

    static JsonObject Obj(JsonObject o)
    {
        var r = new JsonObject();
        foreach (var (k, v) in o) if (OfJson(v) is { } c) r[k] = c;
        return r;
    }

    /// <summary>Every difference between the two shapes, as "path: js ≠ c#".
    /// Object key order is not compared; array order is.</summary>
    public static List<string> Diff(JsonNode? js, JsonNode? cs, string path = "$", int limit = 20)
    {
        var output = new List<string>();
        Walk(js, cs, path, output, limit);
        return output;
    }

    static void Walk(JsonNode? js, JsonNode? cs, string path, List<string> output, int limit)
    {
        if (output.Count >= limit) return;
        switch (js, cs)
        {
            case (null, null): return;
            case (JsonObject a, JsonObject b):
                foreach (var key in a.Select(p => p.Key).Union(b.Select(p => p.Key)))
                    Walk(a[key], b[key], path + "." + key, output, limit);
                return;
            case (JsonArray a, JsonArray b):
                if (a.Count != b.Count) output.Add($"{path}: length {a.Count} != {b.Count}");
                for (int i = 0; i < Math.Min(a.Count, b.Count); i++) Walk(a[i], b[i], $"{path}[{i}]", output, limit);
                return;
            case (JsonValue a, JsonValue b):
                var da = Zerg.Core.Js.AsNumber(a);
                var db = Zerg.Core.Js.AsNumber(b);
                if (da is double x && db is double y)
                {
                    if (x != y) output.Add($"{path}: {R(x)} != {R(y)}");
                    return;
                }
                if (a.ToJsonString() != b.ToJsonString())
                    output.Add($"{path}: {a.ToJsonString()} != {b.ToJsonString()}");
                return;
            default:
                output.Add($"{path}: {Show(js)} != {Show(cs)}");
                return;
        }
    }

    static string R(double d) => d.ToString("R", CultureInfo.InvariantCulture);

    static string Show(JsonNode? n)
    {
        if (n is null) return "(absent)";
        var s = n.ToJsonString();
        return s.Length > 120 ? s[..120] + "…" : s;
    }
}
