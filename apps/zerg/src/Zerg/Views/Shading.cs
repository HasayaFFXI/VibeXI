using System.Windows;
using Zerg.Core.Layout;

namespace Zerg.Views;

/// <summary>
/// How a table's rows are drawn, said once on the card and inherited by
/// every row in it: whether a character's row is shaded in their colour,
/// whether the rows under a character are shaded, and the threshold of the
/// low accuracy mark.
///
/// <para>A card binds the three to the view model
/// (<c>v:Shading.Characters="{Binding ShadeCharacters}"</c>); a row's parts
/// are style triggers on them, or read them when they paint. So when a
/// setting flips, no list is made again and no row is touched by the view
/// model: each row's own triggers flip, and the marks that depend on it
/// paint themselves again.</para>
/// </summary>
public static class Shading
{
    /// <summary>A character's row is shaded to their share, in their colour.
    /// Off, the row is plain and a small bar stands beside the share's figure.</summary>
    public static readonly DependencyProperty CharactersProperty = DependencyProperty.RegisterAttached("Characters", typeof(bool),
        typeof(Shading), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>The rows under a character (an action, a heal) are shaded to their share.</summary>
    public static readonly DependencyProperty ActionsProperty = DependencyProperty.RegisterAttached("Actions", typeof(bool),
        typeof(Shading), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>The low accuracy mark's threshold, in percent.</summary>
    public static readonly DependencyProperty LowAccuracyProperty = DependencyProperty.RegisterAttached("LowAccuracy", typeof(int),
        typeof(Shading), new FrameworkPropertyMetadata(Core.LowMark.Installed, FrameworkPropertyMetadataOptions.Inherits));

    public static bool GetCharacters(DependencyObject d) => (bool)d.GetValue(CharactersProperty);
    public static void SetCharacters(DependencyObject d, bool value) => d.SetValue(CharactersProperty, value);
    public static bool GetActions(DependencyObject d) => (bool)d.GetValue(ActionsProperty);
    public static void SetActions(DependencyObject d, bool value) => d.SetValue(ActionsProperty, value);
    public static int GetLowAccuracy(DependencyObject d) => (int)d.GetValue(LowAccuracyProperty);
    public static void SetLowAccuracy(DependencyObject d, int value) => d.SetValue(LowAccuracyProperty, value);
}

/// <summary>
/// Says a table is in a room too narrow for all of its columns, so it
/// drops some.
///
/// <para>The element the table stands in is given the table's full set of
/// columns (<c>v:Shed.Full="{StaticResource Columns}"</c>) and from then on
/// says, to everything inside it, whether they fit its width
/// (<see cref="NarrowProperty"/>, inherited): the heading and every row
/// collapse the cells of the columns that go, by a style trigger, and a
/// collapsed cell takes its column with it (<see cref="Cells"/>). When the
/// columns stop fitting is not chosen here: it is where the full set's own
/// least widths run out (<c>Zerg.Core/Layout/Columns.Sheds</c>).</para>
///
/// <para>A table may drop columns in two steps. Given a second set as well
/// (<see cref="LessProperty"/>: the columns left after the first step), the
/// element also says when that one does not fit either
/// (<see cref="TightProperty"/>), and the cells that go second collapse on
/// that.</para>
/// </summary>
public static class Shed
{
    /// <summary>What is kept clear beside a table's rows for the scroll
    /// bar, which is drawn over the content.</summary>
    public const double Margin = 14;

    /// <summary>The table's full set of columns, as <see cref="Cells.Widths"/> is written.</summary>
    public static readonly DependencyProperty FullProperty = DependencyProperty.RegisterAttached("Full", typeof(string),
        typeof(Shed), new PropertyMetadata(null, (d, e) =>
        {
            if (d is not FrameworkElement el) return;
            el.SizeChanged -= OnSize;
            if (e.NewValue is string) el.SizeChanged += OnSize;
            Decide(el);
        }));

    /// <summary>The columns left once the first to go have gone, written the same way.</summary>
    public static readonly DependencyProperty LessProperty = DependencyProperty.RegisterAttached("Less", typeof(string),
        typeof(Shed), new PropertyMetadata(null, (d, _) =>
        {
            if (d is FrameworkElement el) Decide(el);
        }));

    /// <summary>The room is too narrow even for the columns left after the first step.</summary>
    public static readonly DependencyProperty TightProperty = DependencyProperty.RegisterAttached("Tight", typeof(bool),
        typeof(Shed), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>The room is too narrow for every column.</summary>
    public static readonly DependencyProperty NarrowProperty = DependencyProperty.RegisterAttached("Narrow", typeof(bool),
        typeof(Shed), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static string? GetFull(DependencyObject d) => (string?)d.GetValue(FullProperty);
    public static void SetFull(DependencyObject d, string? value) => d.SetValue(FullProperty, value);
    public static bool GetNarrow(DependencyObject d) => (bool)d.GetValue(NarrowProperty);
    public static void SetNarrow(DependencyObject d, bool value) => d.SetValue(NarrowProperty, value);
    public static string? GetLess(DependencyObject d) => (string?)d.GetValue(LessProperty);
    public static void SetLess(DependencyObject d, string? value) => d.SetValue(LessProperty, value);
    public static bool GetTight(DependencyObject d) => (bool)d.GetValue(TightProperty);
    public static void SetTight(DependencyObject d, bool value) => d.SetValue(TightProperty, value);

    static void OnSize(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged) Decide((FrameworkElement)sender);
    }

    static void Decide(FrameworkElement el)
    {
        // Said outright each time, not only when it differs from what is
        // inherited: a table inside another (a drill-down under a row of a
        // floating table) must not take the outer one's answer for its own.
        SetNarrow(el, Columns.Sheds(GetFull(el), el.ActualWidth, Margin));
        SetTight(el, GetLess(el) is { } less && Columns.Sheds(less, el.ActualWidth, Margin));
    }
}
