using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zerg.Charts;
using Zerg.Core;

namespace Zerg;

/// <summary>
/// One of the two slots a parse is loaded into: A, the baseline, or B, the
/// run compared against it. An error stays on the slot it happened in, and a
/// file that would not load leaves what the slot already held.
/// </summary>
public sealed partial class RunSlot(CompareViewModel owner, bool isA) : ObservableObject
{
    public CompareViewModel Owner => owner;
    public bool IsA { get; } = isA;
    public string Letter => IsA ? "A" : "B";
    public string Role => IsA ? "Baseline" : "Compared";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(IsLoaded), nameof(OpenText), nameof(OpenTip))]
    private ImportedParse? run;

    public bool IsEmpty => Run == null;
    public bool IsLoaded => Run != null;
    public string OpenText => IsEmpty ? "Open…" : "Replace…";
    public string OpenTip => IsEmpty ? "Open an exported parse into this slot" : "Open another parse in this one's place";

    /// <summary>What the run is called: its file's name, or when it was taken.</summary>
    [ObservableProperty] private string name = "";
    /// <summary>The event file the parse was recorded from, where it says.</summary>
    [ObservableProperty] private string tip = "";
    [ObservableProperty] private string started = "";
    [ObservableProperty] private string length = "";
    [ObservableProperty] private string party = "";
    [ObservableProperty] private string skipped = "";
    [ObservableProperty] private string error = "";
    /// <summary>A file is being held over the slot.</summary>
    [ObservableProperty] private bool isOver;

    [RelayCommand]
    void Open() => owner.OpenInto(this);

    [RelayCommand(CanExecute = nameof(CanUseCurrent))]
    void UseCurrent() => owner.UseCurrent(this);

    bool CanUseCurrent() => owner.CanUseCurrent;

    [RelayCommand]
    void Clear() => owner.Clear(this);

    /// <summary>A file dropped on the slot.</summary>
    public void Take(string path) => owner.Read(this, () => ParseDialog.Read(path));

    internal void Refresh() => UseCurrentCommand.NotifyCanExecuteChanged();

    internal void Show(ImportedParse parse, string title)
    {
        var info = CompareSheet.Describe(parse);
        Run = parse;
        Name = title;
        Tip = parse.File ?? title;
        (Started, Length, Party, Skipped) = (info.Started, info.Length, info.Party, info.Skipped);
        Error = "";
    }

    internal void Empty()
    {
        Run = null;
        Name = Tip = Started = Length = Party = Skipped = Error = "";
    }

    /// <summary>Everything the slot holds, error included, to hand to the other one.</summary>
    internal (ImportedParse? Run, string Name, string Error) Held => (Run, Name, Error);

    internal void Hold((ImportedParse? Run, string Name, string Error) held)
    {
        if (held.Run != null) Show(held.Run, held.Name);
        else Empty();
        Error = held.Error;
    }
}

/// <summary>A line of the cumulative chart's legend: which run, in its colour.</summary>
public sealed record RunLegend(Brush Swatch, string Name);

/// <summary>A row of the Compare tables that opens onto what is under it.</summary>
public abstract partial class CompareRow(string key, Action<string, bool> flipped) : ObservableObject
{
    public string Key { get; } = key;

    /// <summary>Its actions (or heals) are showing under it. The mark
    /// before its name is drawn from this (the RowCaret style).</summary>
    [ObservableProperty] private bool open;

    partial void OnOpenChanged(bool value)
    {
        flipped(Key, value);
        Unfold();
    }

    /// <summary>What is under the row is only made once the row is opened:
    /// eighteen characters' actions are a few thousand cells nobody asked for.</summary>
    protected abstract void Unfold();

    [RelayCommand]
    void Toggle() => Open = !Open;
}

/// <summary>
/// A row under a row: one action of a character (or job), or one heal of a
/// healer. It opens onto how its hits, or its casts, are spread in each run.
/// </summary>
/// <param name="key">The action under its row's key: an action's name alone is not one.</param>
/// <param name="label">Whose it is, for a screen reader: "Hasaya, Tachi: Gekko".</param>
/// <param name="measure">Works the spread out. Only called once the row is opened.</param>
public abstract partial class CompareSpreadRow(string key, string name, string label, Action<string, bool> flipped,
                                               Func<ActionSpread> measure) : CompareRow(key, flipped)
{
    public string Name { get; } = name;
    public string Label { get; } = label;
    public override string ToString() => Label;

    [ObservableProperty] private ActionSpread? spread;
    [ObservableProperty] private PairedHistogramModel? histogram;

    protected override void Unfold()
    {
        Spread = Open ? measure() : null;
        Histogram = Spread is { } s ? PairedHistogramModel.From(s) : null;
    }
}

/// <summary>One action under a character (or job) of the damage table.</summary>
public sealed class CompareActionRow : CompareSpreadRow
{
    public CompareActionRow(ActionLine line, string key, string label, bool open, Action<string, bool> flipped,
                            Func<ActionSpread> measure)
        : base(key, line.Name, label, flipped, measure)
    {
        Line = line;
        Open = open;
    }

    public ActionLine Line { get; }
}

/// <summary>One heal under a healer (or job) of the healing table.</summary>
public sealed class CompareHealRow : CompareSpreadRow
{
    public CompareHealRow(HealLine line, string key, string label, bool open, Action<string, bool> flipped,
                          Func<ActionSpread> measure)
        : base(key, line.Name, label, flipped, measure)
    {
        Line = line;
        Open = open;
    }

    public HealLine Line { get; }
}

/// <summary>
/// One line of the By target or the By damage type table while damage is
/// compared: what it came to in each run, and a row to press, which
/// isolates it (or, isolated, lets it go).
/// </summary>
/// <param name="line">The line as the sheet has it: a <see cref="TotalLine"/>, or a <see cref="KindLine"/>.</param>
/// <param name="key">What it is picked by: a target's name, a type's key. "" for a line that cannot be picked.</param>
/// <param name="isolated">Damage is isolated to this one, alone or among others.</param>
/// <param name="dimmed">Damage is isolated to others and not this one.</param>
/// <param name="pick">What the pointer is told a press does while nothing is isolated.</param>
/// <param name="flip">Told the key when the row is pressed.</param>
public sealed partial class ComparePickRow(object line, string key, string name, bool isolated, bool dimmed, string pick,
                                           Action<string> flip)
{
    public object Line { get; } = line;
    public bool Isolated { get; } = isolated;
    public bool Dimmed { get; } = dimmed;
    public string Name { get; } = name;
    public override string ToString() => Name;
    /// <summary>The rows that name no target are a line of the table, with nothing to isolate.</summary>
    public bool CanPick => key.Length > 0;
    public string Tip => !CanPick ? "Damage that named no target"
        : Isolated ? "Isolated: only this is counted. Press to let it go"
        : Dimmed ? "Isolate " + Name + " as well"
        : pick;

    [RelayCommand(CanExecute = nameof(CanPick))]
    void Pick() => flip(key);
}

/// <summary>One character, or one job, in the damage table.</summary>
public sealed partial class CompareActorRow : CompareRow
{
    readonly Func<ActionLine, CompareActionRow> action;

    /// <param name="action">Makes the row of one of this row's actions.</param>
    public CompareActorRow(ActorLine line, Identity id, Brush swatch, bool byJob, bool open, Action<string, bool> flipped,
                           Func<ActionLine, CompareActionRow> action)
        : base(line.Key, flipped)
    {
        this.action = action;
        (Line, Name, Swatch) = (line, id.Label, swatch);
        // A character not in a run has no job there; one who was, and whose
        // job the party table never reported, has a question mark. Beside a
        // name that is the job already, the job is not printed again: a dash.
        Members = byJob ? id.Members : id.Hidden ? CompareSheet.Dash : "";
        Unreported = !byJob && !id.Hidden && ((line.InA && id.JobA.Length == 0) || (line.InB && id.JobB.Length == 0));
        Jobs = !byJob && !id.Hidden
            ? new AB(line.InA ? id.JobA.Length > 0 ? id.JobA : "?" : CompareSheet.Dash,
                     line.InB ? id.JobB.Length > 0 ? id.JobB : "?" : CompareSheet.Dash)
            : null;
        Open = open;
    }

    public ActorLine Line { get; }
    public string Name { get; }
    public override string ToString() => Name;
    public Brush Swatch { get; }
    /// <summary>By job: who was on it. By character: "", or a dash while names are hidden.</summary>
    public string Members { get; }
    /// <summary>By character: the job in each run. Null by job, or while names are hidden.</summary>
    public AB? Jobs { get; }
    public string? JobsTip => Unreported ? "?: in that run, but their job was never reported" : null;
    bool Unreported { get; }

    [ObservableProperty] private IReadOnlyList<CompareActionRow> actions = [];
    [ObservableProperty] private bool noActions;

    protected override void Unfold()
    {
        Actions = Open ? Line.Actions.Select(action).ToList() : [];
        NoActions = Open && Line.Actions.Count == 0;
    }
}

/// <summary>One healer, or one job, in the healing table; or the party, which leads it.</summary>
public sealed partial class CompareHealerRow : CompareRow, IGroupedRow
{
    readonly Func<HealLine, CompareHealRow>? heal;

    /// <param name="heal">Makes the row of one of this healer's heals. The party has none to make.</param>
    public CompareHealerRow(HealerLine line, string name, Brush? swatch, bool open, Action<string, bool> flipped,
                            Func<HealLine, CompareHealRow>? heal = null)
        : base(line.Key ?? "", flipped)
    {
        this.heal = heal;
        (Line, Name, Swatch) = (line, name, swatch);
        Open = open && line.Key != null;
    }

    public HealerLine Line { get; }
    public string Name { get; }
    public override string ToString() => Name;
    public Brush? Swatch { get; }
    /// <summary>The party's row: a heading over the healers, with nothing under it to open.</summary>
    public bool IsHeading => Line.Key == null;

    [ObservableProperty] private IReadOnlyList<CompareHealRow> heals = [];
    [ObservableProperty] private bool noHeals;

    protected override void Unfold()
    {
        Heals = Open && heal != null ? Line.Heals.Select(heal).ToList() : [];
        NoHeals = Open && Line.Heals.Count == 0;
    }
}

/// <summary>
/// The Compare section: two exported parses side by side, A the baseline.
/// Every difference is B − A and every percentage that over A.
///
/// <para>It shares two settings with the live sections, Include Skillchains
/// and Hide names, and the theme; and one that only changes how its rows
/// are drawn, "shade characters in their colour" (here: the runs' bands).
/// The two runs' colours are settings of its own, kept by the main view
/// model with the rest. Nothing else: the runs are files, measured by the
/// code that measures a session, and the session goes on being followed
/// underneath while this is on screen. It needs the session only for Use
/// current, which takes a copy of it.</para>
///
/// <para>On screen it is two things, both with this as their data context:
/// its bands (<c>Views/CompareSection</c>) and, once both slots hold a
/// parse, its four panes (<c>Views/ComparePanes</c>).</para>
///
/// <para><b>There is no owner here.</b> The two files may be two people's,
/// and neither is "you": Hide names hides every name, and no colour is kept
/// for anyone.</para>
///
/// <para>Compare by and the Damage | Healing mode are kept between runs of
/// Zerg. The slots are not.</para>
/// </summary>
public sealed partial class CompareViewModel : ObservableObject
{
    public const string Damage = "Damage", Healing = "Healing", Character = "Character", Job = "Job";

    readonly Settings settings;
    readonly MainViewModel host;
    /// <summary>The rows opened onto their actions, and onto their heals, by
    /// key. Kept across a redraw, dropped when the rows change meaning: a new
    /// file in a slot, or Character | Job. An action opened onto its hits, or
    /// a heal onto its casts, is kept the same way, under its row's key and
    /// its own name, and stays kept while its row is closed.</summary>
    readonly HashSet<string> open = new(StringComparer.Ordinal), healOpen = new(StringComparer.Ordinal),
                             spreadOpen = new(StringComparer.Ordinal), healSpreadOpen = new(StringComparer.Ordinal);
    CompareSheet? sheet;
    /// <summary>Something changed while another section was on screen.</summary>
    bool stale = true;

    public RunSlot A { get; }
    public RunSlot B { get; }

    /// <summary>Picks a parse off the disk. The main window supplies it;
    /// nothing here knows what a dialog is.</summary>
    public Func<ParseText?>? Picker { get; set; }

    /// <summary>
    /// Which targets the damage of both runs is isolated to: the Target
    /// button beside Include Skillchains, and the rows of the By target
    /// table. The section's own, not the Damage section's: its targets are
    /// two files', not the session's. Emptied when a slot is given another
    /// parse or cleared.
    /// </summary>
    public PickFilter TargetFilter { get; } = PickFilter.OfTargets(paired: true);

    /// <summary>
    /// Which damage types both runs are isolated to: the Type button beside
    /// the Target button, and the rows of the By damage type table. It and
    /// the targets cut together, and each table is of what the other filter
    /// leaves. Emptied with the targets, when a slot changes.
    /// </summary>
    public PickFilter TypeFilter { get; } = PickFilter.OfTypes(paired: true);

    public CompareViewModel(Settings settings, MainViewModel host)
    {
        (this.settings, this.host) = (settings, host);
        TargetFilter.Changed += () =>
        {
            Log.Write("compare targets " + (TargetFilter.IsOn ? TargetFilter.Names : "all"));
            Redraw();
        };
        TypeFilter.Changed += () =>
        {
            Log.Write("compare types " + (TypeFilter.IsOn ? TypeFilter.Names : "all"));
            Redraw();
        };
        A = new RunSlot(this, isA: true);
        B = new RunSlot(this, isA: false);
        mode = settings.CompareMode == Healing ? Healing : Damage;
        by = settings.CompareBy == Job ? Job : Character;
    }

    // -------------------------------------------------------------- switches

    /// <summary>The section is on screen. Off it, nothing is measured or
    /// drawn; whatever changed meanwhile is drawn on the way back.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsPanes))]
    private bool active;

    /// <summary>The section's panes are on screen: it is, and both slots
    /// hold a parse. (The panes are not inside the bands: they stand in the
    /// main window's page among the other sections' panes, and have to be
    /// told to go when the section does.)</summary>
    public bool ShowsPanes => Active && Ready;

    partial void OnActiveChanged(bool value)
    {
        if (value && stale) Redraw();
    }

    /// <summary>Which side of the two runs is compared. The slots and every
    /// other switch are shared, so this measures nothing again.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDamage), nameof(IsHealing))]
    private string mode;

    public bool IsHealing => Mode == Healing;
    public bool IsDamage => !IsHealing;

    partial void OnModeChanged(string value)
    {
        settings.CompareMode = value;
        settings.Save();
        Show();
    }

    /// <summary>What a row is: a character, matched by name across the runs,
    /// or a main job with everyone on it combined, for runs whose rosters differ.</summary>
    [ObservableProperty] private string by;

    bool ByJob => By == Job;

    partial void OnByChanged(string value)
    {
        settings.CompareBy = value;
        settings.Save();
        Forget();
        Redraw();
    }

    /// <summary>The live sections' own setting, not a second one: switched
    /// here, it is switched there.</summary>
    public bool Skillchains
    {
        get => host.Skillchains;
        set => host.Skillchains = value;
    }

    public string SkillchainsTip => Skillchains
        ? "Skillchains counted, credited to whoever closed them. The same switch as the Damage section's. Click to leave them out."
        : "Skillchains left out of both runs. Click to count them again.";

    /// <summary>Include Skillchains, Hide names or the theme changed.</summary>
    internal void SharedChanged()
    {
        OnPropertyChanged(nameof(Skillchains));
        OnPropertyChanged(nameof(SkillchainsTip));
        Redraw();
    }

    /// <summary>The live sections' "shade characters in their colour", which
    /// here governs the runs' bands behind every row: on, a row is shaded
    /// twice, A over B, each to that run's amount; off, the rows are plain
    /// and the amount's cell has its two small bars back. The section hands
    /// it to its rows (Views/Shading), so a flip is a trigger in each row:
    /// nothing is measured and no row is made again.</summary>
    public bool ShadeCharacters => host.ShadeCharacters;

    internal void ShadingChanged() => OnPropertyChanged(nameof(ShadeCharacters));

    /// <summary>How the section's four panes are arranged under its bands:
    /// MainViewModel.CompareLayout, which is where an arrangement a player
    /// makes is put (MainViewModel.Rearrange) and saved from. Read only
    /// here: the panel says what was asked for, and the main window hands
    /// that to the main view model.</summary>
    public Zerg.Core.Layout.SplitNode Layout => host.CompareLayout;

    internal void LayoutChanged() => OnPropertyChanged(nameof(Layout));

    /// <summary>
    /// A run's colour changed. Whatever is drawn with a run's brush has
    /// followed already (AppTheme.Runs). What is handed a colour, the chart
    /// of both runs and its legend, is handed it again here, and nothing
    /// else is touched: nothing is measured and no row is made again. Off
    /// screen, it is left for the way back.
    /// </summary>
    internal void Recolour()
    {
        if (!Active) stale = true;
        else if (sheet is { } s) DrawPace(s);
    }

    // ----------------------------------------------------------------- slots

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsPanes))]
    private bool ready;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SwapCommand))]
    private bool canSwap;

    /// <summary>There is a session with a clock to take a copy of.</summary>
    public bool CanUseCurrent { get; private set; }

    public string UseCurrentTip => CanUseCurrent
        ? "Take the session being measured right now into this slot, as it stands"
        : "Nothing is being measured yet. Press Start first";

    internal void SessionChanged(bool started)
    {
        if (CanUseCurrent == started) return;
        CanUseCurrent = started;
        OnPropertyChanged(nameof(UseCurrentTip));
        A.Refresh();
        B.Refresh();
    }

    /// <summary>The rows are about to change meaning: nothing that was opened is the same thing any more.</summary>
    void Forget()
    {
        open.Clear();
        healOpen.Clear();
        spreadOpen.Clear();
        healSpreadOpen.Clear();
    }

    internal void OpenInto(RunSlot slot) => Read(slot, () => Picker?.Invoke());

    /// <summary>The session as it stands, by way of the export format, so it
    /// measures exactly as it would had it been saved and opened here.</summary>
    internal void UseCurrent(RunSlot slot) => Read(slot, host.Current);

    internal void Read(RunSlot slot, Func<ParseText?> source)
    {
        try
        {
            if (source() is not { } file) return;
            slot.Show(ParseFile.Import(file.Text), ParseDialog.Title(file.Name));
            Forget();
            // Another run has targets of its own, and types.
            TargetFilter.Reset();
            TypeFilter.Reset();
        }
        catch (ParseImportException e)
        {
            slot.Error = e.Message;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            slot.Error = "Could not open: " + e.Message;
        }
        Redraw();
    }

    internal void Clear(RunSlot slot)
    {
        slot.Empty();
        Forget();
        TargetFilter.Reset();
        TypeFilter.Reset();
        Redraw();
    }

    /// <summary>Makes B the baseline. The errors go with their runs.</summary>
    [RelayCommand(CanExecute = nameof(CanSwap))]
    void Swap()
    {
        var a = A.Held;
        A.Hold(B.Held);
        B.Hold(a);
        Redraw();
    }

    // --------------------------------------------------------------- drawing

    [ObservableProperty] private IReadOnlyList<CompareTile> tiles = [];

    // Each pane's title, the few words after it, and the same at full
    // length for the pointer (Views/Pane: Title, Note, Hint).
    [ObservableProperty] private string paceTitle = "";
    [ObservableProperty] private string paceNote = "";
    [ObservableProperty] private string paceHint = "";
    [ObservableProperty] private LineModel? pace;
    [ObservableProperty] private IReadOnlyList<RunLegend> paceLegend = [];
    [ObservableProperty] private bool paceTableOpen;
    [ObservableProperty] private IReadOnlyList<PaceLine> paceTable = [];

    [ObservableProperty] private string rowsTitle = "";
    [ObservableProperty] private string rowsNote = "";
    [ObservableProperty] private string rowsHint = "";
    [ObservableProperty] private string kindsTitle = "";
    [ObservableProperty] private string kindsNote = "";
    [ObservableProperty] private string targetsNote = "";
    /// <summary>The first column's heading, and the second's.</summary>
    [ObservableProperty] private string nameHead = "";
    [ObservableProperty] private string jobHead = "";
    [ObservableProperty] private IReadOnlyList<CompareActorRow> actors = [];
    /// <summary>The By damage type table's rows: every type, each a row to press.</summary>
    [ObservableProperty] private IReadOnlyList<ComparePickRow> kindRows = [];
    /// <summary>The By target table's rows while damage is compared: every
    /// target, each a row to press.</summary>
    [ObservableProperty] private IReadOnlyList<ComparePickRow> targetRows = [];

    [ObservableProperty] private string healerHead = "";
    [ObservableProperty] private string healNote = "";
    [ObservableProperty] private IReadOnlyList<CompareHealerRow> healers = [];
    [ObservableProperty] private IReadOnlyList<TotalLine> healSpells = [];
    [ObservableProperty] private IReadOnlyList<TotalLine> healTargets = [];

    /// <summary>Measures both runs again and draws them. Needed when a slot,
    /// Character | Job, Include Skillchains, Hide names or the theme changes.</summary>
    void Redraw()
    {
        CanSwap = A.Run != null || B.Run != null;
        if (!Active)
        {
            stale = true;
            return;
        }
        stale = false;
        Ready = A.Run != null && B.Run != null;
        sheet = Ready ? CompareSheet.Of(A.Run!, B.Run!, ByJob, host.Skillchains, host.HideNames, TargetFilter.Only,
                                        TypeFilter.Only) : null;
        Show();
    }

    /// <summary>Draws the mode on screen from the runs as last measured.</summary>
    void Show()
    {
        if (sheet is not { } s)
        {
            Tiles = [];
            Pace = null;
            PaceLegend = [];
            PaceTable = [];
            Actors = [];
            KindRows = [];
            HealSpells = HealTargets = [];
            TargetRows = [];
            TargetFilter.Draw(Array.Empty<PairTotal>(), 0, 0);
            TypeFilter.Draw(Array.Empty<PairTotal>(), 0, 0);
            Healers = [];
            return;
        }
        bool heal = IsHealing, job = s.ByJob;

        Tiles = heal ? s.HealTiles : s.DamageTiles;

        PaceTitle = heal ? "Cumulative healing" : "Cumulative damage";
        PaceNote = heal
            ? "Party healing since each run’s first hit, on one clock. Pet heals not included"
            : "Party damage since each run’s first hit, on one clock";
        PaceHint = PaceNote + ". Hover to read both runs at the same moment.";
        DrawPace(s);
        DrawPaceTable();

        // Only the mode on screen has rows. The other mode's tables are not
        // on screen, and a few hundred cells nobody can see are still made,
        // laid out, and read out by a screen reader.
        NameHead = job ? "Job" : "Character";
        JobHead = job ? "Characters" : "Job";
        // One pane for whoever the rows are: its damage table or its
        // healing table, whichever mode is on screen.
        RowsTitle = job ? "By job" : heal ? "By healer" : "By character";
        RowsNote = heal
            ? "Each figure shows A over B. Select a row to compare its heals"
            : job ? "Every character on a main job, combined. Each figure shows A over B"
                  : "Matched by name. Each figure shows A over B; a dash is a character who was not in that run";
        RowsHint = heal
            ? "Each figure shows A over B. Select a row to compare its heals, and a heal to compare how its casts are spread."
            : (job
                ? "Every character on a main job, combined. A character whose job was not reported falls under Unknown job. " +
                  "Each figure shows A over B. "
                : "Matched by name. Each figure shows A over B; a dash is a character who was not in that run. ") +
              "Select a row to compare its actions, and an action to compare how its hits are spread.";
        KindsTitle = heal ? "By heal" : "By damage type";
        // By damage type lists every type whatever is isolated, as By
        // target does every target; each is of what the other filter leaves.
        var types = TypeFilter.Picked;
        TypeFilter.Draw(s.TypeList, s.A.Kinds.Values.Sum(), s.B.Kinds.Values.Sum());
        KindsNote = heal ? "Every healer’s casts of each spell or ability, combined. Pet heals included"
            : TypeFilter.NoteOf(s.TypeList.Count);
        // By target lists every target whatever is isolated (it is measured
        // before the filter): where one is seen among the rest, and where
        // the next is picked.
        var picked = TargetFilter.Picked;
        TargetFilter.Draw(s.TargetList, s.A.Targets.Values.Sum(), s.B.Targets.Values.Sum());
        TargetsNote = heal ? "Healing each character received, largest first. Pet heals included"
            : TargetFilter.NoteOf(s.TargetList.Count);
        Actors = heal ? [] : s.Actors.Select(r => new CompareActorRow(r, s.Ids[r.Key], SwatchOf(s.Ids[r.Key]), job,
                                                                      open.Contains(r.Key), (k, on) => Flip(open, k, on),
                                                                      x => ActionOf(s, r.Key, x))).ToList();
        KindRows = heal ? [] : s.Kinds.Select(k => new ComparePickRow(k, k.Key, k.Label, types.Contains(k.Key),
                                                                     types.Count > 0 && !types.Contains(k.Key),
                                                                     TypeFilter.PickTip(k.Label),
                                                                     TypeFilter.Toggle)).ToList();
        TargetRows = heal ? [] : s.Targets.Select(t => new ComparePickRow(t, t.Key, t.Name, picked.Contains(t.Key),
                                                                       picked.Count > 0 && !picked.Contains(t.Key),
                                                                       TargetFilter.PickTip(t.Name),
                                                                       TargetFilter.Toggle)).ToList();

        HealerHead = job ? "Job" : "Healer";
        HealNote = s.HealNote;
        // The party leads, over whoever healed. With nobody, the note says why.
        Healers = !heal || s.Healers.Count == 0 ? [] :
        [
            new CompareHealerRow(s.Party, "Party", null, false, (_, _) => { }),
            .. s.Healers.Select(r => new CompareHealerRow(r, s.Ids[r.Key!].Label, SwatchOf(s.Ids[r.Key!]),
                                                          healOpen.Contains(r.Key!), (k, on) => Flip(healOpen, k, on),
                                                          x => HealOf(s, r.Key!, x))),
        ];
        HealSpells = heal ? s.HealSpells : [];
        HealTargets = heal ? s.HealTargets : [];
    }

    /// <summary>The chart of both runs and its legend, in the runs' colours:
    /// the two things here that are handed a colour and cannot follow a
    /// brush. The colours mean "which run" and nothing else: each run's
    /// own, as set or as installed, and a slot's whichever parse is in it.
    /// B is drawn last, on top: it is the one being judged.</summary>
    void DrawPace(CompareSheet s)
    {
        var p = IsHealing ? s.HealPace : s.DamagePace;
        Color ca = AppTheme.Run(a: true), cb = AppTheme.Run(a: false);
        Pace = new LineModel(p.Times, [new LineSeries("A · " + A.Name, p.A, ca), new LineSeries("B · " + B.Name, p.B, cb)]);
        PaceLegend = [new RunLegend(MainViewModel.Solid(ca), "A · " + A.Name), new RunLegend(MainViewModel.Solid(cb), "B · " + B.Name)];
    }

    /// <summary>One action's row, under the row of <paramref name="key"/>. Its
    /// hits are measured on the sheet it was drawn from, and only once it is opened.</summary>
    CompareActionRow ActionOf(CompareSheet s, string key, ActionLine line)
    {
        // No name holds a line break, so this cannot be another row's action.
        var id = key + "\n" + line.Name;
        return new CompareActionRow(line, id, s.Ids[key].Label + ", " + line.Name, spreadOpen.Contains(id),
                                    (k, on) => Flip(spreadOpen, k, on), () => s.Spread(key, line.Name));
    }

    /// <summary>One heal's row, under the healer of <paramref name="key"/>, measured the same way.</summary>
    CompareHealRow HealOf(CompareSheet s, string key, HealLine line)
    {
        var id = key + "\n" + line.Name;
        return new CompareHealRow(line, id, s.Ids[key].Label + ", " + line.Name, healSpreadOpen.Contains(id),
                                  (k, on) => Flip(healSpreadOpen, k, on), () => s.HealSpread(key, line.Name));
    }

    static void Flip(HashSet<string> set, string key, bool on)
    {
        if (on) set.Add(key);
        else set.Remove(key);
    }

    /// <summary>A row's mark: its job's colour, or the fallback colour of its
    /// place in the table for a job that has none, or was never reported.</summary>
    static Brush SwatchOf(Identity id) =>
        MainViewModel.Solid(AppTheme.Job(id.ColorJob, AppTheme.IsDark) ?? AppTheme.Series(id.Slot, AppTheme.IsDark));

    partial void OnPaceTableOpenChanged(bool value) => DrawPaceTable();

    /// <summary>The chart as a table. Only worked out while it is open.</summary>
    void DrawPaceTable() =>
        PaceTable = PaceTableOpen && sheet is { } s ? CompareSheet.Table(IsHealing ? s.HealPace : s.DamagePace) : [];
}
