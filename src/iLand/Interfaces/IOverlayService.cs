using iLand.Models;

namespace iLand.Interfaces;

/// <summary>
/// Antrean overlay sementara di island (notifikasi, volume, kecerahan, charging, perangkat, Caps Lock).
/// SATU sistem untuk semuanya. INotificationService hanyalah pintu masuk untuk notifikasi teks lama.
///
/// Aturan:
/// - Key sama dengan overlay yang tampil: diperbarui di tempat, timer tampil dimulai ulang.
/// - Prioritas lebih tinggi: menggantikan yang tampil. Bila yang digantikan boleh antre, ia menunggu giliran.
/// - Prioritas sama: yang tidak boleh antre (Level, CapsLock) langsung menggantikan, yang boleh antre menunggu.
/// - Prioritas lebih rendah: menunggu giliran bila boleh antre, jika tidak dibuang.
/// - Antrean maksimal 4. Jeda singkat antar overlay (MotionTokens.OverlayGapMs).
/// - Hanya satu timer sekali-jalan dan hanya hidup selama ada overlay atau jeda. Tanpa overlay: tidak ada timer.
/// Semua anggota aman dipanggil dari thread mana pun (dipindahkan ke UI thread), kecuali yang dicatat sebaliknya.
/// </summary>
public interface IOverlayService
{
    /// <summary>Overlay yang sedang tampil, atau null.</summary>
    OverlayRequest? Current { get; }

    /// <summary>
    /// Saat true (island sedang expanded atau disembunyikan karena fullscreen): semua overlay dibuang
    /// dan yang baru ditolak. Hanya diubah dari UI thread.
    /// </summary>
    bool IsSuppressed { get; set; }

    /// <summary>
    /// Saat true (mode rapat): peringatan, charging, dan perangkat tersambung ditahan (dibuang, tidak ditampilkan nanti),
    /// kecuali yang bertanda IsCritical. Bar volume, kecerahan, dan Caps Lock (umpan balik atas tindakan pengguna) tetap tampil.
    /// </summary>
    bool IsMeetingMode { get; set; }

    /// <summary>Diangkat di UI thread saat Current berubah (muncul, diperbarui, diganti, atau hilang).</summary>
    event EventHandler? Changed;

    void Show(OverlayRequest request);

    /// <summary>Menutup overlay yang tampil (klik pengguna). Yang menunggu giliran tetap tampil sesudahnya.</summary>
    void Dismiss();

    /// <summary>Pointer ada di atas overlay: tahan agar tidak hilang.</summary>
    void Hold();

    /// <summary>Pointer pergi: hilang dalam sisa waktu tertentu (milidetik). Tidak berefek bila tidak sedang ditahan.</summary>
    void Release(int remainingMs);

    /// <summary>Membuang overlay yang tampil dan semua yang menunggu.</summary>
    void Clear();
}
