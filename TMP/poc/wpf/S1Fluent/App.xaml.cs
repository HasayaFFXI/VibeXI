using System.Windows;

namespace FluentSpike;

public sealed class Options
{
    public ThemeMode Theme = ThemeMode.System;
    public int Monitor = 1;          // 1 = primary, 2 = the next one to the right...
    public string? SnapDir;          // capture every tab in every theme, then exit
    public string? Report;           // write the readout here (with --snap: at the end)
    public bool ForceMica;           // set DWMWA_SYSTEMBACKDROP_TYPE ourselves

    public static Options Parse(string[] args)
    {
        var o = new Options();
        for (int i = 0; i < args.Length; i++)
            switch (args[i])
            {
                case "--theme" when i + 1 < args.Length: o.Theme = new ThemeMode(args[++i]); break;
                case "--monitor" when i + 1 < args.Length: o.Monitor = int.Parse(args[++i]); break;
                case "--snap" when i + 1 < args.Length: o.SnapDir = args[++i]; break;
                case "--report" when i + 1 < args.Length: o.Report = args[++i]; break;
                case "--force-mica": o.ForceMica = true; break;
            }
        return o;
    }
}

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var o = Options.Parse(e.Args);
        ThemeMode = o.Theme;
        new MainWindow(o).Show();
    }
}
