namespace Zerg.Core;

/// <summary>How the Start button looks: the thing to press, waiting, a re-do,
/// or the answer that drops a measurement.</summary>
public enum StartLook { Idle, Armed, Running, Confirm, Locked }

/// <summary>How the second session button looks.</summary>
public enum SecondLook { Off, Cancel, Live, Held }

/// <summary>The session's state as one signal, for the status dot and the clock.</summary>
public enum SessionLight { Idle, Armed, Live, Held }

/// <summary>
/// The word in the state tag beside the clock: the session's state, or
/// Saved over a parse that is only being looked at. A saved parse's light
/// is Held (its clock is stopped for good), which is not what the tag says
/// of it: nobody is holding it.
/// </summary>
public enum SessionTag { Idle, Armed, Live, Held, Saved }

/// <summary>
/// The two session buttons and the status dot, as plain data: what each says,
/// what it explains on hover, and which state it wears. Worked out in one place
/// and drawn wherever the pair appears (the main window, and every floating
/// panel's bar), so two copies side by side can never disagree.
///
/// <para><b>The second button is Pause or Cancel, never both.</b> While armed
/// there is no clock to hold but there is an arming to call off; once the
/// clock runs there is no arming left to cancel. One slot, one meaning at a
/// time. Cancel is not a toggle, so it has no pressed state to announce.</para>
///
/// <para>Cancel never throws a measurement away. A session with damage in it
/// is ended by Start, not by Cancel: "cancel" would then mean dropping what
/// was measured, a far more destructive act than calling something off
/// before it happened. So it is offered twice, and both times it calls off
/// what has not happened yet: an arming, or a restart that is still only
/// being asked about.</para>
///
/// <para><b>A restart over a measurement is asked about in the pair itself.</b>
/// Start becomes Confirm and the second button Cancel, in the places they
/// already were. Reaching for Pause and pressing Restart is the slip this is
/// for, and the press that follows it lands on the second button: Cancel,
/// which loses nothing.</para>
/// </summary>
/// <param name="StartAsks">Pressing Start now only asks. A menu that holds
/// the pair stays open for the answer.</param>
public sealed record SessionView(
    string StartText, string StartTip, StartLook StartLook, bool StartEnabled, bool StartAsks,
    string SecondText, string SecondTip, SecondLook SecondLook, bool SecondEnabled,
    bool SecondPressed, bool SecondIsToggle, SessionLight Light)
{
    const string Dash = " — ";

    /// <summary>What the state tag says: Saved over an imported parse,
    /// otherwise the light under its own name.</summary>
    public SessionTag Tag { get; private init; }

    /// <summary>
    /// A session is armed or counting: something is being measured, or is
    /// about to be. What the tray menu's heading waits for (the state, the
    /// clock and the total, over the menu's lines): idle, held, or over a
    /// saved parse the menu has no heading. A restart that is only being
    /// asked about changes nothing here, as it changes nothing in the tag.
    /// </summary>
    public bool Underway => Tag is SessionTag.Armed or SessionTag.Live;

    static SessionTag TagOf(SessionLight light) => light switch
    {
        SessionLight.Armed => SessionTag.Armed,
        SessionLight.Live => SessionTag.Live,
        SessionLight.Held => SessionTag.Held,
        _ => SessionTag.Idle,
    };

    /// <param name="imported">An imported parse is a finished recording: both
    /// buttons are held off everywhere they appear.</param>
    /// <param name="asking">Start was pressed over a measurement and waits
    /// for its answer (<see cref="Tracker.Asking"/>).</param>
    public static SessionView Of(Session session, bool imported = false, bool asking = false)
    {
        if (imported)
        {
            const string why = "Viewing a saved parse. Switch to Damage or Healing to measure your own.";
            return new SessionView("Start", why, StartLook.Locked, false, false,
                                   "Pause", why, SecondLook.Off, false, false, false, SessionLight.Held)
            {
                Tag = SessionTag.Saved,
            };
        }

        bool armed = session.Armed, started = session.StartedAt != null, paused = session.PausedAt != null;
        // The light is the session's, question or no question: it is still
        // being measured, or still held, until Confirm is pressed.
        if (asking && started)
            return new SessionView(
                "Confirm", "Confirm" + Dash + "drop this pull and measure a fresh one from here. " +
                           "Left alone for a few seconds, the pull is kept.",
                StartLook.Confirm, true, false,
                "Cancel", "Cancel" + Dash + "keep this pull. Nothing is dropped.",
                SecondLook.Cancel, true, false, false,
                paused ? SessionLight.Held : SessionLight.Live)
            {
                Tag = paused ? SessionTag.Held : SessionTag.Live,
            };

        var light = armed ? SessionLight.Armed : !started ? SessionLight.Idle : paused ? SessionLight.Held : SessionLight.Live;
        return new SessionView(
            StartText: armed || started ? "Restart" : "Start",
            StartTip: armed ? "Armed" + Dash + "the clock starts on the first counted hit. Press again to re-arm."
                    : started ? "Zero the clock and measure a fresh pull from here. Asks first: this pull is dropped."
                    : "Arm the session. The clock starts on the first hit, so pressing early costs nothing.",
            // A state, not a hover: "Restart" alone cannot say whether the clock
            // is running or still waiting, and that is the one thing being watched.
            StartLook: armed ? StartLook.Armed : started ? StartLook.Running : StartLook.Idle,
            StartEnabled: true,
            StartAsks: started,

            SecondText: armed ? "Cancel" : paused ? "Resume" : "Pause",
            SecondTip: armed ? "Cancel" + Dash + "disarm. Nothing is counted until Start is pressed again."
                     : !started ? "Press Start first"
                     : paused ? "Resume counting and restart the clock"
                     : "Stop counting damage and stop the clock. Damage dealt while paused is not counted " +
                       "and the paused time is not divided into DPS.",
            SecondLook: armed ? SecondLook.Cancel : !started ? SecondLook.Off : paused ? SecondLook.Held : SecondLook.Live,
            SecondEnabled: armed || started,
            SecondPressed: paused,
            SecondIsToggle: !armed,

            Light: light)
        {
            Tag = TagOf(light),
        };
    }
}

/// <summary>
/// What Zerg says about the session in words: under the total, under the
/// clock, in an empty chart, and on the status line.
/// </summary>
public static class SessionText
{
    const string Dash = " — ", Dot = " · ";

    /// <summary>
    /// What an empty chart says. The overwhelmingly common reason a chart is
    /// empty is that nobody has pressed Start, so it says that first. While
    /// armed it says so loudly: with every character excluded nothing can
    /// start the clock, and that is the one way arming can look stuck.
    /// </summary>
    public static string Empty(Session s, bool imported = false, string what = "damage") =>
        imported ? "Nothing to show in this parse"
        : s.Armed ? "Armed" + Dash + "the clock starts on the first hit, or Cancel to call it off"
        : s.StartedAt == null ? "Press Start to begin measuring"
        : s.PausedAt != null ? "Paused" + Dash + "nothing is being counted"
        : "No " + what + " yet";

    /// <summary>
    /// Under the total: the state of the measurement. Not the elapsed time,
    /// which has a tile of its own beside it; two neighbours printing one
    /// clock in two formats is what that tile exists to avoid.
    /// </summary>
    public static string TotalNote(Session s, bool anyone, bool imported = false, string what = "damage") =>
        imported ? "saved parse" + Dash + "read only"
        : s.Armed ? "armed" + Dash + "starts on the first hit"
        : s.StartedAt == null ? "not started" + Dash + "press Start"
        : !anyone ? "no " + what + " yet"
        : s.PausedAt != null ? "held" + Dash + "nothing counting"
        : "counting";

    /// <summary>
    /// Under the clock: when the pull began, as a time of day. It is the one
    /// fact an elapsed clock cannot state.
    /// </summary>
    public static string ClockNote(Session s) =>
        s.Armed ? "waiting for the first hit"
        : s.StartedAt is not double start ? "—"
        : "started " + Format.Clock(start) + (s.PausedAt != null ? Dot + "held" : "");

    /// <summary>The session's part of the status line. An import can be
    /// from any day, so it carries the date as well.</summary>
    public static string Status(Session s, bool imported = false) =>
        s.StartedAt is double start ? "started " + (imported ? Day(start) + " " : "") + Format.Clock(start)
        : s.Armed ? "armed, waiting for the first hit"
        : "not started";

    static string Day(double ms) =>
        DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Truncate(ms)).ToLocalTime()
                      .ToString("yyyy'-'MM'-'dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The clock as it is watched: fixed width, and zero before a start.</summary>
    public static string Stopwatch(Session s, double? now = null) =>
        s.StartedAt != null ? Format.Stopwatch(s.Elapsed(now)) : "00:00";
}
