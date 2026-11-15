using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace iLand.Views.Components;

/// <summary>
/// Visualizer "riwayat suara": batang paling kiri = suara sekarang, ke kanan = suara beberapa saat lalu,
/// yang makin tua mengecil menjadi titik. Hasilnya seperti gelombang yang mengalir ke kanan.
///
/// Ringan: tanpa timer atau animasi sendiri. Riwayat hanya bergeser saat Level berubah (paling sering 1 kali per
/// sekitar 65 ms, mengikuti pembaruan AudioLevelService), dan digambar ulang lewat OnRender pada saat itu juga.
/// Saat Level = 0 riwayat dikosongkan sehingga hanya titik yang tampil dan tidak ada pekerjaan sama sekali.
/// </summary>
public sealed class AudioVisualizer : FrameworkElement
{
    // Jarak minimum antar penggeseran riwayat (milidetik). Mengatur "kecepatan alir" gelombang.
    private const double PushIntervalMs = 65;

    // Pola tetap saat meter tidak tersedia (IsStatic): gelombang menurun ke kanan.
    private static readonly double[] StaticPattern = { 0.55, 0.85, 0.7, 0.45, 0.3, 0.15 };

    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(double), typeof(AudioVisualizer),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender, OnLevelChanged));

    public static readonly DependencyProperty IsStaticProperty = DependencyProperty.Register(
        nameof(IsStatic), typeof(bool), typeof(AudioVisualizer),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BarBrushProperty = DependencyProperty.Register(
        nameof(BarBrush), typeof(Brush), typeof(AudioVisualizer),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BarCountProperty = DependencyProperty.Register(
        nameof(BarCount), typeof(int), typeof(AudioVisualizer),
        new FrameworkPropertyMetadata(5, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, OnBarCountChanged));

    public static readonly DependencyProperty BarWidthProperty = DependencyProperty.Register(
        nameof(BarWidth), typeof(double), typeof(AudioVisualizer),
        new FrameworkPropertyMetadata(2.5, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BarSpacingProperty = DependencyProperty.Register(
        nameof(BarSpacing), typeof(double), typeof(AudioVisualizer),
        new FrameworkPropertyMetadata(2d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    // Riwayat level yang sudah dibentuk (0..1). Indeks 0 = terbaru.
    private double[] _history = new double[5];
    private long _lastPushTicks;

    // Penguat otomatis: lantai dan langit-langit level yang mengikuti lagu secara perlahan.
    // Tanpa ini, lagu yang keras membuat peak meter menempel di dekat 1,0 dan semua batang tampil penuh dan seragam.
    private double _floor;
    private double _ceil;

    public double Level { get => (double)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
    public bool IsStatic { get => (bool)GetValue(IsStaticProperty); set => SetValue(IsStaticProperty, value); }
    public Brush BarBrush { get => (Brush)GetValue(BarBrushProperty); set => SetValue(BarBrushProperty, value); }
    public int BarCount { get => (int)GetValue(BarCountProperty); set => SetValue(BarCountProperty, value); }
    public double BarWidth { get => (double)GetValue(BarWidthProperty); set => SetValue(BarWidthProperty, value); }
    public double BarSpacing { get => (double)GetValue(BarSpacingProperty); set => SetValue(BarSpacingProperty, value); }

    private static void OnBarCountChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var visualizer = (AudioVisualizer)d;
        visualizer._history = new double[Math.Max(1, visualizer.BarCount)];
    }

    private static void OnLevelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var visualizer = (AudioVisualizer)d;
        double level = Math.Clamp((double)e.NewValue, 0, 1);

        if (level <= 0.001)
        {
            // Hening: kosongkan riwayat supaya batang lama tidak "beku" di layar.
            Array.Clear(visualizer._history);
            visualizer._floor = 0;
            visualizer._ceil = 0;
            return;
        }

        long now = Stopwatch.GetTimestamp();
        double elapsedMs = (now - visualizer._lastPushTicks) * 1000.0 / Stopwatch.Frequency;
        if (elapsedMs < PushIntervalMs)
        {
            // Terlalu cepat: perbarui saja nilai terbaru tanpa menggeser riwayat.
            visualizer._history[0] = visualizer.Shape(level);
            return;
        }

        visualizer._lastPushTicks = now;

        double[] history = visualizer._history;
        Array.Copy(history, 0, history, 1, history.Length - 1);
        history[0] = visualizer.Shape(level);
    }

    /// <summary>
    /// Mengubah level mentah (peak meter) menjadi tinggi batang 0..1 yang menonjolkan naik-turunnya lagu.
    /// Langit-langit turun perlahan, lantai naik perlahan: hanya selisih terhadap keadaan lagu belakangan ini yang
    /// ditampilkan, lalu dikalikan sedikit dengan kerasnya suara sebenarnya supaya bagian pelan tetap terlihat pelan.
    /// </summary>
    private double Shape(double level)
    {
        _ceil = Math.Max(level, _ceil * 0.97);
        _floor = level < _floor || _floor == 0 ? level : _floor + (level - _floor) * 0.04;

        double range = Math.Max(_ceil - _floor, 0.15);
        double relative = Math.Clamp((level - _floor) / range, 0, 1);
        double loudness = 0.35 + 0.65 * Math.Sqrt(level);

        return Math.Clamp(Math.Pow(relative, 1.2) * loudness, 0, 1);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        int n = Math.Max(1, BarCount);
        return new Size(n * BarWidth + (n - 1) * BarSpacing, double.IsInfinity(availableSize.Height) ? 14 : availableSize.Height);
    }

    protected override void OnRender(DrawingContext dc)
    {
        int n = Math.Max(1, BarCount);
        double h = ActualHeight;
        double dot = BarWidth;               // batang terpendek = titik bulat
        double radius = BarWidth / 2;
        double x = 0;

        double[] history = _history.Length == n ? _history : new double[n];

        for (int i = 0; i < n; i++)
        {
            double value = IsStatic
                ? (i < StaticPattern.Length ? StaticPattern[i] : 0)
                : history[i];

            double bar = h * value;

            if (bar <= dot + 0.5)
            {
                // Titik: redup, supaya batang yang bersuara menonjol.
                dc.PushOpacity(0.45);
                dc.DrawEllipse(BarBrush, null, new Point(x + radius, h / 2), radius, radius);
                dc.Pop();
            }
            else
            {
                double y = (h - bar) / 2;
                dc.DrawRoundedRectangle(BarBrush, null, new Rect(x, y, BarWidth, bar), radius, radius);
            }

            x += BarWidth + BarSpacing;
        }
    }
}
