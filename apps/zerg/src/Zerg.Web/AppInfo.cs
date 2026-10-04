using System.Reflection;

namespace Zerg;

/// <summary>Names, version and the folders Zerg writes to.</summary>
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

    public static string WebViewDataDir => Path.Combine(DataDir, "WebView2");

    /// <summary>Where the addon writes; must agree with vx_emit.lua's ensure_dir.</summary>
    public static readonly string DefaultEventsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VibeXI", "events");

    // readonly, not const: a const false makes every `if (AppInfo.Debug)` an
    // unreachable-code warning (CS0162) in Release builds.
#if DEBUG
    public static readonly bool Debug = true;
#else
    public static readonly bool Debug = false;
#endif
}
