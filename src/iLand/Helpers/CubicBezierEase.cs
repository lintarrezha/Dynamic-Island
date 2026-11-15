using System.Windows;
using System.Windows.Media.Animation;

namespace iLand.Helpers;

/// <summary>
/// Easing berbentuk kurva Bezier kubik, sama persis dengan fungsi cubic-bezier(x1, y1, x2, y2) di CSS.
/// Dipakai supaya gerak di aplikasi sama dengan gerak di mockup.
///
/// Contoh:
/// new CubicBezierEase(0.34, 1.45, 0.64, 1)  : melaju lalu melewati tujuan sekali (sekitar 8 persen) dan mengendap,
///                                              tanpa getaran berulang. Dipakai island saat membesar dan titik navigasi.
/// new CubicBezierEase(0.25, 0.1, 0.25, 1)   : "ease" bawaan CSS, halus tanpa pantulan. Dipakai untuk memudar.
///
/// Nilai x1 dan x2 harus antara 0 dan 1. Nilai y1 dan y2 boleh di luar 0 sampai 1 (itulah yang membuat pantulan).
/// </summary>
public class CubicBezierEase : EasingFunctionBase
{
    public static readonly DependencyProperty X1Property = DependencyProperty.Register(
        nameof(X1), typeof(double), typeof(CubicBezierEase), new PropertyMetadata(0.25));

    public static readonly DependencyProperty Y1Property = DependencyProperty.Register(
        nameof(Y1), typeof(double), typeof(CubicBezierEase), new PropertyMetadata(0.1));

    public static readonly DependencyProperty X2Property = DependencyProperty.Register(
        nameof(X2), typeof(double), typeof(CubicBezierEase), new PropertyMetadata(0.25));

    public static readonly DependencyProperty Y2Property = DependencyProperty.Register(
        nameof(Y2), typeof(double), typeof(CubicBezierEase), new PropertyMetadata(1.0));

    public double X1
    {
        get => (double)GetValue(X1Property);
        set => SetValue(X1Property, value);
    }

    public double Y1
    {
        get => (double)GetValue(Y1Property);
        set => SetValue(Y1Property, value);
    }

    public double X2
    {
        get => (double)GetValue(X2Property);
        set => SetValue(X2Property, value);
    }

    public double Y2
    {
        get => (double)GetValue(Y2Property);
        set => SetValue(Y2Property, value);
    }

    /// <summary>Dipakai oleh XAML, nilainya diisi lewat atribut X1, Y1, X2, Y2.</summary>
    public CubicBezierEase()
    {
        // Kurva ini sudah lengkap sebagai satu gerakan dari 0 ke 1. EaseIn berarti WPF memakai
        // hasil hitungan kita apa adanya (tidak dibalik atau dicerminkan).
        EasingMode = EasingMode.EaseIn;
    }

    /// <summary>Dipakai dari kode C#: new CubicBezierEase(0.34, 1.45, 0.64, 1)</summary>
    public CubicBezierEase(double x1, double y1, double x2, double y2) : this()
    {
        X1 = x1;
        Y1 = y1;
        X2 = x2;
        Y2 = y2;
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

        double x1 = Math.Clamp(X1, 0, 1);
        double x2 = Math.Clamp(X2, 0, 1);

        // Kurva Bezier dinyatakan dengan parameter u (0 sampai 1), sedangkan animasi memberi kita waktu.
        // Jadi kita cari dulu u yang menghasilkan waktu itu (bisection: bagi dua berulang kali),
        // lalu hitung nilai gerakannya dari u tersebut.
        double low = 0;
        double high = 1;
        double u = normalizedTime;

        for (int i = 0; i < 24; i++)
        {
            u = (low + high) / 2;
            if (Bezier(u, x1, x2) < normalizedTime)
            {
                low = u;
            }
            else
            {
                high = u;
            }
        }

        return Bezier(u, Y1, Y2);
    }

    /// <summary>Titik pada kurva Bezier kubik dengan ujung 0 dan 1 serta dua titik kendali p1 dan p2.</summary>
    private static double Bezier(double u, double p1, double p2)
    {
        double inverse = 1 - u;
        return 3 * inverse * inverse * u * p1 + 3 * inverse * u * u * p2 + u * u * u;
    }

    protected override Freezable CreateInstanceCore()
    {
        return new CubicBezierEase();
    }
}
