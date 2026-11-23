namespace iLand.Interfaces;

/// <summary>
/// Mode rapat otomatis. Aktif saat aplikasi lain membuka mikrofon lebih dari beberapa detik (kecuali aplikasi di daftar
/// "Ignore these apps"). Saat aktif: musik dijeda (hanya bila iLand yang menjedanya dan hanya bila diizinkan), pill peringatan
/// iLand ditahan (kecuali yang kritis), dan island menampilkan tanda mikrofon kecil atau tombol mic.
///
/// Mengikuti AppSettings.MeetingModeEnabled (MATI bawaan). Mati = pemantau mikrofon dihentikan sepenuhnya.
/// Hanya status pemakaian dan nama proses yang dibaca. Tidak pernah audio.
/// </summary>
public interface IMeetingService
{
    /// <summary>True saat mode rapat sedang aktif.</summary>
    bool IsMeetingActive { get; }

    /// <summary>True bila mikrofon default sistem sedang dibisukan (hanya dibaca selama mode rapat aktif).</summary>
    bool IsMicMuted { get; }

    /// <summary>Diangkat di UI thread saat IsMeetingActive atau IsMicMuted berubah.</summary>
    event EventHandler? Changed;

    /// <summary>Dipanggil sekali saat aplikasi mulai (UI thread). Seterusnya mengikuti perubahan pengaturan.</summary>
    void Initialize();

    /// <summary>Membisukan atau membunyikan mikrofon default SISTEM (bukan hanya satu aplikasi). Hanya berefek saat mode rapat aktif.</summary>
    void ToggleMic();
}
