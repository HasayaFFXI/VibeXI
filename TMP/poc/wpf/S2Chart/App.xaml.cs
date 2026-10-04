using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace ChartSpike;

/// <summary>
/// Opens one window with the chart, drives its live edge (and optionally a
/// sweeping crosshair) and, with --seconds, measures this process's CPU over
/// that many seconds after a warm-up, appends one JSON line to --out and exits.
///
///   --file F        the event file (gen-alliance.py)
///   --strategy rebuild|retained
///   --hz N          live-edge updates per second; 0 = every frame (CompositionTarget.Rendering)
///   --hover         a crosshair sweeping the plot every frame, as a moving pointer would
///   --floating      compact margins + end labels (the floating panel's look)
///   --group         "N others" under 5% (off by default: all 18 lines)
///   --light         light theme
///   --size WxH      the chart's size in DIPs (default 1360x360, the docked card)
///   --cache         BitmapCache on the series layer
///   --at X,Y        window position in physical pixels
///   --seconds S --warmup W --out F --snap F.png
/// </summary>
public partial class App : Application
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var a = e.Args;
        string Arg(string name, string def) { int i = Array.IndexOf(a, name); return i >= 0 && i + 1 < a.Length ? a[i + 1] : def; }
        bool Flag(string name) => a.Contains(name);

        string file = Arg("--file", "");
        var strategy = Arg("--strategy", "rebuild") == "retained" ? Strategy.Retained : Strategy.Rebuild;
        int hz = int.Parse(Arg("--hz", "4"));
        bool cache = Flag("--cache");
        var at = Arg("--at", "") is { Length: > 0 } atArg ? atArg.Split(',').Select(int.Parse).ToArray() : null;
        bool hover = Flag("--hover"), floating = Flag("--floating"), group = Flag("--group"), light = Flag("--light");
        var size = Arg("--size", "1360x360").Split('x').Select(double.Parse).ToArray();
        double seconds = double.Parse(Arg("--seconds", "0")), warmup = double.Parse(Arg("--warmup", "5"));
        string? outFile = Arg("--out", "") is { Length: > 0 } o ? o : null;
        string? snapFile = Arg("--snap", "") is { Length: > 0 } sn ? sn : null;

        if (light) ThemeMode = ThemeMode.Light;

        var hits = Data.Load(file);
        double zero = Math.Floor(hits.Min(h => h.T) / 1000) * 1000;
        double Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - zero;

        var model = Data.Cumulative(hits, zero, Now());
        int slot = 0;
        foreach (var s in model.Series.OrderBy(s => s.Name == "Hasaya" ? 0 : 1)) s.Color = Data.Slot(slot++, !light);
        if (group) Data.GroupSmall(model, "Hasaya");

        var chart = new LineChart { Width = size[0], Height = size[1], Compact = floating, EndLabels = floating, Dark = !light, CacheSeries = cache };
        var status = new TextBlock { Margin = new Thickness(12, 8, 12, 4) };
        var root = new DockPanel();
        DockPanel.SetDock(status, Dock.Top);
        root.Children.Add(status);
        root.Children.Add(new Border { Child = chart, Margin = new Thickness(12), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top });
        var win = new Window
        {
            Title = "Zerg S2: chart cost",
            Content = root,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 60, Top = 60,
        };
        if (at is not null)
            win.SourceInitialized += (_, _) => SetWindowPos(new System.Windows.Interop.WindowInteropHelper(win).Handle, 0, at[0], at[1], 0, 0, 0x1 | 0x4 | 0x10);
        string label = $"{strategy} @ {(hz == 0 ? "every frame" : hz + " Hz")}{(hover ? " + hover" : "")}{(cache ? ", cached" : "")}{(floating ? ", floating" : "")}" +
                       $", {model.Series.Count} series x {model.Times.Length} points";
        status.Text = label;
        win.Show();
        chart.SetModel(model, strategy);

        // ---- the live edge
        int frames = 0, edges = 0;
        void Edge() { chart.SetEdge(Now()); edges++; }
        if (hz > 0)
            new DispatcherTimer(TimeSpan.FromMilliseconds(1000.0 / hz), DispatcherPriority.Render, (_, _) => Edge(), Dispatcher);

        // ---- every frame: the edge (hz 0) and the crosshair. Subscribing to
        // Rendering keeps WPF rendering every frame, so only when one is wanted.
        var sweep = Stopwatch.StartNew();
        if (hz == 0 || hover) CompositionTarget.Rendering += (_, _) =>
        {
            frames++;
            if (hz == 0) Edge();
            if (hover)
            {
                double phase = sweep.Elapsed.TotalSeconds % 4 / 4;   // across and back every 4 s
                double f = phase < 0.5 ? phase * 2 : 2 - phase * 2;
                chart.Hover(80 + f * (chart.ActualWidth - 180));
            }
        };

        if (seconds <= 0) return;

        // ---- measurement
        var proc = Process.GetCurrentProcess();
        TimeSpan cpu0 = default;
        int frames0 = 0, edges0 = 0, draws0 = 0;
        double draw0 = 0;
        var wall = new Stopwatch();
        var start = new DispatcherTimer { Interval = TimeSpan.FromSeconds(warmup) };
        start.Tick += (_, _) =>
        {
            start.Stop();
            proc.Refresh();
            cpu0 = proc.TotalProcessorTime;
            frames0 = frames; edges0 = edges; draws0 = chart.Draws; draw0 = chart.DrawTime.Elapsed.TotalMilliseconds;
            wall.Start();
            var stop = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            stop.Tick += (_, _) =>
            {
                stop.Stop();
                proc.Refresh();
                double secs = wall.Elapsed.TotalSeconds;
                double cpuMs = (proc.TotalProcessorTime - cpu0).TotalMilliseconds;
                int draws = chart.Draws - draws0;
                var result = new Dictionary<string, object>
                {
                    ["what"] = "wpf " + label,
                    ["cpuPctOfOneCore"] = Math.Round(cpuMs / secs / 10, 2),
                    ["cpuPctOfMachine"] = Math.Round(cpuMs / secs / 10 / Environment.ProcessorCount, 2),
                    ["framesPerSec"] = Math.Round((frames - frames0) / secs, 1),
                    ["edgesPerSec"] = Math.Round((edges - edges0) / secs, 1),
                    ["uiDrawMsPerSec"] = Math.Round((chart.DrawTime.Elapsed.TotalMilliseconds - draw0) / secs, 2),
                    ["redrawsPerSec"] = Math.Round(draws / secs, 1),
                    ["workingSetMB"] = proc.WorkingSet64 / 1048576,
                    ["chartPx"] = $"{chart.ActualWidth * VisualTreeHelper.GetDpi(chart).DpiScaleX:0}x{chart.ActualHeight * VisualTreeHelper.GetDpi(chart).DpiScaleY:0}",
                };
                if (outFile is not null) File.AppendAllText(outFile, JsonSerializer.Serialize(result) + Environment.NewLine);
                if (snapFile is not null) Screen.Snap(new System.Windows.Interop.WindowInteropHelper(win).Handle, snapFile);
                Shutdown();
            };
            stop.Start();
        };
        start.Start();
    }
}
