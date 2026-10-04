using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Zerg;

/// <summary>
/// Light or dark, for the parts of Zerg the Fluent theme doesn't colour.
///
/// <para>Windows' Fluent theme (<see cref="Application.ThemeMode"/>) restyles
/// every control and supplies the text and surface brushes. Zerg's own
/// colours, the ones that stand for data, come in a light and a dark set
/// (<c>Themes/Light.xaml</c>, <c>Themes/Dark.xaml</c>); this keeps the right
/// set merged into the application's resources and says when it changes, so
/// anything that hands a chart its colours can hand them over again.</para>
/// </summary>
static class AppTheme
{
    static ThemeMode mode = ThemeMode.System;
    static ResourceDictionary? colours;
    static bool watching;

    /// <summary>Whether the dark set is the one in use.</summary>
    public static bool IsDark { get; private set; }

    /// <summary>The colour set changed: light to dark, or back.</summary>
    public static event Action? Changed;

    /// <summary>Switches the whole app: the Fluent theme, then Zerg's colours.</summary>
    public static void Apply(ThemeMode theme)
    {
        mode = theme;
        Application.Current.ThemeMode = theme;
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
        bool dark = mode == ThemeMode.Dark || (mode != ThemeMode.Light && WindowsIsDark());
        if (colours != null && dark == IsDark) return;
        IsDark = dark;

        var next = Load(dark);
        var merged = Application.Current.Resources.MergedDictionaries;
        merged.Add(next);
        if (colours != null) merged.Remove(colours);
        // The set just taken out is the other theme's, for whoever asks for it.
        other = colours;
        colours = next;
        Changed?.Invoke();
        // Once the windows have been laid out again in the new theme.
        Application.Current.Dispatcher.BeginInvoke(RereadRows, DispatcherPriority.ApplicationIdle);
    }

    /// <summary>
    /// Tells a screen reader to look at every list again.
    ///
    /// <para>A theme switch gives each list a new template, and the list
    /// makes its rows afresh. What a screen reader is told about a row is
    /// remembered per item, though, not per row drawn, and an item that was
    /// there before the switch goes on answering with the text blocks of the
    /// row that was thrown away: every figure in it stands still at what it
    /// was at the switch, while the screen moves on. Nothing in WPF drops
    /// that memory unless a reader happens to be listening for changes, so
    /// it is dropped here, top to bottom, in every window.</para>
    ///
    /// <para>Only where something has asked: a window that no reader has
    /// looked at has no such memory, and is left without one.</para>
    /// </summary>
    static void RereadRows()
    {
        // Queued for later, and by then Zerg may be closing.
        if (Application.Current is not { } app) return;
        foreach (Window window in app.Windows)
            if (UIElementAutomationPeer.FromElement(window) is { } root) Reread(root);
    }

    static void Reread(AutomationPeer peer)
    {
        peer.ResetChildrenCache();
        foreach (var child in peer.GetChildren() ?? []) Reread(child);
    }

    /// <summary>Windows' own "app mode" setting, which is what the Fluent
    /// theme follows when it is set to System.</summary>
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
}
