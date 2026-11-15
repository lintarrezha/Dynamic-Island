using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace iLand.Converters;

/// <summary>
/// Menampilkan elemen hanya jika nilai enum sama dengan parameter.
/// Dipakai untuk menampilkan panel tab yang sedang dipilih.
/// </summary>
public sealed class EnumVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isMatch = value is not null && parameter is not null && value.Equals(parameter);
        return isMatch ? Visibility.Visible : Visibility.Collapsed;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
