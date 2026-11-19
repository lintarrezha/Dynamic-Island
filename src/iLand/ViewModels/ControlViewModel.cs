using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iLand.Helpers;
using iLand.Interfaces;
using iLand.Models;
using Microsoft.Extensions.Logging;

namespace iLand.ViewModels;

/// <summary>
/// ViewModel tab Control: Wi-Fi, Bluetooth, mode gelap, mute, kunci layar, dan slider volume.
/// IslandViewModel memanggil SetActive(true) saat tab Control terlihat dan SetActive(false) saat tidak.
/// Volume dan mute memakai IMixerService (yang dipasang oleh IslandViewModel saat tab Control atau Mixer terlihat).
/// </summary>
public sealed partial class ControlViewModel : ObservableObject
{
    private const string RadiosSettingsPage = "ms-settings:privacy-radios";

    private readonly IRadioService _radio;
    private readonly IThemeService _theme;
    private readonly IMixerService _mixer;
    private readonly IBrightnessService _brightness;
    private readonly INightLightService _nightLight;
    private readonly IBatterySaverService _batterySaver;
    private readonly ICaptureService _capture;
    private readonly ILogger<ControlViewModel> _logger;

    [ObservableProperty] private bool _isWifiAvailable;
    [ObservableProperty] private bool _isWifiOn;
    [ObservableProperty] private string _wifiSubtitle = "Off";

    [ObservableProperty] private bool _isBluetoothAvailable;
    [ObservableProperty] private bool _isBluetoothOn;
    [ObservableProperty] private string _bluetoothSubtitle = "Off";

    [ObservableProperty] private bool _isDarkMode;
    [ObservableProperty] private bool _isNightLightOn;
    [ObservableProperty] private bool _isBatterySaverOn;
    [ObservableProperty] private bool _isBatterySaverAvailable;
    [ObservableProperty] private bool _isBrightnessAvailable;

    public ControlViewModel(
        IRadioService radio,
        IThemeService theme,
        IMixerService mixer,
        IBrightnessService brightness,
        INightLightService nightLight,
        IBatterySaverService batterySaver,
        ICaptureService capture,
        ILogger<ControlViewModel> logger)
    {
        _radio = radio;
        _theme = theme;
        _mixer = mixer;
        _brightness = brightness;
        _nightLight = nightLight;
        _batterySaver = batterySaver;
        _capture = capture;
        _logger = logger;

        _mixer.PropertyChanged += OnMixerPropertyChanged;
        _radio.Changed += OnRadioChanged;
        _brightness.Changed += OnBrightnessChanged;
        _batterySaver.Changed += OnBatterySaverChanged;
    }

    // ---- Kecerahan (layar internal laptop) ----

    /// <summary>Kecerahan 0..1. Slider menulis lewat sini; IBrightnessService menggabungkan penulisan.</summary>
    public double Brightness
    {
        get => _brightness.Percent / 100.0;
        set => _brightness.SetPercent((int)Math.Round(value * 100));
    }

    public string BrightnessText => _brightness.IsAvailable ? $"{_brightness.Percent}%" : "--";

    private void OnBrightnessChanged(object? sender, EventArgs e)
    {
        IsBrightnessAvailable = _brightness.IsAvailable;
        OnPropertyChanged(nameof(Brightness));
        OnPropertyChanged(nameof(BrightnessText));
    }

    private void OnBatterySaverChanged(object? sender, EventArgs e) => UpdateBatterySaver();

    private void UpdateBatterySaver()
    {
        BatterySaverState state = _batterySaver.State;
        IsBatterySaverAvailable = state != BatterySaverState.Unavailable;
        IsBatterySaverOn = state == BatterySaverState.On;
    }

    // ---- Volume dan mute (dari IMixerService) ----

    public bool IsAudioAvailable => _mixer.IsMasterAvailable;

    public bool IsMuted => _mixer.IsMasterMuted;

    /// <summary>Volume master 0..1. Slider menulis lewat sini; penulisan digabung oleh IMixerService.</summary>
    public double Volume
    {
        get => _mixer.MasterVolume;
        set => _mixer.MasterVolume = value;
    }

    public string VolumeText => $"{Math.Round(_mixer.MasterVolume * 100):0}%";

    private void OnMixerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(IMixerService.MasterVolume):
                OnPropertyChanged(nameof(Volume));
                OnPropertyChanged(nameof(VolumeText));
                break;
            case nameof(IMixerService.IsMasterMuted):
                OnPropertyChanged(nameof(IsMuted));
                break;
            case nameof(IMixerService.IsMasterAvailable):
                OnPropertyChanged(nameof(IsAudioAvailable));
                break;
        }
    }

    // ---- Siklus hidup: layanan hanya hidup saat tab terlihat ----

    /// <summary>Dipanggil IslandViewModel: true saat tab Control benar-benar terlihat.</summary>
    public void SetActive(bool active)
    {
        if (active)
        {
            IsDarkMode = _theme.IsDarkMode; // dibaca ulang setiap tab dibuka
            IsNightLightOn = _nightLight.IsOn;
            _brightness.Attach();
            _batterySaver.Attach();
            UpdateBatterySaver();
            _ = StartRadiosAsync();
        }
        else
        {
            _radio.Stop();
            _brightness.Detach();
            _batterySaver.Detach();
        }
    }

    private async Task StartRadiosAsync()
    {
        await _radio.StartAsync();
        UpdateRadioState();
    }

    private void OnRadioChanged(object? sender, EventArgs e) => UpdateRadioState();

    private void UpdateRadioState()
    {
        RadioStatus wifi = _radio.Wifi;
        IsWifiAvailable = wifi.IsAvailable;
        IsWifiOn = wifi.IsOn;
        WifiSubtitle = !wifi.IsAvailable ? "Not available"
            : wifi.AccessDenied ? "Not allowed"
            : !wifi.IsOn ? "Off"
            : _radio.WifiNetworkName ?? (_radio.IsWifiConnected ? "Connected" : "On");

        RadioStatus bluetooth = _radio.Bluetooth;
        IsBluetoothAvailable = bluetooth.IsAvailable;
        IsBluetoothOn = bluetooth.IsOn;
        BluetoothSubtitle = !bluetooth.IsAvailable ? "Not available"
            : bluetooth.AccessDenied ? "Not allowed"
            : bluetooth.IsOn ? "On" : "Off";
    }

    // ---- Perintah ----
    // ToggleWifiAsync -> ToggleWifiCommand (akhiran Async dibuang oleh [RelayCommand]).

    [RelayCommand]
    private async Task ToggleWifiAsync()
    {
        if (_radio.Wifi.AccessDenied)
        {
            SystemShell.TryOpenSettingsPage(RadiosSettingsPage);
            return;
        }

        await _radio.SetWifiAsync(!_radio.Wifi.IsOn);
    }

    [RelayCommand]
    private async Task ToggleBluetoothAsync()
    {
        if (_radio.Bluetooth.AccessDenied)
        {
            SystemShell.TryOpenSettingsPage(RadiosSettingsPage);
            return;
        }

        await _radio.SetBluetoothAsync(!_radio.Bluetooth.IsOn);
    }

    [RelayCommand]
    private async Task ToggleDarkModeAsync()
    {
        bool target = !_theme.IsDarkMode;
        if (await _theme.SetDarkModeAsync(target))
        {
            IsDarkMode = _theme.IsDarkMode;
        }
    }

    [RelayCommand]
    private async Task ToggleNightLightAsync()
    {
        bool ok = await _nightLight.SetAsync(!_nightLight.IsOn);
        IsNightLightOn = _nightLight.IsOn;

        // Cara langsung gagal atau tidak didukung di Windows ini: buka halaman Night light supaya pengguna tetap bisa mengaturnya.
        if (!ok)
        {
            SystemShell.TryOpenSettingsPage("ms-settings:nightlight");
        }
    }

    [RelayCommand]
    private async Task ToggleBatterySaverAsync()
    {
        bool ok = await _batterySaver.SetAsync(_batterySaver.State != BatterySaverState.On);
        UpdateBatterySaver();

        if (!ok)
        {
            SystemShell.TryOpenSettingsPage("ms-settings:batterysaver");
        }
    }

    // PickColor: layar dibekukan dan island menutup lewat CaptureStarted milik CaptureService.
    [RelayCommand]
    private void PickColor() => _capture.PickColor();

    [RelayCommand]
    private void ToggleMute() => _mixer.ToggleMasterMute();

    [RelayCommand]
    private void LockScreen()
    {
        if (!SystemShell.LockScreen())
        {
            _logger.LogWarning("Windows refused to lock the screen");
        }
    }
}
