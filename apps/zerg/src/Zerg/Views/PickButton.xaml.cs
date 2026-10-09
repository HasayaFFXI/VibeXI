using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace Zerg.Views;

/// <summary>
/// A pick button and its menu: what damage is isolated to. "Target", on the
/// command bar over the Damage section and on the Compare section's title
/// line while damage is compared; "Type" beside it there, for damage types.
/// Its data context is a <see cref="PickFilter"/>, which says which it is.
///
/// <para>The menu is made of the picker's rows when it opens, a line each,
/// and made again if a line appears while it is open (a session goes on
/// counting under it). A line's figures are bound, so a new total rewrites
/// one cell and makes nothing again. The lines are alphabetical, so none
/// moves under the pointer when a total changes.</para>
///
/// <para><b>A line is chosen, not ticked.</b> Choosing one runs the row's
/// command and the tick follows what that did, as "Lock layout" does: UI
/// Automation invokes a line without clicking it. The menu stays open, so
/// several can be chosen; a press outside it, or Esc, shuts it.</para>
/// </summary>
public partial class PickButton : UserControl
{
    public static readonly DependencyProperty IsOpenProperty =
        DependencyProperty.Register(nameof(IsOpen), typeof(bool), typeof(PickButton), new PropertyMetadata(false));

    /// <summary>What the menu's own window has round the menu, for its
    /// shadow: 14 units at each side and 6 above (the menu's template).</summary>
    const double ShadowSide = 14, ShadowTop = 6;

    readonly ContextMenu menu;
    /// <summary>The rows the open menu was made of.</summary>
    INotifyCollectionChanged? listened;
    /// <summary>When the menu last shut.</summary>
    long shutAt = -1000;
    /// <summary>The press now on the button is the one that shut the menu:
    /// it must not open it again when it is let go.</summary>
    bool shutByPress;

    public PickButton()
    {
        InitializeComponent();
        menu = (ContextMenu)FindResource("Menu");
        // Asked of the property, not of the Closed event: a menu fades out,
        // and says Closed when it has. The press that shuts it is on the
        // button before that, and a menu opened again meanwhile never says
        // it at all (seen: every second press opened it again).
        DependencyPropertyDescriptor.FromProperty(ContextMenu.IsOpenProperty, typeof(ContextMenu)).AddValueChanged(menu, (_, _) =>
        {
            IsOpen = menu.IsOpen;
            if (menu.IsOpen) return;
            shutAt = Environment.TickCount64;
            Listen(null);
        });
        Open.PreviewMouseLeftButtonDown += (_, _) => shutByPress = Environment.TickCount64 - shutAt < 100;
    }

    /// <summary>The menu is open: the button wears its pressed face.</summary>
    public bool IsOpen { get => (bool)GetValue(IsOpenProperty); private set => SetValue(IsOpenProperty, value); }

    void OnOpen(object sender, RoutedEventArgs e)
    {
        bool shut = shutByPress;
        shutByPress = false;
        if (DataContext is not PickFilter picker) return;
        // Pressed while it is open, the button shuts it. A real press never
        // gets here with the menu open (it shut as the button went down, and
        // that is all the press does); a script and a screen reader, which
        // press without a pointer, do.
        if (menu.IsOpen)
        {
            menu.IsOpen = false;
            return;
        }
        if (shut) return;

        menu.DataContext = picker;
        Fill(picker);
        Listen(picker.Rows);
        menu.PlacementTarget = Piece;
        menu.Placement = PlacementMode.Custom;
        menu.CustomPopupPlacementCallback = Place;
        menu.IsOpen = true;
    }

    /// <summary>
    /// Under the button, its left-hand edge on the button's; or, where that
    /// would run it off the window's right-hand edge (the Compare section's
    /// button stands near it), its right-hand edge on the button's. The
    /// sizes are in pixels, the shadow's room among them.
    /// </summary>
    CustomPopupPlacement[] Place(Size popup, Size target, Point offset)
    {
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double side = ShadowSide * scale, down = target.Height - (ShadowTop - 3) * scale;
        var left = new CustomPopupPlacement(new Point(-side, down), PopupPrimaryAxis.Vertical);
        var right = new CustomPopupPlacement(new Point(target.Width - popup.Width + side, down), PopupPrimaryAxis.Vertical);
        bool fits = Window.GetWindow(this) is not { } window ||
                    Piece.TranslatePoint(new Point(0, 0), window).X + (popup.Width / scale - 2 * ShadowSide) <= window.ActualWidth;
        return fits ? [left, right] : [right, left];
    }

    void Listen(INotifyCollectionChanged? rows)
    {
        if (listened != null) listened.CollectionChanged -= OnRows;
        listened = rows;
        if (listened != null) listened.CollectionChanged += OnRows;
    }

    /// <summary>A target was hit for the first time while the menu is open, or a type first dealt.</summary>
    void OnRows(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (menu.IsOpen && DataContext is PickFilter picker) Fill(picker);
    }

    void Fill(PickFilter picker)
    {
        var line = (DataTemplate)FindResource(picker.Paired ? "PairLine" : "OneLine");
        menu.Items.Clear();
        foreach (var row in picker.Rows)
        {
            var item = new MenuItem
            {
                DataContext = row, Header = row, HeaderTemplate = line, Command = row.FlipCommand, StaysOpenOnClick = true,
            };
            item.SetBinding(MenuItem.IsCheckedProperty, new Binding(nameof(PickRow.Ticked)) { Mode = BindingMode.OneWay });
            item.SetBinding(AutomationProperties.NameProperty, new Binding(nameof(PickRow.Name)));
            item.SetBinding(AutomationProperties.ItemStatusProperty, new Binding(nameof(PickRow.Status)));
            menu.Items.Add(item);
            // The line that leads is ruled off from the rest.
            if (row.IsAll && picker.Rows.Count > 1) menu.Items.Add(new Separator());
        }
    }
}
