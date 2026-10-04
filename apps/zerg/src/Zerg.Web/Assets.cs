using System.Reflection;

namespace Zerg;

/// <summary>The page's static files: the damage meter's web\ and shared-ui.</summary>
interface IAssets
{
    /// <summary>The file a URL path names, or null for a 404.</summary>
    byte[]? Get(string urlPath);
    string Describe { get; }
}

static class AssetPath
{
    const string SharedPrefix = "/shared/";

    /// <summary>
    /// Splits a URL path the way damage-meter.py's resolve_static_path does:
    /// "/shared/..." belongs to shared-ui, everything else to web\, and an empty
    /// path is index.html. The path arrives already percent-decoded.
    /// </summary>
    public static (bool Shared, string Rel) Split(string path)
    {
        bool shared = path.StartsWith(SharedPrefix, StringComparison.OrdinalIgnoreCase);
        var rel = (shared ? path[SharedPrefix.Length..] : path).TrimStart('/');
        if (string.IsNullOrWhiteSpace(rel)) rel = "index.html";
        return (shared, rel);
    }

    static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".js"] = "application/javascript; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".svg"] = "image/svg+xml",
        [".ico"] = "image/x-icon",
        [".png"] = "image/png",
        [".woff2"] = "font/woff2",
    };

    public static string MimeOf(string path) =>
        Types.TryGetValue(Path.GetExtension(path), out var t) ? t : "application/octet-stream";
}

/// <summary>
/// Served from resources compiled into the exe. Lookup is an exact match against
/// a fixed table of names, so there is no path to traverse out of: anything that
/// is not a key is a 404.
/// </summary>
sealed class EmbeddedAssets : IAssets
{
    readonly Assembly assembly = typeof(EmbeddedAssets).Assembly;
    readonly Dictionary<string, string> names;   // "web/lib/stats.js" -> manifest name

    public EmbeddedAssets()
    {
        names = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("web\\") || n.StartsWith("shared\\") || n.StartsWith("web/") || n.StartsWith("shared/"))
            .ToDictionary(n => n.Replace('\\', '/'), n => n, StringComparer.OrdinalIgnoreCase);
    }

    public string Describe => $"embedded ({names.Count} files)";

    public byte[]? Get(string urlPath)
    {
        var (shared, rel) = AssetPath.Split(urlPath);
        var key = (shared ? "shared/" : "web/") + rel.Replace('\\', '/');
        if (!names.TryGetValue(key, out var name)) return null;

        using var s = assembly.GetManifestResourceStream(name)!;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}

/// <summary>
/// Served from disk, for --web-root / --dev. Each root gets the same
/// containment check as damage-meter.py, so a traversal out of one cannot land
/// in the other or anywhere else.
/// </summary>
sealed class DiskAssets(string webRoot, string sharedRoot) : IAssets
{
    public string Describe => $"disk ({webRoot})";

    public byte[]? Get(string urlPath)
    {
        var (shared, rel) = AssetPath.Split(urlPath);
        var root = Path.GetFullPath(shared ? sharedRoot : webRoot);
        string full;
        try
        {
            // A rooted rel ("C:/Windows/...") replaces the root entirely in
            // Path.Combine; the containment check below is what catches that.
            full = Path.GetFullPath(Path.Combine(root, rel));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        return File.Exists(full) ? File.ReadAllBytes(full) : null;
    }
}
