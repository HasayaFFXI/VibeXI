using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zerg.Core;

namespace Zerg;

/// <summary>What the status dot shows: the session's state, unless the event
/// file itself is the news.</summary>
public enum StatusLight { Idle, Armed, Live, Held, Waiting, Error }

/// <summary>
/// The main window's state: the session and its two buttons, the filters, and
/// everything the Damage and Healing sections show. The Compare section has
/// a view model of its own (<see cref="Compare"/>).
///
/// <para>Two things drive it. A poll that brought new lines, or any change to
/// a filter or the session, makes a new count and redraws every card from it
/// (<see cref="Recount"/>). The clock, four times a second, rewrites only the
/// figures that move with nothing happening: the elapsed time, and every DPS,
/// which falls as the time under it grows (<see cref="Tick"/>).</para>
///
/// <para><b>An idle poll redraws nothing.</b> Every property raises a change
/// only when its value changes, and the lists keep their rows from one count
/// to the next, so a table is not rebuilt under the pointer for no new data.</para>
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    const string Dot = " · ", None = "—";

    readonly Settings settings;
    readonly EventFeed feed;
    /// <summary>The session being followed. Fed by every poll, whatever is on screen.</summary>
    readonly Tracker live = new();
    readonly HashSet<string> excluded;
    /// <summary>The last count, which the clock re-divides between polls.</summary>
    Snapshot? snapshot;
    /// <summary>The characters with a chip right now: what All and None act on.</summary>
    List<string> listed = [];
    string? error;
    bool hasFile;

    // -------------------------------------------------------------- settings

    [ObservableProperty] private string theme;

    /// <summary>
    /// The section on screen. Damage and Healing are both counted on every
    /// count whichever is showing, Compare included: a card of either may be
    /// floating over the game, and it has to keep up.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDamage), nameof(IsHealing), nameof(IsCompare), nameof(IsLive), nameof(HideNamesTip),
                              nameof(ParseBarOpen))]
    private string section;

    public const string DamageSection = "Damage", HealingSection = "Healing", CompareSection = "Compare";

    public bool IsDamage => Section == DamageSection;
    public bool IsHealing => Section == HealingSection;
    public bool IsCompare => Section == CompareSection;
    /// <summary>One of the two sections that show the session being measured.</summary>
    public bool IsLive => !IsCompare;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SkillchainsTip))]
    private bool skillchains;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HideNamesTip))]
    private bool hideNames;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GroupTip))]
    private bool groupSmallLines;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CharactersToggleText), nameof(CharactersToggleTip))]
    private bool charactersOpen;

    public string SkillchainsTip => Skillchains
        ? "Skillchains counted: credited to the character whose weaponskill closed each one. Click to leave them out."
        : "Skillchains left out of every total. Click to count them again.";

    /// <summary>Over Compare the switch spares nobody: its two runs may be
    /// two people's, and neither is "you".</summary>
    public string HideNamesTip => (HideNames, IsCompare) switch
    {
        (true, false) => "Names hidden: every character but you is drawn as their job. Click to show them again.",
        (true, true) => "Names hidden: every character in both runs is drawn as their job. Click to show them again.",
        (false, false) => "Replace every other character's name with their job, for a screenshot or a stream",
        (false, true) => "Replace every character's name with their job, for a screenshot or a stream",
    };

    public string GroupTip => GroupSmallLines
        ? "Characters under 5% of the party's damage share one \"others\" line. Your own line is never grouped. " +
          "Click to give everyone a line."
        : "Every character has their own line. Click to group everyone under 5% of the party's damage into one.";

    public string CharactersToggleText => CharactersOpen ? "Hide" : "Show";
    public string CharactersToggleTip => (CharactersOpen ? "Collapse" : "Expand") + " the character list";

    // --------------------------------------------------- session and status

    [ObservableProperty] private SessionView view;
    [ObservableProperty] private StatusLight statusLight = StatusLight.Waiting;
    [ObservableProperty] private string statusFile = "waiting for the addon";
    [ObservableProperty] private string statusDetail = "";

    // --------------------------------------------------------------- filters

    public ObservableCollection<ChipRow> Chips { get; } = [];

    /// <summary>The cards that can float over the game, and their opacity.</summary>
    public PanelSet Panels { get; }

    /// <summary>The Compare section: two saved parses side by side.</summary>
    public CompareViewModel Compare { get; }

    [ObservableProperty] private string charactersLabel = "Characters";
    [ObservableProperty] private string charactersHint = "";

    public MainViewModel(Settings settings, EventFeed feed)
    {
        this.settings = settings;
        this.feed = feed;
        excluded = new HashSet<string>(settings.Excluded, StringComparer.Ordinal);
        Panels = new PanelSet(settings);

        theme = settings.Theme;
        section = settings.Section is HealingSection or CompareSection ? settings.Section : DamageSection;
        skillchains = settings.Skillchains;
        hideNames = settings.HideNames;
        groupSmallLines = settings.GroupSmallLines;
        charactersOpen = settings.CharactersOpen;
        view = SessionView.Of(live.Session);
        Compare = new CompareViewModel(settings, this) { Active = IsCompare };

        feed.Updated += OnUpdated;
        feed.Failed += OnFailed;
        // A chart's inks restyle themselves; the colours that stand for
        // characters are handed over, so they are handed over again.
        AppTheme.Changed += () =>
        {
            if (snapshot != null) Recount();
            Compare.SharedChanged();
        };
        Recount();
    }

    // --------------------------------------------------------------- theme

    /// <summary>Applied the moment it changes (the theme switches live) and saved.</summary>
    partial void OnThemeChanged(string value)
    {
        settings.Theme = value;
        AppTheme.Apply(settings.ThemeMode);
        settings.Save();
        Log.Write("theme " + value);
    }

    /// <summary>
    /// The characters listed follow the section: who dealt damage, or who
    /// healed. So this is a count again, though no figure moves. Compare
    /// lists nobody, and draws whatever changed while it was off screen.
    /// </summary>
    partial void OnSectionChanged(string value)
    {
        settings.Section = value;
        settings.Save();
        Compare.Active = IsCompare;
        Recount();
    }

    // ---------------------------------------------------------------- feed

    void OnUpdated(TailUpdate u)
    {
        error = null;
        hasFile = u.File is not null;
        if (u.File is null)
        {
            DescribeStatus();
            return;
        }

        bool fresh = u.Reset;
        if (fresh)
        {
            // A new file is a new character or a new day: nothing carries
            // over, and a clock from the old one would measure the wrong session.
            live.Follow(u.File);
            if (import == null) drill = healDrill = null;
            Log.Write($"following {u.File}");
        }
        live.Feed(u.Lines);

        // An import on screen is not moved by what the addon writes. The
        // lines are kept for the session, which is counted on the way back:
        // an armed one finds its zero then, at the same hit it would have.
        if (import == null && (fresh || u.Lines.Count > 0)) Recount();
        else DescribeStatus();
    }

    void OnFailed(Exception e)
    {
        error = e.Message;
        DescribeStatus();
    }

    // -------------------------------------------------------------- session

    /// <summary>Start arms; during a session it re-arms for the next pull.</summary>
    [RelayCommand]
    void Start()
    {
        // An import has no file behind it to measure: Back to live first.
        if (import != null) return;
        live.Start();
        drill = healDrill = null;
        LogSession("armed");
        Recount();
    }

    /// <summary>Cancel while armed, otherwise Pause or Resume.</summary>
    [RelayCommand]
    void Second()
    {
        if (import != null) return;
        bool armed = live.Session.Armed;
        if (armed) drill = healDrill = null;
        live.Second();
        LogSession(armed ? "cancelled" : live.Session.PausedAt != null ? "paused" : "resumed");
        Recount();
    }

    /// <summary>The session's exact times, so a report of a wrong figure can
    /// be replayed against the event file.</summary>
    void LogSession(string what)
    {
        var s = live.Session;
        Log.Write(FormattableString.Invariant(
            $"session {what}: armed {s.ArmedAt}, zero {s.StartedAt}, paused {s.PausedAt}, {s.Spans.Count} pauses before"));
    }

    // -------------------------------------------------------------- filters

    partial void OnSkillchainsChanged(bool value)
    {
        settings.Skillchains = value;
        settings.Save();
        // A drill-down into a skillchain has nothing left to show once the
        // chains are out, so it closes rather than sitting there empty.
        if (!value && drill is { } d && d.Action.StartsWith("Skillchain:", StringComparison.Ordinal)) drill = null;
        Recount();
        Compare.SharedChanged();
    }

    /// <summary>A display switch and nothing else: every key stays the real
    /// name, so no filter moves and no total can change.</summary>
    partial void OnHideNamesChanged(bool value)
    {
        settings.HideNames = value;
        settings.Save();
        Recount();
        Compare.SharedChanged();
    }

    /// <summary>A display switch on one chart.</summary>
    partial void OnGroupSmallLinesChanged(bool value)
    {
        settings.GroupSmallLines = value;
        settings.Save();
        Recount();
    }

    /// <summary>Folding the list away filters nothing, so nothing is recounted.</summary>
    partial void OnCharactersOpenChanged(bool value)
    {
        settings.CharactersOpen = value;
        settings.Save();
    }

    void SetIncluded(string name, bool included)
    {
        // Already so: the chip was only being brought up to date with a count.
        if (excluded.Contains(name) != included) return;
        if (included) excluded.Remove(name);
        else excluded.Add(name);
        ExclusionsChanged();
    }

    /// <summary>All and None act on the characters listed now, not on every
    /// name ever excluded.</summary>
    [RelayCommand]
    void IncludeAll()
    {
        foreach (var n in listed) excluded.Remove(n);
        ExclusionsChanged();
    }

    [RelayCommand]
    void IncludeNone()
    {
        foreach (var n in listed) excluded.Add(n);
        // A drill-down with nobody left in it closes rather than sitting
        // there empty. The heal one only when it was the healers who were
        // listed: from the Damage section its healer may not be among them.
        drill = null;
        if (IsHealing) healDrill = null;
        ExclusionsChanged();
    }

    void ExclusionsChanged()
    {
        settings.Excluded = [.. excluded.Order(StringComparer.Ordinal)];
        settings.Save();
        Recount();
    }

    // ---------------------------------------------------------- the two beats

    /// <summary>Counts the session as it stands and redraws every card from it.</summary>
    void Recount()
    {
        var now = Session.Now();
        var c = snapshot = Shown.Count(excluded, Skillchains, now);
        if (c.Latched) LogSession("started");
        View = SessionView.Of(Shown.Session, IsImportedNow);
        DescribeParse();
        Compare.SessionChanged(Shown.Session.StartedAt != null);

        DrawChips(c);
        DrawTiles(c);
        DrawLine(c);
        DrawBars(c);
        DrawActions(c);
        DrawDrill(c);
        DrawHealing(c);
        Tick(now);
        DescribeStatus();
    }

    /// <summary>4× a second: the clock, and everything divided by it.</summary>
    public void Tick() => Tick(Session.Now());

    void Tick(double now)
    {
        var s = Shown.Session;
        double ms = s.Elapsed(now), secs = ms / 1000;
        var agg = snapshot?.Totals;

        Light = View.Light;
        ClockText = SessionText.Stopwatch(s, now);
        ClockNote = SessionText.ClockNote(s);
        TotalNote = SessionText.TotalNote(s, agg is { Actors.Count: > 0 }, IsImportedNow);

        // The party's figure and every character's move together or not at
        // all: one falling past a column that stood still would be two right
        // numbers from two different moments.
        DpsText = Format.Num(agg != null && secs > 0 ? agg.Total / secs : 0, 1);
        foreach (var row in Actors) row.Dps = Format.Num(secs > 0 ? row.Total / secs : 0, 1);

        // Healing per second is over the same clock, so it falls beside the
        // DPS it is read against.
        var healing = snapshot?.Healing;
        HealTotalNote = SessionText.TotalNote(s, healing is { Actors.Count: > 0 }, IsImportedNow, what: "healing");
        HpsText = Format.Num(healing != null && secs > 0 ? healing.Total / secs : 0, 1);
        foreach (var row in Healers) row.Hps = Format.Num(secs > 0 ? row.Total / secs : 0, 1);

        // The chart's live edge follows the clock; held, it stays where it is.
        if (s.Running) Edge = ms;
    }

    void DescribeStatus()
    {
        // An import is what the line is about while one is on screen,
        // whatever the event folder is doing. It was never read line by
        // line, so it has no line count.
        if (import != null)
        {
            int rows = import.Reader.Events.Count;
            StatusLight = StatusLight.Held;
            StatusFile = "imported" + Dot + importName;
            StatusDetail = Format.Int(rows) + " event" + (rows == 1 ? "" : "s") + Dot +
                           SessionText.Status(import.Session, imported: true);
            return;
        }
        if (error is not null)
        {
            StatusLight = StatusLight.Error;
            StatusFile = "can't read the event file";
            StatusDetail = error;
            return;
        }
        if (!hasFile)
        {
            StatusLight = StatusLight.Waiting;
            StatusFile = "waiting for the addon";
            StatusDetail = Directory.Exists(feed.Directory)
                ? $"no event file in {feed.Directory} yet. Is VibeXI loaded in game?"
                : $"{feed.Directory} doesn't exist yet. It appears when VibeXI first runs.";
            return;
        }

        int events = Shown.Reader.Events.Count;
        StatusLight = View.Light switch
        {
            SessionLight.Armed => StatusLight.Armed,
            SessionLight.Live => StatusLight.Live,
            SessionLight.Held => StatusLight.Held,
            _ => StatusLight.Idle,
        };
        StatusFile = Shown.File ?? "";
        StatusDetail = Format.Int(events) + " event" + (events == 1 ? "" : "s") + Dot +
                       Format.Int(Shown.Lines) + " lines" + Dot + SessionText.Status(Shown.Session);
    }

    // ------------------------------------------------------- for Compare

    /// <summary>
    /// What is on screen, as the text of an exported parse, for Compare's
    /// Use current; null before its clock has started. A copy: the session
    /// goes on being measured, and a running clock is frozen in the copy
    /// alone. An import on screen is taken under its own file's name.
    /// </summary>
    internal ParseText? Current()
    {
        var now = Session.Now();
        return CompareSheet.Snapshot(Shown.Reader, Shown.Session, Shown.File, now) is { } text
            ? new ParseText(import != null ? importName : "Current session " + Format.Clock(now), text)
            : null;
    }

    // ------------------------------------------------- names and colours

    Roster Roster => Shown.Reader.Roster;

    /// <summary>
    /// The colour a character is drawn in, everywhere: their job's, shaded if
    /// someone slotted before them has the same job. A job with no colour, or
    /// a character with no job on record, takes their own slot's colour.
    /// </summary>
    Color ColorOf(string name) => ColorOf(name, AppTheme.IsDark);

    /// <summary>
    /// The same character on a floating panel. A panel's backdrop is a dark
    /// tint whatever the theme, so it always takes the dark set: under the
    /// light theme the main window's colours are graded for a white page and
    /// would sink into it.
    /// </summary>
    Color PanelColorOf(string name) => ColorOf(name, dark: true);

    Color ColorOf(string name, bool dark)
    {
        if (AppTheme.Job(Roster.JobOf(name)?.Main, dark) is not Color job) return AppTheme.Series(Shown.Cast.SlotOf(name), dark);
        var (r, g, b) = Shades.Step((job.R, job.G, job.B), Shown.Cast.VariantOf(name), !dark);
        return Color.FromRgb(r, g, b);
    }

    static readonly Dictionary<Color, SolidColorBrush> Solids = [];

    internal static SolidColorBrush Solid(Color c)
    {
        if (Solids.TryGetValue(c, out var b)) return b;
        b = new SolidColorBrush(c);
        b.Freeze();
        return Solids[c] = b;
    }

    Brush SwatchOf(string name) => Solid(ColorOf(name));

    /// <summary>
    /// The name as drawn. Every place that puts a character in front of the
    /// user goes through here, and nothing else does: a name stays real
    /// wherever it is a key (the exclusions, the drill-down, a colour, the
    /// count itself). If a total ever moves when names are hidden, something
    /// has started keying on a drawn name.
    /// </summary>
    string NameOf(string name) => HideNames && Shown.Cast.AliasOf(name) is { } alias ? alias : name;

    /// <summary>True when the name drawn for this character is their job,
    /// which is when printing the job again beside it would be noise.</summary>
    bool Aliased(string name) => HideNames && Shown.Cast.AliasOf(name) != null;

    /// <summary>The job that rides beside a name, or "" when the name is the job.</summary>
    string JobBadge(string name) => Aliased(name) ? "" : Roster.JobLabel(name);

    // ----------------------------------------------------------------- chips

    /// <summary>
    /// Whoever the section on screen is about: the characters who dealt
    /// damage, or the ones who healed. One list of exclusions serves both,
    /// by name, so someone switched off in one section is off in the other.
    /// </summary>
    void DrawChips(Snapshot c)
    {
        listed = IsHealing ? c.Healers.Select(a => a.Name).ToList() : c.Listed.Select(a => a.Name).ToList();
        Rows.Sync(Chips, listed, n => n, n => new ChipRow(n) { Flipped = SetIncluded }, (row, n) =>
        {
            bool included = !excluded.Contains(n);
            var full = Roster.JobTitle(n);
            row.Name = NameOf(n);
            row.Job = JobBadge(n);
            row.Swatch = SwatchOf(n);
            row.Included = included;
            row.Tip = (included ? "Exclude " : "Include ") + NameOf(n) + (full.Length > 0 ? Dot + full : "");
        });

        if (listed.Count == 0)
        {
            CharactersLabel = "Characters";
            CharactersHint = "";
            return;
        }
        // Read while the list is folded away, so an exclusion is never
        // invisible. A long list stops naming names; the count is what matters.
        var off = listed.Where(excluded.Contains).ToList();
        CharactersLabel = $"Characters {listed.Count - off.Count}/{listed.Count}";
        CharactersHint = off.Count == 0 ? "all included"
            : off.Count > 4 ? off.Count + " excluded"
            : off.Count + " excluded: " + string.Join(", ", off.Select(NameOf));
    }
}
