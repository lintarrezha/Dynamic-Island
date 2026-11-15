using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using iLand.Helpers;
using iLand.Interfaces;
using iLand.Models;
using Microsoft.Extensions.Logging;

namespace iLand.Services;

/// <summary>
/// Mencari lirik di LRCLIB (https://lrclib.net), layanan lirik terbuka tanpa API key.
///
/// Privasi: fitur ini mati secara bawaan (AppSettings.LyricsEnabled). Saat mati, tidak ada permintaan jaringan
/// dan tidak ada akses cache. Saat menyala, yang dikirim hanya judul, artis, album, dan durasi lagu yang sedang
/// diputar, dan hanya sekali per lagu (hasilnya disimpan di cache lokal, lihat LyricsCache).
/// Judul, artis, dan teks lirik tidak pernah ditulis ke log.
///
/// Urutan: cache, lalu GET /api/get (pencocokan tepat), lalu GET /api/search (cadangan yang lebih longgar).
/// Waktu tunggu 5 detik per pencarian. HttpClient baru dibuat saat permintaan pertama.
/// </summary>
public sealed class LyricsService : ILyricsService, IDisposable
{
    private const string ApiBase = "https://lrclib.net/api";

    // Tautan proyek untuk header User-Agent (LRCLIB menganjurkan nama, versi, dan tautan).
    // Isi setelah nama repositori GitHub final. Selama kosong, yang dikirim hanya "iLand/versi".
    private const string ProjectUrl = "";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    // Toleransi durasi untuk pencarian cadangan (/api/get sendiri memakai sekitar 2 detik di sisi server).
    private const double DurationToleranceSeconds = 3;

    private readonly ISettingsService _settingsService;
    private readonly ILogger<LyricsService> _logger;
    private readonly LyricsCache _cache;
    private readonly object _httpGate = new();

    private HttpClient? _http;

    public LyricsService(ISettingsService settingsService, ILogger<LyricsService> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
        _cache = new LyricsCache(AppPaths.LyricsFolder, logger);
    }

    public bool IsEnabled => _settingsService.Current.LyricsEnabled;

    public async Task<LyricsResult> GetLyricsAsync(
        string title,
        string artist,
        string album,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        // Pintu utama privasi: fitur mati = berhenti di sini, tanpa jaringan dan tanpa disk.
        if (!IsEnabled)
        {
            return LyricsResult.Disabled;
        }

        (string cleanTitle, string cleanArtist) = TrackNameCleaner.Clean(title, artist);
        if (cleanTitle.Length == 0)
        {
            return LyricsResult.NotFound;
        }

        int seconds = duration > TimeSpan.Zero ? (int)Math.Round(duration.TotalSeconds) : 0;
        string key = LyricsCache.MakeKey(cleanTitle, cleanArtist, seconds);

        LyricsPayload? cached = await _cache.TryGetAsync(key).ConfigureAwait(false);
        if (cached is not null)
        {
            return cached.ToResult();
        }

        // Dua pembatal digabung: pembatal dari pemanggil (lagu berganti) dan waktu tunggu 5 detik.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);

        try
        {
            LyricsPayload payload = await FetchAsync(cleanTitle, cleanArtist, album ?? string.Empty, seconds, timeout.Token)
                .ConfigureAwait(false);

            await _cache.SaveAsync(key, payload).ConfigureAwait(false);
            return payload.ToResult();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // dibatalkan pemanggil: bukan kegagalan
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Lyrics request timed out");
            return LyricsResult.Error;
        }
        catch (HttpRequestException ex)
        {
            // Hanya kode status yang dicatat. Pesan exception bisa memuat alamat lengkap (berisi judul lagu).
            _logger.LogInformation("Lyrics request failed (status {StatusCode})", ex.StatusCode);
            return LyricsResult.Error;
        }
        catch (JsonException)
        {
            _logger.LogInformation("Lyrics response could not be read");
            return LyricsResult.Error;
        }
    }

    // ---- Pencarian ke LRCLIB ----

    private async Task<LyricsPayload> FetchAsync(
        string title, string artist, string album, int seconds, CancellationToken ct)
    {
        // 1) Pencocokan tepat.
        var getUrl = new StringBuilder($"{ApiBase}/get?track_name={Uri.EscapeDataString(title)}");
        if (artist.Length > 0)
        {
            getUrl.Append("&artist_name=").Append(Uri.EscapeDataString(artist));
        }

        if (album.Length > 0)
        {
            getUrl.Append("&album_name=").Append(Uri.EscapeDataString(album));
        }

        if (seconds > 0)
        {
            getUrl.Append("&duration=").Append(seconds);
        }

        string? getBody = await GetJsonAsync(getUrl.ToString(), ct).ConfigureAwait(false);
        LyricsPayload? exact = getBody is null ? null : ParseTrack(getBody);

        if (exact is { HasLyrics: true })
        {
            return exact;
        }

        // 2) Cadangan: pencarian longgar (judul dan artis pertama saja), lalu pilih hasil terbaik.
        var searchUrl = new StringBuilder($"{ApiBase}/search?track_name={Uri.EscapeDataString(title)}");
        if (artist.Length > 0)
        {
            searchUrl.Append("&artist_name=").Append(Uri.EscapeDataString(TrackNameCleaner.FirstArtist(artist)));
        }

        string? searchBody = await GetJsonAsync(searchUrl.ToString(), ct).ConfigureAwait(false);
        LyricsPayload? best = searchBody is null ? null : PickBest(searchBody, seconds);
        if (best is not null)
        {
            return best;
        }

        // Tidak ada lirik. Jika /api/get menandai lagunya instrumental, teruskan tanda itu.
        return exact is { Instrumental: true } ? exact : LyricsPayload.Empty;
    }

    /// <summary>GET dan kembalikan isi JSON. 404 dan 400 berarti "tidak ada hasil" (null). Kegagalan lain dilempar sebagai HttpRequestException.</summary>
    private async Task<string?> GetJsonAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using HttpResponseMessage response = await GetHttp()
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    private static LyricsPayload? ParseTrack(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return ReadTrack(document.RootElement);
    }

    /// <summary>
    /// Memilih hasil terbaik dari daftar hasil /api/search: yang punya lirik tersinkron didahulukan,
    /// lalu yang durasinya paling dekat. Hasil dengan selisih durasi lebih dari toleransi dilewati.
    /// </summary>
    private static LyricsPayload? PickBest(string json, int seconds)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        LyricsPayload? best = null;
        int bestSyncRank = int.MaxValue;
        double bestDiff = double.MaxValue;

        foreach (JsonElement element in document.RootElement.EnumerateArray())
        {
            LyricsPayload? candidate = ReadTrack(element);
            if (candidate is null || !candidate.HasLyrics)
            {
                continue;
            }

            double diff = 0;
            if (seconds > 0 && element.TryGetProperty("duration", out JsonElement durationElement)
                            && durationElement.ValueKind == JsonValueKind.Number
                            && durationElement.TryGetDouble(out double candidateSeconds))
            {
                diff = Math.Abs(candidateSeconds - seconds);
                if (diff > DurationToleranceSeconds)
                {
                    continue;
                }
            }

            int syncRank = string.IsNullOrWhiteSpace(candidate.Synced) ? 1 : 0;
            if (syncRank < bestSyncRank || (syncRank == bestSyncRank && diff < bestDiff))
            {
                best = candidate;
                bestSyncRank = syncRank;
                bestDiff = diff;
            }
        }

        return best;
    }

    private static LyricsPayload? ReadTrack(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        bool instrumental = element.TryGetProperty("instrumental", out JsonElement flag)
                            && flag.ValueKind == JsonValueKind.True;

        return new LyricsPayload(
            ReadString(element, "syncedLyrics"),
            ReadString(element, "plainLyrics"),
            instrumental);
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
        {
            string? text = value.GetString();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        return null;
    }

    // ---- HttpClient ----

    private HttpClient GetHttp()
    {
        lock (_httpGate)
        {
            if (_http is null)
            {
                var client = new HttpClient
                {
                    // Waktu tunggu diatur lewat CancellationToken per pencarian (5 detik), bukan di sini.
                    Timeout = Timeout.InfiniteTimeSpan,
                    MaxResponseContentBufferSize = 2 * 1024 * 1024
                };

                string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0";
                string userAgent = ProjectUrl.Length > 0 ? $"iLand/{version} (+{ProjectUrl})" : $"iLand/{version}";
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", userAgent);
                client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");

                _http = client;
            }

            return _http;
        }
    }

    public void Dispose()
    {
        lock (_httpGate)
        {
            _http?.Dispose();
            _http = null;
        }
    }
}
