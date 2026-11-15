using System.Globalization;
using System.IO;
using System.Text.Json;
using iLand.Helpers;
using iLand.Interfaces;
using Microsoft.Extensions.Logging;

namespace iLand.Services;

/// <summary>
/// Menyimpan hitungan sesi fokus per hari di %APPDATA%\iLand\timer-stats.json.
/// Isi file hanya tanggal lokal (yyyy-MM-dd) dan satu angka. File rusak atau hilang berarti hitungan 0.
/// settings.json tidak disentuh, jadi settings.json lama tetap terbaca.
/// </summary>
public sealed class FocusStatsService : IFocusStatsService
{
    private const int MaxSessions = 9999;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly ILogger<FocusStatsService> _logger;

    private string _date = string.Empty;
    private int _count;

    public FocusStatsService(ILogger<FocusStatsService> logger)
    {
        _logger = logger;
        Load();
    }

    public int SessionsToday => _date == Today() ? _count : 0;

    public void RecordFocusSession()
    {
        string today = Today();

        // Hari baru: mulai dari nol lagi.
        if (_date != today)
        {
            _date = today;
            _count = 0;
        }

        _count = Math.Min(_count + 1, MaxSessions);
        Save();
    }

    private static string Today()
    {
        return DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(AppPaths.TimerStatsFilePath))
            {
                return;
            }

            string json = File.ReadAllText(AppPaths.TimerStatsFilePath);
            FocusStatsData? data = JsonSerializer.Deserialize<FocusStatsData>(json);
            if (data is not null && data.Sessions >= 0)
            {
                _date = data.Date ?? string.Empty;
                _count = Math.Min(data.Sessions, MaxSessions);
            }
        }
        catch (Exception ex)
        {
            // File rusak: abaikan, hitungan mulai dari 0.
            _logger.LogWarning(ex, "Failed to read timer stats, starting from zero");
            _date = string.Empty;
            _count = 0;
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.AppDataFolder);

            // Tulis ke file sementara dulu lalu timpa file asli, sama seperti todos.json.
            string tempPath = AppPaths.TimerStatsFilePath + ".tmp";
            var data = new FocusStatsData { Date = _date, Sessions = _count };
            File.WriteAllText(tempPath, JsonSerializer.Serialize(data, JsonOptions));
            File.Move(tempPath, AppPaths.TimerStatsFilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save timer stats");
        }
    }

    /// <summary>Bentuk isi file. Nama properti = nama field JSON.</summary>
    private sealed class FocusStatsData
    {
        public string? Date { get; set; }

        public int Sessions { get; set; }
    }
}
