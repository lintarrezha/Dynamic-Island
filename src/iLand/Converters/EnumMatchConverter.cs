using System.Globalization;
using System.Windows.Data;

namespace iLand.Converters;

/// <summary>
/// Mengubah "nilai enum" menjadi true/false untuk tombol radio.
/// Contoh: SelectedTab = Music, parameter = Music  ->  true.
/// Dua arah: saat tombol radio dipilih, nilai parameter dikembalikan ke SelectedTab.
/// </summary>
public sealed class EnumMatchConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is not null && parameter is not null && value.Equals(parameter);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Hanya saat tombol dipilih (true) nilai diubah. Saat tombol dilepas (false), abaikan.
        return value is true && parameter is not null ? parameter : Binding.DoNothing;
    }
}
