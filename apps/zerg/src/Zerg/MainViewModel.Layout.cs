using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zerg.Core.Layout;

namespace Zerg;

// How each section's panes are arranged: a tree of splits per section
// (Zerg.Core/Layout/SplitTree), which the section's v:SplitPanel is bound
// to and arranges its panes by.
//
// A player changes one in the window: a rule dragged, a pane folded by the
// mark in its heading, a pane carried by its heading and put down somewhere
// else. The panel works out the tree that makes and hands it here
// (Rearrange); the panel follows, since these are observable, and the three
// are written to the settings (settings.layouts), which is where they come
// from at the next start. Every change to a tree is a function in
// Zerg.Core that returns another tree: nothing here does arithmetic.
//
// The View section has no tree of its own: it shows the Damage or the
// Healing section's panes, so it has that section's arrangement, and
// rearranging them there rearranges that section.
public sealed partial class MainViewModel
{
    /// <summary>The Damage section's panes: bars, actions, line, types,
    /// targets, drill. A tree saved by a build that had only four of them
    /// is given the other two where they are installed (SplitTree.Repair).</summary>
    [ObservableProperty] private SplitNode damageLayout = PaneLayouts.Damage;

    /// <summary>The Healing section's panes: hbars, hactions, hline, hdrill.</summary>
    [ObservableProperty] private SplitNode healingLayout = PaneLayouts.Healing;

    /// <summary>The Compare section's panes, under its bands: cactors, cpace,
    /// ckinds, ctargets. One arrangement for its Damage and its Healing
    /// mode. The section's view model hands it on (CompareViewModel.Layout),
    /// since the section's data context is that and not this.</summary>
    [ObservableProperty] private SplitNode compareLayout = PaneLayouts.Compare;

    /// <summary>
    /// The arrangement is locked: no rule is dragged, no pane is moved, and
    /// the grips in the headings are not drawn. Folding still works (it is
    /// a press on a mark, not something done by accident with a drag), and
    /// so does Pop out. Remembered between runs.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LayoutTip))]
    private bool layoutLocked;

    /// <summary>What the right-hand end of the status line says while the
    /// arrangement is in someone's hand: a pane being carried, a rule being
    /// dragged. Empty otherwise. The window sets it from what the panes'
    /// panel says.</summary>
    [ObservableProperty] private string layoutNote = "";

    DispatcherTimer? layoutSaver;
    /// <summary>What was last done to an arrangement, for the log.</summary>
    string layoutWhat = "";
    /// <summary>The trees are being read from the settings, not changed by anyone.</summary>
    bool readingLayouts;

    /// <summary>
    /// The three arrangements and the lock as the settings have them. A
    /// settings file from before they were saved has none and gives the
    /// installed ones; so does anything under the key that is not a tree;
    /// and a tree that names panes this build has not got, or leaves one
    /// out, is repaired (PaneLayouts.Read). From the constructor.
    /// </summary>
    void ReadLayouts()
    {
        readingLayouts = true;
        DamageLayout = PaneLayouts.Read(settings.Layouts, PaneLayouts.DamageSection);
        HealingLayout = PaneLayouts.Read(settings.Layouts, PaneLayouts.HealingSection);
        CompareLayout = PaneLayouts.Read(settings.Layouts, PaneLayouts.CompareSection);
        LayoutLocked = settings.LayoutLocked;
        readingLayouts = false;
        if (settings.Layouts != null)
            Log.Write("layouts read: " + string.Join(", ", PaneLayouts.Sections.Select(s =>
                s + (LayoutOf(s) == PaneLayouts.Installed(s) ? " as installed" : " rearranged"))) +
                (LayoutLocked ? "; locked" : ""));
    }

    /// <summary>A section's arrangement by the section's name.</summary>
    SplitNode? LayoutOf(string section) => section switch
    {
        PaneLayouts.DamageSection => DamageLayout,
        PaneLayouts.HealingSection => HealingLayout,
        PaneLayouts.CompareSection => CompareLayout,
        _ => null,
    };

    /// <summary>
    /// A section's panes arranged another way: the tree a drag, a fold or a
    /// move made. On screen at once; in the settings object at once, so
    /// that closing loses nothing; written to the file once the arrangement
    /// has stood still (a rule moved with the arrow keys reports every
    /// press).
    /// </summary>
    /// <param name="section">"Damage", "Healing" or "Compare". The View
    /// section's panes are the Damage or the Healing section's.</param>
    /// <param name="what">What was done, for the log.</param>
    public void Rearrange(string section, SplitNode tree, string what)
    {
        if (PaneLayouts.Installed(section) is not { } installed) return;
        // Whatever made it, the tree kept has every pane of the section, once.
        tree = SplitTree.Repair(tree, installed);
        if (tree == LayoutOf(section)) return;
        layoutWhat = section + ": " + what;
        switch (section)
        {
            case PaneLayouts.DamageSection: DamageLayout = tree; break;
            case PaneLayouts.HealingSection: HealingLayout = tree; break;
            default: CompareLayout = tree; break;
        }
    }

    partial void OnDamageLayoutChanged(SplitNode value) => LayoutsChanged();
    partial void OnHealingLayoutChanged(SplitNode value) => LayoutsChanged();

    partial void OnCompareLayoutChanged(SplitNode value)
    {
        Compare.LayoutChanged();
        LayoutsChanged();
    }

    void LayoutsChanged()
    {
        if (readingLayouts) return;
        settings.Layouts = PaneLayouts.Write(DamageLayout, HealingLayout, CompareLayout);
        Settle(ref layoutSaver, () =>
        {
            settings.Save();
            Log.Write("layout " + layoutWhat);
        });
    }

    partial void OnLayoutLockedChanged(bool value)
    {
        if (readingLayouts) return;
        settings.LayoutLocked = value;
        settings.Save();
        Log.Write(value ? "layout locked" : "layout unlocked");
    }

    // ------------------------------------------------------ the layout button

    /// <summary>The section whose panes are on screen, by the name its
    /// arrangement is kept under; null over the Settings page and over the
    /// View section with nothing open, where there are none.</summary>
    public string? LayoutSection => IsCompare ? PaneLayouts.CompareSection
        : IsHealing ? PaneLayouts.HealingSection
        : IsDamage ? PaneLayouts.DamageSection : null;

    /// <summary>The middle line of the layout button's menu: "Reset Damage layout".</summary>
    public string ResetLayoutText => LayoutSection is { } section ? $"Reset {section} layout" : "Reset this section's layout";

    public string LayoutTip => LayoutLocked
        ? "Layout: locked. No pane can be moved and no divider dragged until it is unlocked here"
        : "Layout: lock the arrangement of the panes, or set it back";

    /// <summary>The arrangement of the section on screen, as installed
    /// again. It stays locked if it was.</summary>
    [RelayCommand(CanExecute = nameof(CanResetLayout))]
    void ResetLayout()
    {
        if (LayoutSection is { } section) Rearrange(section, PaneLayouts.Installed(section)!, "set back to the installed arrangement");
    }

    bool CanResetLayout() => LayoutSection != null;

    /// <summary>Which section's panes are on screen follows the section, the
    /// View section's side and whether a parse is open there: the menu's
    /// middle line follows all three.</summary>
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(IsDamage) or nameof(IsHealing) or nameof(IsCompare))
        {
            OnPropertyChanged(nameof(LayoutSection));
            OnPropertyChanged(nameof(ResetLayoutText));
            ResetLayoutCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    void ResetAllLayouts()
    {
        foreach (string section in PaneLayouts.Sections)
            Rearrange(section, PaneLayouts.Installed(section)!, "set back to the installed arrangement (all were)");
    }

    /// <summary>A line of a menu: choosing it flips the lock, and the tick follows.</summary>
    [RelayCommand]
    void ToggleLayoutLock() => LayoutLocked = !LayoutLocked;

    /// <summary>
    /// A drill-down that has just been given something to show is opened,
    /// if the player had folded its pane by the mark: "select an action to
    /// drill down" has to show the drill-down. Folding it again by the mark
    /// keeps what is picked.
    /// </summary>
    void OpenDrillPane(string section, string key)
    {
        if (LayoutOf(section) is { } tree && SplitTree.Leaf(tree, key) is { Folded: true })
            Rearrange(section, SplitTree.Fold(tree, key, false), "the drill-down opened for a pick");
    }
}
