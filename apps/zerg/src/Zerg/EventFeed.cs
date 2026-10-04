using System.IO;
using System.Windows.Threading;
using Zerg.Core;

namespace Zerg;

/// <summary>
/// Follows the event folder. A 250 ms poll on the dispatcher is the source of
/// truth; a FileSystemWatcher only brings the next poll forward when Windows
/// reports a change, since Windows reports appends to a file another process
/// holds open late or not at all. Files are read off the UI thread, one poll at
/// a time, and every event is raised on the dispatcher.
/// </summary>
public sealed class EventFeed : IDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(250);

    readonly EventTail tail;
    readonly Dispatcher dispatcher;
    readonly DispatcherTimer timer;
    readonly object gate = new();
    FileSystemWatcher? watcher;
    bool polling, pollAgain, earlyQueued, disposed;

    /// <summary>After every successful poll, idle ones included.</summary>
    public event Action<TailUpdate>? Updated;

    /// <summary>When the file couldn't be read. The next poll retries the same bytes.</summary>
    public event Action<Exception>? Failed;

    public string Directory => tail.Directory;

    /// <summary>Polls made, and how many of them the watcher brought forward.</summary>
    public long Polls { get; private set; }
    public long EarlyPolls { get; private set; }

    public EventFeed(string directory)
    {
        tail = new EventTail(directory);
        dispatcher = Dispatcher.CurrentDispatcher;
        timer = new DispatcherTimer(Interval, DispatcherPriority.Normal, (_, _) => Poll(), dispatcher);
        timer.Stop();
    }

    public void Start()
    {
        timer.Start();
        Poll();
    }

    async void Poll()
    {
        if (disposed) return;
        if (polling)
        {
            // A change reported mid-read may be past where that read stopped.
            pollAgain = true;
            return;
        }
        polling = true;
        Polls++;
        try
        {
            EnsureWatcher();
            var update = await Task.Run(tail.Poll);
            if (!disposed) Updated?.Invoke(update);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (!disposed) Failed?.Invoke(e);
        }
        finally
        {
            polling = false;
        }
        if (pollAgain)
        {
            pollAgain = false;
            Poll();
        }
    }

    /// <summary>The folder may not exist until the addon first runs, and a
    /// watcher can't watch what isn't there: retried on every poll until it can.</summary>
    void EnsureWatcher()
    {
        if (watcher is not null || !System.IO.Directory.Exists(tail.Directory)) return;
        try
        {
            var w = new FileSystemWatcher(tail.Directory, "*.jsonl")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            w.Changed += OnChange;
            w.Created += OnChange;
            w.Renamed += OnChange;
            w.Deleted += OnChange;
            // The folder went away, or the change buffer overflowed: drop it
            // and let a later poll make a new one.
            w.Error += (_, e) => dispatcher.BeginInvoke(() =>
            {
                Log.Write("watcher: " + e.GetException().Message);
                DropWatcher();
            });
            w.EnableRaisingEvents = true;
            watcher = w;
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
        {
            Log.Write("watcher: " + e.Message);
        }
    }

    /// <summary>On a pool thread. A burst of changes queues one early poll.</summary>
    void OnChange(object sender, FileSystemEventArgs e)
    {
        lock (gate)
        {
            if (earlyQueued) return;
            earlyQueued = true;
        }
        dispatcher.BeginInvoke(() =>
        {
            lock (gate) earlyQueued = false;
            EarlyPolls++;
            Poll();
        });
    }

    void DropWatcher()
    {
        watcher?.Dispose();
        watcher = null;
    }

    public void Dispose()
    {
        disposed = true;
        timer.Stop();
        DropWatcher();
    }
}
