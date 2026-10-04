using System.Windows;
using System.Windows.Controls;

namespace Zerg;

/// <summary>
/// The tray icon's menu. Everything on it but its first and last lines is
/// bound to the view model; those two act on the main window, which is not
/// the view model's to know about.
/// </summary>
public partial class TrayMenu : ContextMenu
{
    readonly Action show, exit;

    public TrayMenu(MainViewModel model, Action show, Action exit)
    {
        this.show = show;
        this.exit = exit;
        InitializeComponent();
        DataContext = model;
    }

    void OnShow(object sender, RoutedEventArgs e) => show();

    void OnExit(object sender, RoutedEventArgs e) => exit();
}
