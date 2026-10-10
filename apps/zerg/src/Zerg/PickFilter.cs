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
/// One line of the By target or the By damage type table over one parse:
/// a target, or a damage type, with what it came to, and a row to press,
/// which isolates it (or, isolated, lets it go) as its line of the pick
/// button's menu does. Kept from one count to the next and told what
/// changed. What <c>Views/TallyCard</c> draws; the Compare section's rows,
/// which are of two runs, are <see cref="ComparePickRow"/>, and the two
/// kinds of row say <see cref="Isolated"/>, <see cref="Dimmed"/>,
/// <see cref="Tip"/> and <c>PickCommand</c> alike, for the styles they
/// share (<c>SidePick</c> and the three after it, in <c>App.xaml</c>).
/// </summary>
public sealed partial class TallyRow(string key, Action<string> flip) : Row(key)
{
    [ObservableProperty] private string name = "";
    public override string ToString() => Name;
    [ObservableProperty] private string damage = "";
    /// <summary>Out of the damage to them all, or of every type.</summary>
    [ObservableProperty] private string share = "";
    /// <summary>How long the row's shade is, or the bar beside its share:
    /// this one's damage out of the largest's in the table.</summary>
    [ObservableProperty] private double fraction;
    /// <summary>Damage is isolated to this one, alone or among others.</summary>
    [ObservableProperty] private bool isolated;
    /// <summary>Damage is isolated to others and not this one.</summary>
    [ObservableProperty] private bool dimmed;
    [ObservableProperty] private string tip = "";

    /// <summary>The rows that name no target are a line of the table, with nothing to isolate.</summary>
    public bool CanPick => Key != PickFilter.Rest;

    [RelayCommand(CanExecute = nameof(CanPick))]
    void Pick() => flip(Key);
}

/// <summary>
/// What damage is isolated to, along one line of cut, and the list it is
/// picked from: what a pick button (<c>Views/PickButton</c>) and its menu
/// are drawn from, and, over one parse, the table that lists the same
/// (<c>Views/TallyCard</c>: the Damage section's By target and By damage
/// type panes). There are two kinds, by target (<see cref="OfTargets"/>)
/// and by damage type (<see cref="OfTypes"/>), and the Damage section has
/// one of each, over the session or the parse open in the View section,
/// and the Compare section one of each of its own, over its two runs.
///
/// <para><b>Several can be picked.</b> None picked is everything. The list
/// is alphabetical by what each is called (<see cref="Targets.SortedBy"/>)
/// and names every one whatever is picked, each with its own total, so one
/// is seen among the rest and the next is picked from the same place. The
/// table lists the same ones as the Compare section's tables do: targets
/// largest first (<see cref="Targets.Ranked"/>), damage types in that
/// table's own order (<see cref="DamageTypes.Listed"/>).</para>
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
    /// <summary>The key of the table's line for what named nothing to pick
    /// (the rows that name no target). No name is it: no name holds a
    /// control character.</summary>
    public static readonly string Rest = ((char)1).ToString();
    const string None = "—";

    readonly HashSet<string> picked = new(StringComparer.Ordinal);
    readonly Func<string, string> called;
    readonly Func<string, string> busy;
    readonly string idle;
    /// <summary>The words that differ between the two kinds, for a table's row and its pane.</summary>
    readonly Words words;

    /// <param name="Pick">What the pointer is told a press on a table's row does, of a name.</param>
    /// <param name="Rest">What the pointer is told of the line that cannot be picked.</param>
    /// <param name="Note">What a table's pane says after its title while nothing is isolated.</param>
    /// <param name="Order">The lines of a table in the order it lists them,
    /// those picked that came to nothing among them (with no total).</param>
    sealed record Words(string Title, string Clear, string ShareTip, Func<string, string> Pick, string Rest, string Note,
                        Func<IReadOnlyList<TargetTotal>, IReadOnlyCollection<string>, List<TargetTotal>> Order);

    PickFilter(bool paired, string word, string allRow, string clearName, string clearTip, string idle,
           Func<string, string> busy, Words words, Func<string, string>? called = null)
    {
        (Paired, Word, AllRow, ClearName, ClearTip) = (paired, word, allRow, clearName, clearTip);
        (this.idle, this.busy, this.called, this.words) = (idle, busy, called ?? (k => k), words);
        tip = idle;
        tableNote = words.Note;
    }

    /// <summary>By target.</summary>
    /// <param name="paired">Over the Compare section's two runs, and not one parse.</param>
    public static PickFilter OfTargets(bool paired) => new(paired, "Target", "All targets",
        "Clear the target filter", "Count every target again",
        "Count only the damage dealt to the targets you pick. Every target counts now.",
        names => "Counting only the damage dealt to " + names + ". No DPS is given meanwhile: the clock is the " +
                 (paired ? "whole run’s" : "session’s") + ", not the time a target was fought.",
        new Words("By target", "Clear the isolated targets", "Out of the damage dealt to every target",
                  name => "Count only the damage dealt to " + name, "Damage that named no target",
                  "Party damage dealt to each target name. Select one to isolate it",
                  (list, picked) => Targets.Ranked(list, picked)));

    /// <summary>By damage type. A key is a type as a row has it ("ws"); it
    /// is called what its line of Compare's By damage type table is
    /// ("Weaponskills").</summary>
    /// <param name="paired">Over the Compare section's two runs, and not one parse.</param>
    public static PickFilter OfTypes(bool paired) => new(paired, "Type", "All types",
        "Clear the type filter", "Count every type of damage again",
        "Count only the damage of the types you pick. Every type counts now.",
        names => "Counting only " + names + " damage.",
        new Words("By damage type", "Clear the isolated types", "Out of the damage of every type",
                  name => "Count only " + name + " damage", "Damage that named no type",
                  "Where the party’s damage came from. Select a type to isolate it",
                  (list, picked) => DamageTypes.Listed(list, picked)),
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

    /// <summary>What the table that lists these is called: "By target", "By damage type".</summary>
    public string Title => words.Title;
    /// <summary>What the link that clears is called in that table's
    /// heading, where the button's own mark has another name: both can be
    /// on screen, and a script presses each by its name.</summary>
    public string TableClearName => words.Clear;
    /// <summary>What the pointer is told of the table's Share column.</summary>
    public string ShareTip => words.ShareTip;

    /// <summary>What the pointer is told a press on a table's row does
    /// while nothing is isolated: "Count only the damage dealt to Kirin".</summary>
    public string PickTip(string name) => words.Pick(name);

    /// <summary>What the pointer is told of a table's row, whichever
    /// section's table it is in.</summary>
    /// <param name="pick">What a press does while nothing is isolated
    /// (<see cref="PickTip"/>); null for a line that cannot be picked.</param>
    public string RowTip(string name, bool isolated, bool dimmed, string? pick) =>
        pick is null ? words.Rest
        : isolated ? "Isolated: only this is counted. Press to let it go"
        : dimmed ? "Isolate " + name + " as well"
        : pick;

    /// <summary>What a table's pane says after its title: what the table
    /// is and that its rows pick, or how many of them are isolated.</summary>
    /// <param name="listed">How many lines the table has that can be picked.</param>
    public string NoteOf(int listed) =>
        picked.Count > 0 ? picked.Count + " of " + listed + " isolated. Select another to add it" : words.Note;

    /// <summary>What is picked changed, by a press here.</summary>
    public event Action? Changed;

    public ObservableCollection<PickRow> Rows { get; } = [];

    /// <summary>Over one parse, the same as a table: every one largest
    /// first, each a row to press (<c>Views/TallyCard</c>). Empty over two
    /// runs: the Compare section's tables are its sheet's.</summary>
    public ObservableCollection<TallyRow> Table { get; } = [];
    /// <summary>The table has a line: something was dealt, or something is picked.</summary>
    [ObservableProperty] private bool hasTable;
    /// <summary>What the table's pane says after its title (<see cref="NoteOf"/>).</summary>
    [ObservableProperty] private string tableNote;

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
        foreach (var row in Table) Mark(row);
        TableNote = NoteOf(Table.Count(r => r.CanPick));
    }

    void Tick(PickRow row)
    {
        row.Ticked = row.IsAll ? picked.Count == 0 : picked.Contains(row.Key);
        row.Status = row.Ticked && !row.IsAll ? "isolated" : "";
    }

    /// <summary>A table's row, as what is picked leaves it: isolated,
    /// dimmed beside the isolated, or neither. Its name is set first.</summary>
    void Mark(TallyRow row)
    {
        row.Isolated = row.CanPick && picked.Contains(row.Key);
        row.Dimmed = picked.Count > 0 && !row.Isolated;
        row.Tip = RowTip(row.Name, row.Isolated, row.Dimmed, row.CanPick ? PickTip(row.Name) : null);
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

        // The same as a table, in the table's order (a target's by what it
        // took, a type's its own), whatever is picked that came to nothing
        // among them, then what named nothing to pick. A row is kept from
        // one count to the next and moved when its place changes.
        var lines = words.Order(list, picked);
        double rest = Targets.Unnamed(list, whole);
        if (rest > 0) lines.Add(new TargetTotal(Rest, rest));
        Zerg.Rows.Sync(Table, lines, t => t.Name, t => new TallyRow(t.Name, Flip), (row, t) =>
        {
            bool hit = !double.IsNaN(t.Total);
            row.Name = row.CanPick ? draw(called(t.Name)) : None;
            row.Damage = hit ? Format.Int(t.Total) : None;
            row.Share = hit && whole > 0 ? Format.Num(t.Total / whole * 100, 1) + "%" : None;
            row.Fraction = hit ? RowMarks.Fraction(t.Total, most) : 0;
            Mark(row);
        });
        HasTable = Table.Count > 0;
        TableNote = NoteOf(Table.Count(r => r.CanPick));
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
