namespace Zerg.Core;

/// <summary>
/// A key pressed with modifiers held, as the settings file writes it
/// ("Ctrl+Alt+Z") and as Windows is asked to watch for it in every
/// application. <see cref="Key"/> is the key's virtual-key code.
///
/// <para><b>Ctrl, Alt or Win must be among the modifiers.</b> The chord is
/// taken from whatever has the keyboard, the game included: a bare letter, or
/// one with only Shift, would stop that letter being typed anywhere, and a
/// bare function key is one the game already uses.</para>
/// </summary>
public sealed record KeyChord(bool Ctrl, bool Alt, bool Shift, bool Win, int Key)
{
    /// <summary>What switches the floating panels between click-through and
    /// clickable until the settings file says otherwise.</summary>
    public const string Default = "Ctrl+Alt+Z";

    /// <summary>
    /// Reads a chord: modifiers and one key joined by "+", in any order and
    /// any case. The key is a letter, a digit, or F1 to F24. Null for
    /// anything else, so a damaged setting is never registered as some other
    /// key than the one meant.
    /// </summary>
    public static KeyChord? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        bool ctrl = false, alt = false, shift = false, win = false;
        int key = 0;
        foreach (var raw in text.Split('+'))
        {
            var part = raw.Trim().ToUpperInvariant();
            switch (part)
            {
                // Named twice is a slip of the hand somewhere, not a chord.
                case "CTRL" or "CONTROL" when !ctrl: ctrl = true; break;
                case "ALT" when !alt: alt = true; break;
                case "SHIFT" when !shift: shift = true; break;
                case "WIN" or "WINDOWS" when !win: win = true; break;
                default:
                    if (key != 0 || KeyOf(part) is not int k) return null;
                    key = k;
                    break;
            }
        }
        return key != 0 && (ctrl || alt || win) ? new KeyChord(ctrl, alt, shift, win, key) : null;
    }

    /// <summary>The chord in a setting, or the default when the setting is not one.</summary>
    public static KeyChord OrDefault(string? text) => Parse(text) ?? Parse(Default)!;

    /// <summary>
    /// A chord as it was pressed: the modifiers held, and the virtual-key
    /// code of the key that went down with them. Null for what
    /// <see cref="Parse"/> would not read back, so a chord taken from the
    /// keyboard is always one the settings file can hold.
    /// </summary>
    public static KeyChord? Of(bool ctrl, bool alt, bool shift, bool win, int key)
    {
        bool known = key is (>= 'A' and <= 'Z') or (>= '0' and <= '9') or (>= 0x70 and <= 0x87);
        return known && (ctrl || alt || win) ? new KeyChord(ctrl, alt, shift, win, key) : null;
    }

    static int? KeyOf(string part)
    {
        // Letters and digits have their own character as their code.
        if (part.Length == 1 && part[0] is (>= 'A' and <= 'Z') or (>= '0' and <= '9')) return part[0];
        if (part.Length is 2 or 3 && part[0] == 'F' && part[1] != '0' && part.AsSpan(1).IndexOfAnyExceptInRange('0', '9') < 0
            && int.Parse(part.AsSpan(1)) is >= 1 and <= 24 and var n) return 0x70 + n - 1;
        return null;
    }

    /// <summary>As it is shown, and as it is written back: modifiers in the
    /// order Windows lists them, then the key.</summary>
    public override string ToString() =>
        (Ctrl ? "Ctrl+" : "") + (Alt ? "Alt+" : "") + (Shift ? "Shift+" : "") + (Win ? "Win+" : "") +
        (Key >= 0x70 ? "F" + (Key - 0x70 + 1) : ((char)Key).ToString());
}
