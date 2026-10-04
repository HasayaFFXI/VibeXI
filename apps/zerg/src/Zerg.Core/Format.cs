using System.Globalization;
using System.Numerics;
using System.Text;

namespace Zerg.Core;

/// <summary>
/// How Zerg spells its numbers and times. US-style grouping (1,234,567)
/// whatever the machine's culture, so a figure reads the same on every
/// player's screen and in every screenshot.
/// </summary>
public static class Format
{
    /// <summary>Rounded (halves up) and grouped: 1,234,568.</summary>
    public static string Int(double n) => Grouped(Js.Round(n), 0);

    /// <summary>Grouped, with exactly <paramref name="dp"/> decimals: 1,234.6.</summary>
    public static string Num(double n, int dp = 1) => Grouped(n, dp);

    /// <summary>
    /// Rounds the number as it is written (its shortest decimal form, so 2.675
    /// is 2.675 and rounds to 2.68 even though the double is a hair under), with
    /// halves away from zero, then groups the thousands. A negative number keeps
    /// its sign even when it rounds to zero ("-0.0"). This is how browsers
    /// format numbers for US English; .NET's own "N" format rounds the binary
    /// value instead, and sends exact halves to even, so it is not used.
    /// </summary>
    static string Grouped(double n, int dp)
    {
        if (double.IsNaN(n)) return "NaN";
        if (double.IsInfinity(n)) return n > 0 ? "∞" : "-∞";
        bool neg = n < 0 || (n == 0 && double.IsNegative(n));

        BigInteger scaled = BigInteger.Zero;   // |n| × 10^dp, rounded
        if (n != 0)
        {
            var (digits, exp) = Js.ShortestDigits(n);
            var value = BigInteger.Parse(digits, CultureInfo.InvariantCulture);
            int shift = exp - digits.Length + dp;   // |n| × 10^dp = value × 10^shift
            if (shift >= 0) scaled = value * BigInteger.Pow(10, shift);
            else
            {
                var den = BigInteger.Pow(10, -shift);
                scaled = BigInteger.DivRem(value, den, out var rem);
                if (rem * 2 >= den) scaled += 1;
            }
        }

        var all = scaled.ToString(CultureInfo.InvariantCulture).PadLeft(dp + 1, '0');
        var whole = all[..^dp];
        var sb = new StringBuilder(all.Length + all.Length / 3 + 2);
        if (neg) sb.Append('-');
        for (int i = 0; i < whole.Length; i++)
        {
            if (i > 0 && (whole.Length - i) % 3 == 0) sb.Append(',');
            sb.Append(whole[i]);
        }
        if (dp > 0) sb.Append('.').Append(all, all.Length - dp, dp);
        return sb.ToString();
    }

    /// <summary>1.2M, 45.6K, or the whole number under 10,000.</summary>
    public static string Compact(double n)
    {
        var a = Math.Abs(n);
        if (a >= 1e6) return Num(n / 1e6, 1) + "M";
        if (a >= 1e4) return Num(n / 1e3, 1) + "K";
        return Int(n);
    }

    /// <summary>A time of day, local: 21:07:45. Only for when something happened
    /// in the world; a measurement is drawn with <see cref="Elapsed"/>.</summary>
    public static string Clock(double ms)
    {
        if (!double.IsFinite(ms) || Math.Abs(ms) > 8.64e15) return "NaN:NaN:NaN";   // not a date
        var d = DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Truncate(ms)).ToLocalTime();
        return d.ToString("HH':'mm':'ss", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Milliseconds since the zero → the label Zerg reads time in: <c>m:ss</c>
    /// under an hour, <c>h:mm:ss</c> over. The leading field is unpadded, so
    /// <c>3:07</c> cannot be misread as a time of day.
    /// </summary>
    public static string Elapsed(double ms)
    {
        bool neg = ms < 0;
        var sec = Math.Max(0, Math.Floor(Math.Abs(ms) / 1000));
        var h = Math.Floor(sec / 3600);
        var m = Math.Floor(sec % 3600 / 60);
        var s = sec % 60;
        var output = Truthy(h)
            ? Js.NumberToString(h) + ":" + Pad2(m) + ":" + Pad2(s)
            : Js.NumberToString(m) + ":" + Pad2(s);
        return neg ? "-" + output : output;
    }

    /// <summary>
    /// The same clock at a fixed width: <c>MM:SS</c>, and <c>H:MM:SS</c> past
    /// the hour. For a readout that is watched: a figure that changes width at
    /// 0:59 → 1:00 shifts every digit beside it.
    /// </summary>
    public static string Stopwatch(double ms)
    {
        var sec = Math.Max(0, Math.Floor(Math.Abs(ms) / 1000));
        var h = Math.Floor(sec / 3600);
        var mmss = Pad2(Math.Floor(sec % 3600 / 60)) + ":" + Pad2(sec % 60);
        return Truthy(h) ? Js.NumberToString(h) + ":" + mmss : mmss;
    }

    /// <summary>Seconds → "1h 5m", "4m 07s" or "42s".</summary>
    public static string Duration(double sec)
    {
        sec = Math.Max(0, Js.Round(sec));
        var h = Math.Floor(sec / 3600);
        var m = Math.Floor(sec % 3600 / 60);
        var s = sec % 60;
        if (Truthy(h)) return Js.NumberToString(h) + "h " + Js.NumberToString(m) + "m";
        if (Truthy(m)) return Js.NumberToString(m) + "m " + Pad2(s) + "s";
        return Js.NumberToString(s) + "s";
    }

    static bool Truthy(double v) => v != 0 && !double.IsNaN(v);

    static string Pad2(double v)
    {
        var s = Js.NumberToString(v);
        return s.Length < 2 ? new string('0', 2 - s.Length) + s : s;
    }
}
