using System.Windows;
using System.Windows.Controls;

namespace Zerg.Views;

public partial class HealActionsCard : UserControl
{
    public HealActionsCard() => InitializeComponent();

    /// <summary>A drill-down has just opened under its heal, in a panel: the
    /// list scrolls so the row and what is under it can be seen.</summary>
    void OnDetailLoaded(object sender, RoutedEventArgs e) =>
        (List.ContainerFromElement((DependencyObject)sender) as FrameworkElement)?.BringIntoView();
}
