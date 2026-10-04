using System.Windows;
using System.Windows.Controls;

namespace Zerg.Views;

/// <summary>
/// The Compare section: the two slots and their switches, then, once both
/// hold a parse, the tiles, the cumulative chart and the tables. Its data
/// context is the <see cref="CompareViewModel"/>.
/// </summary>
public partial class CompareSection : UserControl
{
    public static readonly DependencyProperty TileColumnsProperty =
        DependencyProperty.Register(nameof(TileColumns), typeof(int), typeof(CompareSection), new PropertyMetadata(6));

    public static readonly DependencyProperty PairColumnsProperty =
        DependencyProperty.Register(nameof(PairColumns), typeof(int), typeof(CompareSection), new PropertyMetadata(2));

    public CompareSection()
    {
        InitializeComponent();
        SizeChanged += (_, e) => Fit(e.NewSize.Width);
    }

    /// <summary>How many tiles sit in a row: six, or threes, or twos as the window narrows.</summary>
    public int TileColumns { get => (int)GetValue(TileColumnsProperty); set => SetValue(TileColumnsProperty, value); }

    /// <summary>Whether the two slots, and the cards that come in twos, sit side by side or stack.</summary>
    public int PairColumns { get => (int)GetValue(PairColumnsProperty); set => SetValue(PairColumnsProperty, value); }

    void Fit(double width)
    {
        TileColumns = width >= 1200 ? 6 : width >= 640 ? 3 : 2;
        PairColumns = width >= 760 ? 2 : 1;
    }

    // ------------------------------------------------------ dropping a file

    static RunSlot? SlotOf(object sender) => (sender as FrameworkElement)?.DataContext as RunSlot;

    static string? FileIn(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files
            ? files[0] : null;

    /// <summary>Only a file can be dropped on a slot; anything else is turned away.</summary>
    void OnSlotDrag(object sender, DragEventArgs e)
    {
        bool file = FileIn(e) != null;
        e.Effects = file ? DragDropEffects.Copy : DragDropEffects.None;
        if (SlotOf(sender) is { } slot) slot.IsOver = file;
        e.Handled = true;
    }

    void OnSlotLeave(object sender, DragEventArgs e)
    {
        if (SlotOf(sender) is { } slot) slot.IsOver = false;
    }

    void OnSlotDrop(object sender, DragEventArgs e)
    {
        if (SlotOf(sender) is not { } slot) return;
        slot.IsOver = false;
        if (FileIn(e) is { } path) slot.Take(path);
        e.Handled = true;
    }
}
