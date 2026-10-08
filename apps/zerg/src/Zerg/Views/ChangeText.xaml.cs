using System.Windows;
using System.Windows.Controls;
using Zerg.Core;

namespace Zerg.Views;

/// <summary>
/// How run B's figure differs from run A's, in a table cell: the change,
/// toned, over the plain difference. The cell's padding is its
/// <c>Padding</c>. Where the two parts stand apart (a tile, the two columns
/// beside a distribution) they are two text blocks in the section's own
/// <c>Tone</c> style, not this.
/// </summary>
public partial class ChangeText : UserControl
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(Change), typeof(ChangeText));

    public ChangeText() => InitializeComponent();

    public Change? Value { get => (Change?)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
}
