using System.Globalization;

namespace Zerg.Core;

/// <summary>
/// Query-string handling with Python's semantics, because the parity tests
/// compare against <c>parse_qs</c> + <c>_first</c> + <c>_int</c>:
/// pairs are split on '&amp;' only, '+' is a space, pairs without '=' and pairs
/// with an empty value are dropped (so <c>file=</c> means "no file"), and the
/// first value of a repeated key wins.
/// </summary>
public static class Query
{
    public static Dictionary<string, List<string>> Parse(string query)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (query.StartsWith('?')) query = query[1..];

        foreach (var pair in query.Split('&'))
        {
            if (pair.Length == 0) continue;
            int eq = pair.IndexOf('=');
            if (eq < 0) continue;                     // parse_qs drops a bare name
            var rawValue = pair[(eq + 1)..];
            if (rawValue.Length == 0) continue;       // ...and a blank value
            var name = Unquote(pair[..eq]);
            if (!result.TryGetValue(name, out var values)) result[name] = values = [];
            values.Add(Unquote(rawValue));
        }
        return result;
    }

    public static string? First(Dictionary<string, List<string>> query, string name) =>
        query.TryGetValue(name, out var v) && v.Count > 0 ? v[0] : null;

    /// <summary>Python's <c>int(value)</c> with a default: surrounding
    /// whitespace and a sign are allowed, anything else falls back.
    /// (Python also accepts '1_000' and arbitrarily large values; the page sends
    /// neither.)</summary>
    public static long Int(string? value, long fallback) =>
        long.TryParse(value?.Trim(), NumberStyles.AllowLeadingSign,
                      CultureInfo.InvariantCulture, out var n) ? n : fallback;

    static string Unquote(string s) => Uri.UnescapeDataString(s.Replace('+', ' '));
}
