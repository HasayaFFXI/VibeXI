using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Zerg.Core.Layout;

namespace Zerg.Views;

/// <summary>
/// What lies over the rule between two halves of a split, to move it by: 7
/// units across (the rule and three either side), the rule's whole length.
/// It paints the grip at its middle, and the rule's stronger look under the
/// pointer, with the keyboard, and in the hand. <see cref="SplitPanel"/>
/// makes one per rule and does what it asks.
///
/// <para>A <c>Thumb</c> for what Windows gives one (the capture, the drag
/// events, <c>IsDragging</c>). It takes the keyboard: the arrow keys across
/// its rule move it 8 units a press, and Enter sets it back, as a
/// double-click does.</para>
///
/// <para>To UI Automation it is a thumb named for the two panes it is
/// between ("Divider between Damage by character and Actions") with a
/// value: the first of those two's size across the rule, in units, between
/// the least and the most the panes' least sizes allow. Setting the value
/// moves the rule, which is how a script moves one without the mouse.</para>
///
/// <para>The look is the <c>Divider</c> style in <c>Themes/Controls.xaml</c>,
/// which hands it its three brushes.</para>
/// </summary>
public sealed class DividerThumb : Thumb
{
    public static readonly DependencyProperty WayProperty = DependencyProperty.Register(nameof(Way), typeof(SplitWay),
        typeof(DividerThumb), new FrameworkPropertyMetadata(SplitWay.Columns, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The grip at rest.</summary>
    public static readonly DependencyProperty GripProperty = DependencyProperty.Register(nameof(Grip), typeof(Brush),
        typeof(DividerThumb), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The rule under the pointer, and with the keyboard.</summary>
    public static readonly DependencyProperty HotProperty = DependencyProperty.Register(nameof(Hot), typeof(Brush),
        typeof(DividerThumb), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The rule in the hand, and the grip whenever the rule is lit.</summary>
    public static readonly DependencyProperty HeldProperty = DependencyProperty.Register(nameof(Held), typeof(Brush),
        typeof(DividerThumb), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Upright between two columns (it moves sideways), or lying between two rows.</summary>
    public SplitWay Way { get => (SplitWay)GetValue(WayProperty); set => SetValue(WayProperty, value); }
    public Brush? Grip { get => (Brush?)GetValue(GripProperty); set => SetValue(GripProperty, value); }
    public Brush? Hot { get => (Brush?)GetValue(HotProperty); set => SetValue(HotProperty, value); }
    public Brush? Held { get => (Brush?)GetValue(HeldProperty); set => SetValue(HeldProperty, value); }

    /// <summary>Which split's rule this is: <see cref="Divider.Path"/>.</summary>
    public string Path { get; init; } = "";

    /// <summary>The rule as last arranged.</summary>
    public Divider? Divider { get; internal set; }

    /// <summary>Where the rule's middle is across this thumb, in units from its left (or top) edge.</summary>
    internal double Middle { get; set; }

    /// <summary>Where the rule's leading edge was when a reader was last told.</summary>
    internal double Told { get; set; } = double.NaN;

    /// <summary>The keyboard came here by a key, not by a press: the rule
    /// is lit to say where the keyboard is, as a focus ring would. After a
    /// press it has the keyboard too, and is lit only while pointed at.</summary>
    bool keyed;

    /// <summary>The value was set through UI Automation: the first half's size.</summary>
    internal event Action<DividerThumb, double>? Set;

    const double GripLength = 22, GripWidth = 3, LitLength = 26, LitWidth = 5, LitRule = 3;

    static DividerThumb()
    {
        FocusableProperty.OverrideMetadata(typeof(DividerThumb), new FrameworkPropertyMetadata(true));
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsMouseOverProperty || e.Property == IsDraggingProperty) InvalidateVisual();
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        Keyed(InputManager.Current.MostRecentInputDevice is KeyboardDevice);
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        Keyed(false);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        Keyed(true);
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseDown(e);
        Keyed(false);
    }

    void Keyed(bool now)
    {
        if (keyed == now) return;
        keyed = now;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        // The whole of it takes the pointer, lit or not.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

        bool upright = Way == SplitWay.Columns;
        bool held = IsDragging, lit = held || IsMouseOver || (keyed && IsKeyboardFocused);
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double along = upright ? ActualHeight : ActualWidth;

        if (lit && (held ? Held : Hot) is { } rule)
        {
            // Whole pixels, centred on the rule under it.
            double thick = Math.Max(1, Math.Round(LitRule * scale)) / scale;
            dc.DrawRectangle(rule, null, upright ? new Rect(Middle - thick / 2, 0, thick, along) : new Rect(0, Middle - thick / 2, along, thick));
        }
        if ((lit ? Held : Grip) is not { } grip) return;
        double length = Math.Min(lit ? LitLength : GripLength, along), width = lit ? LitWidth : GripWidth;
        double start = (along - length) / 2;
        var box = upright ? new Rect(Middle - width / 2, start, width, length) : new Rect(start, Middle - width / 2, length, width);
        dc.DrawRoundedRectangle(grip, null, box, width / 2, width / 2);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    /// <summary>The first half's size across the rule, and the range it may have.</summary>
    (double Value, double Least, double Most) Range()
    {
        if (Divider is not { } d) return (0, 0, 0);
        bool upright = d.Way == SplitWay.Columns;
        double start = upright ? d.Room.X : d.Room.Y;
        double at = upright ? d.Line.X : d.Line.Y;
        return (at - start, d.Low - start, Math.Max(d.Low, d.High) - start);
    }

    sealed class Peer(DividerThumb owner) : FrameworkElementAutomationPeer(owner), IRangeValueProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Thumb;
        protected override string GetClassNameCore() => nameof(DividerThumb);
        protected override bool IsKeyboardFocusableCore() => owner.Focusable && owner.IsEnabled;

        protected override string GetHelpTextCore() => owner.Way == SplitWay.Columns
            ? "The Left and Right arrow keys move it. Enter, or a double-click, sets it back."
            : "The Up and Down arrow keys move it. Enter, or a double-click, sets it back.";

        public override object? GetPattern(PatternInterface pattern) =>
            pattern == PatternInterface.RangeValue ? this : base.GetPattern(pattern);

        public bool IsReadOnly => !owner.IsEnabled || owner.Divider is null or { Held: true };
        public double Value => Math.Round(owner.Range().Value, 2);
        public double Minimum => Math.Round(owner.Range().Least, 2);
        public double Maximum => Math.Round(owner.Range().Most, 2);
        public double SmallChange => SplitPanel.KeyStep;
        public double LargeChange => SplitPanel.KeyStep * 5;

        public void SetValue(double value)
        {
            if (IsReadOnly) throw new ElementNotEnabledException();
            if (double.IsNaN(value)) throw new ArgumentOutOfRangeException(nameof(value));
            owner.Set?.Invoke(owner, value);
        }
    }

    /// <summary>A reader that is listening hears where the rule is now.</summary>
    internal void Moved(double was)
    {
        if (UIElementAutomationPeer.FromElement(this) is { } peer)
            peer.RaisePropertyChangedEvent(RangeValuePatternIdentifiers.ValueProperty, was, Math.Round(Range().Value, 2));
    }
}

// The rules a player can move: a thumb over each, made, named, placed and
// taken away as the arrangement changes, and what each thumb asks for.
public partial class SplitPanel
{
    /// <summary>How far an arrow key moves a rule, in units.</summary>
    public const double KeyStep = 8;

    /// <summary>How far each side of a rule its thumb reaches: three units,
    /// so seven with the rule.</summary>
    const double Reach = 3;

    /// <summary>The thumbs, in the order of their rules. Visual children,
    /// after the panes and before the overlay.</summary>
    readonly List<DividerThumb> thumbs = [];

    /// <summary>The rule in the hand: the path of its split, and how far
    /// from the rule's leading edge the pointer took hold of it.</summary>
    (string Path, double Grab)? hold;

    /// <summary>
    /// A thumb for every rule that can be moved just now, and none for the
    /// rest: one that is held (a folded pane or a stand-in beside it, or no
    /// room to give), every one while the arrangement is locked, and all of
    /// them while the panes are stacked. Each is measured at the size it
    /// will have. From <see cref="MeasureOverride"/>.
    /// </summary>
    void Thumbs(IReadOnlyList<Divider> dividers, Size room)
    {
        bool locked = GetLocked(this);
        var wanted = new List<Divider>();
        foreach (var divider in dividers)
            if (!divider.Held && !locked) wanted.Add(divider);

        // Kept by path: the thumb in the hand, or with the keyboard, is the
        // same object from one arrangement to the next.
        for (int i = thumbs.Count - 1; i >= 0; i--)
        {
            if (wanted.Exists(d => d.Path == thumbs[i].Path)) continue;
            var gone = thumbs[i];
            thumbs.RemoveAt(i);
            gone.Divider = null;
            RemoveVisualChild(gone);
        }
        for (int i = 0; i < wanted.Count; i++)
        {
            var divider = wanted[i];
            int at = thumbs.FindIndex(t => t.Path == divider.Path);
            DividerThumb thumb;
            if (at < 0)
            {
                thumb = new DividerThumb { Path = divider.Path };
                thumb.DragStarted += OnThumbTaken;
                thumb.DragDelta += OnThumbMoved;
                thumb.DragCompleted += OnThumbLet;
                thumb.MouseDoubleClick += OnThumbTwice;
                thumb.KeyDown += OnThumbKey;
                thumb.Set += OnThumbSet;
                thumbs.Insert(i, thumb);
                AddVisualChild(thumb);
            }
            else
            {
                thumb = thumbs[at];
                if (at != i)
                {
                    thumbs.RemoveAt(at);
                    thumbs.Insert(i, thumb);
                }
            }
            thumb.Way = divider.Way;
            thumb.Cursor = divider.Way == SplitWay.Columns ? Cursors.SizeWE : Cursors.SizeNS;
            thumb.Divider = divider;
            Call(thumb, divider);
            var box = Over(divider);
            thumb.Measure(new Size(box.Width, box.Height));
        }
    }

    /// <summary>Where a rule's thumb lies: over the rule, and
    /// <see cref="Reach"/> either side of it, in whole pixels (the rule is
    /// on whole pixels, and so the thumb is, and what it paints lies
    /// squarely on the rule).</summary>
    Rect Over(Divider divider)
    {
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double reach = Math.Max(1, Math.Round(Reach * scale)) / scale;
        return divider.Way == SplitWay.Columns
            ? new Rect(divider.Line.X - reach, divider.Line.Y, divider.Line.Width + 2 * reach, divider.Line.Height)
            : new Rect(divider.Line.X, divider.Line.Y - reach, divider.Line.Width, divider.Line.Height + 2 * reach);
    }

    /// <summary>The thumbs put over their rules. From <see cref="ArrangeOverride"/>.</summary>
    void Place(IReadOnlyList<Divider> dividers)
    {
        foreach (var thumb in thumbs)
        {
            Divider? divider = null;
            foreach (var d in dividers)
                if (d.Path == thumb.Path) divider = d;
            if (divider is null)
            {
                thumb.Arrange(new Rect(0, 0, 0, 0));
                continue;
            }
            thumb.Divider = divider;
            var box = Over(divider);
            thumb.Arrange(box);
            double middle = divider.Way == SplitWay.Columns
                ? divider.Line.X + divider.Line.Width / 2 - box.X
                : divider.Line.Y + divider.Line.Height / 2 - box.Y;
            if (thumb.Middle != middle)
            {
                thumb.Middle = middle;
                thumb.InvalidateVisual();
            }
            double now = divider.Way == SplitWay.Columns ? divider.Line.X : divider.Line.Y;
            if (thumb.Told != now)
            {
                double was = thumb.Told;
                thumb.Told = now;
                if (!double.IsNaN(was)) thumb.Moved(was);
            }
        }
    }

    /// <summary>A rule is named for the two panes it is between: the first
    /// in reading order of each half.</summary>
    void Call(DividerThumb thumb, Divider divider)
    {
        if (Shown is not { } tree || SplitTree.At(tree, divider.Path) is not { } split) return;
        string name = $"Divider between {TitleOf(SplitTree.Keys(split.First)[0])} and {TitleOf(SplitTree.Keys(split.Second)[0])}";
        if (AutomationProperties.GetName(thumb) != name) AutomationProperties.SetName(thumb, name);
    }

    Divider? DividerAt(string path)
    {
        foreach (var divider in Dividers)
            if (divider.Path == path) return divider;
        return null;
    }

    /// <summary>The pointer's place across a rule: x for an upright one.</summary>
    double Across(Divider divider) =>
        divider.Way == SplitWay.Columns ? Mouse.GetPosition(this).X : Mouse.GetPosition(this).Y;

    // ---------------------------------------------------------- in the hand

    void OnThumbTaken(object sender, DragStartedEventArgs e)
    {
        var thumb = (DividerThumb)sender;
        if (thumb.Divider is not { } divider) return;
        double edge = divider.Way == SplitWay.Columns ? divider.Line.X : divider.Line.Y;
        hold = (thumb.Path, Across(divider) - edge);
        Note("Double-click a divider to set it back");
        WatchEsc(true);
        Readout(divider, edge);
    }

    /// <summary>
    /// The pointer moved with a rule in the hand: the panes are arranged as
    /// if it were let go here. Once per move of the pointer, which is when
    /// Windows says so; nothing here runs on a clock.
    ///
    /// <para>Where the pointer is, is asked of the mouse and not of the
    /// thumb: the thumb moves with the rule, so what it says it was moved
    /// by is measured from a place that has itself moved.</para>
    /// </summary>
    void OnThumbMoved(object sender, DragDeltaEventArgs e)
    {
        if (hold is not { } held || Tree is not { } tree || DividerAt(held.Path) is not { } divider) return;
        double asked = Across(divider) - held.Grab;
        var moved = SplitTree.Drag(tree, divider, asked);
        if (moved != Shown)
        {
            preview = moved == tree ? null : moved;
            InvalidateMeasure();
        }
        // Where the rule will be once that is arranged: no further than
        // the panes' least sizes let it go.
        Readout(divider, Math.Clamp(asked, divider.Low, Math.Max(divider.Low, divider.High)));
    }

    void OnThumbLet(object sender, DragCompletedEventArgs e)
    {
        if (hold is null) return;
        hold = null;
        WatchEsc(false);
        overlay.Clear();
        Note("");
        var made = preview;
        if (e.Canceled || made is null)
        {
            // Put back: the tree was never touched.
            preview = null;
            InvalidateMeasure();
            return;
        }
        Commit(made, "a divider dragged");
    }

    /// <summary>A double-click sets that one split back to the share it
    /// was installed with.</summary>
    void OnThumbTwice(object sender, MouseButtonEventArgs e)
    {
        var thumb = (DividerThumb)sender;
        if (e.ChangedButton != MouseButton.Left || Tree is not { } tree) return;
        // The second press is not the start of a drag.
        e.Handled = true;
        if (thumb.IsDragging) thumb.CancelDrag();
        SetBack(thumb, tree);
    }

    /// <summary>That one split back to the share it was installed with (a
    /// half, for a split a moved pane made).</summary>
    void SetBack(DividerThumb thumb, SplitNode tree)
    {
        double share = SplitTree.InstalledRatio(tree, PaneLayouts.Installed(Section) ?? tree, thumb.Path);
        Commit(SplitTree.SetRatio(tree, thumb.Path, share), "a divider set back");
    }

    /// <summary>With the keyboard on a rule, the arrow keys across it move
    /// it <see cref="KeyStep"/> units a press, as far as the panes' least
    /// sizes let it go; and Enter sets it back, which without it only a
    /// double-click could.</summary>
    void OnThumbKey(object sender, KeyEventArgs e)
    {
        var thumb = (DividerThumb)sender;
        if (thumb.Divider is not { } divider || Tree is not { } tree || hold != null) return;
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            SetBack(thumb, tree);
            return;
        }
        bool upright = divider.Way == SplitWay.Columns;
        int step = (e.Key, upright) switch
        {
            (Key.Left, true) or (Key.Up, false) => -1,
            (Key.Right, true) or (Key.Down, false) => 1,
            _ => 0,
        };
        // The two arrows along the rule do nothing, and are kept all the
        // same: left alone, they would take the keyboard off to whatever
        // control lies that way (seen: Up, then Left three times, moved
        // the rule once), and on a rule an arrow means the rule.
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down) e.Handled = true;
        if (step == 0) return;
        double edge = upright ? divider.Line.X : divider.Line.Y;
        Commit(SplitTree.Drag(tree, divider, edge + step * KeyStep), "a divider moved with the keys");
    }

    /// <summary>A script, or a screen reader, gave the rule a value: the
    /// size of the first of its two halves.</summary>
    void OnThumbSet(DividerThumb thumb, double value)
    {
        if (thumb.Divider is not { } divider || Tree is not { } tree) return;
        double start = divider.Way == SplitWay.Columns ? divider.Room.X : divider.Room.Y;
        Commit(SplitTree.Drag(tree, divider, start + value), "a divider set");
    }

    /// <summary>Both sizes across the rule in the hand, beside the pointer:
    /// "940 | 499". <paramref name="edge"/> is where the rule's leading
    /// edge is, or is about to be.</summary>
    void Readout(Divider divider, double edge)
    {
        bool upright = divider.Way == SplitWay.Columns;
        double start = upright ? divider.Room.X : divider.Room.Y;
        double extent = upright ? divider.Room.Width : divider.Room.Height;
        double rule = upright ? divider.Line.Width : divider.Line.Height;
        string text = string.Create(CultureInfo.InvariantCulture,
            $"{Math.Round(edge - start):0} | {Math.Round(extent - rule - (edge - start)):0}");
        overlay.Hold(upright, edge, Mouse.GetPosition(this), text);
    }
}
