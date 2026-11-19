using iLand.Models;

namespace iLand.Helpers;

/// <summary>Ukuran island saat expanded untuk satu tab (dalam piksel).</summary>
public readonly record struct TabSize(double Width, double Height);

/// <summary>
/// Informasi tambahan yang bisa mengubah ukuran sebuah tab.
/// ItemCount: jumlah item pada tab yang tingginya dinamis (Todo = jumlah tugas, Mixer = jumlah aplikasi).
/// WideLayout: true jika tab meminta lebar penuh (mode lirik di tab Music).
/// </summary>
public readonly record struct TabLayoutRequest(int ItemCount = 0, bool WideLayout = false);

/// <summary>
/// SATU-SATUNYA tempat yang menentukan lebar dan tinggi island expanded untuk tiap tab.
/// IslandWindow cukup memanggil GetSize(), jadi untuk mengubah ukuran sebuah tab, ubah angkanya di sini saja.
///
/// Semua tinggi di bawah adalah tinggi TOTAL island, sudah termasuk header.
/// Sisa ruang untuk isi tab = tinggi - HeaderAndPadding (lihat konstanta di bawah).
/// Tinggi maksimum 290 masih muat di window island 840 x 390 pada skala 115% (lihat IslandProfile dan AppSettings).
/// </summary>
public static class TabLayout
{
    // ---- Tinggi per tab (titik awal, akan disetel setelah diuji langsung) ----
    public const double HomeHeight = 212;
    public const double MusicHeight = 232;

    /// <summary>Tinggi tab Music saat panel lirik terbuka (lebar 640): lebih tinggi agar lirik dan kontrol tidak sesak.</summary>
    public const double MusicLyricsHeight = 280;
    public const double TimerHeight = 220;
    public const double ShelfHeight = 280;

    /// <summary>
    /// Tinggi tab Capture. 212 (isi 138 px) memuat baris chip pintasan (27 px) + empat kartu setinggi 108 px.
    /// Pada 196 (isi 122 px) kartu 108 px tidak muat bersama chip.
    /// </summary>
    public const double CaptureHeight = 212;

    /// <summary>Tab Control: isi 72 + 8 + 56 = 136, ditambah header. Disesuaikan dengan ControlView.xaml.</summary>
    public const double ControlHeight = 136 + HeaderAndPadding;

    // Tab dengan tinggi dinamis: tingginya antara nilai minimum dan maksimum.
    public const double TodoMinHeight = 212;
    public const double TodoMaxHeight = 290;
    public const double MixerMinHeight = 260;
    public const double MixerMaxHeight = 290;

    // ---- Ukuran item tab bar (dipakai style tab bar, indikator, dan penghitung posisi indikator) ----
    public const double TabBarItemWidth = 34;
    public const double TabBarItemHeight = 28;

    // ---- Lebar ----
    // Lebar dasar island expanded (pada skala 100%). Ukuran island kini diatur lewat skala per bentuk di Settings
    // (halaman Island), bukan lewat slider lebar. Mode lirik di tab Music memakai WideWidth.
    public const double BaseWidth = 540;

    // WideWidth harus tetap muat di window island (lihat IslandWindow.xaml, 840 x 390, termasuk skala 115%).
    public const double WideWidth = 640;

    // ---- Angka bantu untuk tinggi dinamis ----

    // Ruang yang dipakai di luar isi tab: margin atas header 10 + header 36 + jarak ke isi 10 + margin bawah isi 18.
    // Angka ini mengikuti Margin header dan TabHost di IslandWindow.xaml.
    private const double HeaderAndPadding = 74;

    // Todo: bagian tetap di dalam tab (header 22 + jarak 4 + jarak 8 + kotak input 38 = 72) dan tinggi satu baris tugas (34 + 2 jarak).
    private const double TodoFixedHeight = HeaderAndPadding + 72;
    private const double TodoRowHeight = 36;

    // Mixer: baris master (46) + jarak (6), lalu tiap aplikasi 44 + jarak 6. Disesuaikan dengan MixerView.xaml.
    private const double MixerMasterHeight = 46 + 6;
    private const double MixerRowHeight = 44 + 6;

    /// <summary>Menghasilkan lebar dan tinggi island expanded untuk tab yang dipilih.</summary>
    /// <param name="tab">Tab yang sedang aktif.</param>
    /// <param name="baseWidth">Lebar dasar (TabLayout.BaseWidth).</param>
    /// <param name="request">Jumlah item dan permintaan layout lebar (boleh dikosongkan).</param>
    public static TabSize GetSize(IslandTab tab, double baseWidth, TabLayoutRequest request = default)
    {
        // Hanya tab Music yang boleh melebar (mode lirik). Tab lain memakai lebar dari Settings.
        double width = tab == IslandTab.Music && request.WideLayout
            ? Math.Max(baseWidth, WideWidth)
            : baseWidth;

        double height = tab switch
        {
            IslandTab.Home => HomeHeight,
            IslandTab.Music => request.WideLayout ? MusicLyricsHeight : MusicHeight,
            IslandTab.Mixer => GetMixerHeight(request.ItemCount),
            IslandTab.Timer => TimerHeight,
            IslandTab.Todo => GetTodoHeight(request.ItemCount),
            IslandTab.Shelf => ShelfHeight,
            IslandTab.Capture => CaptureHeight,
            IslandTab.Control => ControlHeight,
            _ => HomeHeight
        };

        return new TabSize(width, height);
    }

    // Todo: makin banyak tugas makin tinggi, dibatasi antara minimum dan maksimum (sisanya bisa digulir).
    private static double GetTodoHeight(int taskCount)
    {
        double wanted = TodoFixedHeight + Math.Max(taskCount, 0) * TodoRowHeight;
        return Math.Clamp(wanted, TodoMinHeight, TodoMaxHeight);
    }

    // Mixer: tinggi mengikuti jumlah aplikasi, dibatasi antara minimum dan maksimum.
    // Dengan maksimum 290, hanya master + 3 aplikasi yang muat; aplikasi ke-4 dan seterusnya digulir.
    private static double GetMixerHeight(int appCount)
    {
        double wanted = HeaderAndPadding + MixerMasterHeight + Math.Max(appCount, 0) * MixerRowHeight;
        return Math.Clamp(wanted, MixerMinHeight, MixerMaxHeight);
    }
}
