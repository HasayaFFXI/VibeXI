using System.Globalization;
using System.Windows.Data;

namespace Zerg.Views;

/// <summary>
/// A key chord as it is written ("Ctrl+Alt+Z") into its keys, one string
/// each, to draw each as a keycap: the hot key on the Settings page. A
/// chord's keys are modifiers, letters, digits and F1 to F24
/// (<c>Zerg.Core/KeyChord</c>), so a plus sign is only ever between two.
/// </summary>
public sealed class ChordKeysConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value as string ?? "").Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>A whole percentage as a share, 0 to 1: an opacity set as "70" for something drawn at 0.7.</summary>
public sealed class PercentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is IConvertible number ? Math.Clamp(number.ToDouble(CultureInfo.InvariantCulture) / 100, 0, 1) : 1.0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
