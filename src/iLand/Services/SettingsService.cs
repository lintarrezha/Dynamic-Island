using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using iLand.Helpers;
using iLand.Interfaces;
using iLand.Models;
using Microsoft.Extensions.Logging;

namespace iLand.Services;

/// <summary>
/// Membaca dan menyimpan pengaturan ke %APPDATA%\iLand\settings.json.
/// Penyimpanan ditunda 500 ms setelah perubahan terakhir, supaya menggeser slider
/// tidak menulis file puluhan kali.
/// </summary>
public sealed class SettingsService : ISettingsService, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly ILogger<SettingsService> _logger;
    private readonly DispatcherTimer _saveTimer;
    private bool _hasPendingChanges;

    public AppSettings Current { get; }

    public event EventHandler? SettingsChanged;

    public SettingsService(ILogger<SettingsService> logger)
    {
        _logger = logger;
        Current = Load();

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveTimer.Tick += OnSaveTimerTick;
    }

    public void NotifyChanged()
    {
        Current.Validate();
        _hasPendingChanges = true;

        // Mulai ulang hitung mundur 500 ms.
        _saveTimer.Stop();
        _saveTimer.Start();

        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.AppDataFolder);

            // Tulis ke file sementara dulu, lalu timpa file asli.
            // Dengan begitu settings.json tidak rusak kalau aplikasi mati di tengah penulisan.
            string tempPath = AppPaths.SettingsFilePath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(Current, JsonOptions));
            File.Move(tempPath, AppPaths.SettingsFilePath, overwrite: true);

            _hasPendingChanges = false;
            _logger.LogDebug("Settings saved");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings");
        }
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFilePath))
            {
                string json = File.ReadAllText(AppPaths.SettingsFilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded is not null)
                {
                    loaded.Validate();
                    _logger.LogInformation("Settings loaded");
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            // File rusak: jangan crash, pakai nilai bawaan.
            _logger.LogWarning(ex, "Failed to read settings, using defaults");
        }

        return new AppSettings();
    }

    private void OnSaveTimerTick(object? sender, EventArgs e)
    {
        _saveTimer.Stop();
        Save();
    }

    public void Dispose()
    {
        _saveTimer.Stop();
        _saveTimer.Tick -= OnSaveTimerTick;

        // Jika masih ada perubahan yang belum tersimpan saat aplikasi ditutup, simpan sekarang.
        if (_hasPendingChanges)
        {
            Save();
        }
    }
}
