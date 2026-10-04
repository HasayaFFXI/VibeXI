namespace Zerg;

/// <summary>
/// Command-line options. A player never passes any; these exist for development
/// and for the odd machine where the addon writes somewhere unusual.
/// </summary>
sealed record Options(string EventsDir, string? WebRoot, string? SharedRoot)
{
    public const string Usage =
        "Zerg.exe [--events-dir <dir>] [--web-root <dir> | --dev]\n\n" +
        "  --events-dir  where the VibeXI addon writes (default %LOCALAPPDATA%\\VibeXI\\events)\n" +
        "  --web-root    serve the page from this folder on disk instead of from the exe;\n" +
        "                shared-ui is taken from <web-root>\\..\\..\\..\\shared-ui\n" +
        "  --dev         --web-root, pointed at the repo this build came from\n\n" +
        "With --web-root or --dev, a front-end edit is a refresh (F5), not a rebuild.";

    public static Options Parse(string[] args)
    {
        string eventsDir = AppInfo.DefaultEventsDir;
        string? webRoot = null;

        for (int i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i]
                : throw new ArgumentException($"{args[i]} needs a value.");

            switch (args[i])
            {
                case "--events-dir": eventsDir = Next(); break;
                case "--web-root": webRoot = Path.GetFullPath(Next()); break;
                case "--dev":
                    webRoot = FindRepoWeb()
                        ?? throw new ArgumentException("--dev: no apps\\damage-meter\\web above " + AppContext.BaseDirectory);
                    break;
                default: throw new ArgumentException($"Unknown option {args[i]}.");
            }
        }

        if (webRoot is null) return new Options(eventsDir, null, null);
        if (!File.Exists(Path.Combine(webRoot, "index.html")))
            throw new ArgumentException($"--web-root: no index.html in {webRoot}");

        // Same layout the Python server assumes: web\ is apps\damage-meter\web,
        // and shared-ui sits at the repo root.
        var shared = Path.GetFullPath(Path.Combine(webRoot, "..", "..", "..", "shared-ui"));
        return new Options(eventsDir, webRoot, shared);
    }

    static string? FindRepoWeb()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var web = Path.Combine(d.FullName, "apps", "damage-meter", "web");
            if (File.Exists(Path.Combine(web, "index.html"))) return web;
        }
        return null;
    }
}
