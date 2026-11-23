using System.Globalization;
using iLand.Helpers;
using iLand.Interfaces;
using iLand.Models;
using Microsoft.Extensions.Logging;

namespace iLand.Services;

/// <summary>
/// Penghubung antara pendengar volume dan kecerahan dengan antrean overlay.
/// - Volume: IMasterVolumeWatcher (event Core Audio). Kecerahan: IBrightnessService (event WMI, pemilik "overlay").
/// - Hanya terpasang selama ShowVolumeBrightnessOverlay menyala. Dimatikan = Stop dan Detach, tidak ada event tersisa.
/// - Overlay yang sama (Key "level:volume" atau "level:brightness") diperbarui di tempat selama nilai terus berubah.
/// - Nilai awal hanya dicatat sebagai acuan (tidak menampilkan overlay).
/// - Kecerahan hanya ada pada layar internal laptop. Pada PC desktop atau layar eksternal IsAvailable false dan bagian ini diam.
/// Semua anggota dipanggil dari UI thread.
/// </summary>
public sealed class LevelOverlayService : ILevelOverlayService, IDisposable
{
    private const string BrightnessOwner = "overlay";
    private const string VolumeKey = "level:volume";
    private const string BrightnessKey = "level:brightness";

    private readonly ISettingsService _settingsService;
    private readonly IOverlayService _overlayService;
    private readonly IMasterVolumeWatcher _volumeWatcher;
    private readonly IBrightnessService _brightness;
    private readonly ILogger<LevelOverlayService> _logger;

    private bool _initialized;
    private bool _running;
    private int _lastBrightness = -1;

    public LevelOverlayService(
        ISettingsService settingsService,
        IOverlayService overlayService,
        IMasterVolumeWatcher volumeWatcher,
        IBrightnessService brightness,
        ILogger<LevelOverlayService> logger)
    {
        _logger = logger;
        _settingsService = settingsService;
        _overlayService = overlayService;
        _volumeWatcher = volumeWatcher;
        _brightness = brightness;
    }

    public void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        _settingsService.SettingsChanged += OnSettingsChanged;
        Apply();
    }

    public void Dispose()
    {
        if (_initialized)
        {
            _settingsService.SettingsChanged -= OnSettingsChanged;
        }

        Stop();
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        Apply(); // murah: hanya membandingkan satu nilai bool
    }

    private void Apply()
    {
        bool wanted = _settingsService.Current.ShowVolumeBrightnessOverlay;
        if (wanted == _running)
        {
            return;
        }

        if (wanted)
        {
            Start();
        }
        else
        {
            Stop();
        }
    }

    private void Start()
    {
        _logger.LogInformation("Level overlay: start");
        _running = true;
        _lastBrightness = -1;

        _volumeWatcher.Changed += OnVolumeChanged;
        _brightness.Changed += OnBrightnessChanged;

        _volumeWatcher.Start();
        _brightness.Attach(BrightnessOwner);

        // Bila layanan kecerahan sudah menyala karena tab Control, nilainya sudah ada: pakai sebagai acuan.
        if (_brightness.IsAvailable)
        {
            _lastBrightness = _brightness.Percent;
        }
    }

    private void Stop()
    {
        if (!_running)
        {
            return;
        }

        _logger.LogInformation("Level overlay: stop");
        _running = false;
        _volumeWatcher.Changed -= OnVolumeChanged;
        _brightness.Changed -= OnBrightnessChanged;

        _volumeWatcher.Stop();
        _brightness.Detach(BrightnessOwner);
    }

    private void OnVolumeChanged(object? sender, MasterVolumeChangedEventArgs e)
    {
        _overlayService.Show(CreateVolumeRequest(e.Volume, e.IsMuted));
    }

    /// <summary>Overlay volume. Dipakai juga oleh ScrollVolumeService (key sama, jadi bar diperbarui di tempat).</summary>
    public static OverlayRequest CreateVolumeRequest(double volume, bool isMuted)
    {
        bool silent = isMuted || volume <= 0.001;
        int percent = (int)Math.Round(volume * 100);

        string icon = silent ? "IconVolumeOff" : volume < 0.5 ? "IconVolumeLow" : "IconVolumeOn";

        return new OverlayRequest(VolumeKey, OverlayKind.Level, OverlayPriority.Level, MotionTokens.OverlayLevelMs)
        {
            IconKey = icon,
            Level = isMuted ? 0 : volume,
            ValueText = isMuted ? "Muted" : percent.ToString(CultureInfo.InvariantCulture)
        };
    }

    private void OnBrightnessChanged(object? sender, EventArgs e)
    {
        if (!_brightness.IsAvailable)
        {
            _logger.LogInformation("Level overlay: brightness changed but not available");
            return;
        }

        int percent = _brightness.Percent;
        if (_lastBrightness < 0)
        {
            _lastBrightness = percent; // pembacaan pertama: hanya acuan
            _logger.LogInformation("Level overlay: brightness baseline {Percent}", percent);
            return;
        }

        if (percent == _lastBrightness)
        {
            _logger.LogInformation("Level overlay: brightness {Percent} same as last, skipped", percent);
            return;
        }

        _lastBrightness = percent;
        _logger.LogInformation("Level overlay: brightness show {Percent}", percent);

        _overlayService.Show(new OverlayRequest(BrightnessKey, OverlayKind.Level, OverlayPriority.Level, MotionTokens.OverlayLevelMs)
        {
            IconKey = "IconSun",
            Level = percent / 100.0,
            ValueText = percent.ToString(CultureInfo.InvariantCulture)
        });
    }
}
