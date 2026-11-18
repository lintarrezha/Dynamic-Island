using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using iLand.Helpers;
using iLand.Interfaces;
using iLand.Models;
using Microsoft.Extensions.Logging;

namespace iLand.Services;

/// <summary>
/// Menanyakan ke GitHub apakah ada rilis yang lebih baru: satu permintaan GET ke
/// https://api.github.com/repos/{owner}/{repo}/releases/latest (REST API resmi GitHub, tanpa akun atau token).
///
/// Yang dikirim: hanya permintaan HTTP biasa dengan header User-Agent "iLand/{versi}" (GitHub mewajibkan User-Agent)
/// dan Accept "application/vnd.github+json". GitHub tentu melihat alamat IP Anda seperti pada situs web mana pun.
/// Tidak ada data pribadi, tidak ada ID perangkat, dan tidak ada isi log.
/// Hanya dijalankan saat tombol ditekan. Tanpa token, GitHub membatasi sekitar 60 permintaan per jam per alamat IP.
/// </summary>
public sealed class UpdateCheckService : IUpdateCheckService
{
    private readonly ILogger<UpdateCheckService> _logger;

    public UpdateCheckService(ILogger<UpdateCheckService> logger)
    {
        _logger = logger;
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!AppInfo.HasRepository)
        {
            return new UpdateCheckResult(UpdateCheckStatus.NotConfigured);
        }

        // Dibuat per pengecekan: jarang dipakai, jadi tidak ada koneksi yang menggantung di memori.
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"iLand/{AppInfo.Version}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        try
        {
            using HttpResponseMessage response = await client.GetAsync(AppInfo.LatestReleaseApiUrl, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // Belum ada rilis publik (draf dan prarilis tidak dihitung).
                return new UpdateCheckResult(UpdateCheckStatus.NoReleases);
            }

            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                _logger.LogWarning("Update check was rate limited by GitHub ({Status})", (int)response.StatusCode);
                return new UpdateCheckResult(UpdateCheckStatus.RateLimited);
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Update check failed with HTTP {Status}", (int)response.StatusCode);
                return new UpdateCheckResult(UpdateCheckStatus.Failed);
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            JsonElement root = document.RootElement;
            string? tag = root.TryGetProperty("tag_name", out JsonElement tagElement) ? tagElement.GetString() : null;
            string? pageUrl = root.TryGetProperty("html_url", out JsonElement urlElement) ? urlElement.GetString() : null;

            if (!TryParseVersion(tag, out Version latest) || !TryParseVersion(AppInfo.Version, out Version current))
            {
                _logger.LogWarning("Update check could not read a version number from the release");
                return new UpdateCheckResult(UpdateCheckStatus.Failed);
            }

            string releaseUrl = string.IsNullOrWhiteSpace(pageUrl) ? AppInfo.ReleasesPageUrl : pageUrl;

            return latest > current
                ? new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, latest.ToString(3), releaseUrl)
                : new UpdateCheckResult(UpdateCheckStatus.UpToDate, latest.ToString(3), releaseUrl);
        }
        catch (OperationCanceledException)
        {
            // Waktu habis (10 detik) atau dibatalkan.
            _logger.LogWarning("Update check timed out or was cancelled");
            return new UpdateCheckResult(UpdateCheckStatus.Failed);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("Update check network error: {Message}", ex.Message);
            return new UpdateCheckResult(UpdateCheckStatus.Failed);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("Update check got an unreadable reply: {Message}", ex.Message);
            return new UpdateCheckResult(UpdateCheckStatus.Failed);
        }
    }

    /// <summary>Membaca "v1.2.3", "1.2.3", atau "1.2.3-beta" menjadi Version(1, 2, 3).</summary>
    private static bool TryParseVersion(string? text, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string trimmed = text.Trim().TrimStart('v', 'V');
        int cut = trimmed.IndexOfAny(new[] { '-', '+' });
        if (cut >= 0)
        {
            trimmed = trimmed[..cut];
        }

        if (!Version.TryParse(trimmed, out Version? parsed))
        {
            return false;
        }

        // Samakan jumlah komponen (1.2 menjadi 1.2.0) agar perbandingan tidak salah.
        version = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0));
        return true;
    }
}
