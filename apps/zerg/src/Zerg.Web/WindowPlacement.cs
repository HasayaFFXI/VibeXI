using System.Text.Json;

namespace Zerg;

/// <summary>
/// Remembers where each window was, in %LOCALAPPDATA%\VibeXI\zerg\windows.json,
/// keyed by a name ("main" now; pop-outs by title in Phase 3). Best effort: a
/// missing or corrupt file just means default placement.
/// </summary>
static class WindowPlacement
{
    sealed record Saved(int X, int Y, int W, int H, bool Maximized);

    static string FilePath => Path.Combine(AppInfo.DataDir, "windows.json");

    /// <summary>Puts the form where it was last closed. False if there is no
    /// record, or the record is no longer on any screen.</summary>
    public static bool Restore(Form form, string key)
    {
        var all = Load();
        if (!all.TryGetValue(key, out var s)) return false;

        var rect = new Rectangle(s.X, s.Y, s.W, s.H);
        // Monitors get unplugged, and a window restored off every screen is a
        // lost window.
        if (!OnScreen(rect)) return false;

        form.StartPosition = FormStartPosition.Manual;
        form.Bounds = rect;
        if (s.Maximized) form.WindowState = FormWindowState.Maximized;
        return true;
    }

    /// <summary>Saves where the form is. A position on no screen is not saved
    /// at all: Windows parks a window it has hidden for "show desktop" at about
    /// (-32000, -32000) without always reporting it as minimized, and saving that
    /// would overwrite the last good placement with one that can never be used.</summary>
    public static void Remember(Form form, string key)
    {
        var b = form.WindowState == FormWindowState.Normal ? form.Bounds : form.RestoreBounds;
        if (!OnScreen(b)) return;
        var all = Load();
        all[key] = new Saved(b.X, b.Y, b.Width, b.Height, form.WindowState == FormWindowState.Maximized);
        try
        {
            Directory.CreateDirectory(AppInfo.DataDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Whether a useful piece of the rectangle -- enough to grab and
    /// drag -- is on some screen.</summary>
    static bool OnScreen(Rectangle rect) =>
        Screen.AllScreens.Any(sc =>
        {
            var hit = Rectangle.Intersect(sc.WorkingArea, rect);
            return hit.Width >= 120 && hit.Height >= 40;
        });

    static Dictionary<string, Saved> Load()
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, Saved>>(File.ReadAllText(FilePath)) ?? [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }
}
