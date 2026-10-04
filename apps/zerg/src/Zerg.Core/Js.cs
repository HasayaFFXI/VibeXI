using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Zerg.Core;

/// <summary>
/// The handful of JavaScript rules the counting code depends on, written once.
///
/// The event file is JSON written by a Lua addon, and the counting rules were
/// first written in JavaScript, so a few of that language's conventions are
/// part of what the numbers mean: how a field that is not a number becomes one,
/// which values count as "set", that sorting keeps ties in their original
/// order, how a name-keyed map lists its keys, and how an export is spelled.
/// Everything here follows the ECMAScript specification, so Zerg reads and
/// writes these files exactly as the earlier tools did.
/// </summary>
public static class Js
{
    static readonly JsonDocumentOptions ParseOptions = new() { MaxDepth = 4096 };

    /// <summary>
    /// <c>JSON.parse</c>: the value, or null if the text is not JSON. A
    /// repeated key keeps its first position and its last value, and a number
    /// too large for a double is an infinity rather than an error, both as in
    /// JavaScript.
    /// </summary>
    public static bool TryParse(string text, out JsonNode? value)
    {
        value = null;
        // JSON's own whitespace only; anything else before the value is an error.
        try
        {
            using var doc = JsonDocument.Parse(text, ParseOptions);
            value = Build(doc.RootElement);
            return true;
        }
        catch (JsonException) { return false; }
        catch (InvalidOperationException) { return false; }   // a lone surrogate escape
        catch (ArgumentException) { return false; }
    }

    static JsonNode? Build(JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                var o = new JsonObject();
                foreach (var p in e.EnumerateObject()) o[p.Name] = Build(p.Value);
                return o;
            case JsonValueKind.Array:
                var a = new JsonArray();
                foreach (var x in e.EnumerateArray()) a.Add(Build(x));
                return a;
            case JsonValueKind.String: return JsonValue.Create(e.GetString());
            case JsonValueKind.Number:
                return JsonValue.Create(double.Parse(e.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture));
            case JsonValueKind.True: return JsonValue.Create(true);
            case JsonValueKind.False: return JsonValue.Create(false);
            default: return null;
        }
    }

    static bool Is<T>(JsonNode? v, out T value)
    {
        value = default!;
        return v is JsonValue jv && jv.TryGetValue(out value!);
    }

    /// <summary>A number, whichever CLR type the node was built with.</summary>
    static bool TryNum(JsonNode? v, out double d)
    {
        d = 0;
        if (v is not JsonValue jv) return false;
        if (jv.TryGetValue(out d)) return true;
        if (jv.TryGetValue(out long l)) { d = l; return true; }
        if (jv.TryGetValue(out int i)) { d = i; return true; }
        return false;
    }

    /// <summary>A JSON string, or null for anything else.</summary>
    public static string? AsString(JsonNode? v) => Is(v, out string s) ? s : null;

    /// <summary>A JSON number, or null for anything else (no coercion).</summary>
    public static double? AsNumber(JsonNode? v) => TryNum(v, out double d) ? d : null;

    /// <summary>Exactly <c>true</c>; anything else, truthy or not, is false.</summary>
    public static bool AsBool(JsonNode? v) => Is(v, out bool b) && b;

    /// <summary>The value is a string: it, otherwise ''.</summary>
    public static string Str(JsonNode? v) => AsString(v) ?? "";

    /// <summary>A number of any spelling, or 0 when it is not a finite one.</summary>
    public static double Num(JsonNode? v)
    {
        var n = ToNumber(v);
        return double.IsFinite(n) ? n : 0;
    }

    /// <summary>JavaScript truthiness of a parsed JSON value.</summary>
    public static bool Truthy(JsonNode? v) => v switch
    {
        null => false,
        JsonObject or JsonArray => true,
        _ when Is(v, out bool b) => b,
        _ when TryNum(v, out double d) => d != 0 && !double.IsNaN(d),
        _ when Is(v, out string s) => s.Length > 0,
        _ => false,
    };

    /// <summary>The unary <c>+</c> on a parsed JSON value (null and missing are 0).</summary>
    public static double ToNumber(JsonNode? v) => v switch
    {
        null => 0,
        JsonObject => double.NaN,                      // "[object Object]"
        JsonArray a => StringToNumber(ArrayToString(a)),
        _ when Is(v, out bool b) => b ? 1 : 0,
        _ when TryNum(v, out double d) => d,
        _ when Is(v, out string s) => StringToNumber(s),
        _ => double.NaN,
    };

    static string ArrayToString(JsonArray a)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < a.Count; i++)
        {
            if (i > 0) sb.Append(',');
            var x = a[i];
            if (x is null) continue;
            if (x is JsonArray inner) sb.Append(ArrayToString(inner));
            else if (x is JsonObject) sb.Append("[object Object]");
            else if (Is(x, out bool b)) sb.Append(b ? "true" : "false");
            else if (TryNum(x, out double d)) sb.Append(NumberToString(d));
            else if (Is(x, out string s)) sb.Append(s);
        }
        return sb.ToString();
    }

    /// <summary>JavaScript's StringToNumber.</summary>
    public static double StringToNumber(string s)
    {
        s = s.Trim(JsWhitespace);
        if (s.Length == 0) return 0;
        if (s.Length > 2 && s[0] == '0' && (s[1] | 0x20) is 'x' or 'o' or 'b')
        {
            int radix = (s[1] | 0x20) switch { 'x' => 16, 'o' => 8, _ => 2 };
            double n = 0;
            for (int i = 2; i < s.Length; i++)
            {
                int digit = HexDigit(s[i]);
                if (digit < 0 || digit >= radix) return double.NaN;
                n = n * radix + digit;
            }
            return n;
        }
        var body = s[0] is '+' or '-' ? s[1..] : s;
        if (body == "Infinity") return s[0] == '-' ? double.NegativeInfinity : double.PositiveInfinity;
        // Digits, an optional fraction and an optional exponent, and nothing else:
        // .NET would also take "1,000", "NaN" and "∞".
        int k = 0, intDigits = 0, fracDigits = 0;
        while (k < body.Length && char.IsAsciiDigit(body[k])) { k++; intDigits++; }
        if (k < body.Length && body[k] == '.')
        {
            k++;
            while (k < body.Length && char.IsAsciiDigit(body[k])) { k++; fracDigits++; }
        }
        if (intDigits + fracDigits == 0) return double.NaN;
        if (k < body.Length && (body[k] | 0x20) == 'e')
        {
            k++;
            if (k < body.Length && body[k] is '+' or '-') k++;
            int expDigits = 0;
            while (k < body.Length && char.IsAsciiDigit(body[k])) { k++; expDigits++; }
            if (expDigits == 0) return double.NaN;
        }
        if (k != body.Length) return double.NaN;
        return double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    static int HexDigit(char c) =>
        c is >= '0' and <= '9' ? c - '0'
        : (c | 0x20) is >= 'a' and <= 'z' ? (c | 0x20) - 'a' + 10
        : -1;

    // Every Unicode space separator plus tab, the line breaks, and the BOM.
    static readonly char[] JsWhitespace =
    [
        (char)0x0009, (char)0x000A, (char)0x000B, (char)0x000C, (char)0x000D, (char)0x0020, (char)0x00A0,
        (char)0x1680, (char)0x2000, (char)0x2001, (char)0x2002, (char)0x2003, (char)0x2004, (char)0x2005,
        (char)0x2006, (char)0x2007, (char)0x2008, (char)0x2009, (char)0x200A, (char)0x2028, (char)0x2029,
        (char)0x202F, (char)0x205F, (char)0x3000, (char)0xFEFF,
    ];

    /// <summary><c>Math.round</c>: halves go up, towards +∞ (C#'s default
    /// rounds them to even).</summary>
    public static double Round(double x)
    {
        if (!double.IsFinite(x) || x == Math.Floor(x)) return x;
        var r = Math.Floor(x);
        if (x - r >= 0.5) r += 1;
        // -0.4 rounds to -0, as it does there.
        return r == 0 && x < 0 ? -0.0 : r;
    }

    /// <summary>
    /// Sorts in place keeping equal elements in their original order, which
    /// JavaScript's sort guarantees and <see cref="List{T}.Sort()"/> does not.
    /// <paramref name="compare"/> returns a number like a JavaScript comparator:
    /// negative, positive, or zero (and NaN) for a tie.
    /// </summary>
    public static void StableSort<T>(List<T> list, Func<T, T, double> compare)
    {
        if (list.Count < 2) return;
        var keyed = new (T Item, int Index)[list.Count];
        for (int i = 0; i < keyed.Length; i++) keyed[i] = (list[i], i);
        Array.Sort(keyed, (p, q) =>
        {
            var c = compare(p.Item, q.Item);
            return c < 0 ? -1 : c > 0 ? 1 : p.Index.CompareTo(q.Index);
        });
        for (int i = 0; i < keyed.Length; i++) list[i] = keyed[i].Item;
    }

    /// <summary>
    /// The keys of a name-keyed map in the order JavaScript lists an object's
    /// keys: integer-like names ascending first, then the rest in the order
    /// they were added. Only a name like "123" moves.
    /// </summary>
    public static IEnumerable<string> Keys(IEnumerable<string> insertionOrder)
    {
        var list = insertionOrder as IList<string> ?? insertionOrder.ToList();
        var indices = new List<(uint, string)>();
        foreach (var k in list) if (ArrayIndex(k) is uint n) indices.Add((n, k));
        if (indices.Count == 0) return list;
        indices.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return indices.Select(p => p.Item2).Concat(list.Where(k => ArrayIndex(k) is null));
    }

    static uint? ArrayIndex(string k)
    {
        if (k.Length == 0 || k.Length > 10 || (k.Length > 1 && k[0] == '0')) return null;
        foreach (var c in k) if (!char.IsAsciiDigit(c)) return null;
        return ulong.Parse(k, CultureInfo.InvariantCulture) is var n && n < uint.MaxValue ? (uint)n : null;
    }

    /// <summary>JavaScript's Number::toString for a double: the shortest
    /// round-trip digits, with JavaScript's choice of when to use an exponent.</summary>
    public static string NumberToString(double d)
    {
        if (double.IsNaN(d)) return "NaN";
        if (double.IsPositiveInfinity(d)) return "Infinity";
        if (double.IsNegativeInfinity(d)) return "-Infinity";
        if (d == 0) return "0";
        var neg = d < 0;
        var (digits, n) = ShortestDigits(d);
        int k = digits.Length;
        string s;
        if (k <= n && n <= 21) s = digits + new string('0', n - k);
        else if (0 < n && n <= 21) s = digits[..n] + "." + digits[n..];
        else if (-6 < n && n <= 0) s = "0." + new string('0', -n) + digits;
        else
        {
            var ex = n - 1;
            var es = (ex >= 0 ? "+" : "-") + Math.Abs(ex).ToString(CultureInfo.InvariantCulture);
            s = k == 1 ? digits + "e" + es : digits[..1] + "." + digits[1..] + "e" + es;
        }
        return neg ? "-" + s : s;
    }

    /// <summary>
    /// The shortest digits that read back as |<paramref name="d"/>| (finite,
    /// non-zero), and where the point goes: the value is 0.<c>Digits</c> × 10^<c>Exp</c>.
    /// No leading or trailing zeros.
    /// </summary>
    internal static (string Digits, int Exp) ShortestDigits(double d)
    {
        // "E16" is not shortest; "R" is, so take its digits and exponent apart.
        var r = Math.Abs(d).ToString("R", CultureInfo.InvariantCulture);
        int e = r.IndexOfAny(['E', 'e']);
        var mant = e >= 0 ? r[..e] : r;
        int pow = e >= 0 ? int.Parse(r[(e + 1)..], CultureInfo.InvariantCulture) : 0;
        int dot = mant.IndexOf('.');
        var intPart = dot >= 0 ? mant[..dot] : mant;
        var fracPart = dot >= 0 ? mant[(dot + 1)..] : "";
        var digits = (intPart + fracPart).TrimStart('0');
        int lead = (intPart + fracPart).Length - digits.Length;
        return (digits.TrimEnd('0'), intPart.Length - lead + pow);
    }

    // ------------------------------------------------------------- stringify

    /// <summary><c>JSON.stringify(value)</c>, character for character.</summary>
    public static string Stringify(JsonNode? value) => Stringify(value, null);

    /// <summary><c>JSON.stringify(value, null, indent)</c>.</summary>
    public static string Stringify(JsonNode? value, int? indent)
    {
        var sb = new StringBuilder();
        Write(sb, value, indent is > 0 ? new string(' ', Math.Min(indent.Value, 10)) : null, "");
        return sb.ToString();
    }

    static void Write(StringBuilder sb, JsonNode? v, string? gap, string indent)
    {
        switch (v)
        {
            case null: sb.Append("null"); return;
            case JsonObject o:
                if (o.Count == 0) { sb.Append("{}"); return; }
                var inner = indent + gap;
                sb.Append('{');
                bool first = true;
                foreach (var key in Keys(o.Select(p => p.Key).ToList()))
                {
                    if (!first) sb.Append(',');
                    first = false;
                    if (gap != null) sb.Append('\n').Append(inner);
                    Quote(sb, key);
                    sb.Append(gap != null ? ": " : ":");
                    Write(sb, o[key], gap, inner);
                }
                if (gap != null) sb.Append('\n').Append(indent);
                sb.Append('}');
                return;
            case JsonArray a:
                if (a.Count == 0) { sb.Append("[]"); return; }
                var inA = indent + gap;
                sb.Append('[');
                for (int i = 0; i < a.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    if (gap != null) sb.Append('\n').Append(inA);
                    Write(sb, a[i], gap, inA);
                }
                if (gap != null) sb.Append('\n').Append(indent);
                sb.Append(']');
                return;
        }
        if (Is(v, out bool b)) sb.Append(b ? "true" : "false");
        else if (TryNum(v, out double d)) sb.Append(double.IsFinite(d) ? NumberToString(d) : "null");
        else if (Is(v, out string s)) Quote(sb, s);
        else sb.Append(v.ToJsonString());
    }

    static void Quote(StringBuilder sb, string s)
    {
        sb.Append('"');
        for (int i = 0; i < s.Length; i++)
        {
            var c = s[i];
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                    {
                        sb.Append(c).Append(s[i + 1]);
                        i++;
                    }
                    else if (char.IsSurrogate(c)) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }
}
