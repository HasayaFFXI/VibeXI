using System.Globalization;
using System.Windows.Data;

namespace Zerg;

/// <summary>
/// Binds one of a group of toggles to a single value: checked when the value
/// equals the parameter, and checking it sets the value to the parameter.
/// Unchecking (another toggle took over) leaves the value alone.
/// </summary>
sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Equals(value?.ToString(), parameter?.ToString());

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? parameter : Binding.DoNothing;
}
