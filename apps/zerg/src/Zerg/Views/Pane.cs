using System.Collections;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Zerg.Views;

/// <summary>
/// A pane: a region of a section's body with a 30-unit heading and, under
/// it, whatever the pane holds. What a card was, without the frame: panes
/// lie flush against each other and are ruled off by whatever arranges
/// them (<see cref="SplitPanel"/>).
///
/// <para>The heading, left to right: a grip (the heading is the handle a
/// pane is moved by; the grip itself opens the menu of the heading),
/// <see cref="Title"/>, <see cref="Note"/>, and at its right-hand end
/// <see cref="Tools"/> (a switch that belongs to this pane and nothing
/// else), the mark that folds the pane to its heading, and Pop out, where
/// the card can float (<see cref="PopOutCommand"/>). The pane does not
/// move or fold itself: whatever arranges it (<see cref="SplitPanel"/>)
/// hears the press on its heading and the clicks of its grip and its fold
/// mark, and does it.</para>
///
/// <para><b>Folded</b> (<see cref="IsFolded"/>, or because whatever
/// arranges it says so), a pane is its heading and nothing else: what it
/// holds is collapsed, not hidden, so it costs no layout. Whatever arranges
/// the pane gives it the heading's height then; or, where the pane stands
/// alone in its column, a heading's width (<see cref="SplitPanel.RailProperty"/>),
/// and the heading is a rail with the title reading down it.</para>
///
/// <para><b>In a floating panel</b> (<see cref="Float.On"/>) there is no
/// heading: the panel's bar already names the card. The tools stay, on a
/// line of their own, and a fold means nothing there.</para>
///
/// <para>The look is the <c>ZPane</c> style in <c>Themes/Controls.xaml</c>,
/// which a <c>v:Pane</c> gets unasked.</para>
///
/// <para><b>To UI Automation a pane is a group named by its title</b>, with
/// what it holds inside it. It has to say so itself: the title is a
/// <c>TextBlock</c> in the pane's template, and WPF does not count text in
/// a control's template as something to stop at (it is taken for part of
/// the control), so without a peer of the pane's own a screen reader, and
/// a script listing controls, would find six tables and charts with no
/// names. The words are still there to be read as text.</para>
/// </summary>
public class Pane : HeaderedContentControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string),
        typeof(Pane), new FrameworkPropertyMetadata("", (d, e) =>
        {
            // A reader that is listening hears the new title (a drill-down's
            // is the action it is open on).
            if (UIElementAutomationPeer.FromElement((Pane)d) is { } peer)
                peer.RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, e.OldValue as string ?? "",
                                               e.NewValue as string ?? "");
        }));

    public static readonly DependencyProperty NoteProperty = DependencyProperty.Register(nameof(Note), typeof(string),
        typeof(Pane), new FrameworkPropertyMetadata(""));

    public static readonly DependencyProperty HintProperty = DependencyProperty.Register(nameof(Hint), typeof(string),
        typeof(Pane), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty ToolsProperty = DependencyProperty.Register(nameof(Tools), typeof(object),
        typeof(Pane), new FrameworkPropertyMetadata(null, (d, e) => ((Pane)d).OnToolsChanged(e.OldValue, e.NewValue)));

    public static readonly DependencyProperty PopOutCommandProperty = DependencyProperty.Register(nameof(PopOutCommand),
        typeof(ICommand), typeof(Pane), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty IsFoldedProperty = DependencyProperty.Register(nameof(IsFolded), typeof(bool),
        typeof(Pane), new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty IsTallProperty = DependencyProperty.Register(nameof(IsTall), typeof(bool),
        typeof(Pane), new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty SwatchProperty = DependencyProperty.Register(nameof(Swatch), typeof(System.Windows.Media.Brush),
        typeof(Pane), new FrameworkPropertyMetadata(null));

    static Pane()
    {
        // Nothing about a pane takes the keyboard: what is in it does.
        FocusableProperty.OverrideMetadata(typeof(Pane), new FrameworkPropertyMetadata(false));
        IsTabStopProperty.OverrideMetadata(typeof(Pane), new FrameworkPropertyMetadata(false));
    }

    /// <summary>What the pane is: "Actions". Pop out is named after it
    /// ("Pop out Actions"), which is the name a script presses it by.</summary>
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    /// <summary>A few words after the title, in the ink of a label. It is
    /// the first thing to give way in a narrow pane.</summary>
    public string Note { get => (string)GetValue(NoteProperty); set => SetValue(NoteProperty, value); }

    /// <summary>The note at full length, shown when the pointer rests on
    /// the title or the note: what there is no room to say on one line.</summary>
    public string? Hint { get => (string?)GetValue(HintProperty); set => SetValue(HintProperty, value); }

    /// <summary>What else stands in the heading, before the fold mark: a
    /// switch that changes this pane and nothing else. It goes wherever
    /// the pane goes, a floating panel included.</summary>
    public object? Tools { get => GetValue(ToolsProperty); set => SetValue(ToolsProperty, value); }

    /// <summary>Sends the card out to float over the game. Without one, the
    /// heading has no Pop out: a drill-down goes where its table goes.</summary>
    public ICommand? PopOutCommand { get => (ICommand?)GetValue(PopOutCommandProperty); set => SetValue(PopOutCommandProperty, value); }

    /// <summary>Folded to its heading. A pane also folds when whatever
    /// arranges it says so (<see cref="SplitPanel.FoldedProperty"/>).</summary>
    public bool IsFolded { get => (bool)GetValue(IsFoldedProperty); set => SetValue(IsFoldedProperty, value); }

    /// <summary>The heading is a tall one, 44 units: the title larger, the
    /// note on a line of its own under it, and an accent edge down the
    /// heading's left. A drill-down's, while it is open: its title is the
    /// action it is about, and its note says whose and how much. Folded,
    /// or in a floating panel, the heading is as any other pane's.</summary>
    public bool IsTall { get => (bool)GetValue(IsTallProperty); set => SetValue(IsTallProperty, value); }

    /// <summary>A colour to go before the note in a tall heading: whose
    /// action a drill-down is about.</summary>
    public System.Windows.Media.Brush? Swatch
    {
        get => (System.Windows.Media.Brush?)GetValue(SwatchProperty);
        set => SetValue(SwatchProperty, value);
    }

    // ------------------------------------------- the heading's own two controls
    //
    // The grip and the fold mark are controls in the template (Grip, a
    // PaneGrip; Fold, a button). What they do is the SplitPanel's, which
    // hears their clicks; what they are called is said here, because it is
    // made of what the pane is called and whether it is folded: "Fold
    // Actions", "Unfold Actions", "Move Actions". The name is the one that
    // does not change (SplitPanel.Title, where the pane's child has one: a
    // drill-down's heading reads the action it is open on, and its mark is
    // still "Fold Drill-down"). In a floating panel there is no heading,
    // and the two have no name: a script reads the names of hidden things
    // too.

    ButtonBase? fold;
    PaneGrip? grip;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        fold = GetTemplateChild("Fold") as ButtonBase;
        grip = GetTemplateChild("Grip") as PaneGrip;
        Call();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == TitleProperty || e.Property == IsFoldedProperty || e.Property == SplitPanel.FoldedProperty ||
            e.Property == SplitPanel.TitleProperty || e.Property == Float.OnProperty)
            Call();
    }

    void Call()
    {
        if (fold is null && grip is null) return;
        string name = SplitPanel.GetTitle(this) is { Length: > 0 } given ? given : Title ?? "";
        // Nameless too while the pane has no title (Compare's, until both
        // slots hold a parse): "Fold " is no name.
        bool floating = Float.GetOn(this) || name.Length == 0;
        bool folded = SplitPanel.GetFolded(this);
        if (fold != null)
        {
            Say(fold, floating ? "" : (folded ? "Unfold " : "Fold ") + name);
            fold.ToolTip = folded ? "Open this pane again" : "Fold this pane to its heading";
            // A pane that has folded itself has nothing to open onto.
            fold.IsEnabled = folded || !IsFolded;
        }
        if (grip != null) Say(grip, floating ? "" : "Move " + name);

        static void Say(UIElement part, string name)
        {
            if (AutomationProperties.GetName(part) != name) AutomationProperties.SetName(part, name);
        }
    }

    // The tools belong to the pane as its header and its content do, so
    // what is in them finds the pane's data context and resources.
    void OnToolsChanged(object? old, object? now)
    {
        if (old != null) RemoveLogicalChild(old);
        if (now != null) AddLogicalChild(now);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    /// <summary>A group, named by the pane's title.</summary>
    sealed class Peer(Pane owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
        protected override string GetClassNameCore() => nameof(Pane);

        protected override string GetNameCore()
        {
            // A name given in XAML outranks the title.
            var given = base.GetNameCore();
            return string.IsNullOrEmpty(given) ? owner.Title ?? "" : given;
        }

        // Whether it is a control is left to WPF: yes while it can be seen,
        // no while it cannot (the panes of the section that is not on screen).
    }

    protected override IEnumerator LogicalChildren
    {
        get
        {
            var inherited = base.LogicalChildren;
            if (Tools is not { } tools) return inherited;
            var all = new ArrayList();
            while (inherited.MoveNext()) all.Add(inherited.Current);
            all.Add(tools);
            return all.GetEnumerator();
        }
    }
}
