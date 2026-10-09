using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zerg.Core;

namespace Zerg;

/// <summary>
/// One line of a pick button's menu: a target (or a damage type) and what it
/// came to, or the line that leads them, "All targets".
/// </summary>
public sealed partial class PickRow(string key, Action<string> flip) : Row(key)
{
    /// <summary>How long the bar beside a share is, in units.</summary>
    public const double BarLength = 34;

    /// <summary>The line that stands for everything: ticked while nothing is isolated.</summary>
    public bool IsAll => Key == PickFilter.All;

    [ObservableProperty] private string name = "";
    public override string ToString() => Name;
    /// <summary>It is isolated; on the leading line, nothing is.</summary>
    [ObservableProperty] private bool ticked;
    /// <summary>What a screen reader is told of a ticked line: the tick is a mark.</summary>
    [ObservableProperty] private string status = "";

    /// <summary>Over one parse: the damage, and that out of the damage to them all.</summary>
    [ObservableProperty] private string damage = "";
    [ObservableProperty] private string share = "";
    /// <summary>The bar beside the share, in units: this one's damage out
    /// of the largest's. The leading line has none.</summary>
    [ObservableProperty] private double bar;

    /// <summary>Over two runs: the damage in each.</summary>
    [ObservableProperty] private string a = "";
    [ObservableProperty] private string b = "";

    /// <summary>Run by the press, not by the tick: a line of a menu is
    /// chosen, and the tick follows what choosing it did.</summary>
    [RelayCommand]
    void Flip() => flip(Key);
}

/// <summary>
/// What damage is isolated to, along one line of cut, and the list it is
/// picked from: what a pick button (<c>Views/PickButton</c>) and its menu
/// are drawn from. There are two kinds, by target (<see cref="OfTargets"/>)
/// and by damage type (<see cref="OfTypes"/>), and the Damage section has
/// one of each, over the session or the parse open in the View section,
/// and the Compare section one of each of its own, over its two runs.
///
/// <para><b>Several can be picked.</b> None picked is everything. The list
/// is alphabetical by what each is called (<see cref="Targets.SortedBy"/>)
/// and names every one whatever is picked, each with its own total, so one
/// is seen among the rest and the next is picked from the same place.</para>
///
/// <para><b>What is picked is not kept.</b> It is not a setting: a target
/// belongs to the pull it was fought in. Whoever owns this empties it
/// (<see cref="Reset"/>) when what it lists changes hands: Start, a new
/// event file, another parse.</para>
///
/// <para>One that is picked stays on the list while nothing that counts
/// was dealt to it (its character excluded, skillchains switched off), with
/// dashes: a filter that could not be seen could not be taken off.</para>
/// </summary>
public sealed partial class PickFilter : ObservableObject
{
    /// <summary>The key of the leading line. Nothing that can be picked has
    /// it: a row that names no target cannot be.</summary>
    public const string All = "";
    const string None = "—";

    readonly HashSet<string> picked = new(StringComparer.Ordinal);
    readonly Func<string, string> called;
    readonly Func<string, string> busy;
    readonly string idle;

    PickFilter(bool paired, string word, string allRow, string clearName, string clearTip, string idle,
           Func<string, string> busy, Func<string, string>? called = null)
    {
        (Paired, Word, AllRow, ClearName, ClearTip) = (paired, word, allRow, clearName, clearTip);
        (this.idle, this.busy, this.called) = (idle, busy, called ?? (k => k));
        tip = idle;
    }

    /// <summary>By target.</summary>
    /// <param name="paired">Over the Compare section's two runs, and not one parse.</param>
    public static PickFilter OfTargets(bool paired) => new(paired, "Target", "All targets",
        "Clear the target filter", "Count every target again",
        "Count only the damage dealt to the targets you pick. Every target counts now.",
        names => "Counting only the damage dealt to " + names + ". No DPS is given meanwhile: the clock is the " +
                 (paired ? "whole run’s" : "session’s") + ", not the time a target was fought.");

    /// <summary>By damage type. A key is a type as a row has it ("ws"); it
    /// is called what its line of Compare's By damage type table is
    /// ("Weaponskills").</summary>
    /// <param name="paired">Over the Compare section's two runs, and not one parse.</param>
    public static PickFilter OfTypes(bool paired) => new(paired, "Type", "All types",
        "Clear the type filter", "Count every type of damage again",
        "Count only the damage of the types you pick. Every type counts now.",
        names => "Counting only " + names + " damage.",
        DamageTypes.Label);

    /// <summary>The list is of two runs, a column each; otherwise of one
    /// parse, with each one's share.</summary>
    public bool Paired { get; }
    /// <summary>What the button is called, and its menu's first column: "Target", "Type".</summary>
    public string Word { get; }
    /// <summary>The line that leads the menu: "All targets".</summary>
    public string AllRow { get; }
    /// <summary>What the mark that clears is called, and told the pointer.</summary>
    public string ClearName { get; }
    public string ClearTip { get; }

    /// <summary>What is picked changed, by a press here.</summary>
    public event Action? Changed;

    public ObservableCollection<PickRow> Rows { get; } = [];

    /// <summary>What the button says is picked: "All", "Kirin", "Genbu +2".</summary>
    [ObservableProperty] private string label = Targets.Label([]);
    /// <summary>Something is isolated: the button is lit, and can clear.</summary>
    [ObservableProperty] private bool isOn;
    [ObservableProperty] private string tip;
    /// <summary>What goes after a card's name where the button cannot be
    /// seen, on a floating panel's bar: " · Kirin", or "".</summary>
    [ObservableProperty] private string scope = "";

    /// <summary>The keys picked.</summary>
    public IReadOnlyCollection<string> Picked => picked;

    /// <summary>Everything picked as it is called, in order: "Genbu, Kirin".</summary>
    public string Names => string.Join(", ", Targets.SortedBy(picked, called).Select(called));

    /// <summary>What a count is handed: the keys picked, or null for everything.</summary>
    public IReadOnlySet<string>? Only => Targets.Only(picked);

    void Flip(string key)
    {
        if (key == All)
        {
            if (picked.Count == 0) return;
            picked.Clear();
        }
        else if (!picked.Remove(key)) picked.Add(key);
        Said();
        Changed?.Invoke();
    }

    /// <summary>Everything counts again.</summary>
    [RelayCommand]
    void Clear() => Flip(All);

    /// <summary>A row somewhere else that picks: a line of Compare's By
    /// target or By damage type table.</summary>
    public void Toggle(string key)
    {
        if (key != All) Flip(key);
    }

    /// <summary>
    /// Empties what is picked without saying so: for the owner, which is
    /// about to count again anyway. True when anything was picked.
    /// </summary>
    public bool Reset()
    {
        if (picked.Count == 0) return false;
        picked.Clear();
        Said();
        return true;
    }

    void Said()
    {
        Label = Targets.Label(picked.Select(called).ToList());
        IsOn = picked.Count > 0;
        Scope = IsOn ? " · " + Label : "";
        Tip = IsOn ? busy(Names) : idle;
        foreach (var row in Rows) Tick(row);
    }

    void Tick(PickRow row)
    {
        row.Ticked = row.IsAll ? picked.Count == 0 : picked.Contains(row.Key);
        row.Status = row.Ticked && !row.IsAll ? "isolated" : "";
    }

    /// <summary>Every key on the list: those that have damage, and those
    /// picked, alphabetically by what they are called.</summary>
    List<string> Listed(IEnumerable<string> hit) =>
        [All, .. Targets.SortedBy(hit.Concat(picked).Where(k => k.Length > 0), called)];

    /// <summary>
    /// The list over one parse: each one's damage, and its share of the
    /// damage to them all.
    /// </summary>
    /// <param name="whole">The damage to them all: the first line's figure.</param>
    /// <param name="draw">A name as it is drawn (a player's, while names are hidden).</param>
    public void Draw(IReadOnlyList<TargetTotal> list, double whole, Func<string, string> draw)
    {
        var by = list.ToDictionary(t => t.Name, t => t.Total, StringComparer.Ordinal);
        double most = RowMarks.Largest(by.Values);
        Zerg.Rows.Sync(Rows, Listed(by.Keys), k => k, k => new PickRow(k, Flip), (row, k) =>
        {
            Tick(row);
            if (row.IsAll)
            {
                row.Name = AllRow;
                row.Damage = Format.Int(whole);
                row.Share = whole > 0 ? "100%" : None;
                row.Bar = 0;
                return;
            }
            row.Name = draw(called(k));
            bool hit = by.TryGetValue(k, out var v);
            row.Damage = hit ? Format.Int(v) : None;
            row.Share = hit && whole > 0 ? Format.Num(v / whole * 100, 1) + "%" : None;
            row.Bar = hit ? PickRow.BarLength * RowMarks.Fraction(v, most) : 0;
        });
    }

    /// <summary>The list over two runs: each one's damage in each.</summary>
    /// <param name="wholeA">The damage to them all in run A.</param>
    public void Draw(IReadOnlyList<PairTotal> list, double wholeA, double wholeB)
    {
        var by = list.ToDictionary(t => t.Key, StringComparer.Ordinal);
        Zerg.Rows.Sync(Rows, Listed(by.Keys), k => k, k => new PickRow(k, Flip), (row, k) =>
        {
            Tick(row);
            if (row.IsAll)
            {
                (row.Name, row.A, row.B) = (AllRow, Format.Int(wholeA), Format.Int(wholeB));
                return;
            }
            row.Name = called(k);
            (row.A, row.B) = by.TryGetValue(k, out var t) ? (Format.Int(t.A), Format.Int(t.B)) : (None, None);
        });
    }
}
