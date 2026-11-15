using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using iLand.Helpers;

namespace iLand.Views.Components;

/// <summary>
/// Zona drop Shelf (lihat ShelfDropZone.xaml). Property: IsActive (file sedang diseret di atas island)
/// dan DropPulse (angka yang bertambah tiap ada drop berhasil, memberi kilau cahaya sekali).
///
/// Aturan animasi: nilai akhir dipasang sebagai nilai lokal, animasi sekali jalan memakai FillBehavior.Stop dengan nilai awal
/// eksplisit. Animasi berulang (cahaya) HANYA hidup selama IsActive dan terlihat, dilepas dengan
/// BeginAnimation(properti, null). Gerak berkurang (pengaturan Windows) mematikan semua animasi.
/// </summary>
public partial class ShelfDropZone : UserControl
{
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(ShelfDropZone),
        new PropertyMetadata(false, OnIsActiveChanged));

    public static readonly DependencyProperty DropPulseProperty = DependencyProperty.Register(
        nameof(DropPulse), typeof(int), typeof(ShelfDropZone),
        new PropertyMetadata(0, OnDropPulseChanged));

    private const double GlowOnOpacity = 0.55;

    private bool _loopsRunning;

    public ShelfDropZone()
    {
        InitializeComponent();

        Color accent = (TryFindResource("AccentBrush") as SolidColorBrush)?.Color ?? Color.FromRgb(0x0A, 0x84, 0xFF);
        Glow.Fill = BuildGlowBrush(accent, 0xB0);
        Ripple.Fill = BuildGlowBrush(accent, 0xE0);

        IsVisibleChanged += OnIsVisibleChanged;
        Unloaded += (_, _) => StopLoops();
    }

    /// <summary>True selama ada file yang diseret di atas island.</summary>
    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    /// <summary>Angka yang bertambah setiap ada drop berhasil. Perubahan menaik memberi kilau cahaya biru sekali.</summary>
    public int DropPulse
    {
        get => (int)GetValue(DropPulseProperty);
        set => SetValue(DropPulseProperty, value);
    }

    private static void OnIsActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ShelfDropZone)d).ApplyActive((bool)e.NewValue);
    }

    private static void OnDropPulseChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if ((int)e.NewValue > (int)e.OldValue)
        {
            ((ShelfDropZone)d).PlayDrop();
        }
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // Tab dibuka atau ditutup saat file sedang diseret: animasi berulang ikut berhenti atau mulai.
        if (!IsVisible)
        {
            StopLoops();
        }
        else if (IsActive)
        {
            StartLoops();
        }
    }

    private void ApplyActive(bool active)
    {
        bool animate = IsLoaded && IsVisible && !MotionTokens.ReducedMotion;

        // Lepas animasi masuk atau keluar yang masih berjalan, lalu pasang keadaan akhir sebagai nilai lokal.
        Zone.BeginAnimation(OpacityProperty, null);
        Back.BeginAnimation(OpacityProperty, null);
        Glow.BeginAnimation(OpacityProperty, null);
        ZoneScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        ZoneScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

        if (active)
        {
            _loopsRunning = false;
            Zone.Opacity = 1;
            Back.Opacity = 1;
            Glow.Opacity = GlowOnOpacity;
            ZoneScale.ScaleX = 1;
            ZoneScale.ScaleY = 1;

            if (animate)
            {
                var duration = MotionTokens.RowDuration.TimeSpan;
                Zone.BeginAnimation(OpacityProperty, Once(0, 1, duration, MotionTokens.ContentInEase));
                Back.BeginAnimation(OpacityProperty, Once(0, 1, duration, MotionTokens.ContentInEase));
                Glow.BeginAnimation(OpacityProperty, Once(0, GlowOnOpacity, TimeSpan.FromMilliseconds(MotionTokens.DropGlowFadeMs), MotionTokens.ContentInEase));
                ZoneScale.BeginAnimation(ScaleTransform.ScaleXProperty, Once(0.97, 1, duration, MotionTokens.ContentInEase));
                ZoneScale.BeginAnimation(ScaleTransform.ScaleYProperty, Once(0.97, 1, duration, MotionTokens.ContentInEase));
            }

            StartLoops();
        }
        else
        {
            StopLoops();
            Zone.Opacity = 0;
            Back.Opacity = 0;
            Glow.Opacity = 0;

            if (animate)
            {
                // Isi dan latar memudar cepat supaya tile terlihat. Cahaya biru memudar lebih lambat dan tertinggal.
                var fast = TimeSpan.FromMilliseconds(MotionTokens.ContentOutMs);
                Zone.BeginAnimation(OpacityProperty, Once(1, 0, fast, MotionTokens.ContentOutEase));
                Back.BeginAnimation(OpacityProperty, Once(1, 0, fast, MotionTokens.ContentOutEase));
                Glow.BeginAnimation(OpacityProperty,
                    Once(GlowOnOpacity, 0, TimeSpan.FromMilliseconds(MotionTokens.DropGlowFadeMs), MotionTokens.CloseEase));
            }
        }
    }

    /// <summary>Cahaya biru bernapas sangat pelan. Hanya selama IsActive, terlihat, dan gerak tidak dikurangi.</summary>
    private void StartLoops()
    {
        if (_loopsRunning || !IsVisible || MotionTokens.ReducedMotion)
        {
            return;
        }

        _loopsRunning = true;

        // Mulai setelah animasi masuk selesai supaya tidak bertabrakan: animasi masuk memakai FillBehavior.Stop,
        // nilai lokal GlowOnOpacity tetap berlaku sebagai nilai akhir.
        Glow.BeginAnimation(OpacityProperty, new DoubleAnimation(GlowOnOpacity - 0.1, GlowOnOpacity + 0.1,
            TimeSpan.FromMilliseconds(MotionTokens.DropGlowMs))
        {
            BeginTime = TimeSpan.FromMilliseconds(MotionTokens.DropGlowFadeMs),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        });
    }

    private void StopLoops()
    {
        if (!_loopsRunning)
        {
            return;
        }

        _loopsRunning = false;

        // Lepas animasi dulu; nilai lokal diatur oleh ApplyActive.
        Glow.BeginAnimation(OpacityProperty, null);
    }

    /// <summary>
    /// Drop berhasil: riak air sekali jalan (satu kilau cahaya lembut, tanpa cincin). Elemen riak terpisah dari
    /// Zone, jadi tetap terlihat saat zona memudar. Pada urutan di IslandViewModel, DropPulse naik sebelum IsActive turun.
    /// </summary>
    private void PlayDrop()
    {
        if (MotionTokens.ReducedMotion || !IsVisible)
        {
            return;
        }

        TimeSpan duration = TimeSpan.FromMilliseconds(MotionTokens.DropRippleMs);
        IEasingFunction ease = MotionTokens.CloseEase;

        Ripple.BeginAnimation(OpacityProperty, Once(0.6, 0, duration, ease));
        RippleScale.BeginAnimation(ScaleTransform.ScaleXProperty, Once(0.3, 2.6, duration, ease));
        RippleScale.BeginAnimation(ScaleTransform.ScaleYProperty, Once(0.3, 2.6, duration, ease));
    }

    private static DoubleAnimation Once(double from, double to, TimeSpan duration, IEasingFunction ease) =>
        new(from, to, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };

    private static RadialGradientBrush BuildGlowBrush(Color accent, byte centerAlpha)
    {
        var brush = new RadialGradientBrush
        {
            Center = new Point(0.5, 0.5),
            GradientOrigin = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5
        };

        brush.GradientStops.Add(new GradientStop(Color.FromArgb(centerAlpha, accent.R, accent.G, accent.B), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, accent.R, accent.G, accent.B), 1));
        return brush;
    }
}
