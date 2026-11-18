using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace iLand.Converters;

/// <summary>
/// Mengubah nama resource (teks, misalnya "IconIsland") menjadi resource aplikasi itu sendiri (misalnya ikon Geometry).
/// Dipakai menu samping Settings agar ViewModel cukup menyimpan nama ikon, tanpa menyentuh resource WPF.
/// </summary>
public sealed class ResourceKeyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string key && key.Length > 0)
        {
            object? resource = Application.Current?.TryFindResource(key);
            if (resource is not null)
            {
                return resource;
            }
        }

        return Binding.DoNothing;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
