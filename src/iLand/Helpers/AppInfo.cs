using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace iLand.Helpers;

/// <summary>
/// Informasi aplikasi dan tautan luar di SATU tempat.
///
/// CARA MENGAKTIFKAN setelah repositori GitHub ada: isi GitHubOwner dan GitHubRepository (dua konstanta di bawah).
/// Selama masih kosong, tombol "Report a bug", "Check for updates", dan tautan repositori di Settings nonaktif
/// dan bertuliskan "Available after the first release". Isi WebsiteUrl dan DonationUrl dengan cara yang sama.
/// </summary>
public static class AppInfo
{
    // ===== ISI DI SINI =====
    /// <summary>Pemilik repositori GitHub, contoh: "namaakun". Kosong = fitur GitHub nonaktif.</summary>
    public const string GitHubOwner = "";

    /// <summary>Nama repositori GitHub, contoh: "iLand". Kosong = fitur GitHub nonaktif.</summary>
    public const string GitHubRepository = "";

    /// <summary>Alamat situs proyek (https://...). Kosong = tautan nonaktif.</summary>
    public const string WebsiteUrl = "";

    /// <summary>Alamat halaman donasi (https://...). Kosong = tombol nonaktif.</summary>
    public const string DonationUrl = "";
    // =======================

    public static string Version { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";

    /// <summary>Contoh: "Microsoft Windows 10.0.26100". Tidak memuat nama pengguna atau data pribadi.</summary>
    public static string WindowsVersion { get; } = RuntimeInformation.OSDescription;

    public static bool HasRepository =>
        !string.IsNullOrWhiteSpace(GitHubOwner) && !string.IsNullOrWhiteSpace(GitHubRepository);

    public static bool HasWebsite => IsHttpsUrl(WebsiteUrl);

    public static bool HasDonation => IsHttpsUrl(DonationUrl);

    public static string RepositoryUrl =>
        HasRepository ? $"https://github.com/{GitHubOwner}/{GitHubRepository}" : string.Empty;

    /// <summary>Halaman daftar rilis (dibuka di browser).</summary>
    public static string ReleasesPageUrl => HasRepository ? RepositoryUrl + "/releases" : string.Empty;

    /// <summary>Endpoint REST GitHub "Get the latest release" (hanya rilis publik yang bukan draf dan bukan prarilis).</summary>
    public static string LatestReleaseApiUrl =>
        HasRepository ? $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepository}/releases/latest" : string.Empty;

    /// <summary>
    /// Alamat "issues/new" dengan judul dan isi terisi (parameter title dan body didukung GitHub).
    /// Isi hanya memuat versi aplikasi dan versi Windows, tanpa data pribadi dan tanpa isi log.
    /// Dibuat pendek karena URL yang terlalu panjang ditolak server GitHub (414).
    /// </summary>
    public static string BuildNewIssueUrl()
    {
        if (!HasRepository)
        {
            return string.Empty;
        }

        string body =
            $"**iLand version:** {Version}\n" +
            $"**Windows:** {WindowsVersion}\n\n" +
            "**What happened?**\n\n\n" +
            "**What did you expect to happen?**\n\n\n" +
            "_Logs can help: Settings > General > Log files. Please remove anything private before attaching._";

        return $"{RepositoryUrl}/issues/new?title={Uri.EscapeDataString("Bug: ")}&body={Uri.EscapeDataString(body)}";
    }

    /// <summary>Membuka alamat https di browser bawaan. Alamat lain ditolak. Mengembalikan false jika gagal.</summary>
    public static bool TryOpenUrl(string url)
    {
        if (!IsHttpsUrl(url))
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch (Exception)
        {
            return false; // browser tidak bisa dibuka: tidak ada yang rusak
        }
    }

    /// <summary>Membuka folder di File Explorer. Mengembalikan false jika gagal.</summary>
    public static bool TryOpenFolder(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool IsHttpsUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps;
}
