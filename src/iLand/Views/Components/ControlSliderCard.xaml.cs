using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace iLand.Views.Components;

/// <summary>
/// Kartu slider VERTIKAL 0.0 sampai 1.0 untuk volume dan kecerahan (lihat ControlSliderCard.xaml).
/// 0.0 = dasar kartu, 1.0 = puncak kartu. Value berubah sambil digeser (binding dua arah).
/// Selama digeser, nilai dari luar diabaikan supaya tidak berebut dengan mouse pengguna.
/// Kontrol yang dimatikan (IsEnabled = false) tidak bereaksi dan tampil redup.
/// </summary>
public partial class ControlSliderCard : UserControl
{
    // Harus sama dengan Margin di ControlSliderCard.xaml: ikon 20 px dengan jarak 14 px dari dasar, angka dengan jarak 12 px dari puncak.
    private const double IconBottomPadding = 14;
    private const double IconSize = 20;
    private const double TextTopPadding = 12;

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(ControlSliderCard),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnVisualPropertyChanged));

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(Geometry), typeof(ControlSliderCard), new PropertyMetadata(null));

    /// <summary>Warna ikon saat berada di atas isi terang (misalnya kuning untuk kecerahan). Bawaan: gelap.</summary>
    public static readonly DependencyProperty IconBrushProperty = DependencyProperty.Register(
        nameof(IconBrush), typeof(Brush), typeof(ControlSliderCard), new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(ControlSliderCard), new PropertyMetadata(string.Empty, OnVisualPropertyChanged));

    private static readonly Brush DarkBrush = CreateDarkBrush();

    private bool _isDragging;
    private bool _loaded;

    public ControlSliderCard()
    {
        InitializeComponent();
        _loaded = true;
        IsEnabledChanged += (_, _) => UpdateVisual();
    }

    /// <summary>Posisi slider, 0.0 (bawah) sampai 1.0 (atas).</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public Geometry? Icon
    {
        get => (Geometry?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public Brush? IconBrush
    {
        get => (Brush?)GetValue(IconBrushProperty);
        set => SetValue(IconBrushProperty, value);
    }

    /// <summary>Teks angka di atas kartu, misalnya "56%".</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private static Brush CreateDarkBrush()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1E));
        brush.Freeze();
        return brush;
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ControlSliderCard)d).UpdateVisual();

    private void Host_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Clip dengan sudut membulat (ClipToBounds tidak mengikuti CornerRadius).
        // Radius sama dengan kartu lain (token ControlTileRadius), dibatasi setengah sisi terpendek agar tidak aneh pada ukuran kecil.
        double radius = ((CornerRadius)FindResource("ControlTileRadius")).TopLeft;
        radius = Math.Min(radius, Math.Min(Host.ActualWidth, Host.ActualHeight) / 2);
        Bars.Clip = new RectangleGeometry(new Rect(0, 0, Host.ActualWidth, Host.ActualHeight), radius, radius);
        UpdateVisual();
    }

    private void Host_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled)
        {
            return;
        }

        _isDragging = true;
        Host.CaptureMouse();
        Value = FractionAt(e);
        e.Handled = true; // klik ini milik slider, bukan klik pada island
    }

    private void Host_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            Value = FractionAt(e);
        }
    }

    private void Host_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        Value = FractionAt(e);
        _isDragging = false;
        Host.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void Host_LostMouseCapture(object sender, MouseEventArgs e)
    {
        _isDragging = false;
    }

    // Posisi mouse dihitung dari DASAR kartu: makin ke atas makin besar nilainya.
    private double FractionAt(MouseEventArgs e)
    {
        double height = Host.ActualHeight;
        if (height <= 0)
        {
            return 0;
        }

        double y = e.GetPosition(Host).Y;
        return Math.Clamp(1 - y / height, 0, 1);
    }

    private void UpdateVisual()
    {
        if (!_loaded)
        {
            return;
        }

        double height = Host.ActualHeight;
        double value = double.IsNaN(Value) ? 0 : Math.Clamp(Value, 0, 1);
        double fillHeight = height * value;
        FillBar.Height = fillHeight;

        // Di atas isi terang: ikon berwarna (IconBrush) dan angka gelap. Di atas bagian kaca: keduanya putih.
        Brush light = (Brush)FindResource("TextPrimaryBrush");
        bool iconCovered = fillHeight >= IconBottomPadding + IconSize;
        bool textCovered = fillHeight >= height - TextTopPadding - ValueText.ActualHeight / 2;
        IconPath.Stroke = iconCovered ? (IconBrush ?? DarkBrush) : light;
        ValueText.Foreground = textCovered ? DarkBrush : light;

        Host.Opacity = IsEnabled ? 1 : 0.4;
        if (IsEnabled)
        {
            Host.Cursor = Cursors.Hand;
        }
        else
        {
            Host.ClearValue(CursorProperty);
        }
    }
}
