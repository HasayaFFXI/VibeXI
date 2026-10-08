using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Zerg.Core.Charts;

namespace Zerg.Views;

/// <summary>
/// The characters' chips, inside the list that makes them (it is that
/// list's panel). Two ways to lay them out:
///
/// <para><b>On one line</b> (the default): as many chips as fit, first to
/// last, and no more. The ones that do not fit are there but not shown, and
/// <see cref="Hidden"/> says how many, for the "+N" chip that follows the
/// line. Room for that chip is kept back only when it is needed
/// (<see cref="BandMarks.Fit"/>).</para>
///
/// <para><b>Wrapped</b> (<see cref="WrapProperty"/>): every chip, on as many
/// lines as that takes; <see cref="Rows"/> says how many.</para>
///
/// <para>A chip that is not shown is hidden, not collapsed: it keeps its
/// size, so it can be measured for the next layout, and it is out of the
/// way of the pointer, the Tab key and a screen reader meanwhile.</para>
/// </summary>
public sealed class ChipFit : Panel
{
    /// <summary>The room between two chips, along a line and between lines.</summary>
    public const double Gap = 4;

    /// <summary>How many lines of chips the list shows at once (its
    /// MaxHeight in MainWindow.xaml: four chips and three gaps, 92), and the
    /// room a scroll bar over the list's right-hand end needs.</summary>
    const int MostRows = 4;
    const double ScrollRoom = 14;

    public static readonly DependencyProperty WrapProperty = DependencyProperty.Register(nameof(Wrap), typeof(bool),
        typeof(ChipFit), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>How wide the "+N" chip is taken to be when room is kept for it.</summary>
    public static readonly DependencyProperty MoreProperty = DependencyProperty.Register(nameof(More), typeof(double),
        typeof(ChipFit), new FrameworkPropertyMetadata(36.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public bool Wrap { get => (bool)GetValue(WrapProperty); set => SetValue(WrapProperty, value); }
    public double More { get => (double)GetValue(MoreProperty); set => SetValue(MoreProperty, value); }

    /// <summary>How many chips the one line left out, as last measured. None when wrapped.</summary>
    public int Hidden { get; private set; }

    /// <summary>How many lines the chips took, as last measured.</summary>
    public int Rows { get; private set; }

    /// <summary>Where each chip goes, as last measured; null for one not shown.</summary>
    readonly List<Rect?> places = [];

    protected override Size MeasureOverride(Size available)
    {
        var widths = new List<double>(InternalChildren.Count);
        double tall = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            widths.Add(child.DesiredSize.Width);
            tall = Math.Max(tall, child.DesiredSize.Height);
        }

        places.Clear();
        double x = 0, y = 0, widest = 0;
        if (!Wrap)
        {
            int fit = BandMarks.Fit(widths, Gap, available.Width, More);
            for (int i = 0; i < widths.Count; i++)
            {
                if (i >= fit) { places.Add(null); continue; }
                if (i > 0) x += Gap;
                places.Add(new Rect(x, 0, widths[i], tall));
                x += widths[i];
            }
            Hidden = widths.Count - fit;
            Rows = 1;
            widest = x;
        }
        else
        {
            // Past the most lines shown at once the list scrolls, and its
            // scroll bar is drawn over its right-hand end: the chips are
            // wrapped again, short of it.
            bool scrolls = Wrapped(widths, tall, available.Width) > MostRows;
            if (scrolls) Wrapped(widths, tall, available.Width - ScrollRoom);
            Hidden = 0;
            foreach (var at in places)
            {
                widest = Math.Max(widest, at!.Value.Right);
                y = at.Value.Top;
            }
            if (scrolls) widest += ScrollRoom;
        }

        for (int i = 0; i < InternalChildren.Count; i++)
        {
            var want = places[i] is null ? Visibility.Hidden : Visibility.Visible;
            if (InternalChildren[i].Visibility != want) InternalChildren[i].Visibility = want;
        }
        return new Size(widest, widths.Count > 0 ? y + tall : 0);
    }

    /// <summary>Every chip, on as many lines as a list this wide takes.
    /// Fills <see cref="places"/> and <see cref="Rows"/>; returns the rows.</summary>
    int Wrapped(List<double> widths, double tall, double wide)
    {
        places.Clear();
        double x = 0, y = 0;
        int rows = 1;
        for (int i = 0; i < widths.Count; i++)
        {
            // A chip wider than the whole line still gets a line.
            if (x > 0 && x + Gap + widths[i] > wide + 0.01)
            {
                x = 0;
                y += tall + Gap;
                rows++;
            }
            if (x > 0) x += Gap;
            places.Add(new Rect(x, y, widths[i], tall));
            x += widths[i];
        }
        return Rows = rows;
    }

    protected override Size ArrangeOverride(Size final)
    {
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            child.Arrange(i < places.Count && places[i] is { } at ? at : new Rect(0, 0, child.DesiredSize.Width, child.DesiredSize.Height));
        }
        return final;
    }
}

/// <summary>
/// The characters' line, one band under the figures: the count
/// ("CHARACTERS 12/12"), the chips, the chip that says how many are not
/// shown, a short rule, All and None, and who is left out. Each child says
/// which of those it is (<see cref="PartProperty"/>).
///
/// <para>Everything follows the chips, so what comes after them moves as
/// they come and go; the hint takes what room is left and is trimmed to it.
/// The chips are given the room the rest does not need, and decide for
/// themselves how many fit (<see cref="ChipFit"/>, the panel inside the
/// list). With the list opened onto every chip, the band grows by a line
/// per line of chips, and the rest stays on the first.</para>
///
/// <para><b>The chip after the chips</b> folds and unfolds the list. It is
/// there when it has something to do: with the list on one line, while
/// some chips do not fit on it (and it says how many, <see cref="MoreText"/>);
/// with the list opened, while that takes more than one line.</para>
/// </summary>
public sealed class ChipLine : Panel
{
    public enum Role { Label, Chips, More, Tail, Hint }

    /// <summary>The band's first line: everything but the chips' further lines is centred in it.</summary>
    const double Line = 30;
    /// <summary>Room at each end of the band; before and after the chips; before the hint.</summary>
    const double Edge = 14, Around = 12, BeforeHint = 12;
    /// <summary>How much of the hint is kept when the chips want the room.</summary>
    const double HintLeast = 120;
    /// <summary>A chip's height, and the room above the first line of chips.</summary>
    const double ChipTall = 20, Above = (Line - ChipTall) / 2;

    public static readonly DependencyProperty PartProperty = DependencyProperty.RegisterAttached("Part", typeof(Role),
        typeof(ChipLine), new FrameworkPropertyMetadata(Role.Tail, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static Role GetPart(DependencyObject d) => (Role)d.GetValue(PartProperty);
    public static void SetPart(DependencyObject d, Role value) => d.SetValue(PartProperty, value);

    static readonly DependencyPropertyKey MoreTextKey = DependencyProperty.RegisterReadOnly(nameof(MoreText), typeof(string),
        typeof(ChipLine), new PropertyMetadata(""));
    /// <summary>What the chip after the chips says while the list is on one line: "+5".</summary>
    public static readonly DependencyProperty MoreTextProperty = MoreTextKey.DependencyProperty;

    static readonly DependencyPropertyKey CanFoldKey = DependencyProperty.RegisterReadOnly(nameof(CanFold), typeof(bool),
        typeof(ChipLine), new PropertyMetadata(false));
    /// <summary>The chip after the chips has something to do, and is shown.</summary>
    public static readonly DependencyProperty CanFoldProperty = CanFoldKey.DependencyProperty;

    public string MoreText => (string)GetValue(MoreTextProperty);
    public bool CanFold => (bool)GetValue(CanFoldProperty);

    UIElement? Find(Role role)
    {
        foreach (UIElement child in InternalChildren)
            if (GetPart(child) == role) return child;
        return null;
    }

    ChipFit? found;

    /// <summary>The panel inside the chips' list, once the list has made it.
    /// Looked for once, and again only if the list has made another.</summary>
    ChipFit? FitOf(UIElement chips)
    {
        if (found is null || !found.IsDescendantOf(chips)) found = Under(chips);
        return found;
    }

    static ChipFit? Under(DependencyObject under)
    {
        if (under is ChipFit fit) return fit;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(under); i++)
            if (Under(VisualTreeHelper.GetChild(under, i)) is { } inside) return inside;
        return null;
    }

    static double Wide(UIElement? e) => e is null || e.Visibility == Visibility.Collapsed ? 0 : e.DesiredSize.Width;

    protected override Size MeasureOverride(Size available)
    {
        UIElement? label = Find(Role.Label), chips = Find(Role.Chips), more = Find(Role.More),
                   tail = Find(Role.Tail), hint = Find(Role.Hint);
        var free = new Size(double.PositiveInfinity, double.PositiveInfinity);
        label?.Measure(free);
        tail?.Measure(free);
        hint?.Measure(free);

        // What the chips may have: the band, less everything else on the
        // line, with the hint cut down to its least.
        double hintWide = Wide(hint), kept = Math.Min(hintWide, HintLeast);
        double room = available.Width - 2 * Edge - Wide(label) - 2 * Around - Wide(tail) - (kept > 0 ? BeforeHint + kept : 0);
        room = Math.Max(0, room);

        bool folds = false;
        string text = "";
        if (chips != null)
        {
            chips.Measure(new Size(room, double.PositiveInfinity));
            if (FitOf(chips) is { } fit)
            {
                if (fit.Wrap && fit.Rows > 1)
                {
                    // More than one line: the chip that folds them is shown,
                    // and the chips make room for it.
                    folds = true;
                    chips.Measure(new Size(Math.Max(0, room - ChipFit.Gap - fit.More), double.PositiveInfinity));
                }
                else if (!fit.Wrap && fit.Hidden > 0)
                {
                    folds = true;
                    text = "+" + fit.Hidden.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
            }
        }
        if (text != MoreText) SetValue(MoreTextKey, text);
        if (folds != CanFold) SetValue(CanFoldKey, folds);
        // After the two above: they decide what it says and whether it is there.
        more?.Measure(free);

        // The hint again, in the room that is left: it trims itself to what
        // it is measured in, not to where it is put.
        double used = Used(label, chips, more, tail) + (hintWide > 0 ? BeforeHint : 0);
        if (!double.IsInfinity(available.Width))
            hint?.Measure(new Size(Math.Max(0, available.Width - Edge - used), double.PositiveInfinity));

        double chipsTall = chips?.DesiredSize.Height ?? 0;
        double wide = double.IsInfinity(available.Width) ? used + hintWide + Edge : available.Width;
        return new Size(wide, Math.Max(Line, chipsTall + 2 * Above));
    }

    /// <summary>How far along the line everything before the hint reaches.</summary>
    static double Used(UIElement? label, UIElement? chips, UIElement? more, UIElement? tail)
    {
        double x = Edge + Wide(label);
        if (Wide(label) > 0) x += Around;
        x += Wide(chips);
        if (Wide(more) > 0) x += (Wide(chips) > 0 ? ChipFit.Gap : 0) + Wide(more);
        if (Wide(chips) > 0 || Wide(more) > 0) x += Around;
        return x + Wide(tail);
    }

    protected override Size ArrangeOverride(Size final)
    {
        UIElement? label = Find(Role.Label), chips = Find(Role.Chips), more = Find(Role.More),
                   tail = Find(Role.Tail), hint = Find(Role.Hint);

        // Used, above, walks the line the same way: change the two together.
        // Each part is in the first line, at its own height, centred.
        double x = Edge;
        void Put(UIElement? e, double width)
        {
            if (e is null) return;
            double tall = Math.Min(e.DesiredSize.Height, Line);
            e.Arrange(new Rect(x, Math.Round((Line - tall) / 2), Math.Max(0, width), tall));
            x += Math.Max(0, width);
        }

        Put(label, Wide(label));
        if (Wide(label) > 0) x += Around;
        if (chips != null)
        {
            chips.Arrange(new Rect(x, Above, chips.DesiredSize.Width, chips.DesiredSize.Height));
            x += chips.DesiredSize.Width;
        }
        if (Wide(more) > 0)
        {
            if (Wide(chips) > 0) x += ChipFit.Gap;
            Put(more, Wide(more));
        }
        else more?.Arrange(new Rect(0, 0, 0, 0));
        if (Wide(chips) > 0 || Wide(more) > 0) x += Around;
        Put(tail, Wide(tail));
        if (hint != null)
        {
            x += BeforeHint;
            Put(hint, Math.Min(hint.DesiredSize.Width, final.Width - Edge - x));
        }
        return final;
    }
}
