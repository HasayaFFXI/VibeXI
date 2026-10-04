using Microsoft.UI.Xaml;

namespace WinUiSpike;

public partial class App : Application
{
    MainWindow? window;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => File.AppendAllText(
            Path.Combine(Path.GetTempPath(), "winuispike.log"), DateTime.Now + " " + e.Exception + Environment.NewLine);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = new MainWindow();
        window.Activate();
    }
}
