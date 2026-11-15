using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using iLand.Helpers;

namespace iLand.Views.Components;

/// <summary>
/// Cincin progress melingkar (digambar lewat OnRender, tanpa template). Mulai dari atas, searah jarum jam, ujung bulat.
///
/// Cara kerja gerak: TargetProgress diisi dari ViewModel (nilai 0 sampai 1) setiap detik. Cincin lalu menganimasikan
/// DisplayedProgress ke nilai itu:
///   - perubahan kecil (detak tiap detik): linear 1 detik, jadi cincin bergerak mulus tanpa timer tambahan;
///   - perubahan besar (reset, ganti tahap): 300 ms dengan kurva ResizeEase;
///   - lonjakan turun lebih dari setengah putaran (stopwatch kembali ke 0): langsung tanpa animasi mundur.
/// Animasi hanya berjalan saat cincin terlihat di layar. Saat tersembunyi, nilainya langsung disalin.
///
/// Denyut: setiap PulseTrigger bertambah, cincin membesar sedikit lalu kembali (satu kali).
/// </summary>
public sealed class ProgressRing : FrameworkElement
{
    public static readonly DependencyProperty TargetProgressProperty = DependencyProperty.Register(
        nameof(TargetProgress), typeof(double), typeof(ProgressRing),
        new PropertyMetadata(0d, OnTargetProgressChanged));

    public static readonly DependencyProperty DisplayedProgressProperty = DependencyProperty.Register(
        nameof(DisplayedProgress), typeof(double), typeof(ProgressRing),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(ProgressRing),
        new FrameworkPropertyMetadata(5d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RingBrushProperty = DependencyProperty.Register(
        nameof(RingBrush), typeof(Brush), typeof(ProgressRing),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(ProgressRing),
        new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PulseTriggerProperty = DependencyProperty.Register(
        nameof(PulseTrigger), typeof(int), typeof(ProgressRing),
        new PropertyMetadata(0, OnPulseTriggerChanged));

    private readonly ScaleTransform _pulseScale = new(1, 1);

    public ProgressRing()
    {
        IsHitTestVisible = false;
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = _pulseScale;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    /// <summary>Nilai tujuan (0 sampai 1) dari ViewModel.</summary>
    public double TargetProgress
    {
        get => (double)GetValue(TargetProgressProperty);
        set => SetValue(TargetProgressProperty, value);
    }

    /// <summary>Nilai yang sedang digambar. Dianimasikan oleh cincin ini, jangan diikat dari luar.</summary>
    public double DisplayedProgress
    {
        get => (double)GetValue(DisplayedProgressProperty);
        set => SetValue(DisplayedProgressProperty, value);
    }

    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public Brush RingBrush
    {
        get => (Brush)GetValue(RingBrushProperty);
        set => SetValue(RingBrushProperty, value);
    }

    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public int PulseTrigger
    {
        get => (int)GetValue(PulseTriggerProperty);
        set => SetValue(PulseTriggerProperty, value);
    }

    // ---- Gerak ----

    private static void OnTargetProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ProgressRing)d).MoveTo(Math.Clamp((double)e.NewValue, 0, 1));
    }

    private void MoveTo(double target)
    {
        // Tidak terlihat (tab lain, island menutup) atau gerak dikurangi: salin nilai langsung, tanpa animasi.
        if (!IsVisible || MotionTokens.ReducedMotion)
        {
            Snap(target);
            return;
        }

        double current = DisplayedProgress; // membaca nilai saat ini, termasuk yang sedang beranimasi
        double delta = target - current;

        // Turun lebih dari setengah putaran (stopwatch kembali ke nol): langsung, tanpa animasi mundur.
        if (delta < -0.5)
        {
            Snap(target);
            return;
        }

        var animation = Math.Abs(delta) > MotionTokens.RingJumpThreshold
            ? new DoubleAnimation(target, TimeSpan.FromMilliseconds(MotionTokens.RingJumpMs))
            {
                EasingFunction = MotionTokens.ResizeEase
            }
            : new DoubleAnimation(target, TimeSpan.FromMilliseconds(MotionTokens.RingTickMs)); // linear

        BeginAnimation(DisplayedProgressProperty, animation);
    }

    private void Snap(double target)
    {
        BeginAnimation(DisplayedProgressProperty, null); // lepas animasi dulu, baru atur nilai
        DisplayedProgress = target;
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // Saat muncul atau hilang, pastikan gambar sama dengan nilai tujuan terbaru.
        Snap(Math.Clamp(TargetProgress, 0, 1));
    }

    private static void OnPulseTriggerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ProgressRing)d).Pulse();
    }

    private void Pulse()
    {
        if (!IsVisible || MotionTokens.ReducedMotion)
        {
            return;
        }

        var grow = new DoubleAnimation(1, MotionTokens.PulseScale, TimeSpan.FromMilliseconds(MotionTokens.PulseMs / 2))
        {
            AutoReverse = true,
            EasingFunction = MotionTokens.ResizeEase,
            FillBehavior = FillBehavior.Stop // setelah selesai, skala kembali ke nilai dasar 1
        };

        _pulseScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        _pulseScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
    }

    // ---- Gambar ----

    protected override Size MeasureOverride(Size availableSize)
    {
        double w = double.IsInfinity(availableSize.Width) ? 128 : availableSize.Width;
        double h = double.IsInfinity(availableSize.Height) ? 128 : availableSize.Height;
        return new Size(w, h);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        double thickness = Thickness;
        if (size <= thickness || thickness <= 0)
        {
            return;
        }

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        double radius = (size - thickness) / 2;

        // Jalur abu di belakang.
        dc.DrawEllipse(null, new Pen(TrackBrush, thickness), center, radius, radius);

        double progress = Math.Clamp(DisplayedProgress, 0, 1);
        if (progress <= 0.001)
        {
            return;
        }

        var pen = new Pen(RingBrush, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };

        if (progress >= 0.999)
        {
            dc.DrawEllipse(null, pen, center, radius, radius);
            return;
        }

        // Busur dari titik atas, searah jarum jam.
        double angle = progress * 2 * Math.PI;
        var start = new Point(center.X, center.Y - radius);
        var end = new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));

        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(start, false, false);
            ctx.ArcTo(end, new Size(radius, radius), 0, angle > Math.PI, SweepDirection.Clockwise, true, false);
        }

        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }
}
