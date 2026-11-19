namespace iLand.Interfaces;

/// <summary>
/// Kecerahan layar INTERNAL laptop lewat WMI (WmiMonitorBrightness, WmiMonitorBrightnessMethods, WmiMonitorBrightnessEvent).
/// Pada PC desktop atau layar eksternal tidak ada instance WMI-nya, jadi IsAvailable false dan tidak ada yang terjadi.
/// Aktif hanya antara Attach dan Detach. Semua anggota dipanggil dari UI thread.
/// </summary>
public interface IBrightnessService
{
    /// <summary>True jika layar internal yang bisa diatur ditemukan.</summary>
    bool IsAvailable { get; }

    /// <summary>Kecerahan 0..100.</summary>
    int Percent { get; }

    event EventHandler? Changed;

    /// <summary>Mulai membaca dan mendengarkan perubahan. Aman dipanggil berulang.</summary>
    void Attach();

    /// <summary>Berhenti mendengarkan dan melepas objek WMI.</summary>
    void Detach();

    /// <summary>Mengatur kecerahan 0..100. Penulisan beruntun digabung agar slider tidak membanjiri WMI.</summary>
    void SetPercent(int percent);
}
