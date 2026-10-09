namespace Zerg.Views;

/// <summary>
/// The Compare section's four panes: a <see cref="SplitPanel"/> with its
/// panes written into it, so it stands in the main window's page where the
/// Damage and Healing sections' panels stand, and is arranged, measured and
/// (in a window too short for it) scrolled by the same code as they are.
/// The bands over it are <see cref="CompareSection"/>. Its data context is
/// the <see cref="CompareViewModel"/>.
/// </summary>
public partial class ComparePanes : SplitPanel
{
    public ComparePanes() => InitializeComponent();
}
