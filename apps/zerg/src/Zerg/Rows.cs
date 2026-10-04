using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Zerg;

// The rows the lists in both sections are made of. Each is kept from one
// count to the next and only told what changed, so a new total rewrites one
// cell: a list that was rebuilt on every poll would lose the keyboard focus
// and the scroll position four times a second.

/// <summary>A row that lasts across counts. <see cref="Key"/> says which row
/// it is; it never changes, whatever the row is drawn as.</summary>
public abstract class Row(string key) : ObservableObject
{
    public string Key { get; } = key;

    /// <summary>What a screen reader calls the row: the name as drawn, never
    /// the key, which is a real name even while names are hidden.</summary>
    public abstract override string ToString();
}

/// <summary>One character's on/off switch in the filter bar.</summary>
public sealed partial class ChipRow(string key) : Row(key)
{
    /// <summary>Told when the switch is flipped, with the character's real
    /// name. Bound both ways, not run by a click: a screen reader flips a
    /// switch without clicking it.</summary>
    public Action<string, bool>? Flipped { get; init; }

    partial void OnIncludedChanged(bool value) => Flipped?.Invoke(Key, value);

    [ObservableProperty] private string name = "";
    public override string ToString() => Name;
    [ObservableProperty] private string job = "";
    [ObservableProperty] private Brush? swatch;
    [ObservableProperty] private bool included = true;
    [ObservableProperty] private string tip = "";
}

/// <summary>One line's entry in the cumulative chart's legend.</summary>
public sealed partial class LegendRow(string key) : Row(key)
{
    [ObservableProperty] private string name = "";
    public override string ToString() => Name;
    [ObservableProperty] private string job = "";
    [ObservableProperty] private Brush? swatch;
    /// <summary>A line that stands for several characters; the tip names them.</summary>
    [ObservableProperty] private bool group;
    [ObservableProperty] private string? tip;
}

/// <summary>One character in the per-character table.</summary>
public sealed partial class ActorRow(string key) : Row(key)
{
    /// <summary>The damage DPS is worked out from, on every clock tick.</summary>
    public double Total { get; set; }

    [ObservableProperty] private string name = "";
    public override string ToString() => Name;
    [ObservableProperty] private Brush? swatch;
    [ObservableProperty] private string job = "";
    [ObservableProperty] private string jobTip = "";
    [ObservableProperty] private bool showJob = true;
    [ObservableProperty] private string damage = "";
    [ObservableProperty] private string share = "";
    [ObservableProperty] private string dps = "";
    [ObservableProperty] private string accuracy = "";
    [ObservableProperty] private string wsDamage = "";
    [ObservableProperty] private string wsAvg = "";
    [ObservableProperty] private string wsShare = "";
    [ObservableProperty] private string wsAccuracy = "";
    [ObservableProperty] private string scDamage = "";
    [ObservableProperty] private string scShare = "";
    [ObservableProperty] private string petDamage = "";
    [ObservableProperty] private string petAccuracy = "";
}

/// <summary>One character in the strip the per-character card floats as.</summary>
public sealed partial class StripRow(string key) : Row(key)
{
    [ObservableProperty] private string name = "";
    public override string ToString() => Name;
    [ObservableProperty] private string job = "";
    /// <summary>The bar behind the row, in a floating panel's colours.</summary>
    [ObservableProperty] private Brush? fill;
    /// <summary>How long the bar is: this character's damage out of the leader's.</summary>
    [ObservableProperty] private double fraction;
    [ObservableProperty] private string damage = "";
    [ObservableProperty] private string share = "";
    [ObservableProperty] private string accuracy = "";
    /// <summary>The character whose file this is: the one row on every
    /// session, marked so it is found without reading names.</summary>
    [ObservableProperty] private bool isOwner;
    [ObservableProperty] private string tip = "";
}

/// <summary>
/// One line of the actions table: a character's heading, or one of their
/// actions under it.
/// </summary>
public sealed partial class ActionRow(string key, string actor, string? action) : Row(key), IGroupedRow
{
    public string Actor { get; } = actor;
    /// <summary>The action, or null on a character's heading.</summary>
    public string? Action { get; } = action;
    public bool IsHeading => Action == null;

    /// <summary>On a heading the character as drawn; on an action its name.</summary>
    [ObservableProperty] private string name = "";
    [ObservableProperty] private Brush? swatch;
    /// <summary>The swatch in a floating panel's colours.</summary>
    [ObservableProperty] private Brush? panelSwatch;
    [ObservableProperty] private string job = "";
    [ObservableProperty] private string hits = "";
    [ObservableProperty] private string misses = "";
    [ObservableProperty] private string total = "";
    [ObservableProperty] private string avg = "";
    [ObservableProperty] private string min = "";
    [ObservableProperty] private string max = "";
    [ObservableProperty] private string share = "";
    /// <summary>The drill-down is open on this action.</summary>
    [ObservableProperty] private bool selected;
    /// <summary>What a screen reader, or a script, calls the row.</summary>
    [ObservableProperty] private string label = "";

    public override string ToString() => IsHeading ? Name : Label;
}

/// <summary>A line of a table grouped by character: a character's heading,
/// or one of the lines under it.</summary>
public interface IGroupedRow
{
    bool IsHeading { get; }
}

/// <summary>One character in the Healing section's per-character table.</summary>
public sealed partial class HealerRow(string key) : Row(key)
{
    /// <summary>The healing HPS is worked out from, on every clock tick.</summary>
    public double Total { get; set; }

    [ObservableProperty] private string name = "";
    public override string ToString() => Name;
    [ObservableProperty] private Brush? swatch;
    [ObservableProperty] private string job = "";
    [ObservableProperty] private string jobTip = "";
    [ObservableProperty] private bool showJob = true;
    [ObservableProperty] private string healing = "";
    [ObservableProperty] private string share = "";
    [ObservableProperty] private string hps = "";
    [ObservableProperty] private string casts = "";
    [ObservableProperty] private string avg = "";
    [ObservableProperty] private string petHealing = "";
}

/// <summary>One character in the strip the healing-by-character card floats as.</summary>
public sealed partial class HealStripRow(string key) : Row(key)
{
    [ObservableProperty] private string name = "";
    public override string ToString() => Name;
    [ObservableProperty] private string job = "";
    /// <summary>The bar behind the row, in a floating panel's colours.</summary>
    [ObservableProperty] private Brush? fill;
    /// <summary>How long the bar is: this character's healing out of the leader's.</summary>
    [ObservableProperty] private double fraction;
    [ObservableProperty] private string healing = "";
    [ObservableProperty] private string share = "";
    [ObservableProperty] private string casts = "";
    [ObservableProperty] private bool isOwner;
    [ObservableProperty] private string tip = "";
}

/// <summary>
/// One line of the heals table: a character's heading, or one of their heals
/// under it.
/// </summary>
public sealed partial class HealRow(string key, string actor, string? action) : Row(key), IGroupedRow
{
    public string Actor { get; } = actor;
    /// <summary>The heal, or null on a character's heading.</summary>
    public string? Action { get; } = action;
    public bool IsHeading => Action == null;

    /// <summary>On a heading the character as drawn; on a heal its name.</summary>
    [ObservableProperty] private string name = "";
    [ObservableProperty] private Brush? swatch;
    /// <summary>The swatch in a floating panel's colours.</summary>
    [ObservableProperty] private Brush? panelSwatch;
    [ObservableProperty] private string job = "";
    /// <summary>On a heading: what the character's pet healed, said apart
    /// from their own total ("+ 420 pet"), or "" with none.</summary>
    [ObservableProperty] private string pet = "";
    [ObservableProperty] private string casts = "";
    [ObservableProperty] private string total = "";
    [ObservableProperty] private string avg = "";
    [ObservableProperty] private string min = "";
    [ObservableProperty] private string max = "";
    [ObservableProperty] private string share = "";
    /// <summary>The drill-down is open on this heal.</summary>
    [ObservableProperty] private bool selected;
    /// <summary>What a screen reader, or a script, calls the row.</summary>
    [ObservableProperty] private string label = "";

    public override string ToString() => IsHeading ? Name : Label;
}

/// <summary>A small labelled figure.</summary>
public sealed record StatTile(string Label, string Value);

/// <summary>One hit in the drill-down's list, or one cast in the heal drill-down's.</summary>
public sealed record HitRow(string Time, string Target, string Damage, string VsAvg);

static class Rows
{
    /// <summary>
    /// Makes <paramref name="rows"/> match <paramref name="items"/>, in order,
    /// keeping every row whose key is still there: kept rows are updated where
    /// they stand or moved, new ones are made, and the rest are dropped.
    /// </summary>
    public static void Sync<TRow, TItem>(ObservableCollection<TRow> rows, IReadOnlyList<TItem> items,
                                         Func<TItem, string> key, Func<TItem, TRow> make, Action<TRow, TItem> update)
        where TRow : Row
    {
        for (int i = 0; i < items.Count; i++)
        {
            var k = key(items[i]);
            if (i >= rows.Count || rows[i].Key != k)
            {
                int at = -1;
                for (int j = i + 1; j < rows.Count && at < 0; j++)
                    if (rows[j].Key == k) at = j;
                if (at >= 0) rows.Move(at, i);
                else rows.Insert(i, make(items[i]));
            }
            update(rows[i], items[i]);
        }
        while (rows.Count > items.Count) rows.RemoveAt(rows.Count - 1);
    }
}
