using System.Windows;
using System.Windows.Controls;
using Zerg.Core;

namespace Zerg.Views;

/// <summary>A figure for each of the two runs in one table cell, A over B.</summary>
public partial class RunPair : UserControl
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(AB), typeof(RunPair));

    public static readonly DependencyProperty WordsProperty = DependencyProperty.Register(nameof(Words), typeof(bool),
        typeof(RunPair), new PropertyMetadata(false, (d, e) =>
            ((RunPair)d).Lines.HorizontalAlignment = (bool)e.NewValue ? HorizontalAlignment.Left : HorizontalAlignment.Right));

    public RunPair() => InitializeComponent();

    public AB? Value { get => (AB?)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    /// <summary>The cell holds words, not figures: it reads from the left.</summary>
    public bool Words { get => (bool)GetValue(WordsProperty); set => SetValue(WordsProperty, value); }
}
