using System.IO;

namespace Zerg;

/// <summary>
/// Command-line options. A player never passes any: these exist for
/// development. A machine where the addon writes somewhere unusual is what the
/// Settings page is for.
/// </summary>
/// <param name="EventsDir">The folder to follow for this run, over the one in
/// the settings and without changing it; null when not given.</param>
sealed record Options(string? EventsDir)
{
    public const string Usage =
        "Zerg.exe [--events-dir <dir>]\n\n" +
        "  --events-dir  where the VibeXI addon writes, for this run only\n" +
        "                (default: the folder on the Settings page)";

    public static Options Parse(string[] args)
    {
        string? eventsDir = null;

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
