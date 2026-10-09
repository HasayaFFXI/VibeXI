using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Zerg.Core.Layout;

// A tree as it is written in settings.json, and read back.
//
// A split is {"split": "columns", "share": 0.653, "first": ..., "second": ...}
// ("rows" for one over the other); a pane is {"pane": "bars"}, with
// "folded": true while it is folded. Shares of the room, never sizes, so
// what is saved holds at any window size and on any screen.
//
// Reading forgives everything. The file is the player's and may have been
// edited by hand, cut short, or written by a build with other panes: what
// can be made out is kept and repaired (SplitTree.Repair), and what cannot
// is the installed arrangement. Nothing here throws on anything read.
public static partial class SplitTree
{
    /// <summary>A tree as JSON.</summary>
    public static JsonNode ToJson(SplitNode tree)
    {
        if (tree is PaneSplit split)
            return new JsonObject
            {
                ["split"] = split.Way == SplitWay.Rows ? "rows" : "columns",
                // Four places: a ten-thousandth of a room is under a pixel
                // on any screen, and the file is for a person to read.
                ["share"] = Math.Round(double.IsNaN(split.Ratio) ? 0.5 : Math.Clamp(split.Ratio, 0, 1), 4),
                ["first"] = ToJson(split.First),
                ["second"] = ToJson(split.Second),
            };
        var leaf = (PaneLeaf)tree;
        var pane = new JsonObject { ["pane"] = leaf.Key };
        if (leaf.Folded) pane["folded"] = true;
        return pane;
    }

    /// <summary>
    /// What can be made out of a piece of JSON as a tree, or null if it is
    /// not one. Not yet repaired: it may name panes this build has not got,
    /// or the same one twice. A split with only one half that can be read
    /// is that half.
    /// </summary>
    public static SplitNode? FromJson(JsonElement json) => FromJson(json, 0);

    static SplitNode? FromJson(JsonElement json, int depth)
    {
        if (depth > Deepest || json.ValueKind != JsonValueKind.Object) return null;

        if (json.TryGetProperty("pane", out var pane))
        {
            if (pane.ValueKind != JsonValueKind.String || pane.GetString() is not { Length: > 0 } key) return null;
            bool folded = json.TryGetProperty("folded", out var f) && f.ValueKind == JsonValueKind.True;
            return new PaneLeaf(key, folded);
        }

        if (!json.TryGetProperty("split", out var way)) return null;
        var first = json.TryGetProperty("first", out var a) ? FromJson(a, depth + 1) : null;
        var second = json.TryGetProperty("second", out var b) ? FromJson(b, depth + 1) : null;
        if (first is null) return second;
        if (second is null) return first;

        double share = 0.5;
        if (json.TryGetProperty("share", out var s))
        {
            if (s.ValueKind == JsonValueKind.Number && s.TryGetDouble(out double number)) share = number;
            // A share someone wrote in quotes is still a share.
            else if (s.ValueKind == JsonValueKind.String &&
                     double.TryParse(s.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double written)) share = written;
        }
        if (double.IsNaN(share) || double.IsInfinity(share)) share = 0.5;
        bool rows = way.ValueKind == JsonValueKind.String &&
                    string.Equals(way.GetString(), "rows", StringComparison.OrdinalIgnoreCase);
        return new PaneSplit(rows ? SplitWay.Rows : SplitWay.Columns, Math.Clamp(share, 0, 1), first, second);
    }
}

public static partial class PaneLayouts
{
    /// <summary>The sections that have panes to arrange, as the settings
    /// name them. The View section has none of its own: it shows the Damage
    /// or the Healing section's panes, in that section's arrangement.</summary>
    public const string DamageSection = "Damage", HealingSection = "Healing", CompareSection = "Compare";

    /// <summary>The three, in the order they are written.</summary>
    public static IReadOnlyList<string> Sections { get; } = [DamageSection, HealingSection, CompareSection];

    /// <summary>The arrangement a section is installed with, or null for a
    /// name that is not a section's.</summary>
    public static SplitNode? Installed(string? section) => section switch
    {
        DamageSection => Damage,
        HealingSection => Healing,
        CompareSection => Compare,
        _ => null,
    };

    /// <summary>
    /// A section's arrangement out of what the settings hold under
    /// "layouts": an object with a tree per section, by the section's name.
    /// No such key (a file from before arrangements were saved), a section
    /// it does not mention, or anything that is not a tree: the installed
    /// arrangement. A tree that can be read is repaired against the panes
    /// this build has (<see cref="SplitTree.Repair"/>). Never throws.
    /// </summary>
    public static SplitNode Read(JsonElement? layouts, string section)
    {
        var installed = Installed(section) ?? throw new ArgumentException("not a section with panes: " + section, nameof(section));
        try
        {
            if (layouts is not { ValueKind: JsonValueKind.Object } all || !all.TryGetProperty(section, out var saved)) return installed;
            return SplitTree.Repair(SplitTree.FromJson(saved), installed);
        }
        // A JsonElement whose document has been let go, say. The file is
        // not worth more than the installed arrangement.
        catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException or JsonException or FormatException)
        {
            return installed;
        }
    }

    /// <summary>
    /// What to hold under "layouts" for these three arrangements: a tree
    /// for each section that is not as installed, or null when all three
    /// are (the key is then not written at all, and a build that changes an
    /// installed arrangement changes it for everyone who never made their
    /// own).
    /// </summary>
    public static JsonElement? Write(SplitNode damage, SplitNode healing, SplitNode compare)
    {
        var all = new JsonObject();
        if (damage != Damage) all[DamageSection] = SplitTree.ToJson(damage);
        if (healing != Healing) all[HealingSection] = SplitTree.ToJson(healing);
        if (compare != Compare) all[CompareSection] = SplitTree.ToJson(compare);
        if (all.Count == 0) return null;
        using var document = JsonDocument.Parse(all.ToJsonString());
        return document.RootElement.Clone();
    }
}
