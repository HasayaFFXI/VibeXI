namespace Zerg.Core;

/// <summary>A completed pause, in wall-clock milliseconds.</summary>
public sealed record PauseSpan(double From, double To);

/// <summary>
/// The session clock. Every number Zerg draws is measured from the session's
/// zero; the x axis, the DPS denominator and the per-hit times all come from
/// <see cref="At"/> and <see cref="Elapsed"/> and from nothing else.
///
/// <para><b>Start arms; it is not the zero.</b> The clock starts on the first
/// event that would actually be counted, and that event's own time becomes
/// zero. Pressing Start a beat early (while the puller runs back, while still
/// buffing) costs nothing. A stopwatch that has to be pressed on the frame of
/// the first swing is always a second or two wrong, and every DPS figure is
/// divided by that error.</para>
///
/// Three states, each real:
/// <list type="bullet">
/// <item>idle (nothing armed, no zero): nothing counted, nothing drawn, so a
/// window left open cannot quietly collect a pull nobody asked for;</item>
/// <item>armed (armed, no zero yet): still nothing counted, but the next counted
/// event sets the zero;</item>
/// <item>started: running, or paused.</item>
/// </list>
/// <see cref="At"/> and <see cref="Elapsed"/> look at <see cref="StartedAt"/>
/// alone, so an armed session behaves exactly like an idle one until it latches.
///
/// <para>Times are wall-clock milliseconds since the Unix epoch, the same clock
/// the event rows carry.</para>
/// </summary>
public sealed class Session
{
    /// <summary>When Start was pressed, floored to the second; null when idle.</summary>
    public double? ArmedAt { get; set; }
    /// <summary>The zero: the time of the first counted event. Set once, never moved.</summary>
    public double? StartedAt { get; set; }
    /// <summary>Completed pauses, in order.</summary>
    public List<PauseSpan> Spans { get; set; } = [];
    /// <summary>When Pause was pressed, while paused.</summary>
    public double? PausedAt { get; set; }
    /// <summary>Where a paused clock is read: the end of the second its last
    /// counted row landed in (<see cref="Snap"/>). Null while running, and for
    /// a pause nobody has snapped, which is read at <see cref="PausedAt"/>.</summary>
    public double? EndedAt { get; set; }

    /// <summary>The wire clock is whole seconds: a row stamped <c>t</c> landed
    /// somewhere in the second that begins there.</summary>
    public const double Second = 1000;

    public static double Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>
    /// A session armed at <paramref name="now"/>, floored to the second on
    /// purpose. The wire clock is whole seconds, so a swing 300 ms before the
    /// press and one 300 ms after carry the same time and cannot be told apart.
    /// Rounding down counts that second rather than discarding it: catching the
    /// pull's first swing matters more than excluding one that beat the button
    /// by a moment, and dropping it would drop it from accuracy too.
    /// </summary>
    public static Session Arm(double? now = null) => new()
    {
        ArmedAt = Math.Floor((now ?? Now()) / 1000) * 1000,
    };

    public static Session Idle() => new();

    /// <summary>Latches the zero, once. A later filter change can never re-date
    /// a running session and re-scale every number in it.</summary>
    public Session Start(double t)
    {
        StartedAt ??= t;
        return this;
    }

    /// <summary>Paused time that had already gone by at wall-clock
    /// <paramref name="t"/>. A pause still open counts up to <paramref name="t"/>,
    /// which is what freezes the clock while paused.</summary>
    public double PausedBefore(double t)
    {
        double ms = 0;
        foreach (var s in Spans)
        {
            if (s.From >= t) break;
            ms += Math.Min(s.To, t) - s.From;
        }
        if (PausedAt is double p && p < t) ms += t - p;
        return ms;
    }

    /// <summary>Is <paramref name="t"/> inside a pause? Those events are dropped, not shifted.
    /// A snapped pause begins where its clock is read, not at the press.</summary>
    public bool InPause(double t)
    {
        if (EndedAt is double end && t >= end) return true;
        foreach (var s in Spans)
            if (t >= s.From && t < s.To) return true;
        return PausedAt is double p && t >= p;
    }

    /// <summary>
    /// Wall clock → milliseconds since the zero, or null for an instant the
    /// session does not cover (before it began, or inside a pause).
    ///
    /// A pause is subtracted, not skipped over: two swings either side of a
    /// three-minute pause come out three minutes closer together than their
    /// timestamps, so the chart is continuous and its axis agrees with the
    /// denominator DPS is divided by. Time the session was not measuring is
    /// time that did not happen.
    /// </summary>
    public double? At(double t)
    {
        if (StartedAt is not double start) return null;
        if (t < start) return null;
        if (InPause(t)) return null;
        return t - start - PausedBefore(t);
    }

    /// <summary>How long the session has run at <paramref name="now"/>. Frozen
    /// while paused, because the paused time grows at exactly the same rate;
    /// a snapped pause is frozen where it ended.</summary>
    public double Elapsed(double? now = null)
    {
        if (StartedAt is not double start) return 0;
        var n = now ?? Now();
        if (EndedAt is double end && n > end) n = end;
        if (n < start) return 0;
        return n - start - PausedBefore(n);
    }

    /// <summary>Started and not paused.</summary>
    public bool Running => StartedAt != null && PausedAt == null;

    /// <summary>Armed and still waiting for the event that will be its zero.</summary>
    public bool Armed => ArmedAt != null && StartedAt == null;

    public Session Pause(double? now = null)
    {
        if (Running) PausedAt = now ?? Now();
        return this;
    }

    /// <summary>The pause that ends is the one that was pressed: the quiet
    /// stretch a snap took off the end goes back on the clock, since the
    /// session turned out not to be over.</summary>
    public Session Resume(double? now = null)
    {
        if (PausedAt is double p)
        {
            Spans.Add(new PauseSpan(p, now ?? Now()));
            PausedAt = null;
            EndedAt = null;
        }
        return this;
    }

    /// <summary>
    /// Snaps a paused clock back to the last thing it measured.
    /// <paramref name="last"/> is the time of the last counted row the session
    /// covers; the clock is then read at the end of that row's second, and
    /// whatever came after it (the wait before somebody pressed Pause, an
    /// earlier pause and resume with nothing counted since) is time nobody
    /// was fighting, and no DPS is divided by it.
    ///
    /// <para>The end of the second, not its start: the row landed somewhere
    /// inside it, which is why arming rounds down to count the first one. A
    /// pull of one swing is a second long, not nothing. Never later than the
    /// clock really stopped: the press, or an earlier pause that began inside
    /// that second.</para>
    ///
    /// <para>The press and the pauses are left as they happened, so Resume
    /// undoes it. Nothing to snap to, or not paused: read at the press.</para>
    /// </summary>
    public Session Snap(double? last)
    {
        EndedAt = null;
        if (PausedAt is not double p || last is not double t) return this;
        double end = Math.Min(t + Second, p);
        foreach (var s in Spans)
            if (s.From > t) { end = Math.Min(end, s.From); break; }
        EndedAt = end;
        return this;
    }

    /// <summary>A deep copy, so a snapshot cannot be moved by the live session.</summary>
    public Session Clone() => new()
    {
        ArmedAt = ArmedAt, StartedAt = StartedAt, Spans = [.. Spans], PausedAt = PausedAt, EndedAt = EndedAt,
    };
}
