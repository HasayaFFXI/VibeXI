using System.Windows;
using System.Windows.Threading;
using Zerg.Native;

namespace Zerg;

public partial class App : Application
{
    EventFeed? feed;
    bool showingCrash;
    /// <summary>Another Zerg was already running; this one only told it so.</summary>
    bool second;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Before the log, the options and the settings: they are the first
        // one's, and a second launch touches none of them.
        if (!SingleInstance.Claim())
        {
            second = true;
            SingleInstance.Signal();
            Shutdown(0);
            return;
        }
        Log.Start();
        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += (_, x) => Crash(x.ExceptionObject as Exception, fatal: true);
        TaskScheduler.UnobservedTaskException += (_, x) =>
        {
            Log.Write("UNOBSERVED " + x.Exception);
            x.SetObserved();
        };

        Options options;
        try
        {
            options = Options.Parse(e.Args);
        }
        catch (ArgumentException x)
        {
            TaskDialog.Show("Zerg can't start with those options", x.Message + "\n\n" + Options.Usage,
                            TaskDialog.Icon.Warning);
            Shutdown(2);
            return;
        }

        var settings = Settings.Load();
        AppTheme.Apply(settings.ThemeMode);

        Log.Write($"events dir {options.EventsDir}; theme {settings.Theme}");

        feed = new EventFeed(options.EventsDir);
        var window = new MainWindow(new MainViewModel(settings, feed), settings);
        MainWindow = window;
        window.Show();
        // The panels that were out last time, now there is a window to open them beside.
        window.Reopen();
        feed.Start();

        SingleInstance.Listen(() => Dispatcher.BeginInvoke(() =>
        {
            Log.Write("launched again: brought forward");
            window.ComeForward();
        }));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (second)
        {
            base.OnExit(e);
            return;
        }
        feed?.Dispose();
        Log.Write("exit");
        base.OnExit(e);
    }

    /// <summary>An exception on the UI thread is logged and shown, and Zerg
    /// carries on: most are one bad poll or one bad click, and a meter that
    /// quits mid-fight loses the fight's numbers.</summary>
    void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        Crash(e.Exception, fatal: false);
    }

    void Crash(Exception? e, bool fatal)
    {
        Log.Write((fatal ? "FATAL " : "UNHANDLED ") + e);
        // A fault in something that runs 4x a second would otherwise stack a
        // dialog on every tick while the first one is still open.
        if (showingCrash || e is null) return;
        showingCrash = true;
        try
        {
            TaskDialog.Show(
                fatal ? "Zerg has to close" : "Zerg ran into a problem",
                e.Message + (fatal ? "" : "\n\nZerg will carry on. If something looks wrong, restart it."),
                TaskDialog.Icon.Error,
                details: e.ToString(),
                footer: "Details are in " + Log.FilePath,
                owner: fatal ? null : MainWindow is { IsVisible: true } w ? w : null);
        }
        finally
        {
            showingCrash = false;
        }
    }
}
