using iLand.Models;

namespace iLand.Interfaces;

/// <summary>
/// Wi-Fi dan Bluetooth lewat Windows.Devices.Radios (API resmi). Semua anggota dipanggil dari UI thread.
/// Hanya aktif selama tab Control terlihat: Start memasang pendengar, Stop melepas semuanya.
/// </summary>
public interface IRadioService
{
    RadioStatus Wifi { get; }

    RadioStatus Bluetooth { get; }

    /// <summary>Nama jaringan Wi-Fi yang tersambung, atau null jika tidak diketahui (Location Services mati memblokirnya).</summary>
    string? WifiNetworkName { get; }

    /// <summary>True jika ada jaringan yang tersambung (nama boleh tidak diketahui).</summary>
    bool IsWifiConnected { get; }

    /// <summary>Diangkat di UI thread setiap status berubah (dari iLand maupun dari luar).</summary>
    event EventHandler? Changed;

    Task StartAsync();

    void Stop();

    Task<bool> SetWifiAsync(bool on);

    Task<bool> SetBluetoothAsync(bool on);
}
