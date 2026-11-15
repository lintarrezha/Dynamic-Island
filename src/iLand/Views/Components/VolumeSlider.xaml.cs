using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace iLand.Views.Components;

/// <summary>
/// Slider volume 0.0 sampai 1.0 (lihat VolumeSlider.xaml). Value berubah sambil digeser (binding dua arah).
/// Selama digeser, nilai dari luar diabaikan supaya tidak berebut dengan mouse pengguna.
/// Kontrol yang dimatikan (IsEnabled = false) tidak bereaksi dan tampil redup.
/// </summary>
public partial class VolumeSlider : UserControl
{
    private const double ThumbSize = 12;

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(VolumeSlider),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

    public static readonly DependencyProperty FillBrushProperty = DependencyProperty.Register(
        nameof(FillBrush), typeof(System.Windows.Media.Brush), typeof(VolumeSlider), new PropertyMetadata(null));

    private bool _isDragging;

    public VolumeSlider()
    {
        InitializeComponent();

        // Warna isi bawaan = putih. Pemakai bisa menggantinya (misalnya aksen dari sampul album).
        SetCurrentValue(FillBrushProperty, (System.Windows.Media.Brush)FindResource("TextPrimaryBrush"));

        IsEnabledChanged += (_, _) => UpdateVisual();
    }

    /// <summary>Brush untuk bagian slider yang sudah terisi.</summary>
    public System.Windows.Media.Brush? FillBrush
    {
        get => (System.Windows.Media.Brush?)GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    /// <summary>Posisi slider, 0.0 sampai 1.0.</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((VolumeSlider)d).UpdateVisual();
    }

    private void Root_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateVisual();

    private void Root_MouseChanged(object sender, MouseEventArgs e) => UpdateVisual();

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled)
        {
            return;
        }

        _isDragging = true;
        Root.CaptureMouse();
        Value = FractionAt(e);
        UpdateVisual();
        e.Handled = true; // klik ini milik slider, bukan klik pada island
    }

    private void Root_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            Value = FractionAt(e);
        }
    }

    private void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        Value = FractionAt(e);
        _isDragging = false;
        Root.ReleaseMouseCapture();
        UpdateVisual();
        e.Handled = true;
    }

    private void Root_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            UpdateVisual();
        }
    }

    private double FractionAt(MouseEventArgs e)
    {
        double width = Root.ActualWidth;
        if (width <= 0)
        {
            return 0;
        }

        return Math.Clamp(e.GetPosition(Root).X / width, 0, 1);
    }

    private void UpdateVisual()
    {
        double width = Root.ActualWidth;
        double value = double.IsNaN(Value) ? 0 : Math.Clamp(Value, 0, 1);

        FillBar.Width = width * value;
        ThumbShift.X = Math.Clamp(width * value - ThumbSize / 2, 0, Math.Max(0, width - ThumbSize));

        // Thumb memakai Opacity (bukan warna) supaya konsisten dengan aturan hover di seluruh island.
        ThumbDot.Opacity = IsEnabled && (_isDragging || Root.IsMouseOver) ? 1 : 0;

        Root.Opacity = IsEnabled ? 1 : 0.35;

        if (IsEnabled)
        {
            Root.Cursor = Cursors.Hand;
        }
        else
        {
            Root.ClearValue(CursorProperty);
        }
    }
}
