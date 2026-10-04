using System.IO;

namespace Zerg;

/// <summary>
/// Command-line options. A player never passes any; these exist for development
/// and for the odd machine where the addon writes somewhere unusual.
/// </summary>
sealed record Options(string EventsDir)
{
    public const string Usage =
        "Zerg.exe [--events-dir <dir>]\n\n" +
        "  --events-dir  where the VibeXI addon writes (default %LOCALAPPDATA%\\VibeXI\\events)";

    public static Options Parse(string[] args)
    {
        string eventsDir = AppInfo.DefaultEventsDir;

        for (int i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i]
                : throw new ArgumentException($"{args[i]} needs a value.");

            switch (args[i])
            {
                case "--events-dir": eventsDir = Path.GetFullPath(Next()); break;
                default: throw new ArgumentException($"Unknown option {args[i]}.");
            }
        }
        return new Options(eventsDir);
    }
}
