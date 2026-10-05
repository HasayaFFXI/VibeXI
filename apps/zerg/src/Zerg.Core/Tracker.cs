namespace Zerg.Core;

/// <summary>
/// One count of the session as it stands: what every card is drawn from, the
/// Damage section's and the Healing section's alike. Both are measured in the
/// one count, over the same window and the same exclusions, so a figure in
/// one section and a figure in the other are never from two different moments.
/// </summary>
/// <param name="Events">The rows that count, credited and on the session clock.</param>
/// <param name="Totals">The party, over those rows.</param>
/// <param name="Listed">Every character who acted in the session, largest
/// first, whether or not they are excluded: the ones a filter chip is shown for.</param>
/// <param name="Elapsed">The session clock when this was counted, in milliseconds.</param>
/// <param name="Latched">An armed session found its zero during this count.</param>
public sealed record Snapshot(IReadOnlyList<CombatEvent> Events, Aggregate Totals, IReadOnlyList<ActorTotals> Listed,
                           double Elapsed, bool Latched)
{
    /// <summary>The heals that count: in the session, the party's, nobody
    /// excluded, on the session clock, a pet's credited to its owner.</summary>
    public IReadOnlyList<HealEvent> Heals { get; init; } = [];

    /// <summary>The party's healing, over those heals.</summary>
    public HealTotals Healing { get; init; } = new();

    /// <summary>Every character who healed in the session, largest first,
    /// excluded or not: who a chip is shown for in the Healing section.</summary>
    public IReadOnlyList<HealerTotals> Healers { get; init; } = [];

    /// <summary>The biggest hit of any action; the first found wins a tie.</summary>
    public BestHit? Best
    {
        get
        {
            BestHit? best = null;
            foreach (var a in Totals.Actors)
                foreach (var act in a.ActionList)
                    if (best == null || act.Max > best.Max) best = new BestHit(act.Max, a.Name, act.Name);
            return best;
        }
    }

    /// <summary>The biggest heal: what one target got from one action, a
    /// pet's included. The earliest wins a tie.</summary>
    public BestHit? BestHeal
    {
        get
        {
            HealEvent? best = null;
            foreach (var h in Heals)
                if (best == null || h.Hp > best.Hp) best = h;
            return best == null ? null : new BestHit(best.Hp, best.Actor, best.Action);
        }
    }

    /// <summary>
    /// Who leads by healing, a pet's left out as it is from the Healing
    /// column; nobody while nobody has healed anything. Of two level, the
    /// one listed first.
    /// </summary>
    public HealerTotals? TopHealer
    {
        get
        {
            HealerTotals? top = null;
            foreach (var a in Healing.Actors)
                if (a.Total > 0 && (top == null || a.Total > top.Total)) top = a;
            return top;
        }
    }
}

/// <summary>
/// The live session: the event file being followed, the rows read from it,
/// the session clock, and who has which colour. No UI and no file access; it
/// is handed lines and asked for counts.
///
/// <para><b>Start is also the reset.</b> There is no separate Reset: pressing
/// Start during a session is how the next pull gets measured. It re-arms, and
/// the next counted hit is the new zero. The rows read so far are dropped,
/// which changes nothing on screen (the new session would not cover them) but
/// keeps each count proportional to the rows since Start rather than to the
/// whole evening. The place in the file is not moved: nothing is re-read.</para>
///
/// <para><b>Over a measurement the button asks first</b> (<see cref="First"/>).
/// Start sits beside Pause, and reaching for one and pressing the other at
/// the end of a fight would drop the fight. So the first press only asks,
/// and the pair of buttons is the question's two answers: Start is Confirm
/// and the second button is Cancel. Nothing opens: the pair is pressed from
/// a panel over the game, where a dialog would take the keyboard from it.</para>
///
/// <para>What a restart keeps: the colour slots (a character changing hue
/// between pulls is worse than a stale slot) and the whole roster (monsters
/// stay monsters, and jobs are only written when they change).</para>
///
/// <para><b>A new event file starts from nothing</b>, session included. A
/// clock still running from the previous character or the previous day would
/// be measuring a session that is not this one.</para>
///
/// <para>The session is never saved. A clock restored after a restart would
/// be measuring time the player was not in a fight for.</para>
///
/// <para><b>An imported parse is a tracker too</b> (<see cref="Of"/>), so it is
/// counted, coloured and drawn by the same code as the session, and nothing
/// downstream can tell the two apart. It is a finished recording: its clock
/// is paused for good, and Start, Pause, Cancel and new lines are all
/// refused. Resuming it would run its clock on today's time over a file that
/// will never grow.</para>
/// </summary>
public sealed class Tracker
{
    /// <summary>The event file being followed (name only), or null before the
    /// first. For an import, the event file the parse was recorded from, if it says.</summary>
    public string? File { get; private set; }

    /// <summary>This is an imported parse, not the session: read only.</summary>
    public bool Imported { get; private init; }

    /// <summary>An imported parse, to be counted as a session is. The owner
    /// is the exporter's, so slot 0 and the one name Hide names keeps are
    /// theirs, not the viewer's.</summary>
    public static Tracker Of(ImportedParse parse) => new()
    {
        Imported = true, File = parse.File, Reader = parse.Source, Session = parse.Session,
    };
    public EventReader Reader { get; private set; } = new();
    public Session Session { get; private set; } = Session.Idle();
    public Cast Cast { get; private set; } = new();
    /// <summary>Lines read from the file, whatever they held.</summary>
    public long Lines { get; private set; }

    /// <summary>
    /// How soon after the asking press another one is no answer, in
    /// milliseconds: Windows' own double-click time, unless changed. Two
    /// presses that close together are one gesture, and a question a
    /// double-click answers protects nothing.
    /// </summary>
    public const double Settle = 500;

    /// <summary>When Start was pressed over a measurement and has not been
    /// answered yet; null otherwise.</summary>
    public double? AskedAt { get; private set; }

    /// <summary>The two buttons are Confirm and Cancel: a restart is waiting for its answer.</summary>
    public bool Asking => AskedAt != null;

    /// <summary>A different file is being followed, or the same one from its top.</summary>
    public void Follow(string file)
    {
        if (Imported) return;
        File = file;
        Reader = new EventReader(EventReader.ParseFilename(file).Owner);
        Cast = new Cast();
        Session = Session.Idle();
        Lines = 0;
        AskedAt = null;
    }

    /// <summary>No file is being followed any more: the folder they are
    /// looked for in has changed. As a tracker starts, session included,
    /// until a file is found in the new one.</summary>
    public void Unfollow()
    {
        if (Imported) return;
        File = null;
        Reader = new EventReader();
        Cast = new Cast();
        Session = Session.Idle();
        Lines = 0;
        AskedAt = null;
    }

    public void Feed(IReadOnlyList<string> lines)
    {
        if (Imported) return;
        foreach (var line in lines) Reader.Feed(line);
        Lines += lines.Count;
        // A row from before the press can be read after it: the end is
        // worked out from the rows' own times, like everything else.
        if (Session.PausedAt != null) Snap(Session);
    }

    /// <summary>Arms the session; during one, re-arms it for the next pull.
    /// The act itself, with no question: <see cref="First"/> is the button.</summary>
    public void Start(double? now = null)
    {
        if (Imported) return;
        Reader.Reset();
        Cast.Rewind();
        Session = Session.Arm(now);
        AskedAt = null;
    }

    /// <summary>
    /// The first button: Start, and over a measurement a question before it.
    /// Idle or armed there is nothing to lose, so it arms at once. Once the
    /// clock has started the first press only asks, and the next one is the
    /// answer, unless it comes within <see cref="Settle"/> of the first.
    /// The session is measured as before while the question is up.
    /// </summary>
    /// <returns>True when the session was armed; false when the press only
    /// asked, or came too soon to be an answer.</returns>
    public bool First(double? now = null)
    {
        if (Imported) return false;
        double t = now ?? Session.Now();
        if (AskedAt is double asked)
        {
            if (t - asked < Settle) return false;
        }
        else if (Session.StartedAt != null)
        {
            AskedAt = t;
            return false;
        }
        Start(now);
        return true;
    }

    /// <summary>The question is taken back, and the measurement kept: Cancel
    /// was pressed, or nobody answered.</summary>
    public void Withdraw() => AskedAt = null;

    /// <summary>The second button: Cancel while a restart is being asked
    /// about or the session is armed, otherwise Pause or Resume.</summary>
    public void Second(double? now = null)
    {
        if (Asking) Withdraw();
        else if (Session.Armed) Cancel();
        else TogglePause(now);
    }

    /// <summary>
    /// Calls off an armed session: back to counting nothing, exactly as a new
    /// event file leaves things. Arming is a statement about a pull that has
    /// not happened yet, and it can turn out wrong; without this the only way
    /// out would be to let it catch a hit and throw that session away. The
    /// rows read while armed are kept: an idle session counts nothing, so
    /// they cannot be seen, and the next Start drops them anyway.
    /// </summary>
    public void Cancel()
    {
        if (Session.Armed) Session = Session.Idle();
    }

    /// <summary>
    /// Pause stops the damage and the clock together. Freeze one without the
    /// other and the figures lie: a running clock over a frozen total reads as
    /// a wipe, a frozen clock over a running total as a record. Reading does
    /// not stop: rows that land during a pause are kept and left out by their
    /// own times. Nothing to do before the clock has started.
    ///
    /// <para><b>A paused clock is read at the last thing it measured</b>
    /// (<see cref="Snap"/>), not at the press. Pause is pressed some time
    /// after the last swing, never on it, and that wait would otherwise be
    /// in every DPS the parse is remembered by. Resume puts it back.</para>
    /// </summary>
    public void TogglePause(double? now = null)
    {
        if (Imported || Session.StartedAt == null) return;
        if (Session.Running) Snap(Session.Pause(now));
        else Session.Resume(now);
    }

    /// <summary>
    /// Ends a paused session at the end of the second its last party damage
    /// row landed in. Whose row is not asked, nor whether it was a
    /// skillchain: the end belongs to the session, as its zero does, so the
    /// screen, the exported file and a compared run all divide by one clock
    /// whatever filters each is seen through.
    /// </summary>
    Session Snap(Session s)
    {
        s.EndedAt = null;   // or a row behind the old end would not be seen
        return s.Snap(Counting.LastCounted(Reader.Events, s, new FilterOptions { Roster = Reader.Roster }));
    }

    /// <summary>
    /// There is a finished measurement to save: the clock has started and is
    /// stopped. A running clock is a moving target, out of date before the
    /// file is written; a paused one has a fixed denominator, which is what
    /// makes a parse worth handing to someone. An import is always paused,
    /// so it can be exported again.
    /// </summary>
    public bool CanExport => Session.StartedAt != null && Session.PausedAt != null;

    /// <summary>
    /// The paused parse as the text of an export, or null while there is
    /// none to save. Only the rows the session can ever count go in: after
    /// the zero and outside every pause. An import cannot be resumed, so
    /// nothing else could be seen under any filter its reader sets, and the
    /// file is the parse, not whatever happened to have been read. The
    /// filters are not in it: exclusions, skillchains and hidden names are
    /// the viewer's, and the reader's own apply.
    ///
    /// <para>The file ends where the parse does: at its last party damage
    /// row, not at the press (<see cref="Snap"/>). The session on screen
    /// already does. A parse imported from a file that predates this is
    /// shown with the clock it was saved with, and exported again it is
    /// snapped like any other.</para>
    /// </summary>
    public string? Export(double? now = null)
    {
        if (!CanExport) return null;
        var ended = Snap(Session.Clone());
        return ParseFile.Stringify(ParseFile.Export(Reader, ended, t => ended.At(t) != null, File, now));
    }

    /// <summary>
    /// Counts the session as it stands at <paramref name="now"/>.
    ///
    /// <para>An armed session is given its zero here, first, if a row has
    /// qualified: the same pass must draw the row that started the clock. A
    /// filter change comes through here too, on purpose. Switching skillchains
    /// back on, or re-including a character, can make a row already read the
    /// first that counts, and it is then the right zero. The zero is set once
    /// and never moved.</para>
    /// </summary>
    /// <param name="excluded">Characters switched off, by name.</param>
    public Snapshot Count(IReadOnlySet<string> excluded, bool skillchains, double? now = null)
    {
        var all = Reader.Events;
        var roster = Reader.Roster;
        Cast.Assign(all, roster);

        var actors = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var n in excluded) actors[n] = false;

        bool latched = false;
        if (Session.Armed &&
            Counting.FirstCounted(all, Session, new FilterOptions { Roster = roster, Actors = actors, Skillchains = skillchains })
                is double zero)
        {
            Session.Start(zero);
            latched = true;
        }

        // The one call that knows the time of day: it drops what the session
        // does not cover and puts the rest on its clock.
        var scoped = Counting.Filter(all, new FilterOptions { Session = Session, Roster = roster, Skillchains = skillchains });
        // No session here: these rows are on its clock already.
        var shown = Counting.Filter(scoped, new FilterOptions { Actors = actors });

        // The session clock is what every DPS is divided by, read once so the
        // party's figure and each character's cannot differ by a moment.
        var elapsed = Session.Elapsed(now);
        var totals = Counting.Aggregate(shown, elapsed / 1000);
        var listed = shown.Count == scoped.Count ? totals : Counting.Aggregate(scoped, elapsed / 1000);

        // Healing, over the same window and the same exclusions. Heals are
        // kept apart from the damage rows, so nothing above can see one, and
        // none of them can start the clock: only a counted hit does that, and
        // a cure cast before the first swing falls before the zero.
        var healScoped = Healing.Filter(Reader.Heals, new FilterOptions { Session = Session, Roster = roster });
        var healShown = Healing.Filter(healScoped, new FilterOptions { Actors = actors });
        var healing = Healing.Totals(healShown);
        var healers = healShown.Count == healScoped.Count ? healing : Healing.Totals(healScoped);

        return new Snapshot(shown, totals, listed.Actors, elapsed, latched)
        {
            Heals = healShown, Healing = healing, Healers = healers.Actors,
        };
    }
}
