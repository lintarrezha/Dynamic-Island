using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace iLand.Views.Components;

/// <summary>
/// Tampilan kosong yang seragam: ikon di atas, teks pendek di bawah.
/// Dua property yang diisi dari XAML: Icon (bentuk ikon, misalnya {StaticResource IconMusic}) dan Text.
/// </summary>
public partial class EmptyState : UserControl
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(Geometry), typeof(EmptyState), new PropertyMetadata(null));

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(EmptyState), new PropertyMetadata(string.Empty));

    /// <summary>Bentuk ikon (kanvas 24 x 24, gaya garis).</summary>
    public Geometry? Icon
    {
        get => (Geometry?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>Teks keterangan di bawah ikon.</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public EmptyState()
    {
        InitializeComponent();
    }
}
