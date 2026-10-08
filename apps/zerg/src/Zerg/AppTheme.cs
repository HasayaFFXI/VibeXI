using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Zerg;

/// <summary>What the Settings page offers: follow Windows, or one of the two.</summary>
enum ThemeChoice { System, Light, Dark }

/// <summary>
/// Light or dark: which set of Zerg's colours is in use.
///
/// <para>No theme of Windows' is loaded (<c>ThemeMode="None"</c> in
/// <c>App.xaml</c>): every stock control is drawn by Zerg's own templates
/// (<c>Themes/Controls.xaml</c>), and what those are drawn in is here. The
/// colours come in a light and a dark set (<c>Themes/Light.xaml</c>,
/// <c>Themes/Dark.xaml</c>): the tokens every surface, rule and piece of ink
/// is drawn in, and the colours that stand for data. This keeps the right
/// set merged into the application's resources and says when it changes, so
/// anything that hands a chart its colours can hand them over again.</para>
///
/// <para>The set is merged at run time, after <c>App.xaml</c> has been
/// read, so XAML asks for a token with <c>DynamicResource</c>; a
/// <c>StaticResource</c> to one fails at startup.</para>
///
/// <para>The colours of Compare's two runs are the user's to set, so the
/// brushes a run is drawn with are not in either file: they are made here
/// (<see cref="Runs"/>, at the end of this class) from the colour in use
/// and the theme in use, and asked for in the same way.</para>
/// </summary>
static class AppTheme
{
    static ThemeChoice mode = ThemeChoice.System;
    static ResourceDictionary? colours;
    static bool watching;

    /// <summary>Whether the dark set is the one in use.</summary>
    public static bool IsDark { get; private set; }

    /// <summary>The colour set changed: light to dark, or back.</summary>
    public static event Action? Changed;

    /// <summary>Switches the whole app to the set the choice asks for.</summary>
    public static void Apply(ThemeChoice theme)
    {
        mode = theme;
        if (!watching)
        {
            // "Follow Windows" has to notice Windows changing its mind.
            watching = true;
            SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (e.Category == UserPreferenceCategory.General)
                    Application.Current?.Dispatcher.BeginInvoke(Refresh);
            };
        }
        Refresh();
    }

    static void Refresh()
    {
        bool dark = mode == ThemeChoice.Dark || (mode != ThemeChoice.Light && WindowsIsDark());
        if (colours != null && dark == IsDark) return;
        IsDark = dark;

        var next = Load(dark);
        var merged = Application.Current.Resources.MergedDictionaries;
        // The new set in before the old one is out, so no token is ever
        // without a value. Last in the list, and the last answers first.
        merged.Add(next);
        if (colours != null) merged.Remove(colours);
        // The set just taken out is the other theme's, for whoever asks for it.
        other = colours;
        colours = next;
        // Before anyone is told: a run's installed colour, its band's
        // strength and its badge's ink are all this theme's.
        PutRuns();
        Changed?.Invoke();
    }

    /// <summary>Windows' own "app mode" setting, which is what "System"
    /// follows.</summary>
    static bool WindowsIsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            return false;
        }
    }

    static ResourceDictionary Load(bool dark) => new()
    {
        Source = new Uri($"/Zerg;component/Themes/{(dark ? "Dark" : "Light")}.xaml", UriKind.Relative),
    };

    /// <summary>
    /// The colour set to read from. Usually the one in use; a floating panel
    /// is dark whatever the theme, so under the light theme it asks for the
    /// other one, which is loaded the first time that happens.
    /// </summary>
    static ResourceDictionary? Set(bool dark) => dark == IsDark ? colours : other ??= Load(dark);

    static ResourceDictionary? other;

    /// <summary>
    /// A job's own colour, by the game's three-letter name, or null when it
    /// has none. Four jobs have no colour, and neither has "no job": the
    /// caller has something better to fall back to than a made-up hue, the
    /// character's own slot.
    /// </summary>
    public static Color? Job(string? abbrev, bool dark) =>
        !string.IsNullOrEmpty(abbrev) && Set(dark)?["Job" + abbrev.ToUpperInvariant()] is Color c ? c : null;

    /// <summary>How many fallback colours there are: one per character of a full alliance.</summary>
    public const int Slots = 18;

    /// <summary>
    /// The fallback colour for a slot. Past the last slot the colours repeat:
    /// that takes more characters than an alliance holds, and every panel
    /// names its rows as well as colouring them.
    /// </summary>
    public static Color Series(int slot, bool dark) =>
        Set(dark)?[$"Series{(slot % Slots + Slots) % Slots + 1}"] is Color c ? c : Colors.Gray;

    /// <summary>
    /// A token's colour by its key in the theme files ("Bg1", "Text2",
    /// "Crit"), for code that has to work a colour out and cannot ask XAML
    /// for a brush: how strong a row's shade may be, say. Gray for a key
    /// neither file has.
    ///
    /// <para>These are the application's values. A floating panel redefines
    /// some brushes for itself (<c>Themes/Panel.xaml</c>: its ink is a step
    /// lighter, its surfaces are white at a strength), and this does not
    /// read that file.</para>
    /// </summary>
    public static Color Token(string key, bool dark) => Set(dark)?[key] is Color c ? c : Colors.Gray;

    // ------------------------------------------ the two runs of a comparison
    //
    // Compare's runs, A and B, each have a colour that says "which run"
    // and nothing else, and each is the user's to set. Six brushes follow
    // from the two colours, and XAML asks for them by key like any token:
    //
    //   RunABrush, RunBBrush          the colour: a badge, a slot's edge, the
    //                                 mark before a job, a chart's columns
    //   RunABandBrush, RunBBandBrush  the same colour at the strength a band
    //                                 behind a row may have (RunColours.Band)
    //   RunAInkBrush, RunBInkBrush    the letter on the run's badge
    //
    // They are not in the theme files, which hold only the installed
    // colours (the Colors RunA and RunB): they are made here, frozen, and
    // kept in a dictionary of their own among the application's, replaced
    // whole when a colour or the theme changes. Whatever asked with
    // DynamicResource follows; nothing is counted or laid out again.

    /// <summary>The runs' colours as set: "#3987E5", or null for the
    /// installed colour of the theme in use.</summary>
    static string? runA, runB;
    static ResourceDictionary? runs;

    /// <summary>
    /// Sets the two runs' colours, each as it is written ("#3987E5") or
    /// null for the installed one. Anything that is not a colour counts as
    /// null. Takes effect at once, in every theme: a colour someone picked
    /// is theirs under both. Before the first theme is applied it is only
    /// remembered.
    /// </summary>
    public static void Runs(string? a, string? b)
    {
        (a, b) = (Core.RunColours.Normal(a), Core.RunColours.Normal(b));
        if (a == runA && b == runB) return;
        (runA, runB) = (a, b);
        if (colours != null) PutRuns();
    }

    /// <summary>The colour run A (or B) is drawn in: the one set, or the
    /// installed one of the theme in use. For code that hands a chart its
    /// colours; XAML asks for <c>RunABrush</c>.</summary>
    public static Color Run(bool a) =>
        Core.RunColours.TryParse(a ? runA : runB, out var c) ? Color.FromRgb(c.R, c.G, c.B) : Token(a ? "RunA" : "RunB", IsDark);

    /// <summary>How strong that run's band is behind a row, in whole percent, in the theme in use.</summary>
    public static int RunBand(bool a) =>
        Core.RunColours.Band(Rgb(Run(a)), Rgb(Token("Bg1", IsDark)), Rgb(Token("Crit", IsDark)), Rgb(Token("Live", IsDark)));

    static (byte R, byte G, byte B) Rgb(Color c) => (c.R, c.G, c.B);

    static void PutRuns()
    {
        // The pale ink a badge's letter may take: the dark theme's own text
        // colour; white under the light theme, whose text is near-black.
        var next = RunBrushes(Run(true), Run(false), Token("Bg1", IsDark), Token("Crit", IsDark), Token("Live", IsDark),
                              IsDark ? Token("Text1", true) : Colors.White);
        var merged = Application.Current.Resources.MergedDictionaries;
        // The new six in the place of the old six, in one step: no key is
        // ever without a value, and every window is told once, not twice
        // (once for the new set and once for the old one going). A colour
        // picker asks for this at every point its marker is dragged
        // through, and being told is a walk of everything in the window.
        int at = runs is null ? -1 : merged.IndexOf(runs);
        if (at >= 0) merged[at] = next;
        else merged.Add(next);
        runs = next;
    }

    /// <summary>
    /// The six brushes of two runs' colours, on a surface, with the red a
    /// worse change is written in, the green of a better one, and the pale
    /// ink a badge's letter may take. All frozen. A band's strength is its brush's own, so whatever
    /// is filled with it needs no opacity of its own.
    /// </summary>
    public static ResourceDictionary RunBrushes(Color a, Color b, Color surface, Color crit, Color live, Color pale)
    {
        var set = new ResourceDictionary();
        void Put(string run, Color c)
        {
            var ink = Core.RunColours.InkOn(Rgb(c), Rgb(pale));
            set["Run" + run + "Brush"] = Frozen(new SolidColorBrush(c));
            set["Run" + run + "BandBrush"] =
                Frozen(new SolidColorBrush(c) { Opacity = Core.RunColours.Band(Rgb(c), Rgb(surface), Rgb(crit), Rgb(live)) / 100.0 });
            set["Run" + run + "InkBrush"] = Frozen(new SolidColorBrush(Color.FromRgb(ink.R, ink.G, ink.B)));
        }
        Put("A", a);
        Put("B", b);
        return set;
    }

    static SolidColorBrush Frozen(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}
