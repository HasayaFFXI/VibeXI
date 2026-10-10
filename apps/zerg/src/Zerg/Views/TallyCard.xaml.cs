using System.Windows;
using System.Windows.Controls;

namespace Zerg.Views;

/// <summary>
/// One of the Damage section's two side tables: By target, or By damage
/// type. What each target took, or what each type of damage came to, over
/// one parse, largest first; every row a button that isolates what it
/// names, exactly as its line of the Target or Type button's menu does,
/// because it is the same <see cref="PickFilter"/> that is told.
///
/// <para>One card serves both. Which it is, is the filter it is given
/// (<see cref="Filter"/>): its title, its note, the word over its first
/// column and its rows are all the filter's. The card's data context stays
/// the view model, as every card's does, for the shading setting and for
/// what an empty table says.</para>
///
/// <para>It can float (<see cref="Info"/>), as a table: the same rows in
/// a panel's colours, which are the tokens a panel has under the same
/// keys. Nothing in it moves with the clock.</para>
/// </summary>
public partial class TallyCard : UserControl
{
    public static readonly DependencyProperty FilterProperty = DependencyProperty.Register(nameof(Filter), typeof(PickFilter),
        typeof(TallyCard), new PropertyMetadata(null));

    public static readonly DependencyProperty InfoProperty = DependencyProperty.Register(nameof(Info), typeof(PanelInfo),
        typeof(TallyCard), new PropertyMetadata(null));

    public TallyCard() => InitializeComponent();

    /// <summary>What the table lists and what its rows tell: the view
    /// model's filter by target, or its filter by damage type.</summary>
    public PickFilter? Filter { get => (PickFilter?)GetValue(FilterProperty); set => SetValue(FilterProperty, value); }

    /// <summary>The panel the card floats in, for Pop out.</summary>
    public PanelInfo? Info { get => (PanelInfo?)GetValue(InfoProperty); set => SetValue(InfoProperty, value); }
}
