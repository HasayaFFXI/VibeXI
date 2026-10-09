using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Zerg;

// The rows the lists in both sections are made of. Each is kept from one
// count to the next and only told what changed, so a new total rewrites one
// cell: a list that was rebuilt on every poll would lose the keyboard focus
// and the scroll position several times a second.

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
    [ObservableProperty] private Brush? swatch;
    [ObservableProperty] private bool included = true;
    [ObservableProperty] private string tip = "";
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

    /// <summary>The row's shade: the character's colour at the strength
    /// that leaves the figures legible on it.</summary>
    [ObservableProperty] private Brush? shade;
    /// <summary>How long the shade is: this character's damage out of the leader's.</summary>
    [ObservableProperty] private double fraction;
    /// <summary>The character whose file this is.</summary>
    [ObservableProperty] private bool isOwner;
    /// <summary>What the pointer resting on the row is told: who, their
    /// job in full, and their average per action.</summary>
    [ObservableProperty] private string tip = "";
    /// <summary>The three rates as numbers, 0 to 1, for the low accuracy
    /// mark; null where there is nothing to measure and the cell is a dash.</summary>
    [ObservableProperty] private double? accuracyRate;
    [ObservableProperty] private double? wsAccuracyRate;
    [ObservableProperty] private double? petAccuracyRate;
}

/// <summary>
/// The line that closes the per-character table: the party as a whole under
/// the columns it sums. Not a row of the list: it stands still under the
/// rows, and there is one.
/// </summary>
public sealed partial class PartyRow : ObservableObject
{
    /// <summary>How many characters the table lists, under Job.</summary>
    [ObservableProperty] private string count = "";
    [ObservableProperty] private string damage = "";
    [ObservableProperty] private string share = "";
    /// <summary>The party's DPS: rewritten on the draw beat with every row's.</summary>
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
    /// <summary>The row's shade: the character's colour as a floating
    /// panel has it, at the strength that leaves the figures legible on it
    /// there (<c>Zerg.Core/RowShade.Strip</c>).</summary>
    [ObservableProperty] private Brush? fill;
    /// <summary>The same colour, solid: the edge at the row's left, which
    /// stands in for a swatch, and the small bar beside the share while
    /// rows are not shaded.</summary>
    [ObservableProperty] private Brush? edge;
    /// <summary>How long the shade is: this character's damage out of the leader's.</summary>
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
    /// <summary>On a heading: the character's actions are showing under it.</summary>
    [ObservableProperty] private bool open;
    /// <summary>What a screen reader, or a script, calls the row.</summary>
    [ObservableProperty] private string label = "";

    /// <summary>On a heading: the row's shade, the character's colour at
    /// the strength the raised surface allows.</summary>
    [ObservableProperty] private Brush? shade;
    /// <summary>The same in a floating panel's colours.</summary>
    [ObservableProperty] private Brush? panelShade;
    /// <summary>On a heading, how long the shade is: this character's
    /// damage out of the leader's.</summary>
    [ObservableProperty] private double fraction;
    /// <summary>On an action, how long its share is drawn: its total out of
    /// the largest of this character's actions.</summary>
    [ObservableProperty] private double shareFraction;
    /// <summary>On an action: its least hit, its average and its greatest,
    /// each out of the character's biggest hit of any action.</summary>
    [ObservableProperty] private double spreadMin;
    [ObservableProperty] private double spreadAvg;
    [ObservableProperty] private double spreadMax;

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

    /// <summary>The row's shade, in the character's colour.</summary>
    [ObservableProperty] private Brush? shade;
    /// <summary>How long the shade is: this character's healing out of the leading healer's.</summary>
    [ObservableProperty] private double fraction;
    /// <summary>The character whose file this is.</summary>
    [ObservableProperty] private bool isOwner;
    /// <summary>What the pointer resting on the row is told.</summary>
    [ObservableProperty] private string tip = "";
}

/// <summary>The line that closes the Healing section's per-character table:
/// the party as a whole under the columns it sums.</summary>
public sealed partial class HealPartyRow : ObservableObject
{
    /// <summary>How many characters the table lists, under Job.</summary>
    [ObservableProperty] private string count = "";
    [ObservableProperty] private string healing = "";
    [ObservableProperty] private string share = "";
    /// <summary>The party's HPS: rewritten on the draw beat with every row's.</summary>
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
    /// <summary>The row's shade, in the character's colour as a floating
    /// panel has it, at a panel's strength.</summary>
    [ObservableProperty] private Brush? fill;
    /// <summary>The same colour, solid: the edge at the row's left.</summary>
    [ObservableProperty] private Brush? edge;
    /// <summary>How long the shade is: this character's healing out of the leader's.</summary>
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
    /// <summary>On a heading: the character's heals are showing under it.</summary>
    [ObservableProperty] private bool open;
    /// <summary>What a screen reader, or a script, calls the row.</summary>
    [ObservableProperty] private string label = "";

    /// <summary>On a heading: the row's shade, in the character's colour.</summary>
    [ObservableProperty] private Brush? shade;
    /// <summary>The same in a floating panel's colours.</summary>
    [ObservableProperty] private Brush? panelShade;
    /// <summary>On a heading, how long the shade is: this character's
    /// healing out of the leading healer's.</summary>
    [ObservableProperty] private double fraction;
    /// <summary>On a heal, how long its share is drawn: its total out of
    /// the largest of this character's heals.</summary>
    [ObservableProperty] private double shareFraction;

    public override string ToString() => IsHeading ? Name : Label;
}

/// <summary>A small labelled figure.</summary>
public sealed record StatTile(string Label, string Value)
{
    /// <summary>What a screen reader calls it: a list names each of its
    /// items by this.</summary>
    public override string ToString() => Value.Length > 0 ? Label + " " + Value : Label;
}

/// <summary>One hit in the drill-down's list, or one cast in the heal drill-down's.</summary>
/// <param name="Offset">How far it fell from the average, out of the
/// furthest any hit in the list fell: over it up to 1, under it down to -1.</param>
/// <param name="Crit">A critical hit, marked with a spark beside the figure.</param>
public sealed record HitRow(string Time, string Target, string Damage, string VsAvg, double Offset = 0, bool Crit = false)
{
    /// <summary>What a screen reader calls the row: what it reads across,
    /// and that a hit was critical in a word, since the spark is a mark.</summary>
    public override string ToString() => $"{Time}, {Target}, {Damage}{(Crit ? " critical" : "")}, {VsAvg} against the average";
}

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
