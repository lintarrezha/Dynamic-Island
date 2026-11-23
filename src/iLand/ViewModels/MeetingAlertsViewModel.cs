using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iLand.Interfaces;
using iLand.Models;

namespace iLand.ViewModels;

/// <summary>
/// Halaman Meeting &amp; Alerts: mode rapat otomatis, daftar aplikasi yang diabaikan, dan toggle notifikasi perangkat
/// (volume dan kecerahan, charging, Bluetooth, Caps Lock). Toggle notifikasi perangkat dipindahkan ke sini dari halaman Island.
/// </summary>
public sealed partial class MeetingAlertsViewModel : SettingsPageViewModel
{
    private const int MaxIgnoredApps = 50;

    // ---- Mode rapat ----
    [ObservableProperty] private bool _meetingModeEnabled;
    [ObservableProperty] private bool _meetingPauseMedia = true;
    [ObservableProperty] private bool _meetingShowMicButton = true;

    // ---- Notifikasi perangkat ----
    [ObservableProperty] private bool _showVolumeBrightnessOverlay = true;
    [ObservableProperty] private bool _chargingAnimationEnabled = true;
    [ObservableProperty] private bool _bluetoothConnectedOverlay = true;
    [ObservableProperty] private bool _capsLockOverlay;

    // ---- Aplikasi yang diabaikan ----
    [ObservableProperty] private string _newIgnoredApp = string.Empty;

    public ObservableCollection<string> IgnoredApps { get; } = new();

    public bool HasIgnoredApps => IgnoredApps.Count > 0;

    public MeetingAlertsViewModel(ISettingsService settingsService) : base(settingsService)
    {
        IgnoredApps.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasIgnoredApps));

        Load(() =>
        {
            MeetingModeEnabled = Current.MeetingModeEnabled;
            MeetingPauseMedia = Current.MeetingPauseMedia;
            MeetingShowMicButton = Current.MeetingShowMicButton;

            ShowVolumeBrightnessOverlay = Current.ShowVolumeBrightnessOverlay;
            ChargingAnimationEnabled = Current.ChargingAnimationEnabled;
            BluetoothConnectedOverlay = Current.BluetoothConnectedOverlay;
            CapsLockOverlay = Current.CapsLockOverlay;

            IgnoredApps.Clear();
            foreach (string app in Current.MeetingIgnoredApps)
            {
                IgnoredApps.Add(app);
            }
        });
    }

    partial void OnMeetingModeEnabledChanged(bool value) => Update(s => s.MeetingModeEnabled = value);
    partial void OnMeetingPauseMediaChanged(bool value) => Update(s => s.MeetingPauseMedia = value);
    partial void OnMeetingShowMicButtonChanged(bool value) => Update(s => s.MeetingShowMicButton = value);

    partial void OnShowVolumeBrightnessOverlayChanged(bool value) => Update(s => s.ShowVolumeBrightnessOverlay = value);
    partial void OnChargingAnimationEnabledChanged(bool value) => Update(s => s.ChargingAnimationEnabled = value);
    partial void OnBluetoothConnectedOverlayChanged(bool value) => Update(s => s.BluetoothConnectedOverlay = value);
    partial void OnCapsLockOverlayChanged(bool value) => Update(s => s.CapsLockOverlay = value);

    [RelayCommand]
    private void AddIgnoredApp()
    {
        string name = AppSettings.NormalizeProcessName(NewIgnoredApp);
        NewIgnoredApp = string.Empty;

        if (name.Length == 0
            || IgnoredApps.Count >= MaxIgnoredApps
            || IgnoredApps.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        IgnoredApps.Add(name);
        SaveIgnoredApps();
    }

    [RelayCommand]
    private void RemoveIgnoredApp(string? name)
    {
        if (name is null)
        {
            return;
        }

        if (IgnoredApps.Remove(name))
        {
            SaveIgnoredApps();
        }
    }

    private void SaveIgnoredApps()
    {
        List<string> snapshot = IgnoredApps.ToList();
        Update(s => s.MeetingIgnoredApps = snapshot);
    }
}
