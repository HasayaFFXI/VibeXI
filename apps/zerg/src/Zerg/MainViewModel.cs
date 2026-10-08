using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zerg.Core;

namespace Zerg;

/// <summary>What the status dot shows: the session's state, unless the event
/// file itself is the news.</summary>
public enum StatusLight { Idle, Armed, Live, Held, Waiting, Error }

/// <summary>
/// The main window's state: the session and its two buttons, the filters, and
/// everything the Damage and Healing sections show, of the session or of the
/// parse opened in the View section. The Compare section has a view model of
/// its own (<see cref="Compare"/>).
///
/// <para>Two things drive it. A poll that brought new lines, or any change to
/// a filter or the session, makes a new count and redraws every card from it
/// (<see cref="Recount"/>). The draw beat, at the draw frequency set on the
/// Settings page, rewrites only what moves with nothing happening: the
/// elapsed time, every DPS, which falls as the time under it grows, and the
/// live edge of the cumulative charts (<see cref="Tick"/>).</para>
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
    /// <summary>What the last count was of: the session, or a saved parse.</summary>
    Tracker? drawn;
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
    [NotifyPropertyChangedFor(nameof(IsDamage), nameof(IsHealing), nameof(IsView), nameof(IsCompare), nameof(ShowsParse),
                              nameof(FillsPage), nameof(HideNamesTip), nameof(IsSettings), nameof(ImportTip))]
    private string section;

    public const string DamageSection = "Damage", HealingSection = "Healing", ViewSection = "View", CompareSection = "Compare";

    /// <summary>The damage tiles and cards are on screen: the session's, or
    /// those of the parse the View section has open.</summary>
    public bool IsDamage => Section == DamageSection || (Viewing && ViewMode == DamageSection);
    public bool IsHealing => Section == HealingSection || (Viewing && ViewMode == HealingSection);
    public bool IsView => Section == ViewSection;
    public bool IsCompare => Section == CompareSection;
    /// <summary>A parse's tiles and cards are on screen, either side of it.</summary>
    public bool ShowsParse => IsDamage || IsHealing;
    /// <summary>The section on screen is made of panes, which take exactly
    /// what the window leaves under whatever stands above them: a parse's
    /// four, or Compare's. The Settings page, and the View section with
    /// nothing open, are as tall as they are.</summary>
    public bool FillsPage => ShowsParse || IsCompare;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SkillchainsTip))]
    private bool skillchains;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HideNamesTip))]
    private bool hideNames;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GroupTip))]
    private bool groupSmallLines;

    /// <summary>The characters' line is opened onto every chip, on as many
    /// lines as they take; otherwise it is one line of as many as fit. The
    /// chip after the chips flips it.</summary>
    [ObservableProperty] private bool charactersOpen;

    public string SkillchainsTip => Skillchains
        ? "Skillchains counted: credited to the character whose weaponskill closed each one. Click to leave them out."
        : "Skillchains left out of every total. Click to count them again.";

    /// <summary>Over Compare the switch spares nobody: its two runs may be
    /// two people's, and neither is "you". A parse in the View section keeps
    /// the name of whoever recorded it.</summary>
    public string HideNamesTip => (HideNames, IsCompare, Viewing) switch
    {
        (true, true, _) => "Names hidden: every character in both runs is drawn as their job. Click to show them again.",
        (true, _, true) => "Names hidden: every character but the one who recorded the parse is drawn as their job. " +
                           "Click to show them again.",
        (true, _, _) => "Names hidden: every character but you is drawn as their job. Click to show them again.",
        (false, true, _) => "Replace every character's name with their job, for a screenshot or a stream",
        (false, _, _) => "Replace every other character's name with their job, for a screenshot or a stream",
    };

    /// <summary>What the grouping switch does, and, while there is a line
    /// that stands for several, who is in it: the chart names that line by
    /// how many they are, and nothing else on screen says who.</summary>
    public string GroupTip => GroupSmallLines
        ? "Characters under 5% of the party's damage share one \"others\" line. Your own line is never grouped. " +
          "Click to give everyone a line." +
          (GroupMembers is { Length: > 0 } who ? "\n\nIn that line now: " + who + "." : "")
        : "Every character has their own line. Click to group everyone under 5% of the party's damage into one.";

    // --------------------------------------------------- session and status

    [ObservableProperty] private SessionView view;
    [ObservableProperty] private StatusLight statusLight = StatusLight.Waiting;
    [ObservableProperty] private string statusFile = "waiting for the addon";
    [ObservableProperty] private string statusDetail = "";

    /// <summary>The right-hand end of the status line: how many cards are
    /// floating over the game, and whether clicks pass through them. Empty
    /// with none out, and then that end of the line is not drawn.</summary>
    public string StatusPanels
    {
        get
        {
            int n = Panels.OutCount;
            if (n == 0) return "";
            return (n == 1 ? "1 panel out" : n + " panels out") + (Panels.ClickThrough ? ", click-through" : "");
        }
    }

    /// <summary>The last thing on the status line, in the window's lower
    /// right-hand corner: which Zerg this is, "v0.2.0".</summary>
    public string StatusVersion { get; } = "v" + AppInfo.Version;

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
        Panels.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(PanelSet.OutCount) or nameof(PanelSet.ClickThrough))
                OnPropertyChanged(nameof(StatusPanels));
        };

        theme = settings.Theme;
        // Not the View section: no parse is open yet, and Import is its way in.
        section = settings.Section is HealingSection or CompareSection ? settings.Section : DamageSection;
        viewMode = settings.ViewMode == HealingSection ? HealingSection : DamageSection;
        skillchains = settings.Skillchains;
        hideNames = settings.HideNames;
        groupSmallLines = settings.GroupSmallLines;
        charactersOpen = settings.CharactersOpen;
        shadeCharacters = settings.ShadeCharacters;
        shadeActions = settings.ShadeActions;
        lowAccuracy = LowMark.Clamp(settings.LowAccuracy);
        // As the file has them; AppTheme was given the same two at startup.
        runA = RunColours.Normal(settings.RunA);
        runB = RunColours.Normal(settings.RunB);
        drawFrequency = DrawRate.Clamp(settings.DrawFrequency);
        chord = KeyChord.OrDefault(settings.ClickThroughKey);
        hotKeyText = chord.ToString();
        DescribeFolder();
        view = SessionView.Of(live.Session);
        Compare = new CompareViewModel(settings, this) { Active = IsCompare };
        ReadLayouts();

        feed.Updated += OnUpdated;
        feed.Failed += OnFailed;
        // A chart's inks restyle themselves; the colours that stand for
        // characters are handed over, so they are handed over again.
        AppTheme.Changed += () =>
        {
            if (snapshot != null) Recount();
            Compare.SharedChanged();
            ThemeShown();
        };
        Recount();
    }

    // --------------------------------------------------------------- theme

    /// <summary>Applied the moment it changes (the theme switches live) and saved.</summary>
    partial void OnThemeChanged(string value)
    {
        settings.Theme = value;
        AppTheme.Apply(settings.ThemeChoice);
        settings.Save();
        Log.Write("theme " + value);
    }

    /// <summary>
    /// The characters listed follow the section: who dealt damage, or who
    /// healed. So this is a count again, though no figure moves. Compare
    /// lists nobody, and draws whatever changed while it was off screen.
    /// Into the View section, or out of it, what is counted changes hands
    /// between the session and the parse open there. The Settings page is
    /// not saved as the section: the next start opens on the one it was
    /// opened over.
    /// </summary>
    partial void OnSectionChanged(string value)
    {
        if (value == SettingsSection)
        {
            // What is in the folder may have changed since the page was last open.
            DescribeFolder();
        }
        else
        {
            CancelHotKey();
            settings.Section = value;
            settings.Save();
        }
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
            // A new file is a new load of the addon or a new character: nothing carries
            // over, and a clock from the old one would measure the wrong session.
            live.Follow(u.File);
            if (!Viewing) drill = healDrill = null;
            Log.Write($"following {u.File}");
            // The Settings page says which file is newest in the folder.
            if (IsSettings) DescribeFolder();
        }
        live.Feed(u.Lines);

        // A saved parse on screen is not moved by what the addon writes. The
        // lines are kept for the session, which is counted on the way back:
        // an armed one finds its zero then, at the same hit it would have.
        if (!Viewing && (fresh || u.Lines.Count > 0)) Recount();
        else DescribeStatus();
    }

    void OnFailed(Exception e)
    {
        error = e.Message;
        DescribeStatus();
    }

    // -------------------------------------------------------------- session

    /// <summary>
    /// How long Confirm waits to be pressed. After that the question is taken
    /// back and the pull kept: the second button is Cancel while it is up,
    /// and a Pause that cannot be reached is worse than a Restart that has
    /// to be pressed again.
    /// </summary>
    static readonly TimeSpan AskFor = TimeSpan.FromSeconds(5);
    DispatcherTimer? askTimer;

    /// <summary>
    /// Start arms; during a session it re-arms for the next pull. Over a
    /// measurement the first press only asks, and the pair of buttons is
    /// the answer: this one says Confirm, the second Cancel.
    /// </summary>
    [RelayCommand]
    void Start()
    {
        // A saved parse has no file behind it to measure: that is done from
        // the session's own sections.
        if (Viewing) return;
        bool asking = live.Asking;
        if (!live.First())
        {
            // Still asking: the second half of a double-click is no answer.
            if (asking) return;
            askTimer ??= new DispatcherTimer(AskFor, DispatcherPriority.Background, (_, _) => Lapse(),
                                             Dispatcher.CurrentDispatcher);
            askTimer.Stop();
            askTimer.Start();
            LogSession("restart asked");
            Recount();
            return;
        }
        askTimer?.Stop();
        drill = healDrill = null;
        LogSession("armed");
        Recount();
    }

    /// <summary>Nobody answered: the pull is kept and the pair is itself again.</summary>
    void Lapse()
    {
        askTimer?.Stop();
        // Answered since, or ended by a new event file.
        if (!live.Asking) return;
        live.Withdraw();
        LogSession("restart not confirmed");
        Recount();
    }

    /// <summary>Cancel while a restart is asked about or the session is
    /// armed, otherwise Pause or Resume.</summary>
    [RelayCommand]
    void Second()
    {
        if (Viewing) return;
        bool armed = live.Session.Armed, asking = live.Asking;
        if (armed) drill = healDrill = null;
        live.Second();
        if (asking) askTimer?.Stop();
        LogSession(asking ? "restart called off" : armed ? "cancelled"
                 : live.Session.PausedAt != null ? "paused" : "resumed");
        Recount();
    }

    /// <summary>The session's exact times, so a report of a wrong figure can
    /// be replayed against the event file.</summary>
    void LogSession(string what)
    {
        var s = live.Session;
        Log.Write(FormattableString.Invariant(
            $"session {what}: armed {s.ArmedAt}, zero {s.StartedAt}, paused {s.PausedAt}, ended {s.EndedAt}, {s.Spans.Count} pauses before"));
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

    /// <summary>How the chips are laid out filters nothing, so nothing is recounted.</summary>
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
        // A drill-down names a character and an action of what was on screen:
        // it closes when the session and a saved parse change places.
        if (!ReferenceEquals(drawn, Shown))
        {
            drawn = Shown;
            drill = healDrill = null;
        }
        var c = snapshot = Shown.Count(excluded, Skillchains, now);
        if (c.Latched) LogSession("started");
        View = SessionView.Of(Shown.Session, Viewing, live.Asking);
        DescribeParse();
        Compare.SessionChanged(live.Session.StartedAt != null);

        DrawChips(c);
        DrawTiles(c);
        DrawLine(c);
        DrawBars(c);
        DrawActions(c);
        DrawDrill(c);
        DrawHealing(c);
        Tick(now, damage: true, healing: true);
        DescribeStatus();
    }

    /// <summary>
    /// The main window is where it can be seen: not minimized, and not put
    /// away in the tray. The main window says so. DPS and HPS are printed
    /// nowhere else (a floating panel shows the clock and a total), so while
    /// this is false the beat leaves them alone.
    /// </summary>
    public bool Seen { get; set; } = true;

    /// <summary>
    /// At the draw frequency: the clock, everything divided by it, and the
    /// cumulative charts' live edge. Everything on screen that time alone
    /// moves, and nothing an event moves: that is a count's.
    ///
    /// <para>The rates are rewritten only while the tiles and tables that
    /// print them are on screen. WPF lays out text it is not showing, and
    /// thirty times a second that was 8% of a core for a window in the
    /// tray. So the beat rewrites the damage section's rates while that
    /// section is the one on screen, the healing section's while that one
    /// is, and neither while the window cannot be seen or shows something
    /// else. A count rewrites both whatever is on screen, and changing
    /// section is a count, so a section never comes up with figures older
    /// than the moment it came up.</para>
    /// </summary>
    public void Tick() => Tick(Session.Now(), damage: Seen && IsDamage, healing: Seen && IsHealing);

    void Tick(double now, bool damage, bool healing)
    {
        var s = Shown.Session;
        double ms = s.Elapsed(now), secs = ms / 1000;
        var agg = snapshot?.Totals;

        // The word beside the clock, and the clock's colour. A change only
        // when the session's state changes: on every other beat this is the
        // value it already had, and nothing is told or redrawn.
        Tag = View.Tag;
        ClockText = SessionText.Stopwatch(s, now);
        ClockNote = SessionText.ClockNote(s);
        TotalNote = SessionText.TotalNote(s, agg is { Actors.Count: > 0 }, Viewing);
        var heals = snapshot?.Healing;
        HealTotalNote = SessionText.TotalNote(s, heals is { Actors.Count: > 0 }, Viewing, what: "healing");

        if (damage)
        {
            // The party's figure and every character's move together or not
            // at all: one falling past a column that stood still would be
            // two right numbers from two different moments.
            DpsText = Format.Num(agg != null && secs > 0 ? agg.Total / secs : 0, 1);
            foreach (var row in Actors) row.Dps = Format.Num(secs > 0 ? row.Total / secs : 0, 1);
            // The Party line under the rows is the party's figure again.
            Party.Dps = DpsText;
        }
        if (healing)
        {
            // Healing per second is over the same clock as the damage's.
            HpsText = Format.Num(heals != null && secs > 0 ? heals.Total / secs : 0, 1);
            foreach (var row in Healers) row.Hps = Format.Num(secs > 0 ? row.Total / secs : 0, 1);
            HealParty.Hps = HpsText;
        }

        // The chart's live edge follows the clock; held, it stays where it
        // is. A chart that is not on screen does not redraw for it.
        if (s.Running) Edge = ms;
    }

    void DescribeStatus()
    {
        // A saved parse is what the line is about while one is on screen,
        // whatever the event folder is doing. It was never read line by
        // line, so it has no line count.
        if (Viewing)
        {
            int rows = viewed!.Reader.Events.Count;
            StatusLight = StatusLight.Held;
            StatusFile = "viewing" + Dot + viewedFile;
            StatusDetail = Format.Int(rows) + " event" + (rows == 1 ? "" : "s") + Dot +
                           SessionText.Status(viewed.Session, imported: true);
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
    /// The session, as the text of an exported parse, for Compare's Use
    /// current; null before its clock has started. A copy: the session goes
    /// on being measured, and a running clock is frozen in the copy alone.
    /// </summary>
    internal ParseText? Current()
    {
        var now = Session.Now();
        return CompareSheet.Snapshot(live.Reader, live.Session, live.File, now) is { } text
            ? new ParseText("Current session " + Format.Clock(now), text)
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

    // A row's shade: the character's colour at the strength that leaves
    // the quieter ink legible on it (Zerg.Core/RowShade has the rule).
    // Frozen and kept, as the solid brushes are: the same few brushes are
    // handed to every row on every count.

    /// <summary>What a shade lies on, which decides how strong it may be.</summary>
    enum ShadeOn { Row, Heading, Strip, PanelHeading }

    static readonly Dictionary<(Color Colour, ShadeOn On, bool Dark), SolidColorBrush> ShadeBrushes = [];

    static SolidColorBrush Shade(Color c, ShadeOn on, bool dark)
    {
        if (ShadeBrushes.TryGetValue((c, on, dark), out var b)) return b;
        static (byte, byte, byte) Rgb(Color x) => (x.R, x.G, x.B);
        // In a panel, against the panel's own backdrop and ink, which are
        // the same whatever the theme (RowShade.Strip).
        int percent = on == ShadeOn.Strip ? RowShade.Strip(Rgb(c))
            : on == ShadeOn.PanelHeading ? RowShade.PanelHeading(Rgb(c))
            : RowShade.Strength(Rgb(c), Rgb(AppTheme.Token(on == ShadeOn.Heading ? "Bg2" : "Bg1", dark)),
                                Rgb(AppTheme.Token("Text2", dark)),
                                dark ? RowShade.DarkTarget : RowShade.LightTarget, dark ? RowShade.DarkCap : RowShade.LightCap);
        // The strength is the brush's own, so whatever is filled with it
        // needs no opacity of its own.
        b = new SolidColorBrush(c) { Opacity = percent / 100.0 };
        b.Freeze();
        return ShadeBrushes[(c, on, dark)] = b;
    }

    /// <summary>The shade behind a character's row in the per-character tables.</summary>
    Brush ShadeOf(string name) => Shade(ColorOf(name), ShadeOn.Row, AppTheme.IsDark);

    /// <summary>The shade behind a character's heading in the Actions and
    /// Heals tables, which lies on the raised surface.</summary>
    Brush HeadingShadeOf(string name) => Shade(ColorOf(name), ShadeOn.Heading, AppTheme.IsDark);

    /// <summary>The same character's shade on a floating panel, behind
    /// their line of a strip.</summary>
    Brush PanelShadeOf(string name) => Shade(PanelColorOf(name), ShadeOn.Strip, dark: true);

    /// <summary>And behind their heading in the Actions and Heals panels,
    /// which lies on the panel's raised surface.</summary>
    Brush PanelHeadingShadeOf(string name) => Shade(PanelColorOf(name), ShadeOn.PanelHeading, dark: true);

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
        // Read while a chip is not on the line (more characters than fit on
        // it), so an exclusion is never invisible. A long list stops naming
        // names; the count is what matters.
        var off = listed.Where(excluded.Contains).ToList();
        CharactersLabel = $"Characters {listed.Count - off.Count}/{listed.Count}";
        CharactersHint = off.Count == 0 ? "all included"
            : off.Count > 4 ? off.Count + " excluded"
            : off.Count + " excluded: " + string.Join(", ", off.Select(NameOf));
    }
}
