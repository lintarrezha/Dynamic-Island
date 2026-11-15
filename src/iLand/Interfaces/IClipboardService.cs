using iLand.Models;

namespace iLand.Interfaces;

/// <summary>
/// Riwayat teks yang disalin untuk tab Shelf.
///
/// Privasi: bawaannya MATI. Saat dinyalakan, hanya teks yang dicatat, maksimal 20 item, hanya di memori
/// (tidak ditulis ke disk, tidak dikirim ke mana pun). Salinan yang ditandai "pribadi" oleh aplikasi
/// pengelola kata sandi diabaikan. Isi clipboard tidak pernah ditulis ke log.
/// </summary>
public interface IClipboardService
{
    /// <summary>Dipanggil setiap daftar berubah (item baru, dihapus, atau dikosongkan). Selalu di UI thread.</summary>
    event EventHandler? Changed;

    /// <summary>Daftar teks, yang terbaru di urutan pertama. Kosong jika fitur mati.</summary>
    IReadOnlyList<ClipboardEntry> Items { get; }

    /// <summary>Dipanggil sekali saat aplikasi mulai. Mengikuti pengaturan "Clipboard history" setelahnya.</summary>
    void Initialize();

    /// <summary>Menyalin sebuah item kembali ke clipboard. False jika gagal.</summary>
    bool CopyBack(Guid id);

    void Remove(Guid id);

    void Clear();
}
