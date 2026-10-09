using System.Windows;
using System.Windows.Controls;
using Zerg.Core;

namespace Zerg.Views;

/// <summary>
/// A figure for each of the two runs in one table cell, A over B. Given as
/// a pair (<see cref="Value"/>), or line by line (<see cref="A"/> and
/// <see cref="B"/>: the two figures of an amount that is also drawn as
/// bars). The cell's padding is its <c>Padding</c>, its size, weight and
/// ink the usual text properties.
/// </summary>
public partial class RunPair : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(AB),
        typeof(RunPair), new PropertyMetadata(null, (d, e) =>
        {
            d.SetCurrentValue(AProperty, (e.NewValue as AB)?.A ?? "");
            d.SetCurrentValue(BProperty, (e.NewValue as AB)?.B ?? "");
        }));

    public static readonly DependencyProperty AProperty =
        DependencyProperty.Register(nameof(A), typeof(string), typeof(RunPair), new PropertyMetadata(""));

    public static readonly DependencyProperty BProperty =
        DependencyProperty.Register(nameof(B), typeof(string), typeof(RunPair), new PropertyMetadata(""));

    public static readonly DependencyProperty WordsProperty = DependencyProperty.Register(nameof(Words), typeof(bool),
        typeof(RunPair), new PropertyMetadata(false, (d, e) =>
            ((RunPair)d).Lines.HorizontalAlignment = (bool)e.NewValue ? HorizontalAlignment.Left : HorizontalAlignment.Right));

    public RunPair() => InitializeComponent();

    public AB? Value { get => (AB?)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    /// <summary>Run A's line, where the two are not given as a pair.</summary>
    public string A { get => (string)GetValue(AProperty); set => SetValue(AProperty, value); }

    /// <summary>Run B's line.</summary>
    public string B { get => (string)GetValue(BProperty); set => SetValue(BProperty, value); }

    /// <summary>The cell holds words, not figures: it reads from the left.</summary>
    public bool Words { get => (bool)GetValue(WordsProperty); set => SetValue(WordsProperty, value); }
}
