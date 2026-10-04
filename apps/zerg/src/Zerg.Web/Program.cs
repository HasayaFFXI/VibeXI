using Microsoft.Web.WebView2.Core;

namespace Zerg;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Log.Start();
        Application.ThreadException += (_, e) => Crash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write("UNHANDLED " + e.ExceptionObject);

        Options options;
        try
        {
            options = Options.Parse(args);
        }
        catch (ArgumentException e)
        {
            MessageBox.Show(e.Message + "\n\n" + Options.Usage, AppInfo.Name,
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 2;
        }

        // Windows 11 ships the runtime and Windows 10 has had it through Windows
        // Update since 2021, so this is rare -- but when it happens the player
        // needs a sentence and a link, not a crash (PLAN.md, Decision 7).
        try
        {
            CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show(
                "Zerg needs the Microsoft Edge WebView2 Runtime, which is not installed on this PC.\n\n" +
                "Download the Evergreen Bootstrapper from:\n" +
                "https://developer.microsoft.com/microsoft-edge/webview2/\n\n" +
                "then start Zerg again.",
                AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }

        Log.Write($"events dir {options.EventsDir}; web {(options.WebRoot ?? "embedded")}");
        Application.Run(new MainForm(options));
        Log.Write("exit");
        return 0;
    }

    static void Crash(Exception e)
    {
        Log.Write("UNHANDLED " + e);
        MessageBox.Show("Something went wrong:\n\n" + e.Message +
                        "\n\nDetails are in " + Path.Combine(AppInfo.DataDir, "zerg.log"),
                        AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
