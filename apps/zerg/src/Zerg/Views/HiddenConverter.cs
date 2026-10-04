using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Zerg.Views;

/// <summary>Shows something only while a flag is off: the opposite of the
/// stock true-is-visible converter.</summary>
public sealed class HiddenConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not Visibility.Visible;
}
