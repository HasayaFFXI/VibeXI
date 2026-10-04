using System.IO;
using System.Reflection;

namespace Zerg;

/// <summary>Names, version and the folders Zerg reads and writes.</summary>
static class AppInfo
{
    public const string Name = "Zerg";

    public static readonly string Version =
        Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0";

    /// <summary>%LOCALAPPDATA%\VibeXI\zerg -- beside the addon's events\ folder,
    /// for the same reasons: not beside the exe (may be unwritable), not Roaming,
    /// not OneDrive.</summary>
    public static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VibeXI", "zerg");

    /// <summary>In a folder of its own: the WebView2 build that came before
    /// writes zerg.log at the top of DataDir, and the two may run side by side
    /// while one is checked against the other.</summary>
    public static readonly string LogDir = Path.Combine(DataDir, "logs");

    /// <summary>Where the addon writes; must agree with vx_emit.lua's ensure_dir.</summary>
    public static readonly string DefaultEventsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VibeXI", "events");
}
