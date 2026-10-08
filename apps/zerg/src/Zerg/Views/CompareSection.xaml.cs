using System.Windows;
using System.Windows.Controls;

namespace Zerg.Views;

/// <summary>
/// The Compare section's bands: what to compare about the two runs, the two
/// slots and, once both hold a parse, six figures. The panes under them are
/// <see cref="ComparePanes"/>. Its data context is the
/// <see cref="CompareViewModel"/>.
///
/// <para>It is one of the things that stand above a section's panes in the
/// main window's page (<see cref="PageStack"/>), so it is as tall as it
/// asks to be and the panes have what is left.</para>
/// </summary>
public partial class CompareSection : UserControl
{
    public static readonly DependencyProperty PairColumnsProperty =
        DependencyProperty.Register(nameof(PairColumns), typeof(int), typeof(CompareSection), new PropertyMetadata(2));

    public CompareSection()
    {
        InitializeComponent();
        SizeChanged += (_, e) => Fit(e.NewSize.Width);
    }

    /// <summary>Whether the two slots stand side by side, with the switches
    /// on the title's line (2); or one over the other, with the switches on
    /// a line of their own (1).</summary>
    public int PairColumns { get => (int)GetValue(PairColumnsProperty); set => SetValue(PairColumnsProperty, value); }

    void Fit(double width) => PairColumns = width >= 760 ? 2 : 1;

    // ------------------------------------------------------ dropping a file

    static RunSlot? SlotOf(object sender) => (sender as FrameworkElement)?.DataContext as RunSlot;

    internal static string? FileIn(DragEventArgs e) =>
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
