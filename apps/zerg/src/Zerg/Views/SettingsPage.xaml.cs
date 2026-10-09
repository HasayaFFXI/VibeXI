using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Zerg.Core;

namespace Zerg.Views;

/// <summary>
/// The Settings page: one ruled row to a setting. The events folder, the
/// click-through hot key, the pop-outs' default opacity, the draw
/// frequency, how the tables' rows are shaded, the low accuracy mark, the
/// theme, and the colours of Compare's two runs. Its data context is the
/// <see cref="MainViewModel"/>, which holds them all (the opacity in its
/// <see cref="PanelSet"/>). Two things are done here: reading a key chord
/// off the keyboard, and opening and closing the colour picker.
/// </summary>
public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
        // The picture of a Compare row: one made-up character in two runs.
        PairAmount.Content = new BarPair(0.863, 1, "40,659", "47,112");
        PairChange.Value = new Change("+15.9%", "+6,453", ChangeTone.Better);

        Picker.Picked += OnPicked;
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is INotifyPropertyChanged was) was.PropertyChanged -= OnModelChanged;
            if (e.NewValue is INotifyPropertyChanged now) now.PropertyChanged += OnModelChanged;
        };
        // The page went: another section, or the window.
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) Pick(null);
        };
        Unloaded += (_, _) => Pick(null);
    }

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

    // ---------------------------------------------------- the colour picker
    //
    // One picker for the two wells, under whichever is open. It is part of
    // the page (in Runs, under the wells), not a window: it takes room,
    // scrolls with the page, and goes when the page goes.
    //
    // A well is a toggle, and the picker follows its IsChecked (UI
    // Automation opens a toggle without a click). Open, it shows the
    // colour that run is drawn in; each colour picked in it becomes the
    // run's setting there and then (the view model puts the save off until
    // the colour stands still), so the page's own picture of a Compare row
    // and the Compare section itself follow the marker.
    //
    // It closes when its well is pressed again; on Esc (the keyboard goes
    // back to the well); on a press anywhere outside Runs; when the
    // keyboard goes to anything outside Runs; and when the page goes.

    /// <summary>The run whose well is open: true for A, false for B, null for neither.</summary>
    bool? picking;

    /// <summary>The wells are being set here, not pressed.</summary>
    bool setting;

    /// <summary>The window whose presses are being watched while the picker is open.</summary>
    Window? watched;

    void OnWell(object sender, RoutedEventArgs e)
    {
        if (setting) return;
        var well = (ToggleButton)sender;
        Pick(well.IsChecked == true ? ReferenceEquals(well, WellA) : null);
    }

    /// <summary>Opens the picker under a run's well, moves it to the other's, or closes it.</summary>
    void Pick(bool? run)
    {
        if (run == picking && (run is null || Picker.Visibility == Visibility.Visible)) return;
        picking = run;
        setting = true;
        WellA.IsChecked = run == true;
        WellB.IsChecked = run == false;
        setting = false;

        if (run is not bool a)
        {
            Picker.Visibility = Visibility.Collapsed;
            Watch(false);
            return;
        }
        // Under its well: B's is one well and the gap further along.
        Picker.Margin = new Thickness(a ? 0 : WellA.Width + WellB.Margin.Left, 4, 0, 0);
        Picker.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, a ? "Colour picker for run A" : "Colour picker for run B");
        Show(a);
        Picker.Visibility = Visibility.Visible;
        Watch(true);
        // Once it has been laid out: in view, and with the keyboard.
        Dispatcher.BeginInvoke(() =>
        {
            if (picking != a) return;
            // The keyboard first: WPF scrolls whatever takes it into view,
            // which is the square alone, and of two such requests waiting
            // the page's viewer keeps the later. Asked for in the other
            // order, the picker's lower part (its code, its swatch) was
            // left under the window's edge in a short window.
            Picker.TakeKeyboard();
            UpdateLayout();
            // With its well above it, and a little room under it.
            Picker.BringIntoView(new Rect(0, -WellA.Height - 8, Picker.ActualWidth, Picker.ActualHeight + WellA.Height + 20));
        }, DispatcherPriority.Loaded);
    }

    /// <summary>The colour the run is drawn in and how strong its band is, as the view model has them.</summary>
    void Show(bool a)
    {
        if (Model is not { } model) return;
        if (RunColours.TryParse(a ? model.RunACode : model.RunBCode, out var c)) Picker.Show(Color.FromRgb(c.R, c.G, c.B));
        Picker.Band = a ? model.RunABand : model.RunBBand;
    }

    void OnPicked(Color colour)
    {
        if (Model is not { } model || picking is not bool a) return;
        string code = RunColours.Hex((colour.R, colour.G, colour.B));
        if (a) model.RunA = code;
        else model.RunB = code;
    }

    /// <summary>A run's colour changed, by the picker or not (Use default,
    /// the theme): the open picker shows what its run is drawn in now.
    /// Shown the colour it holds, it does not move.</summary>
    void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (picking is not bool a) return;
        if (e.PropertyName is nameof(MainViewModel.RunACode) or nameof(MainViewModel.RunBCode)
                or nameof(MainViewModel.RunABand) or nameof(MainViewModel.RunBBand))
            Show(a);
    }

    void OnPageKey(object sender, KeyEventArgs e)
    {
        if (picking is not bool a || e.Key != Key.Escape) return;
        Pick(null);
        (a ? WellA : WellB).Focus();
        e.Handled = true;
    }

    /// <summary>The keyboard left the wells and the picker for something
    /// else in the window. (Not for another window: then nothing has it,
    /// and the picker is still there when Zerg comes back.)</summary>
    void OnRunsFocus(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (picking != null && !Runs.IsKeyboardFocusWithin && Keyboard.FocusedElement != null) Pick(null);
    }

    void Watch(bool on)
    {
        if (watched != null)
        {
            watched.RemoveHandler(Mouse.PreviewMouseDownEvent, (MouseButtonEventHandler)OnPressAnywhere);
            watched = null;
        }
        if (!on || Window.GetWindow(this) is not { } window) return;
        // Handled or not: a press on a button is handled by the button.
        window.AddHandler(Mouse.PreviewMouseDownEvent, (MouseButtonEventHandler)OnPressAnywhere, handledEventsToo: true);
        watched = window;
    }

    /// <summary>A press anywhere in the window while the picker is open:
    /// outside the wells and the picker, it closes. The press still does
    /// whatever it was for.</summary>
    void OnPressAnywhere(object sender, MouseButtonEventArgs e)
    {
        if (picking is null) return;
        for (var at = e.OriginalSource as DependencyObject; at != null;
             at = at is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
            if (ReferenceEquals(at, Runs)) return;
        Pick(null);
    }
}

/// <summary>
/// One setting's row on the Settings page: what it is called and what it
/// does, and its control. In a row wide enough the words have a column 430
/// units wide and the control stands beside them, 16 units on, with the
/// rest of the row; in a narrower one (a window under about 850 units
/// wide) the control goes under the words and both have the whole width.
/// Its first child is the words, its second the control.
/// </summary>
public sealed class SettingRow : Panel
{
    /// <summary>The words' column, the room after it, the least the
    /// control is given beside them, and the room between the two when the
    /// control is under the words.</summary>
    const double Words = 430, Gap = 16, Least = 356, Under = 10;

    static bool Beside(double width) => width >= Words + Gap + Least;

    protected override Size MeasureOverride(Size available)
    {
        double width = double.IsInfinity(available.Width) ? Words + Gap + 606 : available.Width;
        bool beside = Beside(width);
        double tall = 0;
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            double room = !beside ? width : i == 0 ? Words : width - Words - Gap;
            child.Measure(new Size(room, double.PositiveInfinity));
            if (child.Visibility == Visibility.Collapsed) continue;
            tall = beside ? Math.Max(tall, child.DesiredSize.Height) : tall + (tall > 0 ? Under : 0) + child.DesiredSize.Height;
        }
        return new Size(width, tall);
    }

    protected override Size ArrangeOverride(Size final)
    {
        bool beside = Beside(final.Width);
        double y = 0;
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            if (beside)
                child.Arrange(i == 0 ? new Rect(0, 0, Words, final.Height)
                                     : new Rect(Words + Gap, 0, Math.Max(0, final.Width - Words - Gap), final.Height));
            else
            {
                if (child.Visibility == Visibility.Collapsed) continue;
                if (y > 0) y += Under;
                child.Arrange(new Rect(0, y, final.Width, child.DesiredSize.Height));
                y += child.DesiredSize.Height;
            }
        }
        return final;
    }
}
