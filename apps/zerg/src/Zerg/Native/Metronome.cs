using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Win32.SafeHandles;
using Zerg.Core;

namespace Zerg.Native;

/// <summary>
/// A beat on the dispatcher, a set number of times a second: what redraws
/// everything the clock moves, at the draw frequency.
///
/// <para>Not a <see cref="DispatcherTimer"/>, which cannot keep this time:
/// Windows rounds its waits up to the system clock's 15.6 ms, so one asked
/// for 30 a second gives 21 and one asked for 60 gives 40 (measured here).
/// A high-resolution waitable timer on a thread of its own keeps the rate
/// asked for, without raising the clock's resolution for the whole system.</para>
///
/// <para>Each beat is aimed at a time counted from the first, not from the
/// one before, so being late once does not make every later beat late. A
/// beat the dispatcher has not got to yet is not sent again: a busy window
/// drops beats instead of queueing them.</para>
/// </summary>
sealed class Metronome : IDisposable
{
    readonly Dispatcher dispatcher;
    readonly Action beat;
    readonly Action onBeat;
    readonly WaitHandle timer;
    readonly ManualResetEvent stop = new(false);
    readonly Thread thread;
    volatile int perSecond;
    /// <summary>A beat is on its way to the dispatcher, or being played.</summary>
    int sent;

    public Metronome(int perSecond, Action beat, Dispatcher dispatcher)
    {
        this.perSecond = DrawRate.Clamp(perSecond);
        this.beat = beat;
        this.dispatcher = dispatcher;
        onBeat = Play;
        timer = new Waitable(Create());
        thread = new Thread(Run) { IsBackground = true, Name = "Zerg draw beat" };
        thread.Start();
    }

    /// <summary>Beats a second. A change is taken up at the next beat.</summary>
    public int PerSecond
    {
        get => perSecond;
        set => perSecond = DrawRate.Clamp(value);
    }

    void Run()
    {
        WaitHandle[] either = [stop, timer];
        var clock = Stopwatch.StartNew();
        long due = 0;
        while (true)
        {
            due += DrawRate.Interval(perSecond).Ticks;
            long now = clock.Elapsed.Ticks, wait = due - now;
            if (wait > 0)
            {
                // A negative time is from now, in 100 ns: a TimeSpan's own ticks.
                if (!SetWaitableTimer(timer.SafeWaitHandle, -wait, 0, 0, 0, false)) break;
                if (WaitHandle.WaitAny(either) == 0) break;
            }
            // Too late for this one (the machine slept, or the rate rose):
            // counted again from now, with no burst of beats to catch up.
            else if (stop.WaitOne(0)) break;
            else due = now;

            if (Interlocked.Exchange(ref sent, 1) == 0)
                dispatcher.BeginInvoke(DispatcherPriority.Normal, onBeat);
        }
    }

    void Play()
    {
        try { beat(); }
        finally { Volatile.Write(ref sent, 0); }
    }

    public void Dispose()
    {
        stop.Set();
        thread.Join();
        timer.Dispose();
        stop.Dispose();
    }

    // ------------------------------------------------------------- interop

    /// <summary>A timer's handle as something .NET can wait on beside an event.</summary>
    sealed class Waitable : WaitHandle
    {
        public Waitable(SafeWaitHandle handle) => SafeWaitHandle = handle;
    }

    /// <summary>High resolution where Windows has it (10, version 1803, and
    /// later); before that an ordinary timer, which beats at the clock's grain.</summary>
    static SafeWaitHandle Create()
    {
        var handle = CreateWaitableTimerExW(0, null, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            handle = CreateWaitableTimerExW(0, null, 0, TIMER_ALL_ACCESS);
            Log.Write("draw beat: no high-resolution timer here; the draw frequency will be approximate");
        }
        return handle;
    }

    const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x2, TIMER_ALL_ACCESS = 0x1F0003;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeWaitHandle CreateWaitableTimerExW(nint attributes, string? name, uint flags, uint access);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetWaitableTimer(SafeWaitHandle timer, in long due, int period, nint routine, nint arg,
                                        [MarshalAs(UnmanagedType.Bool)] bool resume);
}
