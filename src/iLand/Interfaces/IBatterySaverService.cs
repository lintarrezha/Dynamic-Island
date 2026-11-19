using iLand.Models;

namespace iLand.Interfaces;

/// <summary>
/// Battery saver (Energy saver di Windows 11 24H2). Status DIBACA lewat Windows.System.Power.PowerManager (resmi).
/// Menyalakan dan mematikan tidak punya API resmi. Satu-satunya cara tanpa membuka Settings adalah mengubah
/// ambang otomatis di baterai (SUB_ENERGYSAVER / ESBATTTHRESHOLD, 0 = tidak pernah, 100 = selalu) di paket daya aktif:
/// menyalakan = ambang 100, mematikan = mengembalikan nilai asli pengguna. Efeknya hanya saat di baterai.
/// Nilai asli disimpan di settings.json (BatterySaverOriginalThreshold) agar bisa dipulihkan walau iLand tertutup mendadak.
/// </summary>
public interface IBatterySaverService
{
    BatterySaverState State { get; }

    event EventHandler? Changed;

    /// <summary>Mulai mendengarkan perubahan status. Juga memulihkan ambang asli jika Battery saver sudah mati.</summary>
    void Attach();

    void Detach();

    /// <summary>Menyalakan atau mematikan lewat ambang. Mengembalikan false jika gagal (pemanggil membuka Settings).</summary>
    Task<bool> SetAsync(bool on);
}
