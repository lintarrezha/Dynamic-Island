namespace iLand.Interfaces;

/// <summary>
/// Kecerahan layar INTERNAL laptop lewat WMI (WmiMonitorBrightness, WmiMonitorBrightnessMethods, WmiMonitorBrightnessEvent).
/// Pada PC desktop atau layar eksternal tidak ada instance WMI-nya, jadi IsAvailable false dan tidak ada yang terjadi.
/// Aktif hanya antara Attach dan Detach. Semua anggota dipanggil dari UI thread.
/// Boleh dipakai beberapa pemilik sekaligus (tab Control = "ui", overlay = "overlay"): layanan baru berhenti
/// setelah SEMUA pemilik melepas, jadi satu Detach tidak mematikan pendengar pemilik lain.
/// </summary>
public interface IBrightnessService
{
    /// <summary>True jika layar internal yang bisa diatur ditemukan.</summary>
    bool IsAvailable { get; }

    /// <summary>Kecerahan 0..100.</summary>
    int Percent { get; }

    event EventHandler? Changed;

    /// <summary>Sama dengan Attach("ui"). Dipakai tab Control.</summary>
    void Attach();

    /// <summary>Sama dengan Detach("ui").</summary>
    void Detach();

    /// <summary>Mulai membaca dan mendengarkan perubahan atas nama satu pemilik. Aman dipanggil berulang.</summary>
    void Attach(string owner);

    /// <summary>Melepas satu pemilik. Pendengar WMI baru dilepas bila tidak ada pemilik tersisa.</summary>
    void Detach(string owner);

    /// <summary>Mengatur kecerahan 0..100. Penulisan beruntun digabung agar slider tidak membanjiri WMI.</summary>
    void SetPercent(int percent);
}
