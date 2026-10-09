using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zerg.Core;

namespace Zerg;

// Export, and the View section: a paused parse saved to a file another copy
// of Zerg can open, and one opened here to look through, its damage or its
// healing.
//
// Viewing takes nothing away. The session being followed is left whole, and
// goes on being fed by every poll while the parse is on screen: a clock that
// was running keeps running, since it is wall time, and the Damage and
// Healing sections return to it with everything the addon wrote meanwhile
// already read. Only what is counted and drawn changes hands, and only while
// the View section is the one on screen.
public sealed partial class MainViewModel
{
    /// <summary>How long the note about the last Export stays up.</summary>
    static readonly TimeSpan NoteFor = TimeSpan.FromSeconds(8);

    /// <summary>The parse opened in the View section, or null. Kept while
    /// another section is on screen, so coming back finds it still open.</summary>
    Tracker? viewed;
    /// <summary>The file it was opened from, by name.</summary>
    string viewedFile = "";
    DispatcherTimer? noteTimer;

    /// <summary>The View section is on screen with a parse open in it.</summary>
    bool Viewing => IsView && viewed != null;

    /// <summary>
    /// What is counted and drawn: the parse in the View section while that is
    /// on screen, otherwise the session. Everything that puts a figure, a
    /// name or a colour on screen reads this, so a saved parse is drawn by
    /// the code that draws the session, panels included, and the two cannot
    /// differ in how they count.
    /// </summary>
    Tracker Shown => Viewing ? viewed! : live;

    // ------------------------------------------------------ the View section

    /// <summary>A parse is open in the View section, whichever section is on screen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDamage), nameof(IsHealing), nameof(ShowsParse), nameof(FillsPage), nameof(HideNamesTip),
                              nameof(ImportTip))]
    private bool hasParse;

    /// <summary>Which side of the parse the View section shows: "Damage" or "Healing".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDamage), nameof(IsHealing))]
    private string viewMode;

    /// <summary>The characters listed follow the side on screen, as they
    /// follow the section.</summary>
    partial void OnViewModeChanged(string value)
    {
        settings.ViewMode = value;
        settings.Save();
        if (Viewing) Recount();
    }

    /// <summary>What the parse is called: its file's name.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImportTip))]
    private string viewedName = "";
    /// <summary>The event file the parse was recorded from, where it says.</summary>
    [ObservableProperty] private string viewedTip = "";
    [ObservableProperty] private string viewedStarted = "";
    [ObservableProperty] private string viewedLength = "";
    [ObservableProperty] private string viewedParty = "";
    [ObservableProperty] private string viewedSkipped = "";
    /// <summary>Why the last file would not open. What was open stays open.</summary>
    [ObservableProperty] private string viewError = "";
    /// <summary>A file is being held over the section's card.</summary>
    [ObservableProperty] private bool viewOver;

    /// <summary>Picks a parse off the disk, and picks where to save one
    /// (given the name to offer). The main window supplies both.</summary>
    public Func<ParseText?>? Picker { get; set; }
    public Func<string, string?>? Saver { get; set; }

    /// <summary>Opens an exported parse in the View section. A second one
    /// replaces the first.</summary>
    [RelayCommand]
    void OpenParse() => Read(() => Picker?.Invoke());

    /// <summary>
    /// Import, on the command bar: the way into the View section. It opens a
    /// parse and shows it. A parse left open while another section was on
    /// screen is gone back to, not asked for again; the section's own card
    /// replaces or closes it.
    /// </summary>
    [RelayCommand]
    void Import()
    {
        if (HasParse && !IsView || Read(() => Picker?.Invoke())) Section = ViewSection;
    }

    public string ImportTip => HasParse && !IsView
        ? $"Back to {ViewedName}, the parse open here. The session goes on being measured underneath"
        : "Open a parse exported from Zerg, to look through its damage and its healing. " +
          "The session goes on being measured underneath";

    /// <summary>A file dropped on the section's card.</summary>
    public void Take(string path) => Read(() => ParseDialog.Read(path));

    /// <summary>False when no file was chosen. A file that would not open
    /// is still an answer: the section's card says why.</summary>
    bool Read(Func<ParseText?> source)
    {
        try
        {
            if (source() is not { } file) return false;
            var parse = ParseFile.Import(file.Text);
            var info = CompareSheet.Describe(parse);
            viewed = Tracker.Of(parse);
            viewedFile = file.Name;
            ViewedName = ParseDialog.Title(file.Name);
            ViewedTip = parse.File ?? file.Name;
            (ViewedStarted, ViewedLength, ViewedParty, ViewedSkipped) = (info.Started, info.Length, info.Party, info.Skipped);
            ViewError = "";
            Log.Write($"viewing {file.Name}");
            // Counted before the cards come on screen, so they arrive drawn.
            Recount();
            HasParse = true;
        }
        catch (ParseImportException e)
        {
            ViewError = e.Message;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ViewError = "Could not open: " + e.Message;
        }
        return true;
    }

    /// <summary>Empties the View section. The session was never touched.</summary>
    [RelayCommand]
    void CloseParse()
    {
        if (viewed == null) return;
        viewed = null;
        viewedFile = "";
        ViewedName = ViewedTip = ViewedStarted = ViewedLength = ViewedParty = ViewedSkipped = ViewError = "";
        Log.Write("closed the viewed parse");
        HasParse = false;
        // The panels were showing it too: back to the session.
        Recount();
    }

    // ---------------------------------------------------------------- export

    /// <summary>The parse on screen is paused, so it can be saved.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private bool canExport;

    [ObservableProperty] private string exportTip = "";

    /// <summary>What the last Export did. Clears itself.</summary>
    [ObservableProperty] private string parseNote = "";
    /// <summary>The note is about something that went wrong.</summary>
    [ObservableProperty] private bool parseNoteBad;

    void Note(string text, bool bad = false)
    {
        noteTimer ??= new DispatcherTimer(NoteFor, DispatcherPriority.Background, (_, _) => Note(""),
                                          Dispatcher.CurrentDispatcher);
        noteTimer.Stop();
        ParseNote = text;
        ParseNoteBad = bad;
        if (text.Length > 0) noteTimer.Start();
    }

    /// <summary>Export follows the clock of whatever is on screen.</summary>
    void DescribeParse()
    {
        var s = Shown.Session;
        CanExport = Shown.CanExport;
        ExportTip = CanExport ? "Save this parse to a file another copy of Zerg can open with Import or in Compare"
            : s.StartedAt != null ? "Pause first — a parse is exported once its clock has stopped"
            : "Nothing to export yet — Start, then Pause, to export a pull";
    }

    /// <summary>
    /// Saves the paused parse on screen. The text is made first, before the
    /// dialog opens: a snapshot, so a poll, or a button on a floating panel,
    /// cannot change what is written while the dialog is up.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanExport))]
    void Export()
    {
        var shown = Shown;
        if (shown.Export() is not { } text || shown.Session.StartedAt is not double began) return;
        try
        {
            if (Saver?.Invoke(ParseFile.SuggestedName(shown.Reader.Roster.Owner, began)) is not { } path) return;
            if (!ParseFile.CanSaveAs(path))
            {
                Note("Not exported: .jsonl is the addon's own file type, and the newest one in its folder " +
                     "is taken for the live events. Save the parse as " + ParseFile.Extension + ".", bad: true);
                return;
            }
            File.WriteAllText(path, text);
            Log.Write($"exported {path}");
            Note("Exported " + Path.GetFileName(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Note("Export failed: " + e.Message, bad: true);
        }
    }
}
