using System.Windows;

namespace iLand.Interfaces;

/// <summary>
/// Mengamati lingkungan di sekitar island: apakah ada ISI (tab, tombol, teks) di bawah island
/// dan apakah ada aplikasi fullscreen yang aktif.
/// </summary>
public interface IWindowContextService
{
    /// <summary>
    /// True jika island menutupi sesuatu yang terlihat di belakangnya (misalnya tab browser).
    /// False jika di belakangnya hanya ada area kosong, atau tidak ada window sama sekali.
    /// </summary>
    bool IsContentBelowIsland { get; }

    /// <summary>True jika ada aplikasi fullscreen (game, video, presentasi).</summary>
    bool IsFullscreenAppActive { get; }

    /// <summary>
    /// Smart collapse (deteksi isi di belakang island) menyala atau mati. Mengikuti pengaturan
    /// "Auto-minimize when content is below" dan TIDAK bergantung pada "Hide island from screenshots".
    /// Jika false, tidak ada pengambilan gambar dan tidak ada timer pemantau.
    /// </summary>
    bool IsContentDetectionEnabled { get; set; }

    /// <summary>
    /// True jika island SAAT INI sudah dikecualikan dari tangkapan layar secara permanen
    /// (pengaturan "Hide island from screenshots" aktif dan Windows menerimanya).
    /// Jika true, gambar area di belakang island bisa diambil kapan saja tanpa melihat island.
    /// Jika false, service mengecualikan island hanya sesaat selama pengambilan gambar
    /// (lihat CaptureExclusionSetter). Diatur oleh window island.
    /// </summary>
    bool IsIslandExcludedFromCapture { get; set; }

    /// <summary>
    /// Fungsi dari window island untuk mengecualikan (true) atau tidak (false) island dari tangkapan layar.
    /// Mengembalikan false jika Windows menolak. Dipakai service untuk pengecualian sesaat
    /// saat pengaturan "Hide island from screenshots" mati. Harus dipanggil dari UI thread.
    /// </summary>
    Func<bool, bool>? CaptureExclusionSetter { get; set; }

    /// <summary>Dipanggil saat salah satu nilai di atas berubah.</summary>
    event EventHandler? Changed;

    /// <summary>
    /// Beri tahu area pill island NORMAL (dalam PIKSEL layar), yaitu tempat island biasanya berada.
    /// Dipanggil oleh window island setiap kali posisinya berubah.
    /// </summary>
    void SetIslandZone(Rect pillAreaInPixels);
}
