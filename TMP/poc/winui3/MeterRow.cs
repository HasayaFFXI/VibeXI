using System.ComponentModel;
using Microsoft.UI.Xaml;

namespace WinUiSpike;

/// <summary>One fake-meter row, bound with x:Bind OneWay. The bar is two star
/// columns (value / rest), so no layout code is needed to size it.</summary>
public sealed class MeterRow : INotifyPropertyChanged
{
    public MeterRow(string name) => Name = name;

    public string Name { get; }
    public long Damage { get; private set; }
    public GridLength BarWidth { get; private set; } = new(0, GridUnitType.Star);
    public GridLength RestWidth { get; private set; } = new(1, GridUnitType.Star);
    public string Text => Damage.ToString("N0");

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Add(long dmg) => Damage += dmg;

    public void Scale(long max)
    {
        double f = max > 0 ? (double)Damage / max : 0;
        BarWidth = new(f, GridUnitType.Star);
        RestWidth = new(1 - f, GridUnitType.Star);
        PropertyChanged?.Invoke(this, new(nameof(BarWidth)));
        PropertyChanged?.Invoke(this, new(nameof(RestWidth)));
        PropertyChanged?.Invoke(this, new(nameof(Text)));
    }
}
