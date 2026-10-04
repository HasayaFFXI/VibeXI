using System.Windows;
using System.Windows.Controls;
using Zerg.Core;

namespace Zerg.Views;

/// <summary>How run B's figure differs from run A's, in a table cell or on a tile.</summary>
public partial class ChangeText : UserControl
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(Change), typeof(ChangeText));

    public static readonly DependencyProperty BesideProperty =
        DependencyProperty.Register(nameof(Beside), typeof(bool), typeof(ChangeText));

    public ChangeText() => InitializeComponent();

    public Change? Value { get => (Change?)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    /// <summary>The plain difference goes beside the percentage, not under it.</summary>
    public bool Beside { get => (bool)GetValue(BesideProperty); set => SetValue(BesideProperty, value); }
}
