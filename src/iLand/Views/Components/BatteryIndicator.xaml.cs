using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace iLand.Views.Components;

/// <summary>
/// Cincin baterai. Tiga property bisa di-binding dari XAML:
/// Percent (0-100) menentukan panjang busur dan warna, IsCharging menampilkan petir di tengah cincin,
/// RingSize menentukan diameter cincin dalam piksel (bawaan 18, dipakai di pill idle).
///
/// Warna busur mengikuti persentase:
/// 50% ke atas = hijau, 30% sampai 49% = kuning, di bawah 30% = merah.
/// Warnanya diambil dari Theme.xaml (SuccessBrush, WarningBrush, DangerBrush).
/// </summary>
public partial class BatteryIndicator : UserControl
{
    // Batas persentase untuk perubahan warna.
    private const int GreenFromPercent = 50;
    private const int YellowFromPercent = 30;

    // Ketebalan cincin = 2,5 px pada diameter 18 px. Ukuran lain mengikuti perbandingan yang sama.
    private const double ThicknessRatio = 2.5 / 18.0;

    // Ukuran terkecil supaya cincin tidak rusak jika diberi angka terlalu kecil.
    private const double MinRingSize = 8;

    public static readonly DependencyProperty PercentProperty = DependencyProperty.Register(
        nameof(Percent), typeof(int), typeof(BatteryIndicator), new PropertyMetadata(0, OnVisualChanged));

    public static readonly DependencyProperty IsChargingProperty = DependencyProperty.Register(
        nameof(IsCharging), typeof(bool), typeof(BatteryIndicator), new PropertyMetadata(false, OnVisualChanged));

    public static readonly DependencyProperty RingSizeProperty = DependencyProperty.Register(
        nameof(RingSize), typeof(double), typeof(BatteryIndicator), new PropertyMetadata(18.0, OnVisualChanged));

    public int Percent
    {
        get => (int)GetValue(PercentProperty);
        set => SetValue(PercentProperty, value);
    }

    public bool IsCharging
    {
        get => (bool)GetValue(IsChargingProperty);
        set => SetValue(IsChargingProperty, value);
    }

    /// <summary>Diameter cincin dalam piksel.</summary>
    public double RingSize
    {
        get => (double)GetValue(RingSizeProperty);
        set => SetValue(RingSizeProperty, value);
    }

    public BatteryIndicator()
    {
        InitializeComponent();
        UpdateVisual();
    }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((BatteryIndicator)d).UpdateVisual();
    }

    private void UpdateVisual()
    {
        if (Arc is null || Track is null || BoltBox is null)
        {
            return; // XAML belum selesai dimuat
        }

        // ---- Ukuran ----
        double size = Math.Max(RingSize, MinRingSize);
        double thickness = size * ThicknessRatio;
        double radius = (size - thickness) / 2; // jari-jari garis tengah cincin
        double center = size / 2;

        Width = size;
        Height = size;
        Track.StrokeThickness = thickness;
        Arc.StrokeThickness = thickness;
        BoltBox.Width = size * 0.5;
        BoltBox.Height = size * 0.5;

        // ---- Warna ----
        int percent = Math.Clamp(Percent, 0, 100);
        Arc.Stroke = percent >= GreenFromPercent ? GetBrush("SuccessBrush", Brushes.LimeGreen)
            : percent >= YellowFromPercent ? GetBrush("WarningBrush", Brushes.Gold)
            : GetBrush("DangerBrush", Brushes.Red);

        // ---- Busur: mulai dari jam 12, searah jarum jam ----
        if (percent == 0)
        {
            Arc.Visibility = Visibility.Collapsed;
        }
        else
        {
            Arc.Visibility = Visibility.Visible;
            Arc.Data = CreateArc(center, radius, percent);
        }

        BoltBox.Visibility = IsCharging ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Membuat busur dari jam 12 searah jarum jam sepanjang persen yang diberikan.
    /// Pada 100% sudut dibatasi 359,9 derajat karena busur dengan titik awal dan akhir sama tidak tergambar.
    /// </summary>
    private static Geometry CreateArc(double center, double radius, int percent)
    {
        double angle = Math.Min(percent / 100.0 * 360.0, 359.9);
        double radians = angle * Math.PI / 180.0;

        var start = new Point(center, center - radius);
        var end = new Point(
            center + radius * Math.Sin(radians),
            center - radius * Math.Cos(radians));

        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment(
            end,
            new Size(radius, radius),
            0,
            angle > 180,                 // busur lebih dari setengah lingkaran = "large arc"
            SweepDirection.Clockwise,
            true));

        var geometry = new PathGeometry(new[] { figure });
        geometry.Freeze();
        return geometry;
    }

    /// <summary>Mengambil brush dari resource aplikasi. Jika tidak ada (misalnya saat desain), dipakai cadangan.</summary>
    private static Brush GetBrush(string key, Brush fallback)
    {
        return Application.Current?.TryFindResource(key) as Brush ?? fallback;
    }
}
