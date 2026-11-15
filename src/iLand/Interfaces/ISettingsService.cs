using iLand.Models;

namespace iLand.Interfaces;

public interface ISettingsService
{
    /// <summary>Pengaturan yang sedang aktif. Ubah nilainya, lalu panggil NotifyChanged().</summary>
    AppSettings Current { get; }

    /// <summary>Dipanggil setiap ada pengaturan yang berubah, agar tampilan langsung ikut berubah.</summary>
    event EventHandler? SettingsChanged;

    /// <summary>Beritahu bahwa pengaturan berubah. Penyimpanan ke file ditunda sebentar (debounce).</summary>
    void NotifyChanged();

    /// <summary>Simpan langsung ke file.</summary>
    void Save();
}
