using System.Diagnostics;

namespace Zerg;

/// <summary>Which URLs belong to the meter, and what happens to the rest.</summary>
static class Links
{
    public static bool IsOurs(string uri) =>
        string.IsNullOrEmpty(uri) ||
        uri.StartsWith(HostRouter.Origin, StringComparison.OrdinalIgnoreCase) ||
        uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
        uri.StartsWith("blob:" + HostRouter.Origin.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    /// <summary>Hands a web link to the player's own browser. Only http(s):
    /// never an arbitrary scheme (file:, ms-settings:, a custom protocol) on a
    /// page's say-so.</summary>
    public static void OpenExternally(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var u) || (u.Scheme != "https" && u.Scheme != "http")) return;
        Log.Write("sent to the browser: " + u.AbsoluteUri);
        try { Process.Start(new ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }
}
