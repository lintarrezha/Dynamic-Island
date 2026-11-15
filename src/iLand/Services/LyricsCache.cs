using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using iLand.Helpers;
using iLand.Models;
using Microsoft.Extensions.Logging;

namespace iLand.Services;

/// <summary>
/// Isi mentah jawaban LRCLIB untuk satu lagu, yaitu yang disimpan di cache. Bisa diubah menjadi LyricsResult.
/// </summary>
internal sealed record LyricsPayload(string? Synced, string? Plain, bool Instrumental)
{
    /// <summary>Tidak ada lirik sama sekali (lagu tidak ditemukan).</summary>
    public static LyricsPayload Empty { get; } = new(null, null, false);

    public bool HasLyrics => !string.IsNullOrWhiteSpace(Synced) || !string.IsNullOrWhiteSpace(Plain);

    /// <summary>
    /// Berapa lama isi ini boleh dipakai dari cache. null = tanpa batas (lirik tersinkron jarang berubah).
    /// Lirik biasa 30 hari (bisa saja nanti ada versi tersinkron), "tidak ditemukan" dan instrumental 7 hari.
    /// </summary>
    public TimeSpan? Lifetime
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Synced))
            {
                return null;
            }

            return !string.IsNullOrWhiteSpace(Plain) ? TimeSpan.FromDays(30) : TimeSpan.FromDays(7);
        }
    }

    public LyricsResult ToResult()
    {
        if (!string.IsNullOrWhiteSpace(Synced))
        {
            IReadOnlyList<LyricLine> lines = LrcParser.Parse(Synced);
            if (lines.Count > 0)
            {
                return new LyricsResult(LyricsStatus.Synced, lines, Plain ?? string.Empty);
            }
        }

        if (!string.IsNullOrWhiteSpace(Plain))
        {
            return new LyricsResult(LyricsStatus.PlainOnly, Array.Empty<LyricLine>(), Plain.Trim());
        }

        return Instrumental ? LyricsResult.Instrumental : LyricsResult.NotFound;
    }
}

/// <summary>
/// Cache lirik di %APPDATA%\iLand\lyrics\, satu file JSON per lagu.
/// Batas: 300 file atau 20 MB. Jika terlewati, file yang paling lama tidak dipakai dihapus lebih dulu
/// (waktu "terakhir diubah" file diperbarui setiap kali cache dipakai). Hasil "tidak ditemukan" ikut disimpan
/// dengan masa berlaku. Folder baru dibuat saat pertama kali ada yang disimpan.
/// Teks lirik, judul, dan artis tidak pernah ditulis ke log.
/// </summary>
internal sealed class LyricsCache
{
    private const int MaxFiles = 300;
    private const long MaxBytes = 20L * 1024 * 1024;

    private readonly string _folder;
    private readonly ILogger _logger;
    private readonly object _pruneGate = new();

    public LyricsCache(string folder, ILogger logger)
    {
        _folder = folder;
        _logger = logger;
    }

    /// <summary>
    /// Kunci cache dari judul, artis, dan durasi (detik). Berupa hash, jadi nama file tidak membocorkan judul lagu.
    /// </summary>
    public static string MakeKey(string title, string artist, int durationSeconds)
    {
        string raw = $"{title.ToLowerInvariant()}|{artist.ToLowerInvariant()}|{durationSeconds}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant();
    }

    /// <summary>Mengembalikan isi cache, atau null jika tidak ada, rusak, atau sudah kedaluwarsa.</summary>
    public async Task<LyricsPayload?> TryGetAsync(string key)
    {
        string path = PathFor(key);

        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            string json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            CacheFile? file = JsonSerializer.Deserialize<CacheFile>(json);

            if (file is null || (file.ExpiresAtUtc is not null && file.ExpiresAtUtc <= DateTime.UtcNow))
            {
                TryDelete(path);
                return null;
            }

            File.SetLastWriteTimeUtc(path, DateTime.UtcNow); // tandai baru dipakai
            return new LyricsPayload(file.Synced, file.Plain, file.Instrumental);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogDebug("Lyrics cache entry could not be read ({ErrorType})", ex.GetType().Name);
            TryDelete(path); // file rusak: buang, nanti diambil ulang
            return null;
        }
    }

    public async Task SaveAsync(string key, LyricsPayload payload)
    {
        try
        {
            Directory.CreateDirectory(_folder);

            var file = new CacheFile
            {
                Synced = payload.Synced,
                Plain = payload.Plain,
                Instrumental = payload.Instrumental,
                ExpiresAtUtc = payload.Lifetime is { } lifetime ? DateTime.UtcNow + lifetime : null
            };

            string path = PathFor(key);
            string tempPath = path + ".tmp";

            await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(file)).ConfigureAwait(false);
            File.Move(tempPath, path, overwrite: true);

            Prune();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug("Lyrics cache entry could not be saved ({ErrorType})", ex.GetType().Name);
        }
    }

    private string PathFor(string key) => Path.Combine(_folder, key + ".json");

    /// <summary>Menghapus file terlama sampai jumlah dan ukuran total kembali di bawah batas.</summary>
    private void Prune()
    {
        lock (_pruneGate)
        {
            try
            {
                List<FileInfo> files = new DirectoryInfo(_folder)
                    .GetFiles("*.json")
                    .OrderBy(f => f.LastWriteTimeUtc)
                    .ToList();

                long totalBytes = files.Sum(f => f.Length);
                int count = files.Count;

                foreach (FileInfo file in files)
                {
                    if (count <= MaxFiles && totalBytes <= MaxBytes)
                    {
                        break;
                    }

                    long size = file.Length;
                    file.Delete();
                    totalBytes -= size;
                    count--;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug("Lyrics cache cleanup failed ({ErrorType})", ex.GetType().Name);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Tidak bisa dihapus sekarang: tidak masalah, dicoba lagi lain kali.
        }
    }

    /// <summary>Bentuk file JSON di disk.</summary>
    private sealed class CacheFile
    {
        public string? Synced { get; set; }
        public string? Plain { get; set; }
        public bool Instrumental { get; set; }
        public DateTime? ExpiresAtUtc { get; set; }
    }
}
