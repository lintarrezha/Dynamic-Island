using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace iLand.Views.SettingsControls;

/// <summary>
/// Baris di kartu pengaturan: judul dan deskripsi di kiri, kontrol (Content) di kanan.
/// Tampilannya ada di SettingsTheme.xaml (gaya implisit untuk tipe ini), jadi baris ini hanya tampil benar
/// di jendela yang memuat SettingsTheme.xaml.
/// Pakai: &lt;sc:SettingsRow Title="Hover to expand" Description="..."&gt; &lt;CheckBox Style="{StaticResource SettingsToggleStyle}" .../&gt; &lt;/sc:SettingsRow&gt;
/// Judul otomatis menjadi AutomationProperties.Name kontrol di dalamnya jika kontrol itu belum punya nama (pembaca layar).
/// </summary>
public class SettingsRow : ContentControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(SettingsRow),
        new PropertyMetadata(string.Empty, (d, _) => ((SettingsRow)d).ApplyAutomationName()));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(SettingsRow),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsStackedProperty = DependencyProperty.Register(
        nameof(IsStacked), typeof(bool), typeof(SettingsRow),
        new PropertyMetadata(false));

    static SettingsRow()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SettingsRow), new FrameworkPropertyMetadata(typeof(SettingsRow)));
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>True: kontrol ditaruh di bawah teks dan melebar penuh (untuk slider).</summary>
    public bool IsStacked
    {
        get => (bool)GetValue(IsStackedProperty);
        set => SetValue(IsStackedProperty, value);
    }

    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        ApplyAutomationName();
    }

    private void ApplyAutomationName()
    {
        if (Content is DependencyObject control
            && !string.IsNullOrEmpty(Title)
            && string.IsNullOrEmpty(AutomationProperties.GetName(control)))
        {
            AutomationProperties.SetName(control, Title);
        }
    }
}
