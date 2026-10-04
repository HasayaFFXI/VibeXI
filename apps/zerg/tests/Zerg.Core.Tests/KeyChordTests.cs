namespace Zerg.Core.Tests;

public class KeyChordTests
{
    [Fact]
    public void The_default_is_a_chord()
    {
        var c = KeyChord.Parse(KeyChord.Default);
        Assert.Equal(new KeyChord(Ctrl: true, Alt: true, Shift: false, Win: false, Key: 'Z'), c);
        Assert.Equal("Ctrl+Alt+Z", c!.ToString());
    }

    [Theory]
    [InlineData("ctrl + alt + z", "Ctrl+Alt+Z")]
    [InlineData("Z+Alt+Control", "Ctrl+Alt+Z")]
    [InlineData("Ctrl+Shift+F12", "Ctrl+Shift+F12")]
    [InlineData("win+shift+7", "Shift+Win+7")]
    [InlineData("Alt+F1", "Alt+F1")]
    [InlineData("Ctrl+F24", "Ctrl+F24")]
    public void Order_case_and_spaces_do_not_matter(string written, string read) =>
        Assert.Equal(read, KeyChord.Parse(written)?.ToString());

    [Theory]
    [InlineData("Ctrl+Alt+F12", 0x7B)]
    [InlineData("Ctrl+F1", 0x70)]
    [InlineData("Ctrl+A", 0x41)]
    [InlineData("Ctrl+0", 0x30)]
    public void The_key_is_its_virtual_key_code(string written, int key) =>
        Assert.Equal(key, KeyChord.Parse(written)!.Key);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+Alt")]         // no key
    [InlineData("Ctrl+Alt+Z+X")]     // two keys
    [InlineData("Ctrl+Ctrl+Z")]      // a modifier twice
    [InlineData("Ctrl+Alt+")]        // a part missing
    [InlineData("Ctrl+F0")]
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl+F01")]
    [InlineData("Ctrl+Escape")]      // not a key this reads
    [InlineData("Ctrl+é")]
    public void Anything_else_is_no_chord(string? written) =>
        Assert.Null(KeyChord.Parse(written));

    [Theory]
    [InlineData("Z")]
    [InlineData("Shift+Z")]
    [InlineData("F12")]
    [InlineData("Shift+F12")]
    public void A_chord_that_would_take_a_typing_key_or_a_game_key_is_refused(string written) =>
        Assert.Null(KeyChord.Parse(written));

    [Theory]
    [InlineData(null)]
    [InlineData("nonsense")]
    [InlineData("Shift+Z")]
    public void A_setting_that_is_no_chord_falls_back_to_the_default(string? written) =>
        Assert.Equal(KeyChord.Default, KeyChord.OrDefault(written).ToString());

    [Fact]
    public void A_setting_that_is_a_chord_is_kept() =>
        Assert.Equal("Ctrl+Alt+X", KeyChord.OrDefault("ctrl+alt+x").ToString());

    [Theory]
    [InlineData(true, true, false, false, 0x5A, "Ctrl+Alt+Z")]
    [InlineData(false, true, true, false, 0x7B, "Alt+Shift+F12")]
    [InlineData(false, false, false, true, 0x37, "Win+7")]
    [InlineData(true, false, false, false, 0x87, "Ctrl+F24")]
    public void A_chord_pressed_reads_back_as_itself(bool ctrl, bool alt, bool shift, bool win, int key, string written)
    {
        var c = KeyChord.Of(ctrl, alt, shift, win, key);
        Assert.Equal(written, c?.ToString());
        Assert.Equal(c, KeyChord.Parse(written));
    }

    [Theory]
    [InlineData(false, false, false, false, 0x5A)]  // a bare Z
    [InlineData(false, false, true, false, 0x5A)]   // only Shift
    [InlineData(false, false, false, false, 0x7B)]  // a bare F12
    [InlineData(true, true, false, false, 0x20)]    // Space
    [InlineData(true, true, false, false, 0x1B)]    // Escape
    [InlineData(true, true, false, false, 0x88)]    // past F24
    [InlineData(true, true, false, false, 0x60)]    // numpad 0
    public void A_chord_pressed_that_the_file_could_not_hold_is_refused(bool ctrl, bool alt, bool shift, bool win, int key) =>
        Assert.Null(KeyChord.Of(ctrl, alt, shift, win, key));
}
