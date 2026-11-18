using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace iLand.Views.SettingsControls;

/// <summary>
/// Slider berangka untuk Settings: slider tipis di kiri dan angka di kanan (misalnya "100%").
/// Tampilannya ada di SettingsTheme.xaml (gaya implisit untuk tipe ini).
/// Pakai: &lt;sc:SettingsSlider Minimum="80" Maximum="115" TickFrequency="1" ValueFormat="{}{0:0}%" Value="{Binding Scale}" /&gt;
/// Angka yang tampil = Value x DisplayScale, diformat dengan ValueFormat (misalnya nilai 0,85 dengan DisplayScale 100 menjadi "85%").
/// Nama untuk pembaca layar: isi AutomationProperties.Name, atau biarkan SettingsRow yang mengisinya dari judul baris.
/// </summary>
public class SettingsSlider : Control
{
    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(SettingsSlider), new PropertyMetadata(0.0));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(SettingsSlider), new PropertyMetadata(100.0));

    public static readonly DependencyProperty TickFrequencyProperty = DependencyProperty.Register(
        nameof(TickFrequency), typeof(double), typeof(SettingsSlider), new PropertyMetadata(1.0));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(SettingsSlider),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, _) => ((SettingsSlider)d).UpdateDisplayText()));

    public static readonly DependencyProperty ValueFormatProperty = DependencyProperty.Register(
        nameof(ValueFormat), typeof(string), typeof(SettingsSlider),
        new PropertyMetadata("{0:0}", (d, _) => ((SettingsSlider)d).UpdateDisplayText()));

    public static readonly DependencyProperty DisplayScaleProperty = DependencyProperty.Register(
        nameof(DisplayScale), typeof(double), typeof(SettingsSlider),
        new PropertyMetadata(1.0, (d, _) => ((SettingsSlider)d).UpdateDisplayText()));

    private static readonly DependencyPropertyKey DisplayTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(DisplayText), typeof(string), typeof(SettingsSlider), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DisplayTextProperty = DisplayTextPropertyKey.DependencyProperty;

    static SettingsSlider()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SettingsSlider), new FrameworkPropertyMetadata(typeof(SettingsSlider)));
    }

    public SettingsSlider()
    {
        UpdateDisplayText();
    }

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>Langkah geser. Nilai selalu dibulatkan ke kelipatan ini.</summary>
    public double TickFrequency
    {
        get => (double)GetValue(TickFrequencyProperty);
        set => SetValue(TickFrequencyProperty, value);
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>Format angka di kanan, misalnya "{0:0}%" atau "{0:0} ms".</summary>
    public string ValueFormat
    {
        get => (string)GetValue(ValueFormatProperty);
        set => SetValue(ValueFormatProperty, value);
    }

    /// <summary>Pengali sebelum diformat (100 untuk menampilkan pecahan sebagai persen).</summary>
    public double DisplayScale
    {
        get => (double)GetValue(DisplayScaleProperty);
        set => SetValue(DisplayScaleProperty, value);
    }

    /// <summary>Teks angka yang tampil di kanan slider (hanya baca).</summary>
    public string DisplayText => (string)GetValue(DisplayTextProperty);

    private void UpdateDisplayText()
    {
        double shown = Value * DisplayScale;
        string text;

        try
        {
            text = string.Format(CultureInfo.CurrentCulture, ValueFormat ?? "{0:0}", shown);
        }
        catch (FormatException)
        {
            text = shown.ToString("0.##", CultureInfo.CurrentCulture); // format salah: tampilkan angka apa adanya
        }

        SetValue(DisplayTextPropertyKey, text);
    }
}
