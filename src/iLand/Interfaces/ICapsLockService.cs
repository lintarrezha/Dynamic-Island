namespace iLand.Interfaces;

/// <summary>
/// Menampilkan chip singkat "Caps Lock On" atau "Caps Lock Off" saat status Caps Lock berubah.
///
/// PRIVASI: iLand hanya membaca SATU bit, yaitu apakah Caps Lock sedang menyala. Tidak ada hook keyboard, tidak ada
/// raw input, tidak ada pembacaan tombol lain, dan tidak ada ketikan yang direkam, disimpan, atau dicatat di log.
/// Caranya: GetKeyState(VK_CAPITAL) dibaca tiap sekitar 100 ms, hanya selama fitur menyala.
///
/// Mati secara bawaan (opt-in). Mengikuti AppSettings.CapsLockOverlay: mati = timer dihentikan sepenuhnya.
/// </summary>
public interface ICapsLockService
{
    /// <summary>Dipanggil sekali saat aplikasi mulai (UI thread). Seterusnya mengikuti perubahan pengaturan.</summary>
    void Initialize();
}
