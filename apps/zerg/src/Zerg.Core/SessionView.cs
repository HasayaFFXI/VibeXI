namespace Zerg.Core;

/// <summary>How the Start button looks: the thing to press, waiting, or a re-do.</summary>
public enum StartLook { Idle, Armed, Running, Locked }

/// <summary>How the second session button looks.</summary>
public enum SecondLook { Off, Cancel, Live, Held }

/// <summary>The session's state as one signal, for the status dot and the clock.</summary>
public enum SessionLight { Idle, Armed, Live, Held }

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
/// <para>Cancel is only offered while armed, on purpose. A session with damage
/// in it is ended by Start, not by Cancel: "cancel" would then mean throwing a
/// measurement away, a far more destructive act than calling one off before
/// it began.</para>
/// </summary>
public sealed record SessionView(
    string StartText, string StartTip, StartLook StartLook, bool StartEnabled,
    string SecondText, string SecondTip, SecondLook SecondLook, bool SecondEnabled,
    bool SecondPressed, bool SecondIsToggle, SessionLight Light)
{
    const string Dash = " — ";

    /// <param name="imported">An imported parse is a finished recording: both
    /// buttons are held off everywhere they appear.</param>
    public static SessionView Of(Session session, bool imported = false)
    {
        if (imported)
        {
            const string why = "Viewing an imported parse. Press Back to live to measure your own.";
            return new SessionView("Start", why, StartLook.Locked, false,
                                   "Pause", why, SecondLook.Off, false, false, false, SessionLight.Held);
        }

        bool armed = session.Armed, started = session.StartedAt != null, paused = session.PausedAt != null;
        return new SessionView(
            StartText: armed || started ? "Restart" : "Start",
            StartTip: armed ? "Armed" + Dash + "the clock starts on the first counted hit. Press again to re-arm."
                    : started ? "Zero the clock and measure a fresh pull from here"
                    : "Arm the session. The clock starts on the first hit, so pressing early costs nothing.",
            // A state, not a hover: "Restart" alone cannot say whether the clock
            // is running or still waiting, and that is the one thing being watched.
            StartLook: armed ? StartLook.Armed : started ? StartLook.Running : StartLook.Idle,
            StartEnabled: true,

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

            Light: armed ? SessionLight.Armed : !started ? SessionLight.Idle : paused ? SessionLight.Held : SessionLight.Live);
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
        imported ? "imported parse" + Dash + "read only"
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
