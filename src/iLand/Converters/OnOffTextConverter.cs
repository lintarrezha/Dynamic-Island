using System.Globalization;
using System.Windows.Data;

namespace iLand.Converters;

/// <summary>Mengubah bool menjadi teks "On" atau "Off" untuk keterangan kartu.</summary>
public sealed class OnOffTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? "On" : "Off";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
