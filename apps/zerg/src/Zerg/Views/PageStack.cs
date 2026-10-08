using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Zerg.Views;

/// <summary>
/// The page: everything between the command bar and the status line that
/// may scroll. A scroll viewer that tells what is in it how tall the window
/// has left it, which a scroll viewer otherwise keeps to itself (it asks
/// its content how tall it would like to be, with no limit).
///
/// <para>The content is a <see cref="PageStack"/>, which decides with that
/// whether the page fits the window exactly (the panes of a section, in a
/// window wide enough for them) or is as tall as it is and scrolls.</para>
///
/// <para>A class of Zerg's own finds no style by its type: whoever uses
/// one names the scroll viewer's (<c>ZScrollViewer</c>).</para>
/// </summary>
public sealed class PageView : ScrollViewer
{
    protected override Size MeasureOverride(Size constraint)
    {
        // Before the content is measured, in the same pass. The bars are
        // drawn over the content (ZScrollViewer), so all of this is its.
        if (Content is PageStack stack && stack.Room != constraint.Height) stack.Room = constraint.Height;
        return base.MeasureOverride(constraint);
    }
}

/// <summary>
/// What a <see cref="PageView"/> scrolls: its children one under another,
/// each the page's whole width. All but the last are as tall as they ask to
/// be (the band of figures, the characters' line; over the Compare section,
/// its own bands). The last is the section.
///
/// <para><b>Filling</b> (<see cref="Fill"/>, in a window at least
/// <see cref="NarrowWidth"/> wide), the last child is given exactly what
/// the others leave of the window (<see cref="Room"/>): the page is as tall
/// as the window has room for, nothing scrolls, and what is above the
/// section stays put as if it were outside the page. A section that cannot
/// be made that short says how much it needs (<see cref="NeedsProperty"/>:
/// a <see cref="SplitPanel"/>'s panes have least sizes), is given that
/// instead, and then the page scrolls after all.</para>
///
/// <para><b>Otherwise</b> the last child is as tall as it asks to be, and
/// the page scrolls when that is more than the window has: the Settings
/// page; and every section in a narrow window,
/// where the panes stand in one column (<see cref="SplitPanel.StackedProperty"/>,
/// which this sets for everything in the page) and the figures and the
/// characters scroll away with them, so a small window is not all bands.</para>
///
/// <para><b>Narrow</b> is under <see cref="NarrowWidth"/>, or under what
/// the section says its panes need side by side
/// (<see cref="NeedsWidthProperty"/>), whichever is more. The installed
/// arrangements need less than <see cref="NarrowWidth"/>; one a player has
/// made with three panes in a row needs more, and in a window too narrow
/// for it the panes stand in one column rather than be squeezed under
/// their least width.</para>
/// </summary>
public sealed class PageStack : Panel
{
    /// <summary>How tall the window has left the page. The <see cref="PageView"/> says.</summary>
    public static readonly DependencyProperty RoomProperty = DependencyProperty.Register(nameof(Room), typeof(double),
        typeof(PageStack), new FrameworkPropertyMetadata(double.PositiveInfinity, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>The last child takes what the others leave of the room.</summary>
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(bool),
        typeof(PageStack), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>The width under which nothing fills and panes are stacked (NarrowWidth in App.xaml).</summary>
    public static readonly DependencyProperty NarrowWidthProperty = DependencyProperty.Register(nameof(NarrowWidth),
        typeof(double), typeof(PageStack), new FrameworkPropertyMetadata(700.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>On the last child, or on something directly in it: the
    /// least height it can be shown in, which it says while it is measured.
    /// An element cannot ask for more than it was offered (WPF cuts what it
    /// asks for down to that), so this is how it says the offer was too
    /// small.</summary>
    public static readonly DependencyProperty NeedsProperty = DependencyProperty.RegisterAttached("Needs", typeof(double),
        typeof(PageStack), new FrameworkPropertyMetadata(0.0, OnNeedsChanged));

    /// <summary>On the last child, or on something directly in it: the
    /// least width its panes can stand side by side in, as they are
    /// arranged. Said while it is measured, stacked or not, so that the
    /// answer does not depend on what the page then does with it.</summary>
    public static readonly DependencyProperty NeedsWidthProperty = DependencyProperty.RegisterAttached("NeedsWidth", typeof(double),
        typeof(PageStack), new FrameworkPropertyMetadata(0.0, OnNeedsChanged));

    /// <summary>
    /// What a section needs has changed, so the page it is in measures
    /// again.
    ///
    /// <para>Said while the page is measuring the section, this does
    /// nothing, and nothing is wanted: the page reads the answer as soon as
    /// the section has been measured (WPF does not let an element that is
    /// being measured be asked to measure again). Said at any other time,
    /// it is the only thing that tells the page. A drill-down opening, or a
    /// card floating or coming back, makes the panes' panel measure again
    /// by itself, in the room it had; an element cannot ask for more than
    /// that room, so to the page nothing has changed, and it kept the old
    /// room until the window was next resized: panes cut off under the
    /// window's edge with nothing to scroll, or a page that scrolled with
    /// nothing to scroll to.</para>
    /// </summary>
    static void OnNeedsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        for (var at = d as Visual; at != null; at = VisualTreeHelper.GetParent(at) as Visual)
            if (at is PageStack page)
            {
                page.InvalidateMeasure();
                return;
            }
    }

    public static double GetNeeds(DependencyObject d) => (double)d.GetValue(NeedsProperty);
    public static void SetNeeds(DependencyObject d, double value) => d.SetValue(NeedsProperty, value);
    public static double GetNeedsWidth(DependencyObject d) => (double)d.GetValue(NeedsWidthProperty);
    public static void SetNeedsWidth(DependencyObject d, double value) => d.SetValue(NeedsWidthProperty, value);

    public double Room { get => (double)GetValue(RoomProperty); set => SetValue(RoomProperty, value); }
    public bool Fill { get => (bool)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public double NarrowWidth { get => (double)GetValue(NarrowWidthProperty); set => SetValue(NarrowWidthProperty, value); }

    /// <summary>The children that take room, in order.</summary>
    List<UIElement> Shown()
    {
        var shown = new List<UIElement>(InternalChildren.Count);
        foreach (UIElement child in InternalChildren)
            if (child.Visibility != Visibility.Collapsed) shown.Add(child);
        return shown;
    }

    protected override Size MeasureOverride(Size available)
    {
        var shown = Shown();
        // Narrow for the section as it was last measured; if measuring it
        // now gives another answer (its arrangement has changed since),
        // once more with that. The answer depends on the arrangement and
        // on nothing the page does, so the second time is the last.
        bool narrow = Narrow(available.Width, shown);
        var size = Lay(available, shown, narrow);
        if (Narrow(available.Width, shown) != narrow) size = Lay(available, shown, !narrow);
        return size;
    }

    /// <summary>Too narrow for the panes of the section side by side.</summary>
    bool Narrow(double width, List<UIElement> shown) =>
        width < Math.Max(NarrowWidth, Fill && shown.Count > 0 ? NeededWidth(shown[^1]) : 0);

    Size Lay(Size available, List<UIElement> shown, bool narrow)
    {
        // Said before anything is measured: what is in the page asks it.
        if (SplitPanel.GetStacked(this) != narrow) SplitPanel.SetStacked(this, narrow);

        double above = 0, widest = 0;
        for (int i = 0; i < shown.Count; i++)
        {
            bool last = i == shown.Count - 1;
            double height = last && Fill && !narrow && !double.IsInfinity(Room)
                ? Math.Max(0, Room - above)
                : double.PositiveInfinity;
            shown[i].Measure(new Size(available.Width, height));
            // Offered less than it can be shown in: it has that, and the
            // page is taller than the window.
            if (!double.IsInfinity(height) && Needed(shown[i]) is var needed && needed > height)
            {
                height = needed;
                shown[i].Measure(new Size(available.Width, height));
            }
            widest = Math.Max(widest, shown[i].DesiredSize.Width);
            if (last) return new Size(double.IsInfinity(available.Width) ? widest : available.Width,
                                      above + (double.IsInfinity(height) ? shown[i].DesiredSize.Height
                                                                         : Math.Max(height, shown[i].DesiredSize.Height)));
            above += shown[i].DesiredSize.Height;
        }
        return new Size(double.IsInfinity(available.Width) ? 0 : available.Width, 0);
    }

    /// <summary>The most that an element, or anything shown directly in it, says it needs.</summary>
    static double Needed(UIElement element) => Most(element, NeedsProperty);

    /// <summary>And the widest.</summary>
    static double NeededWidth(UIElement element) => Most(element, NeedsWidthProperty);

    static double Most(UIElement element, DependencyProperty said)
    {
        double most = (double)element.GetValue(said);
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            if (VisualTreeHelper.GetChild(element, i) is UIElement { Visibility: Visibility.Visible } inner)
                most = Math.Max(most, (double)inner.GetValue(said));
        return most;
    }

    protected override Size ArrangeOverride(Size final)
    {
        var shown = Shown();
        double y = 0;
        for (int i = 0; i < shown.Count; i++)
        {
            bool last = i == shown.Count - 1;
            // The last has whatever is left: what it was measured in when
            // the page fills the window, its own height when it does not.
            double height = last ? Math.Max(shown[i].DesiredSize.Height, final.Height - y) : shown[i].DesiredSize.Height;
            shown[i].Arrange(new Rect(0, y, final.Width, height));
            y += height;
        }
        return final;
    }
}
