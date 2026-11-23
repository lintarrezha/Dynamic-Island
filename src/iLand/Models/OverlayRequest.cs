using System.Windows.Media;

namespace iLand.Models;

/// <summary>Tampilan isi overlay sementara di island.</summary>
public enum OverlayKind
{
    /// <summary>Ikon bulat (atau gambar kecil), judul, dan pesan. Dipakai notifikasi lama (timer selesai, screenshot tersimpan, dan sebagainya).</summary>
    Text,

    /// <summary>Ikon, bar tipis, dan angka. Dipakai volume dan kecerahan. Diperbarui di tempat selama nilainya berubah.</summary>
    Level,

    /// <summary>Ikon dan satu baris teks. Dipakai perangkat tersambung dan Caps Lock.</summary>
    Chip,

    /// <summary>Panel kecil dengan cincin baterai besar, persen, dan keterangan. Dipakai saat charger dicolok.</summary>
    Charging
}

/// <summary>
/// Tingkat kepentingan overlay. Yang lebih tinggi menggantikan yang lebih rendah.
/// Urutan sesuai Prompt 7: rekaman dan timer selesai (Alert) lebih penting dari charging, lalu perangkat tersambung,
/// lalu volume dan kecerahan, lalu Caps Lock. Perekaman sendiri punya mode tersendiri di island, bukan overlay.
/// </summary>
public enum OverlayPriority
{
    CapsLock = 10,
    Level = 20,
    Device = 30,
    Charging = 40,
    Alert = 50
}

/// <summary>
/// Satu permintaan overlay. Key menentukan "overlay yang sama": permintaan baru dengan Key yang sama memperbarui
/// overlay yang sedang tampil di tempat (misalnya bar volume yang terus bergerak), bukan membuat pill baru.
/// </summary>
public sealed record OverlayRequest(string Key, OverlayKind Kind, OverlayPriority Priority, int DurationMs)
{
    /// <summary>Judul (Text) atau satu-satunya teks (Chip).</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Pesan di bawah judul (hanya Text).</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Karakter simbol di ikon bulat (hanya Text, dipakai notifikasi lama).</summary>
    public string Glyph { get; init; } = string.Empty;

    /// <summary>Gambar kecil beku di sisi kiri (hanya Text). Null = pakai ikon bulat.</summary>
    public ImageSource? Thumbnail { get; init; }

    /// <summary>Nama resource ikon garis di Navigation.xaml, misalnya "IconVolumeOn" (Level dan Chip).</summary>
    public string IconKey { get; init; } = string.Empty;

    /// <summary>Isi bar 0 sampai 1 (hanya Level).</summary>
    public double Level { get; init; }

    /// <summary>Angka di kanan bar, misalnya "42" (hanya Level).</summary>
    public string ValueText { get; init; } = string.Empty;

    /// <summary>
    /// True: boleh menunggu giliran bila overlay yang lebih penting sedang tampil (perangkat, charging, alert).
    /// False: dibuang bila terhalang (volume, kecerahan, Caps Lock), karena nilainya sudah usang saat gilirannya tiba.
    /// </summary>
    public bool IsQueueable => Priority >= OverlayPriority.Device;

    /// <summary>True: tetap ditampilkan selama mode rapat (misalnya peringatan baterai). Lainnya ditahan.</summary>
    public bool IsCritical { get; init; }
}
