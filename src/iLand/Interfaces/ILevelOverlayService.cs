namespace iLand.Interfaces;

/// <summary>
/// Menampilkan bar volume dan kecerahan di island saat nilainya berubah (oleh tombol keyboard, slider Windows, dan sebagainya).
/// Dipicu oleh perubahan nilai sebenarnya (event Core Audio dan WMI), bukan dengan menyadap tombol.
/// Mengikuti AppSettings.ShowVolumeBrightnessOverlay: mati = semua pendengar dilepas sepenuhnya.
/// </summary>
public interface ILevelOverlayService
{
    /// <summary>Dipanggil sekali saat aplikasi mulai (UI thread). Seterusnya mengikuti perubahan pengaturan.</summary>
    void Initialize();
}
