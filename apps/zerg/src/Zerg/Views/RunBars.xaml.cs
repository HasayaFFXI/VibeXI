using System.Windows;
using System.Windows.Controls;
using Zerg.Core;

namespace Zerg.Views;

/// <summary>The two runs' amounts as a bar each, A above B, in one table cell.</summary>
public partial class RunBars : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(BarPair),
        typeof(RunBars), new PropertyMetadata(null, (d, _) => ((RunBars)d).Size()));

    public RunBars() => InitializeComponent();

    public BarPair? Value { get => (BarPair?)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    void Size()
    {
        Split(BarA, RestA, Value?.A ?? 0);
        Split(BarB, RestB, Value?.B ?? 0);
    }

    static void Split(ColumnDefinition bar, ColumnDefinition rest, double share)
    {
        share = Math.Clamp(share, 0, 1);
        bar.Width = new GridLength(share, GridUnitType.Star);
        rest.Width = new GridLength(1 - share, GridUnitType.Star);
    }
}
