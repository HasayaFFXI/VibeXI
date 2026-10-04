using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zerg.Core;

namespace Zerg;

// The Settings page: the folder the event files are looked for in, the keys
// that switch click-through, and the theme. It takes the place of the section
// on screen, as View and Compare do, with the session still measured
// underneath. It is somewhere to go and come back from: the next start opens
// on the section that was left for it, not on the page.
public sealed partial class MainViewModel
{
    public const string SettingsSection = "Settings";

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
}
