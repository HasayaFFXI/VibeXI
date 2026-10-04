using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using Microsoft.Win32;

namespace Zerg;

/// <summary>A parse file as it was read: what it was called, and its text.</summary>
public sealed record ParseText(string Name, string Text);

/// <summary>
/// Picking an exported parse off the disk, and where to save one. One Open
/// dialog for everything that opens a parse, and one remembered folder for
/// opening and saving alike.
///
/// <para>Zerg saves parses as <c>.zerg</c>; exports made before that are
/// <c>.json</c> and are offered beside them. Neither extension is trusted:
/// what a file is, is decided by reading it.</para>
/// </summary>
static class ParseDialog
{
    /// <summary>Windows remembers a dialog's last folder under this.</summary>
    static readonly Guid Place = new("5f0c1f3e-7a59-4e0b-9d0a-2c41b7c3a6d2");

    /// <summary>The file the player picked, read; null if they cancelled.</summary>
    /// <exception cref="IOException">The file could not be read.</exception>
    /// <exception cref="UnauthorizedAccessException">The file could not be read.</exception>
    public static ParseText? Open(Window owner)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open an exported parse",
            Filter = "Zerg parses (*.zerg, *.json)|*.zerg;*.json|All files (*.*)|*.*",
            CheckFileExists = true,
            ClientGuid = Place,
        };
        return dialog.ShowDialog(owner) == true ? Read(dialog.FileName) : null;
    }

    /// <summary>Where the player chose to save a parse, offered under
    /// <paramref name="name"/>; null if they cancelled. In the folder parses
    /// were last opened from or saved to, so Import opens where exports went.</summary>
    public static string? Save(Window owner, string name)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export this parse",
            FileName = name,
            Filter = "Zerg parse (*" + Zerg.Core.ParseFile.Extension + ")|*" + Zerg.Core.ParseFile.Extension,
            DefaultExt = Zerg.Core.ParseFile.Extension,
            AddExtension = true,
            OverwritePrompt = true,
            ClientGuid = Place,
        };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public static ParseText Read(string path) => new(Path.GetFileName(path), File.ReadAllText(path));

    /// <summary>A parse's name without the extension it was saved under.</summary>
    public static string Title(string fileName) => Extension.Replace(fileName, "");

    static readonly Regex Extension = new(@"\.(zerg|json)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
