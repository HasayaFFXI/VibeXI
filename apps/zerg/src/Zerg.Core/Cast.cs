namespace Zerg.Core;

/// <summary>
/// How each character is told apart on screen: a colour slot, a shade among
/// the characters sharing a job, and the label that stands in for a name while
/// names are hidden.
///
/// <para><b>Colour follows the character, never the rank or the row.</b> A slot
/// is handed out once and never again, so a character keeps their hue when a
/// filter or a lead change reorders every table.</para>
///
/// <para><b>The file's owner always takes slot 0.</b> They are the one
/// character on every chart of every session, so theirs is the hue that must
/// not drift, and it otherwise would: slots go in first-seen order, and
/// whether the owner or the tank swings first is luck.</para>
///
/// <para><b>Only names that are counted get a slot.</b> A monster is never
/// drawn as an actor, so slotting one would push a real party member further
/// down the palette for nothing. Every name is still remembered, so one that is
/// moved into the party by hand picks up the next free slot. A pet has none
/// either: its damage is its owner's, and so is its colour.</para>
///
/// <para>Slots are kept when a session restarts. A character changing hue
/// between two pulls is worse than a slot spent on someone who left.</para>
/// </summary>
public sealed class Cast
{
    /// <summary>What a character with no job on record is called while names are hidden.</summary>
    public const string UnknownJob = "Unknown job";

    readonly Dictionary<string, int> slots = new(StringComparer.Ordinal);
    readonly List<string> seen = [];
    readonly HashSet<string> seenSet = new(StringComparer.Ordinal);
    Dictionary<string, int> variants = new(StringComparer.Ordinal);
    Dictionary<string, string> aliases = new(StringComparer.Ordinal);
    int nextSlot, scanned;

    /// <summary>name → slot, for every character slotted so far.</summary>
    public IReadOnlyDictionary<string, int> Slots => slots;

    /// <summary>
    /// Brings the slots, shades and labels up to date with the rows read so
    /// far. Cheap, and run before every count: a job line can land at any
    /// time, and a shade that lags one count behind is a character changing
    /// colour a beat after everything else that names them changed.
    /// </summary>
    public void Assign(IReadOnlyList<CombatEvent> events, Roster roster)
    {
        for (int i = scanned; i < events.Count; i++)
        {
            // The credited name: a pet's rows are its owner's.
            var n = !string.IsNullOrEmpty(events[i].Owner) ? events[i].Owner! : events[i].Actor;
            if (n.Length > 0 && seenSet.Add(n)) seen.Add(n);
        }
        scanned = events.Count;

        // Before anyone else, so slot 0 is the owner's whoever swung first.
        if (roster.Owner is { Length: > 0 } owner && !slots.ContainsKey(owner) && !roster.IsMob(owner))
            slots[owner] = nextSlot++;
        foreach (var n in seen)
            if (!slots.ContainsKey(n) && !roster.IsMob(n)) slots[n] = nextSlot++;

        AssignVariants(roster);
        AssignAliases(roster);
    }

    /// <summary>
    /// The rows were dropped (a restart) and the list starts again from empty.
    /// The names already seen and their slots stay; only the place reading
    /// resumes from goes back to the top, or every name would be skipped until
    /// the list grew past its old length.
    /// </summary>
    public void Rewind() => scanned = 0;

    /// <summary>A character's slot. One who has none yet takes the next.</summary>
    public int SlotOf(string name)
    {
        if (!slots.TryGetValue(name, out var slot)) slots[name] = slot = nextSlot++;
        return slot;
    }

    /// <summary>
    /// Which of the characters sharing this one's main job they are: 0 for the
    /// first, 1 for the second. A job has one colour, and two identical lines
    /// on a chart cannot be read, so each gets a numbered shade of it.
    ///
    /// <para>Numbered in slot order, not by damage or table position. Slots
    /// never move, so the same warrior keeps the same shade all session;
    /// numbering by anything the user can reorder would swap shades under
    /// them.</para>
    /// </summary>
    public int VariantOf(string name) => variants.GetValueOrDefault(name);

    /// <summary>
    /// What is drawn in place of this character's name while names are hidden:
    /// their job ("SAM/WAR", then "SAM/WAR 2" for a second on the same pair),
    /// or null for the owner, who keeps their name. A readout that cannot say
    /// which line is yours is no use.
    ///
    /// <para>Numbered in slot order, like the shades and for the same reason:
    /// the point is following someone without their name, and a label that
    /// moved when the lead changed would defeat it. The first of a pair is
    /// unnumbered, as the first also takes the job's base colour.</para>
    ///
    /// <para>Slots are not the whole party. A member who never deals damage is
    /// never slotted, but their job line still makes them a known player, so
    /// they are labelled too, after the slotted ones, in name order. Two such
    /// members on one job can swap numbers once, when the one sorting second
    /// acts first, and never again.</para>
    ///
    /// <para>A pet keeps its name. It is not a character, its damage is
    /// already its owner's, and its name is part of the action's.</para>
    /// </summary>
    public string? AliasOf(string name) => aliases.GetValueOrDefault(name);

    List<string> SlotOrder()
    {
        var names = slots.Keys.ToList();
        names.Sort((a, b) => slots[a].CompareTo(slots[b]));
        return names;
    }

    void AssignVariants(Roster roster)
    {
        var used = new Dictionary<string, int>(StringComparer.Ordinal);
        variants = new(StringComparer.Ordinal);
        foreach (var n in SlotOrder())
        {
            var j = roster.JobOf(n);
            if (j is null || string.IsNullOrEmpty(j.Main) || j.Main == "NON") continue;
            var k = used.GetValueOrDefault(j.Main);
            variants[n] = k;
            used[j.Main] = k + 1;
        }
    }

    void AssignAliases(Roster roster)
    {
        var rest = roster.Kinds.Where(p => p.Value == "player" && !slots.ContainsKey(p.Key)).Select(p => p.Key).ToList();
        rest.Sort(StringComparer.Ordinal);

        var used = new Dictionary<string, int>(StringComparer.Ordinal);
        aliases = new(StringComparer.Ordinal);
        foreach (var n in SlotOrder().Concat(rest))
        {
            if (n == roster.Owner) continue;   // the one name that stays
            var label = roster.JobLabel(n) is { Length: > 0 } job ? job : UnknownJob;
            var k = used[label] = used.GetValueOrDefault(label) + 1;
            aliases[n] = k > 1 ? label + " " + Js.NumberToString(k) : label;
        }
    }
}
