using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zerg.Core;

namespace Zerg;

// Export and import: a paused parse saved to a file another copy of Zerg can
// open, and one opened here in the session's place.
//
// Import takes nothing away. The session being followed is left whole, and
// goes on being fed by every poll while the import is on screen: a clock that
// was running keeps running, since it is wall time, and Back to live returns
// to it with everything the addon wrote meanwhile already read. Only what is
// counted and drawn changes hands.
public sealed partial class MainViewModel
{
    /// <summary>How long the note about the last Export or Import stays up.</summary>
    static readonly TimeSpan NoteFor = TimeSpan.FromSeconds(8);

    /// <summary>The imported parse on screen in the session's place, or null.</summary>
    Tracker? import;
    /// <summary>The file it was opened from, by name.</summary>
    string importName = "";
    DispatcherTimer? noteTimer;

    /// <summary>
    /// What is counted and drawn: the import while there is one, otherwise
    /// the session. Everything that puts a figure, a name or a colour on
    /// screen reads this, so an import is drawn by the code that draws the
    /// session, panels included, and the two cannot differ in how they count.
    /// </summary>
    Tracker Shown => import ?? live;

    bool IsImportedNow => import != null;

    /// <summary>An imported parse is on screen. Both session buttons are held
    /// off wherever they appear, and Back to live is offered.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImportTip), nameof(ParseBarOpen))]
    private bool isImported;

    /// <summary>The parse on screen is paused, so it can be saved.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private bool canExport;

    [ObservableProperty] private string exportTip = "";

    public string ImportTip => IsImported
        ? "Open a different exported parse"
        : "Open a parse exported from another copy of Zerg. What you are measuring now is kept, " +
          "and Back to live returns to it.";

    /// <summary>Export and Import are for the two sections that show a
    /// session. Back to live is there wherever an import is, Compare included:
    /// it is what the locked session buttons point at.</summary>
    public bool ParseBarOpen => IsLive || IsImported;

    /// <summary>What the last Export or Import did. Clears itself.</summary>
    [ObservableProperty] private string parseNote = "";
    /// <summary>The note is about something that went wrong.</summary>
    [ObservableProperty] private bool parseNoteBad;

    /// <summary>Picks a parse off the disk, and picks where to save one
    /// (given the name to offer). The main window supplies both.</summary>
    public Func<ParseText?>? Picker { get; set; }
    public Func<string, string?>? Saver { get; set; }

    void Note(string text, bool bad = false)
    {
        noteTimer ??= new DispatcherTimer(NoteFor, DispatcherPriority.Background, (_, _) => Note(""),
                                          Dispatcher.CurrentDispatcher);
        noteTimer.Stop();
        ParseNote = text;
        ParseNoteBad = bad;
        if (text.Length > 0) noteTimer.Start();
    }

    /// <summary>Export follows the clock; Back to live follows the import.</summary>
    void DescribeParse()
    {
        var s = Shown.Session;
        IsImported = import != null;
        CanExport = Shown.CanExport;
        ExportTip = CanExport ? "Save this parse to a file another copy of Zerg can open with Import"
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
                     "is taken for today's events. Save the parse as " + ParseFile.Extension + ".", bad: true);
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

    /// <summary>
    /// Opens an exported parse in the session's place. Always offered: it
    /// takes nothing away. A second import replaces the first, and Back to
    /// live still lands on the session.
    /// </summary>
    [RelayCommand]
    void Import()
    {
        try
        {
            if (Picker?.Invoke() is not { } file) return;
            var parse = ParseFile.Import(file.Text);
            import = Tracker.Of(parse);
            importName = file.Name;
            // A drill-down names a character and an action of what was on screen.
            drill = healDrill = null;
            Log.Write($"imported {file.Name}");
            Recount();
            Note("Imported " + file.Name + (parse.Skipped > 0
                ? " — " + Format.Int(parse.Skipped) + " unreadable record" + (parse.Skipped == 1 ? "" : "s") + " skipped"
                : ""));
        }
        catch (ParseImportException e)
        {
            Note("Import failed: " + e.Message, bad: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Note("Import failed: " + e.Message, bad: true);
        }
    }

    /// <summary>Leaves the imported parse for the session, as it now stands.</summary>
    [RelayCommand]
    void BackToLive()
    {
        if (import == null) return;
        import = null;
        importName = "";
        drill = healDrill = null;
        Log.Write("back to live");
        Note("");
        Recount();
    }
}
