using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zerg.Core;

namespace Zerg;

/// <summary>
/// One card that can float over the game in a window of its own: whether it
/// is out there now, and how see-through its backdrop is.
/// </summary>
public sealed partial class PanelInfo(PanelSet set, string key, string title) : ObservableObject
{
    public string Key { get; } = key;

    /// <summary>The card's heading, which the panel's bar shows in its place.</summary>
    public string Title { get; } = title;

    /// <summary>Floating now. The main window shows a stand-in where the card was.</summary>
    [ObservableProperty] private bool isOut;

    /// <summary>The backdrop, 15 to 100 percent. The text over it is always solid.</summary>
    [ObservableProperty] private int opacity;

    partial void OnOpacityChanged(int value) => set.OwnChanged(this, value);

    [RelayCommand]
    void PopOut() => set.Open(this);

    /// <summary>Back into the main window. Closing a panel any other way is the same thing.</summary>
    [RelayCommand]
    void Dock() => set.Close(this);

    /// <summary>Out if it is in, in if it is out: one line of a menu that lists every card.</summary>
    [RelayCommand]
    void Toggle()
    {
        if (IsOut) set.Close(this);
        else set.Open(this);
    }
}

/// <summary>
/// The floating panels: which cards can go out, the windows that are open,
/// the opacity settings (<see cref="PanelOpacities"/> has their rules), and
/// whether clicks pass through them.
///
/// <para><b>The panels that are out are remembered</b>, and come back at the
/// next start where they were. The list is written each time one goes out or
/// is docked, so it is right even after a crash. Closing Zerg closes the
/// panels without docking them: they are still on the list.</para>
///
/// <para><b>Click-through is one switch for every panel</b>, and it is not
/// remembered: Zerg always starts with panels that can be clicked, so nobody
/// is met by one they cannot move and a key they have forgotten. A panel
/// popped out while it is on comes up click-through like the rest.</para>
/// </summary>
public sealed partial class PanelSet : ObservableObject
{
    // No drill-down among them: that goes with its table, as a card under
    // it in the main window and under the action's row in the table's panel.
    public const string Line = "line", Bars = "bars", Actions = "actions";
    /// <summary>The Healing section's three, each the twin of the one above it.</summary>
    public const string HealLine = "hline", HealBars = "hbars", HealActions = "hactions";

    /// <summary>Whether a panel holds one of the Healing section's cards.</summary>
    public static bool IsHealing(string key) => key is HealLine or HealBars or HealActions;

    readonly Settings settings;
    readonly PanelOpacities opacities;
    readonly Dictionary<string, PanelInfo> panels = [];
    /// <summary>The same panels in the order they are listed: damage, then healing.</summary>
    readonly List<PanelInfo> listed = [];
    readonly Dictionary<string, Window> open = [];
    readonly DispatcherTimer saver;
    /// <summary>The values are being brought into line with the rules, not moved by a slider.</summary>
    bool syncing;
    /// <summary>Panels are opening or closing because Zerg is, not because
    /// anyone asked: the list of what was out is left as it is.</summary>
    bool keeping;

    /// <summary>What a panel is drawn at until its own slider is moved: the
    /// Settings page's slider, which can be reached with no panel open.</summary>
    [ObservableProperty] private int defaultOpacity;

    public PanelSet(Settings settings)
    {
        this.settings = settings;
        opacities = new PanelOpacities(settings.PanelOpacity, settings.PanelOpacities);
        defaultOpacity = opacities.Default;

        Add(Line, "Cumulative damage");
        Add(Bars, "Damage by character");
        Add(Actions, "Actions");
        Add(HealLine, "Cumulative healing");
        Add(HealBars, "Healing by character");
        Add(HealActions, "Heals");

        // A slider reports every step of a drag; the file is written once it settles.
        saver = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(400) };
        saver.Tick += (_, _) => Save();
    }

    void Add(string key, string title)
    {
        syncing = true;
        listed.Add(panels[key] = new PanelInfo(this, key, title) { Opacity = opacities.Of(key) });
        syncing = false;
    }

    /// <summary>A card's panel, by key: what the cards and the main window bind to.</summary>
    public PanelInfo this[string key] => panels[key];

    /// <summary>Every card that can float, damage first.</summary>
    public IReadOnlyList<PanelInfo> All => listed;

    /// <summary>Makes the window for a panel. The main window supplies it;
    /// nothing here knows what a panel looks like.</summary>
    public Func<PanelInfo, Window>? Opener { get; set; }

    internal void Open(PanelInfo panel)
    {
        if (open.ContainsKey(panel.Key) || Opener is null) return;
        var window = Opener(panel);
        open[panel.Key] = window;
        window.Closed += (_, _) =>
        {
            open.Remove(panel.Key);
            panel.IsOut = false;
            Log.Write($"panel {panel.Key} docked");
            Remember();
        };
        panel.IsOut = true;
        window.Show();
        Log.Write($"panel {panel.Key} out at {panel.Opacity}%");
        Remember();
    }

    internal void Close(PanelInfo panel)
    {
        if (open.TryGetValue(panel.Key, out var window)) window.Close();
    }

    /// <summary>Whether a window, by its handle, is one of the panels that are
    /// out: for telling that a panel has ended up with the foreground, which
    /// it is never meant to have.</summary>
    public bool Holds(nint handle) =>
        handle != 0 && open.Values.Any(w => new System.Windows.Interop.WindowInteropHelper(w).Handle == handle);

    /// <summary>At least one card is floating.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DockAllCommand))]
    private bool anyOut;

    /// <summary>How many cards are floating: the main window's status line says.</summary>
    [ObservableProperty] private int outCount;

    /// <summary>Every panel back into the main window.</summary>
    [RelayCommand(CanExecute = nameof(AnyOut))]
    void DockAll()
    {
        foreach (var window in open.Values.ToList()) window.Close();
    }

    /// <summary>Writes down which panels are out, for the next start.</summary>
    void Remember()
    {
        OutCount = open.Count;
        AnyOut = open.Count > 0;
        if (keeping) return;
        settings.OpenPanels = [.. listed.Where(p => open.ContainsKey(p.Key)).Select(p => p.Key)];
        settings.Save();
    }

    /// <summary>
    /// Puts back the panels that were out when Zerg was last closed, each at
    /// its saved place. Call once the main window is on screen: a panel with
    /// no place of its own opens beside it.
    /// </summary>
    public void Reopen()
    {
        keeping = true;
        foreach (var key in settings.OpenPanels)
            if (panels.TryGetValue(key, out var panel)) Open(panel);
        keeping = false;
        if (open.Count > 0) Log.Write("panels reopened: " + string.Join(", ", open.Keys));
    }

    /// <summary>
    /// Zerg is closing: every panel's window goes, each saving its place on
    /// the way, and the list of what was out stays for the next start. The
    /// settings are written.
    /// </summary>
    public void Leave()
    {
        keeping = true;
        foreach (var window in open.Values.ToList()) window.Close();
        if (saver.IsEnabled) Save();
    }

    // -------------------------------------------------------- click-through

    /// <summary>
    /// Clicks pass through every panel to whatever is under it, so the game
    /// can be played through a panel laid over it. A panel then cannot be
    /// pressed, moved or resized, and its bar shows only what is read.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClickThroughTip))]
    private bool clickThrough;

    /// <summary>For a menu line and the hot key, which say "the other way" and not which way.</summary>
    [RelayCommand]
    void ToggleClickThrough() => ClickThrough = !ClickThrough;

    partial void OnClickThroughChanged(bool value) => Log.Write("panels click-through " + (value ? "on" : "off"));

    /// <summary>The keys that switch <see cref="ClickThrough"/> from any
    /// application, as shown ("Ctrl+Alt+Z"); null when Windows would not
    /// give them to Zerg, which is when another program has them.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClickThroughTip))]
    private string? hotKey;

    public string ClickThroughTip =>
        (ClickThrough
            ? "Click-through is on: clicks pass through every floating panel to the game under it, and the panels " +
              "can't be pressed or moved. Click to take them back."
            : "Let clicks pass through every floating panel to the game under it. The panels can't be pressed or " +
              "moved until this is switched off.") +
        (HotKey is { } keys ? $" {keys} switches it from anywhere, the game included."
                            : " Also on the tray icon's menu.");

    // -------------------------------------------------------------- opacity

    partial void OnDefaultOpacityChanged(int value)
    {
        if (syncing) return;
        int v = opacities.SetDefault(value);
        syncing = true;
        DefaultOpacity = v;
        foreach (var p in panels.Values) p.Opacity = opacities.Of(p.Key);
        syncing = false;
        saver.Stop();
        saver.Start();
    }

    internal void OwnChanged(PanelInfo panel, int value)
    {
        if (syncing) return;
        int v = opacities.Set(panel.Key, value);
        syncing = true;
        panel.Opacity = v;
        syncing = false;
        saver.Stop();
        saver.Start();
    }

    void Save()
    {
        saver.Stop();
        settings.PanelOpacity = opacities.Default;
        settings.PanelOpacities = new Dictionary<string, int>(opacities.Own);
        settings.Save();
        Log.Write($"panel opacity {opacities.Default}%" +
                  string.Concat(opacities.Own.Select(o => $", {o.Key} {o.Value}%")));
    }
}
