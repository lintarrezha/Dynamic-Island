namespace iLand.Interfaces;

/// <summary>
/// Memantau aplikasi LAIN yang sedang membuka mikrofon, lewat sesi capture Core Audio (API resmi, sama dengan Volume Mixer).
///
/// PRIVASI: yang dibaca hanya status sesi (aktif atau tidak) dan NAMA PROSES pemiliknya. Tidak pernah audio, tidak ada
/// aliran suara yang dibuka, dan nama proses tidak ditulis ke log atau disk.
/// Sesi iLand sendiri (misalnya rekaman layar dengan mikrofon) dan sesi suara sistem diabaikan.
/// Keterbatasan: yang terlihat adalah proses pemegang sesi. Rapat di dalam browser muncul sebagai proses browser, dan
/// aplikasi yang membisukan mikrofon di dalam aplikasinya tetap terhitung memakai mikrofon.
/// Berbasis event (notifikasi sesi dan perubahan status sesi), tanpa polling. Start dan Stop dari UI thread.
/// </summary>
public interface IMicUsageMonitor
{
    /// <summary>Nama proses (tanpa .exe) aplikasi lain yang sedang membuka mikrofon. Diperbarui sebelum ActiveAppsChanged.</summary>
    IReadOnlyCollection<string> ActiveApps { get; }

    /// <summary>Diangkat di UI thread hanya saat himpunan ActiveApps benar-benar berubah.</summary>
    event EventHandler? ActiveAppsChanged;

    /// <summary>Mulai memantau. Aman dipanggil berulang.</summary>
    void Start();

    /// <summary>Berhenti dan melepas semua objek COM. Aman dipanggil berulang.</summary>
    void Stop();
}
