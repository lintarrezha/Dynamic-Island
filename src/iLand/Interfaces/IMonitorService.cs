using iLand.Models;

namespace iLand.Interfaces;

public interface IMonitorService
{
    /// <summary>Semua monitor yang terhubung (monitor utama di urutan pertama).</summary>
    IReadOnlyList<MonitorInfo> GetMonitors();

    /// <summary>Cari monitor berdasarkan nama. Jika kosong atau tidak ditemukan, pakai monitor utama.</summary>
    MonitorInfo GetMonitorOrPrimary(string? deviceName);

    /// <summary>Dipanggil saat monitor dicolok, dicabut, atau resolusinya berubah.</summary>
    event EventHandler? MonitorsChanged;
}
