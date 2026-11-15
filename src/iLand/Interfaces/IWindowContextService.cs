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
    /// Deteksi isi hanya bisa berjalan jika window island sudah disembunyikan dari tangkapan layar
    /// (kalau tidak, island akan "melihat dirinya sendiri"). Diatur oleh window island.
    /// Jika false, island tidak akan pernah masuk mode mini.
    /// </summary>
    bool IsContentDetectionAvailable { get; set; }

    /// <summary>Dipanggil saat salah satu nilai di atas berubah.</summary>
    event EventHandler? Changed;

    /// <summary>
    /// Beri tahu area pill island NORMAL (dalam PIKSEL layar), yaitu tempat island biasanya berada.
    /// Dipanggil oleh window island setiap kali posisinya berubah.
    /// </summary>
    void SetIslandZone(Rect pillAreaInPixels);
}
