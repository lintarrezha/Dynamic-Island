using System.Text.Json.Serialization;
using iLand.Helpers;

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
    // Independen dari smart collapse (AutoMinimizeWhenWindowBelow): keduanya bebas dinyalakan atau dimatikan.
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

    /// <summary>
    /// Animasi besar menyala (bawaan). Jika dimatikan, island memakai mode gerak berkurang yang sudah ada:
    /// ukuran berubah langsung dan isi hanya crossfade singkat. Digabung dengan pengaturan animasi Windows
    /// (salah satu mati = gerak berkurang). Field baru: file lama tanpa field ini terbaca sebagai true.
    /// </summary>
    public bool AnimationsEnabled { get; set; } = true;
    // TIDAK DIPAKAI LAGI: lebar island kini konstanta TabLayout.BaseWidth, dan ukuran diatur lewat skala (DynamicScale / NotchScale).
    // Properti ini dibiarkan agar settings.json lama tetap terbaca. Nilainya diabaikan.
    public double ExpandedWidth { get; set; } = 540;
    // TIDAK DIPAKAI LAGI: tinggi island expanded sekarang mengikuti tab (lihat Helpers/TabLayout.cs).
    // Properti ini dibiarkan agar settings.json lama tetap terbaca. Nilainya diabaikan.
    public double ExpandedHeight { get; set; } = 240;
    // Radius sudut island expanded. Tidak ada kontrol di Settings lagi (bentuk Dynamic dan Notch punya sudut rancangan sendiri),
    // tetapi nilai lama dari settings.json tetap dihormati oleh IslandProfile. Jangan dihapus tanpa migrasi.
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

    // ---- Bentuk dan skala island (Settings, halaman Island) ----

    /// <summary>Batas skala island: 80% sampai 115%.</summary>
    public const double MinIslandScale = 0.8;
    public const double MaxIslandScale = 1.15;

    /// <summary>Bentuk island: Dynamic (pill melayang) atau Notch (menempel di tepi atas). Disimpan sebagai teks di settings.json.</summary>
    [JsonConverter(typeof(LenientEnumConverter<IslandShape>))]
    public IslandShape IslandShape { get; set; } = IslandShape.Dynamic;

    /// <summary>Skala island bentuk Dynamic (0,8 sampai 1,15). Tiap bentuk punya skala sendiri.</summary>
    public double DynamicScale { get; set; } = 1.0;

    /// <summary>Skala island bentuk Notch (0,8 sampai 1,15).</summary>
    public double NotchScale { get; set; } = 1.0;

    /// <summary>
    /// "When idle: Hide" untuk bentuk Dynamic. Saat true dan island sedang diam (tanpa musik, timer, notifikasi, atau rekaman),
    /// island tidak terlihat dan hanya muncul saat pointer menyentuh garis tipis di tepi atas tengah layar. Bawaan: selalu terlihat.
    /// </summary>
    public bool DynamicHideWhenIdle { get; set; }

    /// <summary>"When idle: Hide" untuk bentuk Notch (tiap bentuk menyimpan pilihannya sendiri, sama seperti skala).</summary>
    public bool NotchHideWhenIdle { get; set; }

    // ---- Tab di island (Settings, halaman Island: "Tabs on island") ----

    /// <summary>
    /// Nama tab opsional yang dimatikan (nilai enum IslandTab sebagai teks, misalnya "Mixer").
    /// Home tidak bisa dimatikan. Disimpan sebagai daftar yang DIMATIKAN (bukan yang menyala) supaya
    /// settings.json lama tanpa field ini terbaca "semua tab menyala" dan tab baru di versi mendatang menyala otomatis.
    /// </summary>
    public List<string> DisabledTabs { get; set; } = new();

    /// <summary>
    /// Menampilkan bar singkat saat volume atau kecerahan berubah. Hanya menyimpan pilihan:
    /// perilakunya dibuat di Prompt 7. Bawaan aktif.
    /// </summary>
    public bool ShowVolumeBrightnessOverlay { get; set; } = true;

    /// <summary>
    /// Panel kecil dengan cincin baterai saat charger dicolok, dan "Fully charged" sekali saat baterai penuh.
    /// Mati = tidak ada panel dan notifikasi "Battery full" lama yang berlaku. Bawaan aktif.
    /// </summary>
    public bool ChargingAnimationEnabled { get; set; } = true;

    /// <summary>
    /// Chip singkat dengan nama perangkat saat earbuds, headset, atau speaker Bluetooth yang sudah dipasangkan baru tersambung.
    /// Mati = pengamat Bluetooth dihentikan sepenuhnya. Bawaan aktif.
    /// </summary>
    public bool BluetoothConnectedOverlay { get; set; } = true;

    /// <summary>
    /// Chip "Caps Lock On/Off" saat status Caps Lock berubah. MATI secara bawaan (opt-in). iLand hanya membaca apakah Caps Lock
    /// menyala (GetKeyState satu tombol), tanpa hook keyboard dan tanpa merekam ketikan. Mati = timer pemeriksa dihentikan.
    /// </summary>
    public bool CapsLockOverlay { get; set; } = false;

    /// <summary>
    /// Scroll di atas pill idle mengubah volume sistem. Hanya bekerja bila HoverToExpand mati. MATI secara bawaan.
    /// </summary>
    public bool ScrollVolumeEnabled { get; set; } = false;

    /// <summary>
    /// Mode rapat otomatis: terdeteksi saat aplikasi lain membuka mikrofon beberapa detik. Menahan peringatan iLand, menjeda musik
    /// (bila MeetingPauseMedia), dan menampilkan tanda atau tombol mic. Hanya status pemakaian dan nama proses yang dibaca, tidak pernah audio.
    /// MATI secara bawaan (opt-in). Mati = pemantau mikrofon dihentikan.
    /// </summary>
    public bool MeetingModeEnabled { get; set; } = false;

    /// <summary>Gaya tampilan tab Music: Classic (bawaan) atau Glass. Dipilih di Settings, Music and Lyrics.</summary>
    [JsonConverter(typeof(LenientEnumConverter<MusicTabStyle>))]
    public MusicTabStyle MusicStyle { get; set; } = MusicTabStyle.Classic;

    /// <summary>Jeda musik saat rapat mulai, dan lanjutkan setelah rapat hanya bila iLand yang menjedanya. Bawaan aktif.</summary>
    public bool MeetingPauseMedia { get; set; } = true;

    /// <summary>Tombol kecil di island untuk membisukan mikrofon default sistem selama rapat. Bawaan aktif. Mati = hanya tanda.</summary>
    public bool MeetingShowMicButton { get; set; } = true;

    /// <summary>Nama proses (tanpa .exe) aplikasi yang membuka mikrofon tetapi bukan rapat. Dinormalkan dan dibatasi di Validate().</summary>
    public List<string> MeetingIgnoredApps { get; set; } = new();

    /// <summary>Merapikan nama proses: dipangkas dan tanpa akhiran .exe.</summary>
    public static string NormalizeProcessName(string? name)
    {
        string value = (name ?? string.Empty).Trim();
        if (value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^4].TrimEnd();
        }

        return value;
    }

    /// <summary>
    /// Ambang otomatis Battery saver milik pengguna (persen), disimpan HANYA saat iLand menaikkannya ke 100 untuk
    /// menyalakan Battery saver dari tab Control. Null = iLand tidak mengubah apa pun. Dipakai untuk mengembalikan nilai aslinya
    /// (juga setelah iLand tertutup mendadak).
    /// </summary>
    public int? BatterySaverOriginalThreshold { get; set; }

    /// <summary>True jika tab ini tampil di island. Home selalu tampil.</summary>
    public bool IsTabEnabled(IslandTab tab)
    {
        return tab == IslandTab.Home || !DisabledTabs.Contains(tab.ToString());
    }

    /// <summary>Menyalakan atau mematikan satu tab opsional. Home diabaikan.</summary>
    public void SetTabEnabled(IslandTab tab, bool enabled)
    {
        if (tab == IslandTab.Home)
        {
            return;
        }

        string name = tab.ToString();
        if (enabled)
        {
            DisabledTabs.Remove(name);
        }
        else if (!DisabledTabs.Contains(name))
        {
            DisabledTabs.Add(name);
        }
    }

    /// <summary>Skala untuk bentuk yang sedang dipilih. Hanya untuk dibaca (tidak disimpan ke settings.json).</summary>
    [JsonIgnore]
    public double ActiveScale => IslandShape == IslandShape.Notch ? NotchScale : DynamicScale;

    /// <summary>Pilihan "When idle: Hide" untuk bentuk yang sedang dipilih. Hanya untuk dibaca (tidak disimpan ke settings.json).</summary>
    [JsonIgnore]
    public bool ActiveHideWhenIdle => IslandShape == IslandShape.Notch ? NotchHideWhenIdle : DynamicHideWhenIdle;

    /// <summary>
    /// Membatasi nilai agar tetap aman, misalnya jika file settings.json diedit manual.
    /// Ukuran window island (840 x 390) cukup untuk ukuran terbesar pada skala 115%. Tinggi tab tidak diatur dari sini (lihat TabLayout).
    /// </summary>
    public void Validate()
    {
        MigrateOldSettings();

        if (!Enum.IsDefined(IslandShape))
        {
            IslandShape = IslandShape.Dynamic;
        }

        // Daftar tab yang dimatikan: buang nama tak dikenal dan Home, hilangkan duplikat, rapikan huruf besar-kecil.
        DisabledTabs = (DisabledTabs ?? new List<string>())
            .Select(name => Enum.TryParse(name, ignoreCase: true, out IslandTab tab) && Enum.IsDefined(tab) ? tab : (IslandTab?)null)
            .Where(tab => tab.HasValue && tab.Value != IslandTab.Home)
            .Select(tab => tab!.Value.ToString())
            .Distinct()
            .ToList();

        if (BatterySaverOriginalThreshold is < 0 or > 100)
        {
            BatterySaverOriginalThreshold = null;
        }

        DynamicScale = SanitizeScale(DynamicScale);
        NotchScale = SanitizeScale(NotchScale);

        CollapseDelayMs = Math.Clamp(CollapseDelayMs, 100, 2000);
        AnimationDurationMs = Math.Clamp(AnimationDurationMs, 150, 800);
        ExpandedWidth = Math.Clamp(ExpandedWidth, 480, 640);
        ExpandedHeight = Math.Clamp(ExpandedHeight, 220, 300);
        ExpandedCornerRadius = Math.Clamp(ExpandedCornerRadius, 16, 36);
        Opacity = Math.Clamp(Opacity, 0.6, 1.0);
        MonitorDeviceName ??= string.Empty;
        LyricsOffsetMs = Math.Clamp(LyricsOffsetMs, -2000, 2000);
        MeetingIgnoredApps = (MeetingIgnoredApps ?? new List<string>())
            .Select(NormalizeProcessName)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .ToList();
    }

    private static double SanitizeScale(double value)
    {
        return double.IsNaN(value) ? 1.0 : Math.Clamp(value, MinIslandScale, MaxIslandScale);
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
