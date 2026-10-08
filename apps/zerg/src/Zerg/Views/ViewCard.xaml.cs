using System.Windows;
using System.Windows.Controls;

namespace Zerg.Views;

/// <summary>
/// The View section's band, under the command bar: the place to open one
/// exported parse, and, once it is open, one line saying what it is, with
/// the Damage | Healing switch over the figures and cards below. Its data
/// context is the <see cref="MainViewModel"/>, which draws the parse with
/// the band of figures and the cards that draw the session.
/// </summary>
public partial class ViewCard : UserControl
{
    public ViewCard() => InitializeComponent();

    MainViewModel? Model => DataContext as MainViewModel;

    // ------------------------------------------------------ dropping a file

    /// <summary>Only a file can be dropped on the band; anything else is turned away.</summary>
    void OnDrag(object sender, DragEventArgs e)
    {
        bool file = CompareSection.FileIn(e) != null;
        e.Effects = file ? DragDropEffects.Copy : DragDropEffects.None;
        if (Model is { } model) model.ViewOver = file;
        e.Handled = true;
    }

    void OnLeave(object sender, DragEventArgs e)
    {
        if (Model is { } model) model.ViewOver = false;
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        if (Model is not { } model) return;
        model.ViewOver = false;
        if (CompareSection.FileIn(e) is { } path) model.Take(path);
        e.Handled = true;
    }
}
