using System.Windows;
using System.Windows.Controls;

namespace iLand.Views.SettingsControls;

/// <summary>
/// Kerangka satu halaman Settings: judul (28 Bold), subjudul redup, lalu isi (kartu-kartu) dalam area yang bisa digulir,
/// lebar maksimal 640 dan terpusat. Tampilannya ada di SettingsTheme.xaml (gaya implisit untuk tipe ini).
/// Pakai: &lt;sc:SettingsPageLayout Title="Island" Subtitle="..."&gt; ...kartu... &lt;/sc:SettingsPageLayout&gt;
/// </summary>
public class SettingsPageLayout : ContentControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(SettingsPageLayout), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle), typeof(string), typeof(SettingsPageLayout), new PropertyMetadata(string.Empty));

    static SettingsPageLayout()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SettingsPageLayout), new FrameworkPropertyMetadata(typeof(SettingsPageLayout)));
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }
}
