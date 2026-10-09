using System.Windows;
using System.Windows.Controls;

namespace Zerg.Views;

/// <summary>
/// The command bar's line: its groups one after another, and a group that
/// does not fit beginning a new line, as in a <c>WrapPanel</c>. Two kinds of
/// child are treated differently, which is what a <c>WrapPanel</c> cannot do.
///
/// <para><b>A rule</b> (<see cref="RuleProperty"/>) is the short upright line
/// between two groups. It is drawn only between two groups that share a
/// line: when the group after it begins a new line, or there is none, the
/// rule is not drawn and takes no room, so no line begins or ends with one.</para>
///
/// <para><b>The note</b> (<see cref="EndProperty"/>) is what the bar says
/// about the last thing done ("Exported ..."). Sharing a line with the
/// groups, it is set against the line's right-hand end, away from them.
/// When there is no room beside them it takes a line of its own, from the
/// left, and is given the bar's whole width: a note longer than that wraps
/// inside it (it was measured in that width), and the bar is that much
/// taller. It comes and goes without moving a button.</para>
///
/// <para>A collapsed child takes no part. Every child is as tall as its
/// line, and places itself in that height.</para>
/// </summary>
public sealed class BarWrap : Panel
{
    public static readonly DependencyProperty RuleProperty = DependencyProperty.RegisterAttached("Rule", typeof(bool),
        typeof(BarWrap), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static bool GetRule(DependencyObject d) => (bool)d.GetValue(RuleProperty);
    public static void SetRule(DependencyObject d, bool value) => d.SetValue(RuleProperty, value);

    public static readonly DependencyProperty EndProperty = DependencyProperty.RegisterAttached("End", typeof(bool),
        typeof(BarWrap), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static bool GetEnd(DependencyObject d) => (bool)d.GetValue(EndProperty);
    public static void SetEnd(DependencyObject d, bool value) => d.SetValue(EndProperty, value);

    protected override Size MeasureOverride(Size available)
    {
        foreach (UIElement child in InternalChildren)
            child.Measure(new Size(available.Width, double.PositiveInfinity));
        return Place(available.Width, arrange: false);
    }

    protected override Size ArrangeOverride(Size final)
    {
        Place(final.Width, arrange: true);
        return final;
    }

    /// <summary>
    /// Works out where everything goes in a bar this wide, and how much room
    /// that takes; puts it there when asked to. One routine for both, so
    /// what is measured is what is arranged.
    /// </summary>
    Size Place(double width, bool arrange)
    {
        // The line being filled: its children with where each starts, how
        // far along it has got, and how tall it is.
        var line = new List<(UIElement Child, double X)>();
        double x = 0, y = 0, height = 0, widest = 0;
        UIElement? rule = null, note = null;

        void Wrap()
        {
            if (arrange)
                foreach (var (child, at) in line)
                    child.Arrange(new Rect(at, y, child.DesiredSize.Width, height));
            widest = Math.Max(widest, x);
            y += height;
            line.Clear();
            x = height = 0;
        }

        void Add(UIElement child)
        {
            line.Add((child, x));
            x += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }

        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            if (GetEnd(child))
            {
                note = child;
                continue;
            }
            if (GetRule(child))
            {
                // Kept back until the group after it says where it goes. A
                // second rule with no group between replaces the first.
                Omit(rule, arrange);
                rule = child;
                continue;
            }

            double lead = rule is not null && line.Count > 0 ? rule.DesiredSize.Width : 0;
            if (line.Count > 0 && x + lead + child.DesiredSize.Width > width)
            {
                Wrap();
                lead = 0;
            }
            if (lead > 0) Add(rule!);
            else Omit(rule, arrange);
            rule = null;
            Add(child);
        }
        Omit(rule, arrange);

        if (note is not null)
        {
            double w = note.DesiredSize.Width;
            if (line.Count > 0 && x + w <= width && !double.IsInfinity(width))
            {
                // Beside the groups, against the right-hand end.
                line.Add((note, width - w));
                height = Math.Max(height, note.DesiredSize.Height);
                x = width;
            }
            else if (line.Count > 0 && x + w <= width)
            {
                // No end to set it against (the bar is being asked how wide
                // it would like to be): after the groups.
                Add(note);
            }
            else
            {
                if (line.Count > 0) Wrap();
                // A line of its own, and the whole of it.
                if (arrange) note.Arrange(new Rect(0, y, double.IsInfinity(width) ? w : width, note.DesiredSize.Height));
                widest = Math.Max(widest, w);
                y += note.DesiredSize.Height;
            }
        }
        if (line.Count > 0) Wrap();

        return new Size(double.IsInfinity(width) ? widest : Math.Min(widest, width), y);
    }

    /// <summary>A rule that is not drawn: given no room, off the bar's edge.</summary>
    static void Omit(UIElement? rule, bool arrange)
    {
        if (arrange) rule?.Arrange(new Rect(0, 0, 0, 0));
    }
}
