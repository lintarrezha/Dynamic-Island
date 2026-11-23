namespace iLand.Interfaces;

/// <summary>
/// Menampilkan chip singkat (ikon Bluetooth dan nama perangkat) saat earbuds, headset, atau speaker Bluetooth BARU tersambung.
/// Hanya membaca daftar perangkat yang sudah dipasangkan (paired) dan status tersambungnya lewat DeviceWatcher.
/// Tidak membaca sisa baterai (tidak ada API resmi yang andal), tidak memindai perangkat sekitar, dan tidak menyimpan
/// daftar perangkat ke disk. Mengikuti AppSettings.BluetoothConnectedOverlay: mati = pengamat dihentikan dan dilepas.
/// </summary>
public interface IBluetoothDeviceService
{
    /// <summary>Dipanggil sekali saat aplikasi mulai (UI thread). Seterusnya mengikuti perubahan pengaturan.</summary>
    void Initialize();
}
