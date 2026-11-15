using System.Windows;
using System.Windows.Media.Animation;

namespace iLand.Helpers;

/// <summary>
/// Easing berbentuk pegas teredam: bergerak cepat ke tujuan, melewatinya sedikit, lalu mengendap.
/// Dipakai di semua animasi iLand supaya seluruh aplikasi terasa punya satu karakter gerak.
///
/// Cara memakai di kode:  new DoubleAnimation(0, 1, duration) { EasingFunction = new SpringEase() }
///
/// Dua nilai yang bisa diatur:
/// DampingRatio     : makin kecil makin memantul. 0,62 = pantulan terasa (sekitar 8 persen),
///                    0,72 = pantulan lembut (sekitar 4 persen). Dibatasi 0,05 sampai 0,99.
/// AngularFrequency : makin besar makin cepat bergetar dan mengendap dalam durasi yang sama.
/// </summary>
public class SpringEase : EasingFunctionBase
{
    public static readonly DependencyProperty DampingRatioProperty = DependencyProperty.Register(
        nameof(DampingRatio), typeof(double), typeof(SpringEase), new PropertyMetadata(0.62));

    public static readonly DependencyProperty AngularFrequencyProperty = DependencyProperty.Register(
        nameof(AngularFrequency), typeof(double), typeof(SpringEase), new PropertyMetadata(14.0));

    public double DampingRatio
    {
        get => (double)GetValue(DampingRatioProperty);
        set => SetValue(DampingRatioProperty, value);
    }

    public double AngularFrequency
    {
        get => (double)GetValue(AngularFrequencyProperty);
        set => SetValue(AngularFrequencyProperty, value);
    }

    public SpringEase()
    {
        // Kurva pegas ini sudah lengkap sebagai satu gerakan "masuk ke tujuan".
        // EaseIn berarti WPF memakai hasil hitungan kita apa adanya (tidak dibalik atau dicerminkan).
        EasingMode = EasingMode.EaseIn;
    }

    protected override double EaseInCore(double normalizedTime)
    {
        if (normalizedTime <= 0)
        {
            return 0;
        }

        if (normalizedTime >= 1)
        {
            return 1;
        }

        double damping = Math.Clamp(DampingRatio, 0.05, 0.99);
        double omega = Math.Max(AngularFrequency, 1);

        // Rumus pegas teredam (underdamped) dengan posisi awal 0 dan tujuan 1.
        double dampedOmega = omega * Math.Sqrt(1 - damping * damping);
        double decay = Math.Exp(-damping * omega * normalizedTime);

        return 1 - decay * (Math.Cos(dampedOmega * normalizedTime)
            + damping * omega / dampedOmega * Math.Sin(dampedOmega * normalizedTime));
    }

    protected override Freezable CreateInstanceCore()
    {
        return new SpringEase();
    }
}
