using System.Windows;
using System.Windows.Controls;

namespace Zerg;

/// <summary>
/// The tray icon's menu. Everything on it but Show Zerg, Settings and Exit
/// is bound to the view model; those three act on the main window, which is
/// not the view model's to know about.
/// </summary>
public partial class TrayMenu : ContextMenu
{
    readonly Action show, settings, exit;

    public TrayMenu(MainViewModel model, Action show, Action settings, Action exit)
    {
        this.show = show;
        this.settings = settings;
        this.exit = exit;
        InitializeComponent();
        DataContext = model;
    }

    void OnShow(object sender, RoutedEventArgs e) => show();

    void OnSettings(object sender, RoutedEventArgs e) => settings();

    void OnExit(object sender, RoutedEventArgs e) => exit();
}
