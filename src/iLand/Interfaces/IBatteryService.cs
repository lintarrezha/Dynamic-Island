using iLand.Models;

namespace iLand.Interfaces;

public interface IBatteryService
{
    /// <summary>Kondisi baterai terbaru.</summary>
    BatteryInfo Current { get; }

    /// <summary>Dipanggil saat persentase atau status charging berubah.</summary>
    event EventHandler? Changed;
}
