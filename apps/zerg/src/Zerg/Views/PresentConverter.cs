using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Zerg.Views;

/// <summary>Shows something only while there is something to show: a value
/// that is not null, a count that is not zero, a text that is not empty.</summary>
public sealed class PresentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null or 0 or "" ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
