using iLand.Models;

namespace iLand.Helpers;

/// <summary>Keadaan island yang bentuknya (celah atas dan radius) ditentukan IslandProfile.</summary>
public enum IslandVisualMode
{
    Normal,
    Expanded,
    Notification,
    Recording,
    Mini,

    /// <summary>"When idle: Hide": hanya garis tipis tak terlihat di tepi atas yang menerima pointer. Isi island tidak tampil.</summary>
    Hidden
}

/// <summary>Celah dari tepi layar, radius sudut atas dan bawah, dan radius lengkung luar ("telinga") di sudut atas Notch.</summary>
public readonly record struct IslandShapeMetrics(double TopGap, double TopRadius, double BottomRadius, double EarRadius);

/// <summary>
/// SATU-SATUNYA tempat yang menentukan bentuk island untuk tiap bentuk (Dynamic atau Notch) dan tiap mode.
/// Dipakai IslandWindow (island sungguhan) dan IslandPreview (pratinjau di Settings), jadi keduanya selalu sama.
/// Untuk mengubah tampilan salah satu bentuk, ubah angka di sini saja.
///
/// Dynamic: melayang dengan celah 6 dari tepi atas, sudut membulat penuh (radius = setengah tinggi, atau radius expanded dari Settings).
/// Notch: celah 0 (menempel di tepi atas), sudut atas rata, sudut bawah membulat, dan di kiri dan kanan atas ada lengkung ke luar
/// (EarRadius) yang membuat bentuknya menyatu dengan tepi layar.
/// Mode mini (smart collapse) sama untuk kedua bentuk: notch kecil tanpa telinga.
/// </summary>
public static class IslandProfile
{
    /// <summary>Lebar pill normal pada skala 100% (juga dasar area deteksi smart collapse).</summary>
    public const double PillWidth = 200;

    /// <summary>Tinggi pill normal pada skala 100%, tanpa celah atas.</summary>
    public const double PillHeight = 34;
    /// <summary>Celah island Dynamic dari tepi atas layar.</summary>
    public const double DynamicTopGap = 6;

    /// <summary>
    /// Radius terbesar bentuk Dynamic yang tidak expanded. Pill pendek tetap setengah tingginya (kapsul),
    /// tetapi overlay yang tinggi (panel charging) tidak menjadi hampir bulat.
    /// </summary>
    public const double MaxPillRadius = 30;

    /// <summary>Radius sudut bawah mode mini.</summary>
    public const double MiniBottomRadius = 7;

    /// <summary>Mode Hidden: lebar dan tinggi (pada skala 100%) garis tipis tak terlihat yang menerima pointer di tepi atas layar.</summary>
    public const double HiddenStripWidth = 160;
    public const double HiddenStripHeight = 5;

    public static double TopGapFor(IslandShape shape, IslandVisualMode mode)
    {
        // Mode Hidden: tinggi garis pemicu diwakili "celah atas", sedangkan badan island sendiri bertinggi nol (tidak terlihat).
        if (mode == IslandVisualMode.Hidden)
        {
            return HiddenStripHeight;
        }

        if (mode == IslandVisualMode.Mini || shape == IslandShape.Notch)
        {
            return 0;
        }

        return DynamicTopGap;
    }

    /// <param name="height">Tinggi bentuk yang terlihat (tanpa celah atas). Dipakai bentuk Dynamic untuk radius pill.</param>
    /// <param name="expandedRadius">Radius sudut island expanded dari Settings.</param>
    public static IslandShapeMetrics Get(IslandShape shape, IslandVisualMode mode, double height, double expandedRadius)
    {
        double gap = TopGapFor(shape, mode);

        if (mode == IslandVisualMode.Mini)
        {
            return new IslandShapeMetrics(0, 0, MiniBottomRadius, 0);
        }

        if (mode == IslandVisualMode.Hidden)
        {
            return new IslandShapeMetrics(gap, 0, 0, 0);
        }

        if (shape == IslandShape.Notch)
        {
            return mode switch
            {
                IslandVisualMode.Expanded => new IslandShapeMetrics(gap, 0, expandedRadius, Math.Min(20, expandedRadius * 0.7)),
                IslandVisualMode.Notification => new IslandShapeMetrics(gap, 0, 20, 14),
                IslandVisualMode.Recording => new IslandShapeMetrics(gap, 0, 16, 12),
                _ => new IslandShapeMetrics(gap, 0, 14, 12)
            };
        }

        double radius = mode == IslandVisualMode.Expanded ? expandedRadius : Math.Min(height / 2, MaxPillRadius);
        return new IslandShapeMetrics(gap, radius, radius, 0);
    }
}
