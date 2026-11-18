using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iLand.Helpers;
using iLand.Interfaces;
using iLand.Models;

namespace iLand.ViewModels;

/// <summary>
/// Halaman General: mulai bersama Windows, animasi, suara notifikasi, lapor bug, folder log, dan cek pembaruan manual.
/// Lapor bug dan cek pembaruan butuh repositori GitHub: selama AppInfo.GitHubOwner/GitHubRepository kosong keduanya nonaktif.
/// </summary>
public sealed partial class GeneralSettingsViewModel : SettingsPageViewModel
{
    private const string UnavailableText = "Available after the first release.";

    private readonly IStartupService _startupService;
    private readonly IUpdateCheckService _updateCheckService;

    // Alamat halaman rilis jika pengecekan terakhir menemukan versi baru (tombol berubah menjadi "Open release page").
    private string? _availableReleaseUrl;

    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private bool _animationsEnabled;
    [ObservableProperty] private double _animationDurationMs;
    [ObservableProperty] private bool _playNotificationSound;

    [ObservableProperty] private string _updateDescription = string.Empty;
    [ObservableProperty] private string _updateButtonText = "Check for updates";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckForUpdatesCommand))]
    private bool _isCheckingForUpdates;

    public GeneralSettingsViewModel(
        ISettingsService settingsService,
        IStartupService startupService,
        IUpdateCheckService updateCheckService)
        : base(settingsService)
    {
        _startupService = startupService;
        _updateCheckService = updateCheckService;

        UpdateDescription = IsRepositoryConfigured
            ? $"You have version {AppInfo.Version}. iLand never checks by itself: it only asks GitHub when you press the button."
            : UnavailableText;

        Load(() =>
        {
            StartWithWindows = _startupService.IsEnabled; // sumber kebenarannya registry, bukan settings.json
            AnimationsEnabled = Current.AnimationsEnabled;
            AnimationDurationMs = Current.AnimationDurationMs;
            PlayNotificationSound = Current.PlayNotificationSound;
        });
    }

    public bool IsRepositoryConfigured => AppInfo.HasRepository;

    public string ReportBugDescription => IsRepositoryConfigured
        ? "Opens a GitHub issue with your iLand and Windows versions filled in. Nothing is sent until you submit it."
        : UnavailableText;

    public string LogFilesDescription =>
        $"Technical notes for troubleshooting, kept for 7 days in {AppPaths.LogsFolder}. They never contain clipboard text or lyrics.";

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (IsLoading)
        {
            return;
        }

        _startupService.SetEnabled(value);
    }

    partial void OnAnimationsEnabledChanged(bool value) => Update(s => s.AnimationsEnabled = value);
    partial void OnAnimationDurationMsChanged(double value) => Update(s => s.AnimationDurationMs = value);
    partial void OnPlayNotificationSoundChanged(bool value) => Update(s => s.PlayNotificationSound = value);

    // ReportBug -> ReportBugCommand (dibuat otomatis oleh [RelayCommand]). Aktif hanya jika repositori sudah diisi.
    [RelayCommand(CanExecute = nameof(IsRepositoryConfigured))]
    private void ReportBug() => AppInfo.TryOpenUrl(AppInfo.BuildNewIssueUrl());

    [RelayCommand]
    private void OpenLogsFolder()
    {
        AppPaths.EnsureFoldersExist();
        AppInfo.TryOpenFolder(AppPaths.LogsFolder);
    }

    private bool CanCheckForUpdates() => IsRepositoryConfigured && !IsCheckingForUpdates;

    // CheckForUpdatesAsync -> CheckForUpdatesCommand (akhiran Async dibuang).
    [RelayCommand(CanExecute = nameof(CanCheckForUpdates))]
    private async Task CheckForUpdatesAsync()
    {
        // Pengecekan sebelumnya menemukan versi baru: tombol kini membuka halaman rilis.
        if (_availableReleaseUrl is not null)
        {
            AppInfo.TryOpenUrl(_availableReleaseUrl);
            return;
        }

        IsCheckingForUpdates = true;
        UpdateDescription = "Asking GitHub...";

        try
        {
            UpdateCheckResult result = await _updateCheckService.CheckAsync();

            switch (result.Status)
            {
                case UpdateCheckStatus.UpdateAvailable:
                    _availableReleaseUrl = result.ReleaseUrl;
                    UpdateDescription = $"Version {result.LatestVersion} is available. You have {AppInfo.Version}.";
                    UpdateButtonText = "Open release page";
                    break;
                case UpdateCheckStatus.UpToDate:
                    UpdateDescription = $"You are up to date (version {AppInfo.Version}).";
                    break;
                case UpdateCheckStatus.NoReleases:
                    UpdateDescription = "No public release has been published yet.";
                    break;
                case UpdateCheckStatus.RateLimited:
                    UpdateDescription = "GitHub is limiting requests from this connection right now. Try again later.";
                    break;
                case UpdateCheckStatus.NotConfigured:
                    UpdateDescription = UnavailableText;
                    break;
                default:
                    UpdateDescription = "Could not reach GitHub. Check your connection and try again.";
                    break;
            }
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }
}
