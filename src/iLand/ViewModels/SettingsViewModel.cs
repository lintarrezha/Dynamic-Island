using System.Diagnostics;
using System.Reflection;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iLand.Helpers;
using iLand.Interfaces;
using iLand.Models;

namespace iLand.ViewModels;

/// <summary>
/// ViewModel untuk jendela Settings. Setiap kali nilai berubah,
/// nilai itu langsung ditulis ke AppSettings dan island ikut berubah (live preview).
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IStartupService _startupService;

    // True saat nilai awal sedang dimuat, supaya tidak dianggap sebagai perubahan dari pengguna.
    private bool _isLoading;

    // ---- Navigasi sidebar ----
    [ObservableProperty] private SettingsPage _selectedPage = SettingsPage.General;

    // ---- General ----
    [ObservableProperty] private bool _startWithWindows;

    // ---- Behavior ----
    [ObservableProperty] private bool _hoverToExpand;
    [ObservableProperty] private bool _autoCollapse;
    [ObservableProperty] private double _collapseDelayMs;
    [ObservableProperty] private bool _autoMinimizeWhenWindowBelow;
    [ObservableProperty] private bool _hideWhenFullscreen;
    [ObservableProperty] private bool _hideFromScreenshots;
    [ObservableProperty] private string _selectedMonitorDevice = string.Empty;
    [ObservableProperty] private bool _playNotificationSound;

    // ---- Appearance ----
    [ObservableProperty] private double _animationDurationMs;
    [ObservableProperty] private double _expandedWidth;
    [ObservableProperty] private double _expandedCornerRadius;
    [ObservableProperty] private double _opacity;

    // ---- Capture ----
    [ObservableProperty] private bool _recordSystemAudio;
    [ObservableProperty] private bool _recordMicrophone;

    // ---- Clipboard ----
    [ObservableProperty] private bool _clipboardHistoryEnabled;
    // ---- Music ----
    [ObservableProperty] private bool _lyricsEnabled;

    /// <summary>Pilihan untuk ComboBox "Display on". Pilihan pertama = monitor utama.</summary>
    public IReadOnlyList<MonitorInfo> MonitorOptions { get; }

    public string AppVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";

    /// <summary>Lokasi folder data iLand (settings, todo, shelf, log). Ditampilkan di halaman About.</summary>
    public string DataFolderPath => AppPaths.AppDataFolder;

    public SettingsViewModel(
        ISettingsService settingsService,
        IStartupService startupService,
        IMonitorService monitorService)
    {
        _settingsService = settingsService;
        _startupService = startupService;

        var options = new List<MonitorInfo>
        {
            new(string.Empty, "Primary monitor (default)", Rect.Empty, true)
        };
        options.AddRange(monitorService.GetMonitors());
        MonitorOptions = options;

        _isLoading = true;
        AppSettings current = _settingsService.Current;
        StartWithWindows = _startupService.IsEnabled; // sumber kebenarannya registry, bukan settings.json
        HoverToExpand = current.HoverToExpand;
        AutoCollapse = current.AutoCollapse;
        CollapseDelayMs = current.CollapseDelayMs;
        AutoMinimizeWhenWindowBelow = current.AutoMinimizeWhenWindowBelow;
        HideWhenFullscreen = current.HideWhenFullscreen;
        HideFromScreenshots = current.HideFromScreenshots;
        SelectedMonitorDevice = current.MonitorDeviceName;
        AnimationDurationMs = current.AnimationDurationMs;
        ExpandedWidth = current.ExpandedWidth;
        ExpandedCornerRadius = current.ExpandedCornerRadius;
        Opacity = current.Opacity;
        PlayNotificationSound = current.PlayNotificationSound;
        RecordSystemAudio = current.RecordSystemAudio;
        RecordMicrophone = current.RecordMicrophone;
        ClipboardHistoryEnabled = current.ClipboardHistoryEnabled;
        LyricsEnabled = current.LyricsEnabled;
        _isLoading = false;
    }

    // Method "On...Changed" dibuat otomatis oleh [ObservableProperty] dan dipanggil saat nilai berubah.
    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_isLoading)
        {
            return;
        }

        _startupService.SetEnabled(value);
    }

    partial void OnHoverToExpandChanged(bool value) => Update(s => s.HoverToExpand = value);
    partial void OnAutoCollapseChanged(bool value) => Update(s => s.AutoCollapse = value);
    partial void OnCollapseDelayMsChanged(double value) => Update(s => s.CollapseDelayMs = value);
    partial void OnAutoMinimizeWhenWindowBelowChanged(bool value) => Update(s => s.AutoMinimizeWhenWindowBelow = value);
    partial void OnHideWhenFullscreenChanged(bool value) => Update(s => s.HideWhenFullscreen = value);
    partial void OnHideFromScreenshotsChanged(bool value) => Update(s => s.HideFromScreenshots = value);
    partial void OnSelectedMonitorDeviceChanged(string value) => Update(s => s.MonitorDeviceName = value ?? string.Empty);
    partial void OnAnimationDurationMsChanged(double value) => Update(s => s.AnimationDurationMs = value);
    partial void OnExpandedWidthChanged(double value) => Update(s => s.ExpandedWidth = value);
    partial void OnExpandedCornerRadiusChanged(double value) => Update(s => s.ExpandedCornerRadius = value);
    partial void OnOpacityChanged(double value) => Update(s => s.Opacity = value);
    partial void OnPlayNotificationSoundChanged(bool value) => Update(s => s.PlayNotificationSound = value);
    partial void OnRecordSystemAudioChanged(bool value) => Update(s => s.RecordSystemAudio = value);
    partial void OnRecordMicrophoneChanged(bool value) => Update(s => s.RecordMicrophone = value);
    partial void OnClipboardHistoryEnabledChanged(bool value) => Update(s => s.ClipboardHistoryEnabled = value);
    partial void OnLyricsEnabledChanged(bool value) => Update(s => s.LyricsEnabled = value);

    private void Update(Action<AppSettings> change)
    {
        if (_isLoading)
        {
            return;
        }

        change(_settingsService.Current);
        _settingsService.NotifyChanged();
    }

    // OpenDataFolder -> OpenDataFolderCommand (dibuat otomatis oleh [RelayCommand]).
    // Membuka folder data iLand di File Explorer.
    [RelayCommand]
    private void OpenDataFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.AppDataFolder}\"")
            {
                UseShellExecute = true
            });
        }
        catch (Exception)
        {
            // File Explorer gagal dibuka: tidak ada yang rusak, jadi aplikasi cukup lanjut berjalan.
        }
    }
}
