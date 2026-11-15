namespace iLand.Models;

/// <summary>
/// Semua pengaturan iLand. Objek ini disimpan sebagai settings.json.
/// Nilai di bawah adalah nilai bawaan (default) saat file belum ada.
/// </summary>
public class AppSettings
{
    // ---- Behavior ----
    public bool HoverToExpand { get; set; } = true;
    public bool AutoCollapse { get; set; } = true;
    public double CollapseDelayMs { get; set; } = 700;
    public bool AutoMinimizeWhenWindowBelow { get; set; } = true;
    public bool HideWhenFullscreen { get; set; } = true;

    // Island tidak ikut terekam di screenshot, rekaman layar, dan screen share.
    // Jika dimatikan, island terlihat di tangkapan layar TETAPI smart collapse ikut mati
    // (deteksi isi di belakang island butuh island tidak terekam).
    public bool HideFromScreenshots { get; set; } = true;
    public bool PlayNotificationSound { get; set; } = true;

    // ---- Perekaman layar ----

    /// <summary>Ikut merekam suara dari speaker (semua aplikasi).</summary>
    public bool RecordSystemAudio { get; set; } = true;

    /// <summary>Ikut merekam mikrofon. Mati secara bawaan demi privasi.</summary>
    public bool RecordMicrophone { get; set; } = false;

    // ---- Clipboard ----

    /// <summary>
    /// Menyimpan riwayat teks yang disalin (hanya di memori, maksimal 20 item) untuk tab Shelf.
    /// Mati secara bawaan demi privasi dan baru aktif jika pengguna menyalakannya.
    /// </summary>
    public bool ClipboardHistoryEnabled { get; set; } = false;

        // ---- Music ----

    /// <summary>
    /// Mencari lirik di lrclib.net (judul, artis, album, dan durasi lagu dikirim ke sana).
    /// Mati secara bawaan demi privasi. File settings.json lama tanpa field ini terbaca false.
    /// </summary>
    public bool LyricsEnabled { get; set; } = false;

    /// <summary>
    /// Geser waktu lirik (milidetik), global untuk semua lagu. Positif = lirik tampil LEBIH CEPAT,
    /// negatif = lebih lambat. Diatur dari stepper di tab Music. File lama tanpa field ini terbaca 0.
    /// </summary>
    public int LyricsOffsetMs { get; set; } = 0;

    /// <summary>Nama perangkat monitor (misalnya \\.\DISPLAY1). Kosong = monitor utama.</summary>
    public string MonitorDeviceName { get; set; } = string.Empty;

    // ---- Appearance ----
    // Durasi membuka island dalam milidetik. Juga menjadi pengali semua gerak lain: nilai / 550 (lihat MotionTokens).
    public double AnimationDurationMs { get; set; } = 550;
    public double ExpandedWidth { get; set; } = 540;
    // TIDAK DIPAKAI LAGI: tinggi island expanded sekarang mengikuti tab (lihat Helpers/TabLayout.cs).
    // Properti ini dibiarkan agar settings.json lama tetap terbaca. Nilainya diabaikan.
    public double ExpandedHeight { get; set; } = 240;
    public double ExpandedCornerRadius { get; set; } = 28;
    public double Opacity { get; set; } = 1.0;

    // ---- Versi file settings ----

    /// <summary>
    /// Nomor versi isi file. File lama tidak punya field ini sehingga terbaca 0.
    /// Versi 2: nilai bawaan AnimationDurationMs berubah dari 400 menjadi 550 (lihat Validate).
    /// </summary>
    public int SettingsVersion { get; set; }

    private const int CurrentSettingsVersion = 2;
    private const double OldDefaultAnimationDurationMs = 400;

    /// <summary>
    /// Membatasi nilai agar tetap aman, misalnya jika file settings.json diedit manual.
    /// Batas lebar mengikuti ukuran window island (720 x 340). Tinggi tidak lagi diatur dari sini (lihat TabLayout).
    /// </summary>
    public void Validate()
    {
        MigrateOldSettings();

        CollapseDelayMs = Math.Clamp(CollapseDelayMs, 100, 2000);
        AnimationDurationMs = Math.Clamp(AnimationDurationMs, 150, 800);
        ExpandedWidth = Math.Clamp(ExpandedWidth, 480, 640);
        ExpandedHeight = Math.Clamp(ExpandedHeight, 220, 300);
        ExpandedCornerRadius = Math.Clamp(ExpandedCornerRadius, 16, 36);
        Opacity = Math.Clamp(Opacity, 0.6, 1.0);
        MonitorDeviceName ??= string.Empty;
        LyricsOffsetMs = Math.Clamp(LyricsOffsetMs, -5000, 5000);
    }

    /// <summary>
    /// Memperbarui nilai dari file settings versi lama. Berjalan sekali per file (lalu SettingsVersion naik).
    /// Hanya nilai yang PERSIS sama dengan bawaan lama (400) yang diganti, karena itu berarti pengguna tidak pernah
    /// mengubahnya. Nilai yang pernah disetel sendiri tetap dihormati.
    /// </summary>
    private void MigrateOldSettings()
    {
        if (SettingsVersion >= CurrentSettingsVersion)
        {
            return;
        }

        if (AnimationDurationMs == OldDefaultAnimationDurationMs)
        {
            AnimationDurationMs = new AppSettings().AnimationDurationMs; // bawaan baru (550)
        }

        SettingsVersion = CurrentSettingsVersion;
    }
}
