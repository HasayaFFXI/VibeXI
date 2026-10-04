namespace Zerg.Core;

/// <summary>
/// How see-through each floating panel is, as a percentage: 100 is a solid
/// backdrop, 15 the faintest that can still be found on screen.
///
/// <para>There is one default, and a panel may have a value of its own. A
/// panel with none follows the default, so "nothing of its own" means "what
/// the main window says" everywhere at once.</para>
///
/// <para><b>Setting the default forgets every panel's own value.</b> A value
/// set once, sessions ago, on a panel that is not open to show what it is
/// doing would otherwise outrank the default quietly, and the default would
/// look broken on exactly the panel being watched. One control, one meaning:
/// the default says what panels are, and a panel's own slider adjusts that
/// panel from there until the default is next touched.</para>
/// </summary>
public sealed class PanelOpacities
{
    public const int Min = 15, Max = 100;

    /// <summary>What a panel opens at before anyone has said otherwise. Not
    /// 100: a panel exists to be laid over the game, and one that comes up
    /// already see-through says what it is for before the slider is found.</summary>
    public const int Initial = 85;

    readonly Dictionary<string, int> own = new(StringComparer.Ordinal);

    public PanelOpacities(double? fallback = null, IEnumerable<KeyValuePair<string, int>>? own = null)
    {
        Default = Clamp(fallback ?? 0, Initial);
        foreach (var (key, value) in own ?? [])
            this.own[key] = Clamp(value, Default);
    }

    /// <summary>What a panel with no value of its own is drawn at.</summary>
    public int Default { get; private set; }

    /// <summary>The panels that have a value of their own, to be saved.</summary>
    public IReadOnlyDictionary<string, int> Own => own;

    public int Of(string key) => own.TryGetValue(key, out var v) ? v : Default;

    /// <summary>Gives one panel its own value and returns what it came to.</summary>
    public int Set(string key, double value) => own[key] = Clamp(value, Default);

    /// <summary>Sets the default, which every panel then takes.</summary>
    public int SetDefault(double value)
    {
        Default = Clamp(value, Default);
        own.Clear();
        return Default;
    }

    /// <summary>A whole percentage within range. Nothing usable (zero, or not
    /// a number: what a damaged settings file reads as) is the fallback, never
    /// the faintest value, which would open a panel all but invisible.</summary>
    public static int Clamp(double value, int fallback)
    {
        if (!double.IsFinite(value)) return fallback;
        var v = Js.Round(value);
        return v == 0 ? fallback : (int)Math.Clamp(v, Min, Max);
    }
}
