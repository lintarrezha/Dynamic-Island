using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iLand.Helpers;

namespace iLand.ViewModels;

/// <summary>
/// Halaman About: versi, folder data, tautan proyek, donasi, kredit, atribusi paket pihak ketiga, dan ringkasan privasi.
/// Tidak mengubah AppSettings. Tautan repositori, situs, dan donasi nonaktif sampai konstantanya diisi di AppInfo.
/// </summary>
public sealed partial class AboutViewModel : ObservableObject
{
    private const string UnavailableText = "Available after the first release.";
    private const string LrclibUrl = "https://lrclib.net";

    public string AppVersion => AppInfo.Version;

    /// <summary>Lokasi folder data iLand (settings, todo, shelf, log).</summary>
    public string DataFolderPath => AppPaths.AppDataFolder;

    public IReadOnlyList<ThirdPartyNotice> ThirdPartyNotices => Helpers.ThirdPartyNotices.All;

    public bool HasRepository => AppInfo.HasRepository;

    public bool HasWebsite => AppInfo.HasWebsite;

    public bool HasDonation => AppInfo.HasDonation;

    public string RepositoryDescription => HasRepository ? AppInfo.RepositoryUrl : UnavailableText;

    public string WebsiteDescription => HasWebsite ? AppInfo.WebsiteUrl : UnavailableText;

    public string DonationDescription => HasDonation
        ? "Donations are voluntary and do not unlock anything."
        : UnavailableText;

    // OpenDataFolder -> OpenDataFolderCommand (dibuat otomatis oleh [RelayCommand]).
    [RelayCommand]
    private void OpenDataFolder() => AppInfo.TryOpenFolder(AppPaths.AppDataFolder);

    [RelayCommand(CanExecute = nameof(HasRepository))]
    private void OpenRepository() => AppInfo.TryOpenUrl(AppInfo.RepositoryUrl);

    [RelayCommand(CanExecute = nameof(HasWebsite))]
    private void OpenWebsite() => AppInfo.TryOpenUrl(AppInfo.WebsiteUrl);

    [RelayCommand(CanExecute = nameof(HasDonation))]
    private void OpenDonation() => AppInfo.TryOpenUrl(AppInfo.DonationUrl);

    [RelayCommand]
    private void OpenLrclib() => AppInfo.TryOpenUrl(LrclibUrl);
}
