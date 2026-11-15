using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace iLand.Helpers;

/// <summary>
/// Umpan balik saat tombol ditekan: tombol mengecil sedikit (skala 0,94) lalu kembali dengan pantulan kecil saat dilepas.
///
/// Cara pakai: tambahkan satu setter di style tombol:
///     &lt;Setter Property="helpers:PressFeedback.IsEnabled" Value="True" /&gt;
///
/// Aturan yang dijaga:
/// - Hanya memakai RenderTransform (ScaleTransform) dengan titik tengah 0,5 / 0,5, jadi layout tidak berubah dan
///   tombol mengecil ke arah tengahnya sendiri (tidak terpotong di tepi).
/// - Tidak menyentuh Background, Foreground, atau Opacity, jadi efek hover milik style tetap bekerja.
/// - Durasi dan kurva dari MotionTokens. Durasi tombol sangat pendek dan tidak ikut pengali Settings.
/// </summary>
public static class PressFeedback
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(PressFeedback), new PropertyMetadata(false, OnIsEnabledChanged));

    // Penanda internal: tombol ini sedang dalam keadaan ditekan oleh PressFeedback.
    private static readonly DependencyProperty IsDownProperty = DependencyProperty.RegisterAttached(
        "IsDown", typeof(bool), typeof(PressFeedback), new PropertyMetadata(false));

    /// <summary>
    /// Skala saat ditekan untuk tombol tertentu. Nol (bawaan) = pakai MotionTokens.PressScale.
    /// Kartu besar memakai MotionTokens.PressScaleLarge lewat setter di style-nya.
    /// </summary>
    public static readonly DependencyProperty ScaleProperty = DependencyProperty.RegisterAttached(
        "Scale", typeof(double), typeof(PressFeedback), new PropertyMetadata(0.0));

    public static double GetScale(DependencyObject element) => (double)element.GetValue(ScaleProperty);

    public static void SetScale(DependencyObject element, double value) => element.SetValue(ScaleProperty, value);

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ButtonBase button)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            button.RenderTransformOrigin = new Point(0.5, 0.5); // skala berpusat di tengah tombol
            button.RenderTransform = new ScaleTransform(1, 1);

            // Event "Preview" dipakai karena ButtonBase menandai klik mouse sebagai sudah ditangani,
            // sehingga event biasa tidak sampai ke sini.
            button.PreviewMouseLeftButtonDown += OnPress;
            button.PreviewMouseLeftButtonUp += OnRelease;
            button.LostMouseCapture += OnRelease;
            button.MouseLeave += OnRelease;
        }
        else
        {
            button.PreviewMouseLeftButtonDown -= OnPress;
            button.PreviewMouseLeftButtonUp -= OnRelease;
            button.LostMouseCapture -= OnRelease;
            button.MouseLeave -= OnRelease;
            button.ClearValue(UIElement.RenderTransformProperty);
            button.ClearValue(UIElement.RenderTransformOriginProperty);
        }
    }

    private static void OnPress(object sender, MouseButtonEventArgs e)
    {
        var button = (ButtonBase)sender;

        // Mode gerak berkurang: tidak ada skala sama sekali.
        if (MotionTokens.ReducedMotion)
        {
            return;
        }

        button.SetValue(IsDownProperty, true);

        double pressScale = GetScale(button) > 0 ? GetScale(button) : MotionTokens.PressScale;

        AnimateScale(button, pressScale,
            TimeSpan.FromMilliseconds(MotionTokens.PressDownMs), MotionTokens.PressDownEase, releasing: false);
    }

    // Dipakai bersama oleh lepas klik, hilangnya capture mouse, dan pointer keluar dari tombol.
    // Hanya animasi kembali yang dijalankan satu kali per tekanan.
    private static void OnRelease(object sender, MouseEventArgs e)
    {
        var button = (ButtonBase)sender;
        if (!(bool)button.GetValue(IsDownProperty))
        {
            return;
        }

        button.SetValue(IsDownProperty, false);

        AnimateScale(button, 1,
            TimeSpan.FromMilliseconds(MotionTokens.PressUpMs), MotionTokens.PressUpEase, releasing: true);
    }

    private static void AnimateScale(ButtonBase button, double target, TimeSpan duration, IEasingFunction ease, bool releasing)
    {
        if (button.RenderTransform is not ScaleTransform scale)
        {
            return;
        }

        // Tanpa nilai awal: gerak dimulai dari skala saat ini, jadi tetap mulus jika dilepas di tengah gerak turun.
        var scaleX = new DoubleAnimation(target, duration) { EasingFunction = ease };
        var scaleY = new DoubleAnimation(target, duration) { EasingFunction = ease };

        if (releasing)
        {
            scaleX.Completed += (_, _) =>
            {
                // Jika tombol ditekan lagi selagi kembali, jangan dilepas. Selain itu, lepas animasi supaya
                // tidak ada animasi yang menempel, lalu atur nilai akhirnya.
                if ((bool)button.GetValue(IsDownProperty))
                {
                    return;
                }

                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                scale.ScaleX = 1;
                scale.ScaleY = 1;
            };
        }

        scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
    }
}
