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

    /// <summary>Its actions (or heals) are showing under it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Caret))]
    private bool open;

    public string Caret => Open ? "▾" : "▸";

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

/// <summary>One character, or one job, in the damage table.</summary>
public sealed partial class CompareActorRow : CompareRow
{
    public CompareActorRow(ActorLine line, Identity id, Brush swatch, bool byJob, bool open, Action<string, bool> flipped)
        : base(line.Key, flipped)
    {
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

    [ObservableProperty] private IReadOnlyList<ActionLine> actions = [];
    [ObservableProperty] private bool noActions;

    protected override void Unfold()
    {
        Actions = Open ? Line.Actions : [];
        NoActions = Open && Line.Actions.Count == 0;
    }
}

/// <summary>One healer, or one job, in the healing table; or the party, which leads it.</summary>
public sealed partial class CompareHealerRow : CompareRow, IGroupedRow
{
    public CompareHealerRow(HealerLine line, string name, Brush? swatch, bool open, Action<string, bool> flipped)
        : base(line.Key ?? "", flipped)
    {
        (Line, Name, Swatch) = (line, name, swatch);
        Open = open && line.Key != null;
    }

    public HealerLine Line { get; }
    public string Name { get; }
    public override string ToString() => Name;
    public Brush? Swatch { get; }
    /// <summary>The party's row: a heading over the healers, with nothing under it to open.</summary>
    public bool IsHeading => Line.Key == null;

    [ObservableProperty] private IReadOnlyList<HealLine> heals = [];
    [ObservableProperty] private bool noHeals;

    protected override void Unfold()
    {
        Heals = Open ? Line.Heals : [];
        NoHeals = Open && Line.Heals.Count == 0;
    }
}

/// <summary>
/// The Compare section: two exported parses side by side, A the baseline.
/// Every difference is B − A and every percentage that over A.
///
/// <para>It shares two settings with the live sections, Include Skillchains
/// and Hide names, and the theme. Nothing else: the runs are files, measured
/// by the code that measures a session, and the session goes on being
/// followed underneath while this is on screen. It needs the session only
/// for Use current, which takes a copy of it.</para>
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
    /// file in a slot, or Character | Job.</summary>
    readonly HashSet<string> open = new(StringComparer.Ordinal), healOpen = new(StringComparer.Ordinal);
    CompareSheet? sheet;
    /// <summary>Something changed while another section was on screen.</summary>
    bool stale = true;

    public RunSlot A { get; }
    public RunSlot B { get; }

    /// <summary>Picks a parse off the disk. The main window supplies it;
    /// nothing here knows what a dialog is.</summary>
    public Func<ParseText?>? Picker { get; set; }

    public CompareViewModel(Settings settings, MainViewModel host)
    {
        (this.settings, this.host) = (settings, host);
        A = new RunSlot(this, isA: true);
        B = new RunSlot(this, isA: false);
        mode = settings.CompareMode == Healing ? Healing : Damage;
        by = settings.CompareBy == Job ? Job : Character;
    }

    // -------------------------------------------------------------- switches

    /// <summary>The section is on screen. Off it, nothing is measured or
    /// drawn; whatever changed meanwhile is drawn on the way back.</summary>
    [ObservableProperty] private bool active;

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
        open.Clear();
        healOpen.Clear();
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

    // ----------------------------------------------------------------- slots

    [ObservableProperty] private bool ready;

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
            open.Clear();
            healOpen.Clear();
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
        open.Clear();
        healOpen.Clear();
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

    [ObservableProperty] private string paceTitle = "";
    [ObservableProperty] private string paceNote = "";
    [ObservableProperty] private LineModel? pace;
    [ObservableProperty] private IReadOnlyList<RunLegend> paceLegend = [];
    [ObservableProperty] private bool paceTableOpen;
    [ObservableProperty] private IReadOnlyList<PaceLine> paceTable = [];

    [ObservableProperty] private string rowsTitle = "";
    [ObservableProperty] private string rowsNote = "";
    /// <summary>The first column's heading, and the second's.</summary>
    [ObservableProperty] private string nameHead = "";
    [ObservableProperty] private string jobHead = "";
    [ObservableProperty] private IReadOnlyList<CompareActorRow> actors = [];
    [ObservableProperty] private IReadOnlyList<KindLine> kinds = [];
    [ObservableProperty] private IReadOnlyList<TotalLine> targets = [];

    [ObservableProperty] private string healTitle = "";
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
        sheet = Ready ? CompareSheet.Of(A.Run!, B.Run!, ByJob, host.Skillchains, host.HideNames) : null;
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
            Kinds = [];
            Targets = HealSpells = HealTargets = [];
            Healers = [];
            return;
        }
        bool heal = IsHealing, job = s.ByJob;

        Tiles = heal ? s.HealTiles : s.DamageTiles;

        PaceTitle = heal ? "Cumulative healing" : "Cumulative damage";
        PaceNote = heal
            ? "Party healing since each run’s first hit, on one clock. Pet heals not included. Hover to read both runs at the same moment."
            : "Party damage since each run’s first hit, on one clock. Hover to read both runs at the same moment.";
        var p = heal ? s.HealPace : s.DamagePace;
        // The runs' colours mean "which run" and nothing else. B is drawn
        // last, on top: it is the one being judged.
        Color ca = AppTheme.Series(0, AppTheme.IsDark), cb = AppTheme.Series(1, AppTheme.IsDark);
        Pace = new LineModel(p.Times, [new LineSeries("A · " + A.Name, p.A, ca), new LineSeries("B · " + B.Name, p.B, cb)]);
        PaceLegend = [new RunLegend(MainViewModel.Solid(ca), "A · " + A.Name), new RunLegend(MainViewModel.Solid(cb), "B · " + B.Name)];
        DrawPaceTable();

        // Only the mode on screen has rows. The other mode's tables are not
        // on screen, and a few hundred cells nobody can see are still made,
        // laid out, and read out by a screen reader.
        NameHead = job ? "Job" : "Character";
        JobHead = job ? "Characters" : "Job";
        RowsTitle = job ? "By job" : "By character";
        RowsNote = job
            ? "Every character on a main job, combined. A character whose job was not reported falls under Unknown job. " +
              "Each figure shows A over B; select a row to compare its actions."
            : "Matched by name. Each figure shows A over B; a dash is a character who was not in that run. " +
              "Select a row to compare its actions.";
        Actors = heal ? [] : s.Actors.Select(r => new CompareActorRow(r, s.Ids[r.Key], SwatchOf(s.Ids[r.Key]), job,
                                                                      open.Contains(r.Key), (k, on) => Flip(open, k, on))).ToList();
        Kinds = heal ? [] : s.Kinds;
        Targets = heal ? [] : s.Targets;

        HealTitle = job ? "By job" : "By healer";
        HealerHead = job ? "Job" : "Healer";
        HealNote = s.HealNote;
        // The party leads, over whoever healed. With nobody, the note says why.
        Healers = !heal || s.Healers.Count == 0 ? [] :
        [
            new CompareHealerRow(s.Party, "Party", null, false, (_, _) => { }),
            .. s.Healers.Select(r => new CompareHealerRow(r, s.Ids[r.Key!].Label, SwatchOf(s.Ids[r.Key!]),
                                                          healOpen.Contains(r.Key!), (k, on) => Flip(healOpen, k, on))),
        ];
        HealSpells = heal ? s.HealSpells : [];
        HealTargets = heal ? s.HealTargets : [];
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
