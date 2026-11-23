namespace iLand.Interfaces;

/// <summary>
/// Menampilkan panel kecil dengan cincin baterai besar saat charger dicolok, dan "Fully charged" sekali saat baterai penuh.
/// Setelah panel hilang, island kembali ke pill idle yang sudah punya cincin kecil dengan petir selama charger tersambung.
/// Dipicu oleh perubahan status baterai (event IBatteryService), bukan polling.
/// Mengikuti AppSettings.ChargingAnimationEnabled: mati = pendengar baterai dilepas.
/// </summary>
public interface IChargingOverlayService
{
    /// <summary>Dipanggil sekali saat aplikasi mulai (UI thread). Seterusnya mengikuti perubahan pengaturan.</summary>
    void Initialize();
}
