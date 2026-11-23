using System.Windows;
using System.Windows.Media.Animation;

namespace iLand.Helpers;

/// <summary>
/// SATU-SATUNYA tempat durasi dan kurva gerak iLand. Untuk menyetel rasa gerak, ubah angkanya di sini saja.
///
/// TABEL GERAK (durasi dasar pada pengali 1.0):
/// ┌──────────────────────────────────┬────────┬───────────────────────────────────────────────┐
/// │ Gerak                            │ Durasi │ Kurva                                         │
/// ├──────────────────────────────────┼────────┼───────────────────────────────────────────────┤
/// │ Membuka island (OpenMs)          │ 550 ms │ CubicBezier(0.34, 1.45, 0.64, 1)  ada pantulan │
/// │ Menutup island (CloseMs)         │ 320 ms │ CubicEase EaseOut                 tanpa pantulan│
/// │ Ukuran saat ganti tab (ResizeMs) │ 420 ms │ CubicBezier(0.22, 1, 0.36, 1)     tanpa pantulan│
/// │ Konten lama keluar (ContentOut)  │ 140 ms │ CubicBezier(0.4, 0, 1, 1)                      │
/// │ Konten baru masuk (ContentIn)    │ 280 ms │ CubicBezier(0.22, 1, 0.36, 1), jeda 70 ms      │
/// │ Indikator tab meluncur           │ 320 ms │ CubicBezier(0.34, 1.3, 0.64, 1)   pantulan kecil│
/// │ Tombol turun (PressDown)         │  90 ms │ CubicEase EaseOut                              │
/// │ Tombol kembali (PressUp)         │ 220 ms │ CubicBezier(0.34, 1.56, 0.64, 1)  pantulan kecil│
/// │ Judul tab di header (TitleMs)    │ 180 ms │ kurva ContentOut / ContentIn                   │
/// │ Ikon tab crossfade (IconCrossfade)│ 200 ms │ linear (dipakai XAML, tidak ikut pengali)     │
/// └──────────────────────────────────┴────────┴───────────────────────────────────────────────┘
///
/// JARAK GESER (piksel, tidak ikut diskalakan): judul tab 6 px, isi tab yang muncul bertahap 8 px (ke atas, vertikal).
/// Panel tab berganti dengan crossfade murni (tanpa geser kiri-kanan).
///
/// MODE GERAK BERKURANG: jika efek animasi Windows dimatikan (SystemParameters.ClientAreaAnimation = false)
/// ATAU toggle "Animations" di Settings dimatikan (AnimationsEnabled = false),
/// semua gerak diganti: ukuran berubah langsung, isi crossfade 120 ms, tanpa geser, tanpa skala tombol.
///
/// PENGALI GLOBAL: AppSettings.AnimationDurationMs (slider "Animation duration" di Settings) dibagi 550.
/// Pada nilai 550 semua durasi di tabel berlaku apa adanya. Pada 275 semuanya dua kali lebih cepat.
/// Hanya durasi yang diskalakan. Jarak geser dan kurva tidak berubah.
///
/// Semua kurva memakai CubicBezierEase atau CubicEase. Tidak ada pegas berosilasi (getaran berulang).
/// </summary>
public static class MotionTokens
{
    /// <summary>Nilai AnimationDurationMs yang dianggap "pengali 1.0" (sama dengan durasi membuka island).</summary>
    public const double ReferenceDurationMs = 550;

    // ---- Durasi dasar (milidetik) ----
    public const double OpenMs = 550;
    public const double CloseMs = 320;
    public const double ResizeMs = 420;
    public const double ContentOutMs = 140;
    public const double ContentInMs = 280;
    public const double ContentInDelayMs = 70;
    public const double IndicatorMs = 320;
    public const double PressDownMs = 90;
    public const double PressUpMs = 220;
    public const double TitleMs = 180;

    /// <summary>Baris aplikasi di tab Mixer masuk atau keluar (tinggi + fade). Tidak ikut pengali Settings.</summary>
    public const double RowMs = 220;

    // ---- Overlay sementara di island (antrean: OverlayService) ----

    /// <summary>Bar volume atau kecerahan: hilang sekian ms setelah perubahan terakhir. Tidak ikut pengali Settings.</summary>
    public const int OverlayLevelMs = 1500;

    /// <summary>Chip (perangkat tersambung, Caps Lock) tampil selama ini.</summary>
    public const int OverlayChipMs = 2500;

    /// <summary>Chip "Caps Lock On/Off" tampil selama ini.</summary>
    public const int OverlayCapsLockMs = 1500;

    /// <summary>Chip "perangkat Bluetooth tersambung" tampil selama ini.</summary>
    public const int OverlayDeviceMs = 3000;

    /// <summary>Panel charging (cincin baterai besar) tampil selama ini, lalu island kembali ke pill idle.</summary>
    public const int OverlayChargingMs = 3000;

    /// <summary>Notifikasi teks (timer selesai, screenshot tersimpan, dst.) tampil selama ini.</summary>
    public const int OverlayAlertMs = 6000;

    /// <summary>Sisa waktu tampil setelah pointer meninggalkan overlay yang tadi ditahan.</summary>
    public const int OverlayAfterHoverMs = 2000;

    /// <summary>Jeda antara satu overlay yang selesai dan overlay berikutnya yang menunggu giliran.</summary>
    public const int OverlayGapMs = 200;

    // ---- Jendela Settings ----

    /// <summary>Toggle di Settings: bulatan bergeser dan warna jalur berubah. Dipakai langsung dari XAML (x:Static).</summary>
    public const double SettingsToggleMs = 160;

    public static Duration SettingsToggleDuration => new(TimeSpan.FromMilliseconds(SettingsToggleMs));

    /// <summary>Pindah halaman di Settings: halaman lama memudar keluar sambil halaman baru memudar masuk (tanpa geser).</summary>
    public const double SettingsPageFadeMs = 150;

    public static Duration SettingsPageFadeDuration => new(TimeSpan.FromMilliseconds(SettingsPageFadeMs));

    // ---- Shelf: zona drop, tile masuk dan keluar ----

    /// <summary>Lapisan kecil (tombol Clear all, tombol hapus tile) memudar saat muncul atau hilang.</summary>
    public const double HoverFadeMs = 120;

    /// <summary>Satu napas cahaya biru di zona drop Shelf (naik lalu turun). Berjalan hanya selama file diseret di atas island.</summary>
    public const double DropGlowMs = 1100;
    /// <summary>Riak air sekali jalan saat file berhasil dijatuhkan ke Shelf.</summary>
    public const double DropRippleMs = 520;

    /// <summary>Cahaya biru zona drop memudar saat masuk dan saat file dilepas atau drag batal.</summary>
    public const double DropGlowFadeMs = 900;
    public const double DropRingDelayMs = 140;

    /// <summary>Jeda antar tile baru saat beberapa file dijatuhkan sekaligus, dan batas jumlah tile yang diberi jeda.</summary>
    public const double TileStaggerMs = 45;
    public const int TileStaggerMaxIndex = 5;

    public static Duration HoverFadeDuration => new(TimeSpan.FromMilliseconds(HoverFadeMs));
    // ---- Tab Timer: cincin progress, denyut, segmented control (tidak ikut pengali Settings) ----
    public const double PulseMs = 420;
    public const double PulseScale = 1.06;
    public const double RingTickMs = 1000;
    public const double RingJumpMs = 300;
    public const double RingJumpThreshold = 0.05;
    public const double SegmentOvershootMaxSegments = 1;
    /// <summary>Pill melebar atau menyempit mengikuti baris lirik. Lebih lambat dan simetris agar terasa halus.</summary>
    public const double PillWidthMs = 380;

    /// <summary>Jeda sebelum baris lirik baru memudar masuk: pill mulai melebar lebih dulu, baru teksnya muncul.</summary>
    public const double PillTextDelayMs = 90;

    // ---- Mode gerak berkurang ----

    /// <summary>True jika pengguna mematikan efek animasi Windows (Settings, Accessibility, Visual effects). Dibaca setiap dipakai.</summary>
    public static bool ReducedMotion => !AnimationsEnabled || !SystemParameters.ClientAreaAnimation;

    /// <summary>
    /// Pilihan "Animations" di Settings (General). Diatur oleh SettingsService setiap pengaturan dimuat atau berubah.
    /// False = gerak berkurang, sama seperti saat efek animasi Windows dimatikan.
    /// </summary>
    public static bool AnimationsEnabled { get; set; } = true;

    /// <summary>Lama crossfade pengganti semua gerak saat mode gerak berkurang.</summary>
    public const double ReducedMotionMs = 120;
    public static TimeSpan ReducedFade => TimeSpan.FromMilliseconds(ReducedMotionMs);

    // ---- Isi tab muncul bertahap ----

    /// <summary>Elemen utama isi tab naik dari jarak ini (piksel) ke posisi 0 sambil muncul.</summary>
    public const double StaggerOffset = 8;

    /// <summary>Jeda antar elemen (milidetik dasar, ikut pengali).</summary>
    public const double StaggerDelayMs = 30;

    /// <summary>
    /// Jumlah elemen maksimum yang dianimasikan per tab. Isi 0 untuk mematikan efek ini.
    /// Bawaan 0: animasi di dalam panel yang sedang memudar membuat panel itu tidak bisa di-cache (lihat TabCrossfadeBitmapCache),
    /// dan efeknya menambah beberapa animasi sekaligus tepat di awal pindah tab. Isi 3 atau 4 untuk menghidupkannya lagi
    /// (dan matikan TabCrossfadeBitmapCache bila ingin membandingkan).
    /// </summary>
    public const int StaggerMaxElements = 0;

    /// <summary>
    /// true: selama crossfade pindah tab, panel yang memudar digambar SEKALI ke tekstur (BitmapCache) lalu hanya transparansi
    /// tekstur yang dianimasikan. Tanpa ini, seluruh isi panel (teks, ikon, lirik, gambar) digambar ulang di setiap frame.
    /// Isi panel tidak berubah ukuran selama animasi karena ukurannya dikunci (FreezeExpandedContentSize), jadi cache stabil.
    /// Cache dilepas lagi saat crossfade selesai.
    /// </summary>
    public const bool TabCrossfadeBitmapCache = true;

    /// <summary>Skala tombol saat ditekan (PressFeedback).</summary>
    public const double PressScale = 0.94;

    /// <summary>Skala saat ditekan untuk kartu besar (tile Capture). Kartu lebar, jadi mengecilnya lebih sedikit (0,96).</summary>
    public const double PressScaleLarge = 0.96;

    /// <summary>Kartu besar naik sedikit saat pointer di atasnya (piksel, negatif = ke atas) dan lama geraknya.</summary>
    public const double TileLiftOffset = -2;
    public const double TileLiftMs = 160;

    /// <summary>Crossfade ikon gelap/terang pada item tab bar. Dipakai langsung oleh XAML, jadi tidak ikut pengali Settings.</summary>
    public const double IconCrossfadeMs = 200;
    public static readonly System.Windows.Duration IconCrossfadeDuration =
        new(TimeSpan.FromMilliseconds(IconCrossfadeMs));

    /// <summary>
    /// Indikator tab hanya memakai kurva dengan pantulan kecil jika berpindah sejauh ini (dalam jumlah tab) atau kurang.
    /// Pantulan kurva IndicatorEase sekitar 3 persen dari jarak. Pada 3 tab (102 px) itu sekitar 3 px, masih di dalam
    /// padding tab bar (4 px). Lebih jauh dari itu, indikator akan menonjol keluar pill, jadi dipakai kurva tanpa pantulan.
    /// </summary>
    public const double IndicatorOvershootMaxTabs = 3;

    // ---- Jarak geser (piksel) ----
    /// <summary>Judul tab di header bergeser vertikal sejauh ini saat berganti.</summary>
    public const double TitleOffset = 6;

    /// <summary>RowMs sebagai Duration, supaya bisa dipakai langsung dari XAML (x:Static).</summary>
    public static Duration RowDuration => new(TimeSpan.FromMilliseconds(RowMs));

    /// <summary>TileLiftMs sebagai Duration, supaya bisa dipakai langsung dari XAML (x:Static).</summary>
    public static Duration TileLiftDuration => new(TimeSpan.FromMilliseconds(TileLiftMs));

    // ---- Kurva (dibekukan supaya aman dipakai berulang oleh banyak animasi) ----
    public static readonly IEasingFunction OpenEase = Frozen(new CubicBezierEase(0.34, 1.45, 0.64, 1));
    public static readonly IEasingFunction CloseEase = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });
    public static readonly IEasingFunction ResizeEase = Frozen(new CubicBezierEase(0.22, 1, 0.36, 1));
    public static readonly IEasingFunction PillWidthEase = Frozen(new CubicBezierEase(0.45, 0, 0.25, 1));
    public static readonly IEasingFunction ContentOutEase = Frozen(new CubicBezierEase(0.4, 0, 1, 1));
    public static readonly IEasingFunction ContentInEase = Frozen(new CubicBezierEase(0.22, 1, 0.36, 1));
    public static readonly IEasingFunction IndicatorEase = Frozen(new CubicBezierEase(0.34, 1.3, 0.64, 1));
    public static readonly IEasingFunction PressDownEase = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });
    public static readonly IEasingFunction PressUpEase = Frozen(new CubicBezierEase(0.34, 1.56, 0.64, 1));

    /// <summary>
    /// Mengubah durasi dasar (ms) menjadi durasi nyata dengan pengali dari Settings.
    /// Contoh: Scale(OpenMs, 550) = 550 ms, Scale(OpenMs, 400) = 400 ms, Scale(CloseMs, 400) = 233 ms.
    /// </summary>
    /// <param name="baseMs">Durasi dasar dari tabel di atas.</param>
    /// <param name="animationDurationMs">Nilai AppSettings.AnimationDurationMs.</param>
    public static TimeSpan Scale(double baseMs, double animationDurationMs)
    {
        double multiplier = animationDurationMs / ReferenceDurationMs;
        return TimeSpan.FromMilliseconds(baseMs * multiplier);
    }

    private static IEasingFunction Frozen(EasingFunctionBase easing)
    {
        easing.Freeze();
        return easing;
    }
}
