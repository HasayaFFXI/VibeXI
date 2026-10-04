using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace WpfOverlayPoc;

/// <summary>The three cards plus the grip strip. Owns no window: the
/// controller (App) moves it between windows when the frame is toggled, since
/// WPF can't change AllowsTransparency on a window that has been shown.</summary>
public partial class OverlayView : UserControl
{
    readonly App app;
    readonly ObservableCollection<MeterRow> rows = new(
        new[] { "Hasaya", "Kirin", "Paradox", "Zergling", "Overlord" }.Select(n => new MeterRow(n)));
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly Random rng = new();
    bool syncing;

    public OverlayView(App app)
    {
        this.app = app;
        InitializeComponent();
        Rows.ItemsSource = rows;

        // Animated content, so a layered window that stops repainting is obvious.
        new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Normal, (_, _) =>
        {
            var s = clock.Elapsed.TotalSeconds;
            int m = (int)(s / 60);
            Clock.Text = $"{m:00}:{s - m * 60:00.0}";
        }, Dispatcher);
        new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Normal, (_, _) => Tick(), Dispatcher);

        // The window is dragged by the strip; WPF's DragMove is the
        // HTCAPTION trick done for us.
        Grip.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) Window.GetWindow(this)?.DragMove(); };

        foreach (var pct in new[] { 100, 85, 70, 55, 40, 25 })
        {
            var item = new MenuItem { Header = pct + "%" };
            item.Click += (_, _) => app.SetOpacity(pct);
            OpacityMenu.Items.Add(item);
        }
        MiTransparent.Click += (_, _) => app.SetTransparent(MiTransparent.IsChecked);
        MiFrameless.Click += (_, _) => app.SetFrameless(MiFrameless.IsChecked);
        MiClick.Click += (_, _) => app.SetClickThrough(MiClick.IsChecked);
        MiTopmost.Click += (_, _) => app.SetTopmost(MiTopmost.IsChecked);
        MiExit.Click += (_, _) => app.Shutdown();

        OpacitySlider.ValueChanged += (_, e) =>
        {
            OpacityVal.Text = $"{(int)e.NewValue}%";
            if (!syncing) app.SetOpacity((int)e.NewValue);
        };
        // Checked/Unchecked rather than Click, so keyboard and UI Automation
        // toggles count too; `syncing` stops Sync() from echoing back.
        Wire(CbTransparent, app.SetTransparent);
        Wire(CbFrameless, app.SetFrameless);
        Wire(CbClick, app.SetClickThrough);
        Wire(CbTopmost, app.SetTopmost);
    }

    void Wire(CheckBox cb, Action<bool> set)
    {
        cb.Checked += (_, _) => { if (!syncing) set(true); };
        cb.Unchecked += (_, _) => { if (!syncing) set(false); };
    }

    void Tick()
    {
        for (int i = 0; i < rows.Count; i++)
            rows[i].Damage += (long)Math.Round(rng.NextDouble() * 400 * (1 - i * 0.15));
        double max = Math.Max(1, rows.Max(r => r.Damage));
        foreach (var r in rows) r.Fraction = r.Damage / max;
    }

    /// <summary>Reflect the controller's state in the controls and menu.</summary>
    public void Sync(Settings s, string readout)
    {
        syncing = true;
        OpacitySlider.Value = s.OpacityPct;
        CbTransparent.IsChecked = MiTransparent.IsChecked = s.Transparent;
        CbFrameless.IsChecked = MiFrameless.IsChecked = s.Frameless;
        CbClick.IsChecked = MiClick.IsChecked = s.ClickThrough;
        CbTopmost.IsChecked = MiTopmost.IsChecked = s.Topmost;
        State.Text = readout;
        syncing = false;
    }
}

public sealed class MeterRow(string name) : INotifyPropertyChanged
{
    long damage;
    double fraction;

    public string Name { get; } = name;
    public long Damage { get => damage; set => Set(ref damage, value); }
    public double Fraction { get => fraction; set => Set(ref fraction, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    void Set<T>(ref T field, T value, [CallerMemberName] string? prop = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }
}

/// <summary>Bind a bar's length; it eases there over 250 ms like the page's
/// CSS transition. Animates a ScaleTransform, so no layout pass per frame.</summary>
public static class Anim
{
    public static readonly DependencyProperty ScaleXProperty = DependencyProperty.RegisterAttached(
        "ScaleX", typeof(double), typeof(Anim), new PropertyMetadata(0.0, (d, e) =>
        {
            if (d is not UIElement el) return;
            // Templates freeze their Freezables for sharing; animate our own copy.
            if (el.RenderTransform is not ScaleTransform st || st.IsFrozen)
                el.RenderTransform = st = new ScaleTransform(0, 1);
            st.BeginAnimation(ScaleTransform.ScaleXProperty,
                    new DoubleAnimation((double)e.NewValue, TimeSpan.FromMilliseconds(250)));
        }));

    public static double GetScaleX(DependencyObject d) => (double)d.GetValue(ScaleXProperty);
    public static void SetScaleX(DependencyObject d, double v) => d.SetValue(ScaleXProperty, v);
}
