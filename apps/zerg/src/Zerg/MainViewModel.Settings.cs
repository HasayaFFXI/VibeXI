using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zerg.Core;

namespace Zerg;

// The Settings page: the folder the event files are looked for in, the keys
// that switch click-through, the pop-outs' default opacity (which is in
// Panels), the draw frequency, how the tables' rows are shaded, the low
// accuracy mark's threshold, the theme, and the colours of Compare's two
// runs. It takes the place of the section on screen, as View and Compare
// do, with the session still measured underneath. It is somewhere to go and
// come back from: the next start opens on the section that was left for
// it, not on the page.
public sealed partial class MainViewModel
{
    public const string SettingsSection = "Settings";

    /// <summary>
    /// Something to do once a control has stood still: a slider reports
    /// every step of a drag and a colour picker every point its marker
    /// passes, and the settings file is not written for each. What is on
    /// screen has followed already; this is the save, and whatever else is
    /// only worth doing once. Each call puts the moment off again.
    ///
    /// <para>Nothing is lost if Zerg closes first: the value is in the
    /// settings by then, and closing saves them.</para>
    /// </summary>
    static void Settle(ref DispatcherTimer? timer, Action done)
    {
        if (timer is null)
        {
            var made = new DispatcherTimer(DispatcherPriority.Background, Dispatcher.CurrentDispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(400),
            };
            made.Tick += (_, _) =>
            {
                made.Stop();
                done();
            };
            timer = made;
        }
        timer.Stop();
        timer.Start();
    }

    /// <summary>The section the page was opened over, to go back to.</summary>
    string before = DamageSection;

    /// <summary>The Settings page is on screen. Setting it opens the page, or
    /// goes back to the section it was opened over.</summary>
    public bool IsSettings
    {
        get => Section == SettingsSection;
        set
        {
            if (value == IsSettings) return;
            if (value) before = Section;
            Section = value ? SettingsSection : before;
        }
    }

    [RelayCommand]
    void CloseSettings() => IsSettings = false;

    // ------------------------------------------------------ the events folder

    /// <summary>The folder in the settings, or the addon's own when none was chosen.</summary>
    internal static string FolderIn(Settings settings) =>
        string.IsNullOrWhiteSpace(settings.EventsDir) ? AppInfo.DefaultEventsDir : settings.EventsDir;

    /// <summary>The folder being followed.</summary>
    [ObservableProperty] private string eventsFolder = "";
    /// <summary>What is in it: how many event files, and the one followed.</summary>
    [ObservableProperty] private string eventsFolderNote = "";
    /// <summary>Said only while the folder followed is not the one chosen
    /// here: it came from the command line, for this run.</summary>
    [ObservableProperty] private string eventsFolderSource = "";

    /// <summary>Picks a folder, opening at the one given; null if the player
    /// cancelled. And asks a question that OK or Cancel answers (a headline,
    /// then a sentence). The main window supplies both.</summary>
    public Func<string, string?>? FolderPicker { get; set; }
    public Func<string, string, bool>? Asker { get; set; }

    [RelayCommand]
    void BrowseFolder()
    {
        if (FolderPicker?.Invoke(EventsFolder) is { } dir) Watch(dir);
    }

    /// <summary>There is something for Use default to change: the folder
    /// followed, or the one saved.</summary>
    bool FolderChosen => settings.EventsDir != null || !SameFolder(feed.Directory, AppInfo.DefaultEventsDir);

    [RelayCommand(CanExecute = nameof(FolderChosen))]
    void DefaultFolder() => Watch(AppInfo.DefaultEventsDir);

    /// <summary>
    /// Follows another folder, and saves it as the one to follow from now on.
    /// A new folder is a new event file, so nothing carries over: the session
    /// ends as it does when a newer file appears. That is asked about first
    /// when there is a session to lose.
    /// </summary>
    void Watch(string dir)
    {
        dir = Path.GetFullPath(dir);
        bool moved = !SameFolder(dir, feed.Directory);
        var s = live.Session;
        if (moved && (s.Armed || s.StartedAt != null) && Asker?.Invoke(
                "Change the events folder and end this session?",
                "The session being measured belongs to the event file in the folder followed now. " +
                "Changing the folder ends it, and what it measured is lost unless it was exported.") != true)
            return;

        settings.EventsDir = SameFolder(dir, AppInfo.DefaultEventsDir) ? null : dir;
        settings.Save();
        if (moved)
        {
            Log.Write($"events dir {dir}");
            feed.Watch(dir);
            // Now, not when the first poll of the new folder lands: it may
            // find no file there, and the old one's rows would stay on screen.
            live.Unfollow();
            error = null;
            hasFile = false;
            if (!Viewing) drill = healDrill = null;
            Recount();
        }
        DescribeFolder();
    }

    void DescribeFolder()
    {
        var dir = feed.Directory;
        EventsFolder = dir;
        EventsFolderNote = Contents(dir);
        EventsFolderSource = SameFolder(dir, FolderIn(settings)) ? ""
            : "Followed for this run only, from the --events-dir option. The folder chosen here is " +
              FolderIn(settings) + ".";
        DefaultFolderCommand.NotifyCanExecuteChanged();
    }

    static string Contents(string dir)
    {
        try
        {
            if (!Directory.Exists(dir))
                return SameFolder(dir, AppInfo.DefaultEventsDir)
                    ? "This folder doesn't exist yet. It appears when VibeXI first runs."
                    : "This folder doesn't exist.";
            int files = Directory.EnumerateFiles(dir).Count(f => f.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase));
            return files == 0
                ? "No event files in this folder yet."
                : Format.Int(files) + (files == 1 ? " event file" : " event files") + " in this folder; the newest is " +
                  EventFiles.Newest(dir)?.Name + ".";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return "This folder can't be read: " + e.Message;
        }
    }

    static bool SameFolder(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b),
                      StringComparison.OrdinalIgnoreCase);

    // ------------------------------------------------- the click-through hot key

    /// <summary>The chord in the settings: what switches click-through,
    /// whenever Windows will give it to Zerg.</summary>
    KeyChord chord = KeyChord.OrDefault(null);

    /// <summary>Asks Windows for a chord in place of the one held, and says
    /// whether it was given; null only lets go. The main window supplies it:
    /// a hot key is registered against a window.</summary>
    public Func<KeyChord?, bool>? Binder { get; set; }

    /// <summary>The chord, as shown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotKeyFace))]
    private string hotKeyText = KeyChord.Default;

    /// <summary>The next keys pressed on the page's button are the new chord.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotKeyFace))]
    private bool recording;

    /// <summary>Why the chord is not in use, why the keys pressed were not
    /// taken, or, while waiting for keys, what to press.</summary>
    [ObservableProperty] private string hotKeyNote = "";
    /// <summary>The note is about something that went wrong.</summary>
    [ObservableProperty] private bool hotKeyNoteBad;

    void Say(string text, bool bad = true)
    {
        HotKeyNote = text;
        HotKeyNoteBad = bad && text.Length > 0;
    }

    /// <summary>What the button that takes a new chord reads.</summary>
    public string HotKeyFace => Recording ? "Press the new keys…" : HotKeyText;

    /// <summary>Takes the chord in the settings. Once the main window has its handle.</summary>
    public void BindHotKey()
    {
        if (KeyChord.Parse(settings.ClickThroughKey) is null)
            Log.Write($"settings: clickThroughKey \"{settings.ClickThroughKey}\" is not a key chord; using {chord}");
        Say(Hold(chord) ? "" : Taken(chord) + NoKey);
    }

    /// <summary>
    /// The page's button was pressed: the keys pressed next are read as the
    /// new chord. The one in use is let go of first, or pressing it again
    /// would switch click-through instead of being read.
    /// </summary>
    public void BeginHotKey()
    {
        if (Recording) return;
        Recording = true;
        // A chord another program holds goes to that program and never
        // arrives here, so nothing can be said about it once it is pressed.
        Say("Waiting for the new keys. Esc cancels. If nothing happens when you press them, another program " +
            "already has those keys.", bad: false);
        Binder?.Invoke(null);
    }

    /// <summary>The keys pressed while recording; null when they are not a
    /// chord Zerg takes, and then it goes on waiting for one.</summary>
    public void TakeHotKey(KeyChord? pressed)
    {
        if (!Recording) return;
        if (pressed is null)
        {
            Say("Not taken: hold Ctrl, Alt or Win and press a letter, a digit or F1 to F24. Esc cancels.");
            return;
        }
        Recording = false;
        Use(pressed);
    }

    /// <summary>Esc, or the button lost the keyboard: the chord stays as it was.</summary>
    public void CancelHotKey()
    {
        if (!Recording) return;
        Recording = false;
        Say(Hold(chord) ? "" : Taken(chord) + NoKey);
    }

    [RelayCommand]
    void DefaultHotKey()
    {
        Recording = false;
        Use(KeyChord.OrDefault(null));
    }

    /// <summary>
    /// Changes the chord, if Windows gives the new one to Zerg. A chord
    /// another program holds cannot be had: the old one is taken back and
    /// stays the setting, so a chord that does nothing is never saved.
    /// </summary>
    void Use(KeyChord wanted)
    {
        if (Hold(wanted))
        {
            chord = wanted;
            settings.ClickThroughKey = wanted.ToString();
            settings.Save();
            Say("");
        }
        else if (wanted != chord && Hold(chord)) Say(Taken(wanted) + $" Still using {chord}.");
        else Say(Taken(wanted) + NoKey);
        HotKeyText = chord.ToString();
    }

    bool Hold(KeyChord keys)
    {
        bool held = Binder?.Invoke(keys) == true;
        Panels.HotKey = held ? keys.ToString() : null;
        Log.Write(held ? $"hot key {keys}: click-through panels"
                       : $"hot key {keys}: taken by another program; click-through is on the tray menu");
        return held;
    }

    static string Taken(KeyChord keys) => $"{keys} is taken by another program.";

    const string NoKey = " Click-through has no hot key until another is chosen; the Click-through button and the " +
                         "tray menu still switch it.";

    // ---------------------------------------- row shading, the low accuracy mark
    //
    // How the tables are drawn, not what is in them: nothing is counted
    // again when one of these changes, and no row is made again. Each card
    // hands the three down to its rows (Views/Shading), and a row's own
    // triggers do the rest. The Settings page has two switches and a
    // slider for them.

    /// <summary>A character's row is shaded in their colour to the length
    /// of their share. Off, the row is plain and a small bar stands beside
    /// the share's figure.</summary>
    [ObservableProperty] private bool shadeCharacters = true;

    /// <summary>The rows under a character are shaded to their share.</summary>
    [ObservableProperty] private bool shadeActions;

    /// <summary>The low accuracy mark's threshold, in percent, 50 to 100.</summary>
    [ObservableProperty] private int lowAccuracy = LowMark.Installed;

    partial void OnShadeCharactersChanged(bool value)
    {
        settings.ShadeCharacters = value;
        settings.Save();
        Log.Write("shade characters " + (value ? "on" : "off"));
        // Compare's rows are shaded by the same switch (its runs' bands).
        Compare.ShadingChanged();
    }

    partial void OnShadeActionsChanged(bool value)
    {
        settings.ShadeActions = value;
        settings.Save();
        Log.Write("shade actions " + (value ? "on" : "off"));
    }

    partial void OnLowAccuracyChanged(int value)
    {
        int v = LowMark.Clamp(value);
        if (v != value)
        {
            LowAccuracy = v;
            return;
        }
        settings.LowAccuracy = v;
        // The marks follow every step of the slider; the file is written
        // once it settles.
        Settle(ref lowSaver, () =>
        {
            settings.Save();
            Log.Write($"low accuracy under {settings.LowAccuracy}%");
        });
    }

    DispatcherTimer? lowSaver;

    // ------------------------------------------- the colours of Compare's runs
    //
    // Each run's colour is the user's to set: as it is written ("#3987E5"),
    // or null for the installed colour of the theme in use. The Settings
    // page has a well for each, which opens a picker, and "Use default".
    //
    // A change does two things at two speeds. The brushes everything of a
    // run is drawn with follow at once (AppTheme.Runs: a badge, a band, a
    // mark, the columns of a chart; nothing is counted, measured or laid
    // out), and so does what the page says of the colour (its code, how
    // strong its band comes out). What cannot follow a brush is done in
    // RunsSettled, once the colour has stood still (a picker reports every
    // point its marker is dragged through): the file is saved, and Compare
    // hands its chart of both runs the colours again.

    /// <summary>Run A's colour as it is written, or null: as installed.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DefaultRunsCommand))]
    private string? runA;

    /// <summary>Run B's colour as it is written, or null: as installed.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DefaultRunsCommand))]
    private string? runB;

    partial void OnRunAChanged(string? value) => UseRuns();
    partial void OnRunBChanged(string? value) => UseRuns();

    void UseRuns()
    {
        // Kept written the one way; what is not a colour is "as installed".
        string? a = RunColours.Normal(RunA), b = RunColours.Normal(RunB);
        if (a != RunA)
        {
            RunA = a;
            return;
        }
        if (b != RunB)
        {
            RunB = b;
            return;
        }
        AppTheme.Runs(a, b);
        (settings.RunA, settings.RunB) = (a, b);
        RunsShown();
        Settle(ref runsSaver, RunsSettled);
    }

    DispatcherTimer? runsSaver;

    /// <summary>The runs' colours have changed and are staying: saved, and
    /// handed to what is drawn from a colour and not from a brush.</summary>
    void RunsSettled()
    {
        settings.Save();
        Log.Write($"run colours A {settings.RunA ?? "as installed"}, B {settings.RunB ?? "as installed"}");
        Compare.Recolour();
    }

    /// <summary>The colour run A is drawn in, as it is written ("#3987E5"):
    /// the one set, or the installed one of the theme in use. What its well
    /// on the Settings page reads.</summary>
    public string RunACode => CodeOf(a: true);

    /// <summary>The same for run B.</summary>
    public string RunBCode => CodeOf(a: false);

    static string CodeOf(bool a)
    {
        var c = AppTheme.Run(a);
        return RunColours.Hex((c.R, c.G, c.B));
    }

    /// <summary>How strong run A's band comes out behind a row, in whole
    /// percent, in the theme in use.</summary>
    public int RunABand => AppTheme.RunBand(a: true);

    /// <summary>The same for run B.</summary>
    public int RunBBand => AppTheme.RunBand(a: false);

    /// <summary>The line under the page's preview of a Compare row.</summary>
    public string RunBandsNote => $"as set; the bands come out at {RunABand}% and {RunBBand}%";

    /// <summary>What the page says of the two colours has changed: one was
    /// set, or the theme was (an installed colour and a band's strength are
    /// the theme's).</summary>
    void RunsShown()
    {
        OnPropertyChanged(nameof(RunACode));
        OnPropertyChanged(nameof(RunBCode));
        OnPropertyChanged(nameof(RunABand));
        OnPropertyChanged(nameof(RunBBand));
        OnPropertyChanged(nameof(RunBandsNote));
    }

    /// <summary>Both runs back to their installed colours. Nothing to do while both are.</summary>
    [RelayCommand(CanExecute = nameof(RunsAreSet))]
    void DefaultRuns() => (RunA, RunB) = (null, null);

    bool RunsAreSet() => RunA != null || RunB != null;

    // ---------------------------------------------------- the page's pictures
    //
    // Four of the page's rows show a small picture of what they set
    // (Views/Sample), made of the tables' own parts with made-up figures.
    // The two characters in them are nobody's: a samurai and a monk, in
    // those jobs' colours, shaded by the rule every row is shaded by. In
    // the picture of a pop-out they are as a panel has them: the dark
    // theme's colours, at a strip's strength.

    static Color SampleColour(int which, bool dark) =>
        AppTheme.Job(which == 0 ? "SAM" : "MNK", dark) ?? AppTheme.Series(which, dark);

    /// <summary>The first made-up character's colour, and the second's.</summary>
    public Brush SampleSwatch1 => Solid(SampleColour(0, AppTheme.IsDark));
    public Brush SampleSwatch2 => Solid(SampleColour(1, AppTheme.IsDark));

    /// <summary>The shade behind each one's row, as a table has it.</summary>
    public Brush SampleShade1 => Shade(SampleColour(0, AppTheme.IsDark), ShadeOn.Row, AppTheme.IsDark);
    public Brush SampleShade2 => Shade(SampleColour(1, AppTheme.IsDark), ShadeOn.Row, AppTheme.IsDark);

    /// <summary>Each one's colour on a floating panel, and the shade
    /// behind their line of a strip there.</summary>
    public Brush SampleEdge1 => Solid(SampleColour(0, dark: true));
    public Brush SampleEdge2 => Solid(SampleColour(1, dark: true));
    public Brush SampleStrip1 => Shade(SampleColour(0, dark: true), ShadeOn.Strip, dark: true);
    public Brush SampleStrip2 => Shade(SampleColour(1, dark: true), ShadeOn.Strip, dark: true);

    /// <summary>The theme changed: whatever the page draws from a colour
    /// of the theme's is handed over again. The pictures' colours and
    /// shades, and what is said of the two runs (an installed colour and a
    /// band's strength are the theme's).</summary>
    void ThemeShown()
    {
        OnPropertyChanged(nameof(SampleSwatch1));
        OnPropertyChanged(nameof(SampleSwatch2));
        OnPropertyChanged(nameof(SampleShade1));
        OnPropertyChanged(nameof(SampleShade2));
        RunsShown();
    }

    // ------------------------------------------------------ the draw frequency

    DispatcherTimer? drawSaver;

    /// <summary>How many times a second <see cref="Tick"/> is called: the
    /// main window keeps that beat, and takes up a change at once.</summary>
    [ObservableProperty] private int drawFrequency = DrawRate.Initial;

    partial void OnDrawFrequencyChanged(int value)
    {
        int v = DrawRate.Clamp(value, settings.DrawFrequency);
        if (v != value)
        {
            DrawFrequency = v;
            return;
        }
        settings.DrawFrequency = v;
        // A slider reports every step of a drag; the file is written once it settles.
        drawSaver ??= new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (_, _) =>
        {
            drawSaver!.Stop();
            settings.Save();
            Log.Write($"draw frequency {settings.DrawFrequency}/s");
        }, Dispatcher.CurrentDispatcher);
        drawSaver.Stop();
        drawSaver.Start();
    }
}
