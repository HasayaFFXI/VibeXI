using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Zerg.Core;

namespace Zerg.Views;

/// <summary>
/// A colour picker: a square (<see cref="ColourField"/>), a strip
/// (<see cref="HueStrip"/>), a swatch, the colour's code in a field, and a
/// word about the colour (<see cref="Band"/>).
///
/// <para>It holds one colour, as a hue, a saturation and a brightness
/// (<c>Zerg.Core/Hsv</c>), not as red, green and blue: black has no hue
/// and a gray no saturation worth the name, and a marker dragged to an
/// edge of the square and back must come back to the hue it left.</para>
///
/// <para>Whoever uses it says which colour it starts from
/// (<see cref="Show"/>), hears every colour picked (<see cref="Picked"/>:
/// each point of a drag, each key, a code taken), and says what is written
/// beside the code. It neither saves nor knows whose colour it is.</para>
/// </summary>
public partial class ColourPicker : UserControl
{
    /// <summary>What is written after "band": how strong a band of the
    /// colour picked comes out, in whole percent. Whoever uses the picker
    /// works it out.</summary>
    public static readonly DependencyProperty BandProperty =
        DependencyProperty.Register(nameof(Band), typeof(int), typeof(ColourPicker), new PropertyMetadata(0));

    /// <summary>The code typed in was not a colour's.</summary>
    public static readonly DependencyProperty BadProperty =
        DependencyProperty.Register(nameof(Bad), typeof(bool), typeof(ColourPicker), new PropertyMetadata(false));

    Hsv held = new(0, 0, 1);

    /// <summary>The picker itself is writing the code, not a person.</summary>
    bool writing;

    public ColourPicker()
    {
        InitializeComponent();
        Draw();
    }

    public int Band { get => (int)GetValue(BandProperty); set => SetValue(BandProperty, value); }
    public bool Bad { get => (bool)GetValue(BadProperty); private set => SetValue(BadProperty, value); }

    /// <summary>The colour picked.</summary>
    public Color Colour
    {
        get
        {
            var (r, g, b) = held.Rgb();
            return Color.FromRgb(r, g, b);
        }
    }

    /// <summary>The colour picked, as it is written: "#3987E5".</summary>
    public string Hex => RunColours.Hex(held.Rgb());

    /// <summary>A person picked a colour: every point of a drag, every key, a code taken.</summary>
    public event Action<Color>? Picked;

    /// <summary>
    /// Shows a colour that came from outside: the one a well holds as the
    /// picker opens, or one set some other way while it is open. Nobody is
    /// told. Shown the colour it already holds, nothing moves.
    /// </summary>
    public void Show(Color colour)
    {
        held = Hsv.Keep((colour.R, colour.G, colour.B), held);
        Bad = false;
        Draw();
    }

    /// <summary>Gives the square the keyboard.</summary>
    public void TakeKeyboard() => Field.Focus();

    /// <summary>Everything as the colour held says.</summary>
    void Draw()
    {
        Field.Hue = held.H;
        Field.Saturation = held.S;
        Field.Brightness = held.V;
        // The strip's far end is red too, and is its own place: left there.
        if (Hsv.At(Strip.Hue, 0, 0).H != held.H) Strip.Hue = held.H;
        var brush = new SolidColorBrush(Colour);
        brush.Freeze();
        Swatch.Background = brush;
        if (Code.Text != Hex)
        {
            writing = true;
            Code.Text = Hex;
            writing = false;
        }
    }

    void OnMoved(object? sender, EventArgs e)
    {
        held = Hsv.At(Strip.Hue, Field.Saturation, Field.Brightness);
        Bad = false;
        Draw();
        Picked?.Invoke(Colour);
    }

    // -------------------------------------------------------------- the code

    /// <summary>Takes what is in the field if it is a colour's code; says so if it is not.</summary>
    void Take()
    {
        if (!RunColours.TryParse(Code.Text, out var colour))
        {
            Bad = true;
            return;
        }
        Bad = false;
        bool same = held.Rgb() == colour;
        held = Hsv.Keep(colour, held);
        // Written the one way, whatever was typed ("e8792b", "#38e").
        Draw();
        if (!same) Picked?.Invoke(Colour);
    }

    void OnCodeKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Take();
        Code.SelectAll();
        // Not the page's to act on as well.
        e.Handled = true;
    }

    /// <summary>The text is being changed by the keyboard: a key or a
    /// character has just come to the field. Said before the change, and
    /// unsaid once the key has been dealt with.</summary>
    bool typing;

    void OnCodeTyping(object sender, EventArgs e)
    {
        typing = true;
        Dispatcher.BeginInvoke(() => typing = false, System.Windows.Threading.DispatcherPriority.Input);
    }

    void OnCodeChanged(object sender, TextChangedEventArgs e)
    {
        if (writing) return;
        Bad = false;
        // Typed, it waits for Enter: half a code is not a colour, and the
        // first three digits of six are another colour's code. Set whole
        // from outside (a script or a screen reader through UI Automation,
        // a paste from the field's menu), it is taken there and then.
        // Whether the field has the keyboard does not tell the two apart:
        // UI Automation gives it the keyboard before it sets the text.
        if (!typing) Take();
    }

    /// <summary>The whole code is picked out as the keyboard comes to the
    /// field, so that what is typed takes its place.</summary>
    void OnCodeEntered(object sender, KeyboardFocusChangedEventArgs e) => Code.SelectAll();

    /// <summary>The press that brings the keyboard to the field only brings
    /// it: left to the field, the same press would put the caret where the
    /// pointer is and undo the picking out. A press once it has the
    /// keyboard is the field's own.</summary>
    void OnCodePress(object sender, MouseButtonEventArgs e)
    {
        if (Code.IsKeyboardFocusWithin) return;
        Code.Focus();
        e.Handled = true;
    }

    void OnCodeLeft(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (RunColours.TryParse(Code.Text, out _)) Take();
        else
        {
            // Not left showing something that is not the colour.
            Bad = false;
            writing = true;
            Code.Text = Hex;
            writing = false;
        }
    }
}
