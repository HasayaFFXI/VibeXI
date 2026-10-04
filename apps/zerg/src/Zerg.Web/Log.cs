namespace Zerg;

/// <summary>
/// %LOCALAPPDATA%\VibeXI\zerg\zerg.log. There is no console to print to, so
/// this is where startup decisions and crashes go -- the first thing to ask a
/// player for in a bug report. Best effort: logging never throws.
/// </summary>
static class Log
{
    static readonly object Gate = new();
    static string FilePath => Path.Combine(AppInfo.DataDir, "zerg.log");
    const long MaxBytes = 1024 * 1024;

    /// <summary>Starts a fresh log once it passes 1 MB, keeping one old copy.</summary>
    public static void Start()
    {
        try
        {
            Directory.CreateDirectory(AppInfo.DataDir);
            var f = new FileInfo(FilePath);
            if (f.Exists && f.Length > MaxBytes) File.Move(FilePath, FilePath + ".old", overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        Write($"---- {AppInfo.Name} {AppInfo.Version} starting, pid {Environment.ProcessId}");
    }

    public static void Write(string message)
    {
        lock (Gate)
        {
            try { File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}"); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
