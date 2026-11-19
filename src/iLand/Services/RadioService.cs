using System.Windows.Threading;
using iLand.Interfaces;
using iLand.Models;
using Microsoft.Extensions.Logging;
using Windows.Devices.Radios;
using Windows.Networking.Connectivity;

namespace iLand.Services;

/// <summary>
/// Wi-Fi dan Bluetooth lewat Windows.Devices.Radios. Perubahan dari luar (Quick Settings, mode pesawat) datang
/// lewat event StateChanged, jadi tidak ada polling. Semua pendengar hanya terpasang antara StartAsync dan Stop.
/// Izin: Radio.RequestAccessAsync dipanggil sekali (dari UI thread) sebelum pengubahan pertama.
/// </summary>
public sealed class RadioService : IRadioService
{
    private readonly ILogger<RadioService> _logger;
    private readonly Dispatcher _ui;

    private Radio? _wifiRadio;
    private Radio? _bluetoothRadio;
    private bool _started;
    private int _startVersion;
    private bool _accessChecked;
    private bool _accessDenied;

    public RadioService(ILogger<RadioService> logger)
    {
        _logger = logger;
        _ui = Dispatcher.CurrentDispatcher; // service ini dibuat di UI thread
        Wifi = RadioStatus.Unavailable;
        Bluetooth = RadioStatus.Unavailable;
    }

    public RadioStatus Wifi { get; private set; }

    public RadioStatus Bluetooth { get; private set; }

    public string? WifiNetworkName { get; private set; }

    public bool IsWifiConnected { get; private set; }

    public event EventHandler? Changed;

    public async Task StartAsync()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        int version = ++_startVersion;

        try
        {
            IReadOnlyList<Radio> radios = await Radio.GetRadiosAsync();

            // Tab ditutup saat menunggu: jangan memasang apa pun.
            if (!_started || version != _startVersion)
            {
                return;
            }

            _wifiRadio = radios.FirstOrDefault(r => r.Kind == RadioKind.WiFi);
            _bluetoothRadio = radios.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);

            if (_wifiRadio != null)
            {
                _wifiRadio.StateChanged += OnRadioStateChanged;
            }

            if (_bluetoothRadio != null)
            {
                _bluetoothRadio.StateChanged += OnRadioStateChanged;
            }

            NetworkInformation.NetworkStatusChanged += OnNetworkStatusChanged;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the radios of this PC");
        }

        Refresh();
    }

    public void Stop()
    {
        if (!_started)
        {
            return;
        }

        _started = false;
        _startVersion++;

        if (_wifiRadio != null)
        {
            _wifiRadio.StateChanged -= OnRadioStateChanged;
            _wifiRadio = null;
        }

        if (_bluetoothRadio != null)
        {
            _bluetoothRadio.StateChanged -= OnRadioStateChanged;
            _bluetoothRadio = null;
        }

        NetworkInformation.NetworkStatusChanged -= OnNetworkStatusChanged;
    }

    public Task<bool> SetWifiAsync(bool on) => SetAsync(_wifiRadio, on);

    public Task<bool> SetBluetoothAsync(bool on) => SetAsync(_bluetoothRadio, on);

    private async Task<bool> SetAsync(Radio? radio, bool on)
    {
        if (radio == null)
        {
            return false;
        }

        try
        {
            if (!_accessChecked)
            {
                RadioAccessStatus access = await Radio.RequestAccessAsync();
                _accessChecked = true;
                _accessDenied = access != RadioAccessStatus.Allowed;
                _logger.LogInformation("Radio access: {Access}", access);
            }

            if (_accessDenied)
            {
                Refresh();
                return false;
            }

            RadioAccessStatus result = await radio.SetStateAsync(on ? RadioState.On : RadioState.Off);
            Refresh();
            return result == RadioAccessStatus.Allowed;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not change a radio");
            return false;
        }
    }

    // Event WinRT datang dari thread lain: pindahkan ke UI thread.
    private void OnRadioStateChanged(Radio sender, object args) => _ui.BeginInvoke(new Action(Refresh));

    private void OnNetworkStatusChanged(object? sender) => _ui.BeginInvoke(new Action(Refresh));

    private void Refresh()
    {
        Wifi = _wifiRadio == null
            ? RadioStatus.Unavailable
            : new RadioStatus(true, _wifiRadio.State == RadioState.On, _accessDenied);

        Bluetooth = _bluetoothRadio == null
            ? RadioStatus.Unavailable
            : new RadioStatus(true, _bluetoothRadio.State == RadioState.On, _accessDenied);

        ReadWifiConnection();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // Nama jaringan dibaca hanya saat Wi-Fi menyala. Nama tidak ditulis ke log dan tidak disimpan.
    private void ReadWifiConnection()
    {
        WifiNetworkName = null;
        IsWifiConnected = false;

        if (!Wifi.IsOn)
        {
            return;
        }

        try
        {
            ConnectionProfile? profile = NetworkInformation.GetInternetConnectionProfile();
            if (profile?.IsWlanConnectionProfile == true)
            {
                IsWifiConnected = true;
                string? ssid = profile.WlanConnectionProfileDetails?.GetConnectedSsid();
                WifiNetworkName = string.IsNullOrWhiteSpace(ssid) ? null : ssid;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read the Wi-Fi network");
        }
    }
}
