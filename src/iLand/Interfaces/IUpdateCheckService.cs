using iLand.Models;

namespace iLand.Interfaces;

/// <summary>
/// Pengecekan pembaruan MANUAL (hanya saat pengguna menekan tombol). Tidak ada pengecekan otomatis atau terjadwal.
/// </summary>
public interface IUpdateCheckService
{
    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);
}
