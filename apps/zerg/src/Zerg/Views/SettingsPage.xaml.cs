using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Zerg.Core;

namespace Zerg.Views;

/// <summary>
/// The Settings page: the events folder, the click-through hot key and the
/// theme. Its data context is the <see cref="MainViewModel"/>, which holds
/// all three; the one thing done here is reading a key chord off the keyboard.
/// </summary>
public partial class SettingsPage : UserControl
{
    public SettingsPage() => InitializeComponent();

    MainViewModel? Model => DataContext as MainViewModel;

    // -------------------------------------------------- reading a new chord

    /// <summary>Pressed once it waits for the new keys; pressed again it stops waiting.</summary>
    void OnKeys(object sender, RoutedEventArgs e)
    {
        if (Model is not { } model) return;
        if (model.Recording) model.CancelHotKey();
        else
        {
            model.BeginHotKey();
            Keys.Focus();
        }
    }

    /// <summary>
    /// A key went down while the button waits for a chord. Every key is
    /// kept from the button and the window, so Space does not press it and
    /// Alt with a letter opens no menu. A modifier going down by itself is
    /// the start of a chord, not one.
    /// </summary>
    void OnKey(object sender, KeyEventArgs e)
    {
        if (Model is not { Recording: true } model) return;
        e.Handled = true;
        // With Alt held the key arrives as a system key.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
                or Key.LWin or Key.RWin) return;

        var held = Keyboard.Modifiers;
        if (key == Key.Escape && held == ModifierKeys.None)
        {
            model.CancelHotKey();
            return;
        }
        model.TakeHotKey(KeyChord.Of(held.HasFlag(ModifierKeys.Control), held.HasFlag(ModifierKeys.Alt),
                                     held.HasFlag(ModifierKeys.Shift), held.HasFlag(ModifierKeys.Windows),
                                     KeyInterop.VirtualKeyFromKey(key)));
    }

    /// <summary>The keyboard went elsewhere: to another control, or another window.</summary>
    void OnKeysLeft(object sender, KeyboardFocusChangedEventArgs e) => Model?.CancelHotKey();
}
