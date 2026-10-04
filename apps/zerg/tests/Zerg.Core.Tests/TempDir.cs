using System.Text;

namespace Zerg.Core.Tests;

/// <summary>A scratch directory under %TEMP%\zerg-tests, removed on dispose.</summary>
sealed class TempDir : IDisposable
{
    public string Dir { get; } =
        Path.Combine(Path.GetTempPath(), "zerg-tests", Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Dir);

    public string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(Dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>UTF-8, no BOM, and exactly the newlines given -- never the platform's.</summary>
    public string Write(string name, string text) => Write(name, Encoding.UTF8.GetBytes(text));

    public void Dispose()
    {
        try { Directory.Delete(Dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
