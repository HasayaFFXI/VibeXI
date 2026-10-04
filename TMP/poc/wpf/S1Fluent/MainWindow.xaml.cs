using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace FluentSpike;

public partial class MainWindow : Window
{
    const string Sample = "Hasaya  SAM/WAR  1,234,567  98.7%  Tachi: Jinpu  0:42";

    static readonly string[] FontCandidates =
    {
        "Segoe UI Variable", "Segoe UI Variable Display", "Segoe UI Variable Text",
        "Segoe UI Variable Small", "Segoe UI", "Cascadia Mono", "Cascadia Code", "Consolas",
    };

    readonly Options opt;
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly Random rng = new(7);
    readonly List<(TextBlock tab, TextBlock prop, Func<string> value)> live = new();
    double total, biggest, top;
    double firstFrameMs = -1;
    bool suppressThemeEvent;

    nint Hwnd => new WindowInteropHelper(this).Handle;

    public MainWindow(Options o)
    {
        opt = o;
        InitializeComponent();

        for (int i = 1; i <= 40; i++)
            Rows.Items.Add($"{i,2}.  Character {i}   WAR/NIN   {rng.Next(10_000, 2_000_000):N0}");

        BuildNumbers();
        BuildText();

        suppressThemeEvent = true;
        (o.Theme == ThemeMode.Light ? ThemeLight : o.Theme == ThemeMode.Dark ? ThemeDark : ThemeSystem).IsChecked = true;
        suppressThemeEvent = false;

        SourceInitialized += (_, _) =>
        {
            PlaceOnMonitor();
            if (opt.ForceMica) Native.SetInt(Hwnd, Native.DWMWA_SYSTEMBACKDROP_TYPE, 2 /* DWMSBT_MAINWINDOW = Mica */);
        };
        ContentRendered += async (_, _) =>
        {
            if (firstFrameMs < 0) firstFrameMs = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;
            UpdateReadout();
            if (opt.SnapDir is not null) await SnapAll();
        };
        DpiChanged += (_, _) => UpdateReadout();

        // 4x a second, as the real clock and DPS decay will tick.
        new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Normal, (_, _) => Tick(), Dispatcher);
        new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => UpdateReadout(), Dispatcher);
    }

    // ---------------------------------------------------------------- theme

    void OnTheme(object sender, RoutedEventArgs e)
    {
        if (suppressThemeEvent) return;
        Application.Current.ThemeMode =
            sender == ThemeLight ? ThemeMode.Light : sender == ThemeDark ? ThemeMode.Dark : ThemeMode.System;
        // Mica is re-applied by the theme on a switch; a forced one has to be redone.
        if (opt.ForceMica) Native.SetInt(Hwnd, Native.DWMWA_SYSTEMBACKDROP_TYPE, 2);
        Dispatcher.BeginInvoke(UpdateReadout, DispatcherPriority.Background);
    }

    void PlaceOnMonitor()
    {
        var mons = Native.Monitors();
        if (opt.Monitor < 1 || opt.Monitor > mons.Count) return;
        var m = mons[opt.Monitor - 1];
        // Physical pixels; WPF's Left/Top are DIPs of whichever monitor it thinks it is on.
        SetWindowPos(Hwnd, 0, m.Work.Left + 60, m.Work.Top + 60, 0, 0, 0x1 | 0x4 | 0x10);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);

    // -------------------------------------------------------------- numbers

    void BuildNumbers()
    {
        NumbersHost.Children.Add(Heading("Live tiles, 4× a second. Top row: Typography.NumeralAlignment=Tabular. Bottom row: the font's default figures."));

        var tabRow = new WrapPanel();
        var propRow = new WrapPanel();
        void Tile(string label, Func<string> value)
        {
            var t = NumberTile(label, true, out var tv);
            var p = NumberTile(label, false, out var pv);
            tabRow.Children.Add(t);
            propRow.Children.Add(p);
            live.Add((tv, pv, value));
        }
        Tile("Total damage", () => total.ToString("N0", CultureInfo.InvariantCulture));
        Tile("Elapsed", () => FmtClock(clock.Elapsed));
        Tile("Party DPS", () => (total / Math.Max(1, clock.Elapsed.TotalSeconds)).ToString("N1", CultureInfo.InvariantCulture));
        Tile("Top DPS", () => top.ToString("N1", CultureInfo.InvariantCulture));
        Tile("Biggest hit", () => biggest.ToString("N0", CultureInfo.InvariantCulture));
        Tile("Fast counter", () => (clock.Elapsed.TotalMilliseconds / 7).ToString("N0", CultureInfo.InvariantCulture));
        NumbersHost.Children.Add(tabRow);
        NumbersHost.Children.Add(propRow);

        NumbersHost.Children.Add(Heading("Measured widths (DIPs) at 14px: a font has tabular figures when 1111111 and 0000000 measure the same."));
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        string[] cols = { "Family", "Installed", "1111111", "0000000", "1111111 tab", "0000000 tab", "Verdict" };
        for (int c = 0; c < cols.Length; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = c == 0 ? new GridLength(220) : new GridLength(110) });
        grid.RowDefinitions.Add(new RowDefinition());
        for (int c = 0; c < cols.Length; c++) Cell(grid, 0, c, cols[c], bold: true);
        int row = 1;
        foreach (var fam in FontCandidates)
        {
            grid.RowDefinitions.Add(new RowDefinition());
            bool installed = IsInstalled(fam);
            double w1 = MeasureWidth(fam, "1111111", false), w0 = MeasureWidth(fam, "0000000", false);
            double t1 = MeasureWidth(fam, "1111111", true), t0 = MeasureWidth(fam, "0000000", true);
            string verdict = !installed ? "falls back"
                : Math.Abs(w1 - w0) < 0.01 ? "tabular by default"
                : Math.Abs(t1 - t0) < 0.01 ? "tabular with the setting"
                : "proportional only";
            Cell(grid, row, 0, fam);
            Cell(grid, row, 1, installed ? "yes" : "NO");
            Cell(grid, row, 2, w1.ToString("0.00"));
            Cell(grid, row, 3, w0.ToString("0.00"));
            Cell(grid, row, 4, t1.ToString("0.00"));
            Cell(grid, row, 5, t0.ToString("0.00"));
            Cell(grid, row, 6, verdict, bold: true);
            row++;
        }
        NumbersHost.Children.Add(grid);
    }

    Border NumberTile(string label, bool tabular, out TextBlock value)
    {
        value = new TextBlock
        {
            FontFamily = (FontFamily)FindResource("Numbers"),
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Right,
        };
        Typography.SetNumeralAlignment(value, tabular ? FontNumeralAlignment.Tabular : FontNumeralAlignment.Normal);
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = label + (tabular ? "  (tabular)" : "  (default)"), FontSize = 12 });
        sp.Children.Add(value);
        var b = new Border { Width = 176, Child = sp };
        b.SetResourceReference(StyleProperty, "Card");
        return b;
    }

    static string FmtClock(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";

    void Tick()
    {
        double hit = rng.Next(80, 2400);
        total += hit * rng.Next(1, 4);
        biggest = Math.Max(biggest, hit);
        top = total / Math.Max(1, clock.Elapsed.TotalSeconds) * 0.31;
        foreach (var (tab, prop, value) in live) tab.Text = prop.Text = value();
        ClockText.Text = FmtClock(clock.Elapsed);
    }

    static bool IsInstalled(string family) =>
        Fonts.SystemFontFamilies.Any(f => f.FamilyNames.Values.Any(n => string.Equals(n, family, StringComparison.OrdinalIgnoreCase)));

    static double MeasureWidth(string family, string text, bool tabular, double size = 14, FontWeight? weight = null)
    {
        var tb = new TextBlock { Text = text, FontFamily = new FontFamily(family), FontSize = size, FontWeight = weight ?? FontWeights.Normal };
        Typography.SetNumeralAlignment(tb, tabular ? FontNumeralAlignment.Tabular : FontNumeralAlignment.Normal);
        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return tb.DesiredSize.Width;
    }

    // ----------------------------------------------------------------- text

    void BuildText()
    {
        TextHost.Children.Add(Heading("TextFormattingMode × TextRenderingMode at small sizes, in the theme's default font. Left half on a card, right half on bare Mica."));

        var modes = new (string name, TextFormattingMode fmt, TextRenderingMode ren)[]
        {
            ("Ideal · Auto", TextFormattingMode.Ideal, TextRenderingMode.Auto),
            ("Display · Auto", TextFormattingMode.Display, TextRenderingMode.Auto),
            ("Ideal · Grayscale", TextFormattingMode.Ideal, TextRenderingMode.Grayscale),
            ("Display · ClearType", TextFormattingMode.Display, TextRenderingMode.ClearType),
        };
        var outer = new Grid();
        outer.ColumnDefinitions.Add(new ColumnDefinition());
        outer.ColumnDefinitions.Add(new ColumnDefinition());
        for (int side = 0; side < 2; side++)
        {
            var sp = new StackPanel();
            foreach (var (name, fmt, ren) in modes)
            {
                sp.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 2) });
                foreach (double size in new[] { 9.0, 10, 11, 12, 13, 14 })
                {
                    var tb = new TextBlock { Text = $"{size,2}px  {Sample}", FontSize = size };
                    TextOptions.SetTextFormattingMode(tb, fmt);
                    TextOptions.SetTextRenderingMode(tb, ren);
                    sp.Children.Add(tb);
                }
            }
            FrameworkElement host = sp;
            if (side == 0)
            {
                var card = new Border { Child = sp };
                card.SetResourceReference(StyleProperty, "Card");
                host = card;
            }
            else host.Margin = new Thickness(16, 0, 0, 0);
            Grid.SetColumn(host, side);
            outer.Children.Add(host);
        }
        TextHost.Children.Add(outer);

        TextHost.Children.Add(Heading("Candidate families at 12px (Ideal · Auto)."));
        foreach (var fam in FontCandidates)
            TextHost.Children.Add(new TextBlock
            {
                Text = $"{fam,-26} {Sample}",
                FontFamily = new FontFamily(fam + ", Segoe UI"),
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 0),
            });
    }

    TextBlock Heading(string text)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        return t;
    }

    static void Cell(Grid g, int r, int c, string text, bool bold = false)
    {
        var t = new TextBlock { Text = text, Margin = new Thickness(0, 2, 8, 2), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
        Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        Grid.SetRow(t, r);
        Grid.SetColumn(t, c);
        g.Children.Add(t);
    }

    // -------------------------------------------------------------- readout

    void UpdateReadout()
    {
        if (Hwnd == 0) return;
        var sb = new StringBuilder();
        var dpi = VisualTreeHelper.GetDpi(this);
        var appsLight = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", null);
        var probe = new TextBlock();

        sb.AppendLine($"Application.ThemeMode   {Application.Current.ThemeMode.Value}");
        sb.AppendLine($"Window.ThemeMode        {ThemeMode.Value}");
        sb.AppendLine($"Windows apps theme      {(appsLight is int v ? (v == 1 ? "Light" : "Dark") : "?")}  (AppsUseLightTheme)");
        sb.AppendLine($"SystemColors.AccentColor {SystemColors.AccentColor}");
        sb.AppendLine($"Window.Background       {Describe(Background)}");
        sb.AppendLine($"DWM backdrop type       {Native.GetInt(Hwnd, Native.DWMWA_SYSTEMBACKDROP_TYPE)}  (0 auto, 1 none, 2 Mica, 3 Acrylic, 4 Mica Alt){(opt.ForceMica ? "  [forced]" : "")}");
        sb.AppendLine($"DWM immersive dark      {Native.GetInt(Hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE)}");
        sb.AppendLine($"DWM corner preference   {Native.GetInt(Hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE)}  (0 default, 1 square, 2 round, 3 small)");
        sb.AppendLine($"WPF DPI scale           {dpi.DpiScaleX:0.##} ({dpi.PixelsPerInchX:0} dpi)");
        sb.AppendLine($"Window frame (px)       {FrameText()}");
        foreach (var (m, i) in Native.Monitors().Select((m, i) => (m, i)))
            sb.AppendLine($"Monitor {i + 1}               {m.Device} {m.Bounds.Left},{m.Bounds.Top} {m.Bounds.Right - m.Bounds.Left}x{m.Bounds.Bottom - m.Bounds.Top} @ {m.Dpi} dpi ({m.Dpi / 96.0:P0}){(m.Primary ? " primary" : "")}");
        sb.AppendLine($"Default TextBlock font  {Describe(TextHost.Children.OfType<TextBlock>().FirstOrDefault()?.FontFamily ?? probe.FontFamily)} {TextHost.Children.OfType<TextBlock>().FirstOrDefault()?.FontSize}px");
        sb.AppendLine($"Window.FontFamily       {FontFamily.Source}  {FontSize}px");
        sb.AppendLine($"SystemFonts.Message     {SystemFonts.MessageFontFamily.Source} {SystemFonts.MessageFontSize}px");
        sb.AppendLine($"Theme dictionaries      {string.Join(", ", Application.Current.Resources.MergedDictionaries.Select(d => d.Source?.ToString() ?? $"(inline, {d.Count} keys)"))}");
        foreach (var key in new[] { "ContentControlThemeFontFamily", "BodyStrongTextBlockStyle", "AccentButtonStyle", "CardBackgroundFillColorDefaultBrush", "ApplicationBackgroundBrush" })
            sb.AppendLine($"  resource {key,-36} {(TryFindResource(key) is { } r ? Describe(r) : "MISSING")}");
        sb.AppendLine($"Fonts                   {string.Join(", ", FontCandidates.Select(f => f + (IsInstalled(f) ? "" : " (NOT installed)")))}");
        var variable = Fonts.SystemFontFamilies.Where(f => f.Source.StartsWith("Segoe UI Variable", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var f in variable)
            sb.AppendLine($"  {f.Source}: {string.Join(", ", f.FamilyTypefaces.Select(t => $"{t.Weight}/{t.Style}"))}");
        sb.AppendLine($"Icons font              {Describe(FindResource("Icons"))}  installed: {IsInstalled("Segoe Fluent Icons")}");
        foreach (var fam in new[] { "Segoe UI Variable Display, Segoe UI", "Segoe UI Variable Display", "Segoe UI" })
            sb.AppendLine($"Tile style widths       {fam,-36} SemiBold 28: 1111111 {MeasureWidth(fam, "1111111", false, 28, FontWeights.SemiBold):0.0} / tab {MeasureWidth(fam, "1111111", true, 28, FontWeights.SemiBold):0.0}, 0000000 {MeasureWidth(fam, "0000000", false, 28, FontWeights.SemiBold):0.0}");
        foreach (var (tab, prop, _) in live)
            sb.AppendLine($"  live tile '{tab.Text}': tabular {tab.ActualWidth:0.0}, default {prop.ActualWidth:0.0}  (family {prop.FontFamily.Source}, weight {prop.FontWeight})");
        sb.AppendLine($"Render tier             {RenderCapability.Tier >> 16}");
        sb.AppendLine($"First frame             {(firstFrameMs < 0 ? "-" : $"{firstFrameMs:0} ms after process start")}");
        sb.AppendLine($"Working set             {Environment.WorkingSet / 1048576} MB");
        Readout.Text = sb.ToString();

        StatusLine.Text = $"Theme {Application.Current.ThemeMode.Value} · backdrop {Native.GetInt(Hwnd, Native.DWMWA_SYSTEMBACKDROP_TYPE)} · " +
                          $"dark caption {Native.GetInt(Hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE)} · scale {dpi.DpiScaleX:0.##} · {FmtClock(clock.Elapsed)}";
    }

    string FrameText()
    {
        var r = Native.Frame(Hwnd);
        return $"{r.Left},{r.Top} {r.Right - r.Left}x{r.Bottom - r.Top}";
    }

    static string Describe(object o) => o switch
    {
        SolidColorBrush b => $"SolidColorBrush {b.Color}",
        FontFamily f => f.Source,
        Style s => $"Style for {s.TargetType.Name}",
        null => "null",
        _ => o.GetType().Name,
    };

    // ----------------------------------------------------------------- snap

    /// <summary>Every tab in Light, Dark and System, plus the menus open,
    /// saved as PNGs of what is actually on screen. Then exit.</summary>
    async Task SnapAll()
    {
        Directory.CreateDirectory(opt.SnapDir!);
        Topmost = true;
        Activate();
        var log = new StringBuilder();
        foreach (var theme in new[] { ThemeMode.Light, ThemeMode.Dark, ThemeMode.System })
        {
            (theme == ThemeMode.Light ? ThemeLight : theme == ThemeMode.Dark ? ThemeDark : ThemeSystem).IsChecked = true;
            await Task.Delay(900);
            string[] names = { "controls", "numbers", "text", "readout" };
            for (int i = 0; i < names.Length; i++)
            {
                Tabs.SelectedIndex = i;
                await Task.Delay(500);
                UpdateReadout();
                await Task.Delay(100);
                Native.Snap(Hwnd, Path.Combine(opt.SnapDir!, $"{theme.Value.ToLowerInvariant()}-{names[i]}.png"));
            }
            log.AppendLine($"== {theme.Value} ==").AppendLine(Readout.Text);

            if (theme != ThemeMode.System)
            {
                Tabs.SelectedIndex = 0;
                var viewMenu = (MenuItem)ViewMenu.Items[0];
                viewMenu.IsSubmenuOpen = true;
                await Task.Delay(500);
                Native.Snap(Hwnd, Path.Combine(opt.SnapDir!, $"{theme.Value.ToLowerInvariant()}-menu.png"));
                viewMenu.IsSubmenuOpen = false;

                // Popups that normally need a pointer: a tooltip and the drop-down.
                var tip = new ToolTip { Content = "Fold small contributors into one line", PlacementTarget = PlainBtn, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
                tip.IsOpen = true;
                SectionCombo.IsDropDownOpen = true;
                await Task.Delay(600);
                Native.Snap(Hwnd, Path.Combine(opt.SnapDir!, $"{theme.Value.ToLowerInvariant()}-popups.png"));
                tip.IsOpen = false;
                SectionCombo.IsDropDownOpen = false;
            }
        }
        if (opt.Report is not null) File.WriteAllText(opt.Report, log.ToString());
        Application.Current.Shutdown();
    }
}
