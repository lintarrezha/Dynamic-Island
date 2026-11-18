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

    // Membaca dengan toleran: komentar dan koma di akhir (hasil suntingan tangan) tidak membuat file dianggap rusak.
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ILogger<SettingsService> _logger;
    private readonly DispatcherTimer _saveTimer;
    private bool _hasPendingChanges;

    public AppSettings Current { get; }

    public event EventHandler? SettingsChanged;

    public SettingsService(ILogger<SettingsService> logger)
    {
        _logger = logger;
        Current = Load();
        MotionTokens.AnimationsEnabled = Current.AnimationsEnabled;

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveTimer.Tick += OnSaveTimerTick;
    }

    public void NotifyChanged()
    {
        Current.Validate();
        MotionTokens.AnimationsEnabled = Current.AnimationsEnabled;
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
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, ReadOptions);
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
            // File rusak: jangan crash, pakai nilai bawaan. Salinan file asli disimpan dulu karena
            // perubahan pengaturan berikutnya akan menimpa settings.json dengan nilai bawaan.
            _logger.LogWarning(ex, "Failed to read settings, using defaults");
            BackUpUnreadableFile();
        }

        return new AppSettings();
    }

    private void BackUpUnreadableFile()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFilePath))
            {
                File.Copy(AppPaths.SettingsFilePath, AppPaths.SettingsFilePath + ".bak", overwrite: true);
                _logger.LogWarning("The unreadable settings.json was copied to settings.json.bak");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not back up the unreadable settings file");
        }
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
