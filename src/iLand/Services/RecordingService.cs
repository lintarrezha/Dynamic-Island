using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using iLand.Interfaces;
using iLand.Models;
using iLand.Native;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Windows.Graphics.Capture;
using Windows.Storage;

namespace iLand.Services;

/// <summary>
/// Mengatur perekaman layar: kapan mulai, kapan berhenti, di mana file disimpan, dan memberi tahu
/// pengguna lewat notifikasi island. Pekerjaan berat (menangkap dan meng-encode) ada di RecordingSession.
///
/// Perekaman hanya berjalan atas perintah pengguna (tombol atau pintasan). Hasilnya berupa file MP4
/// di folder Videos\Captures milik pengguna, dan tidak pernah dikirim ke mana pun.
///
/// Suara: suara sistem (speaker) dan mikrofon bisa ikut direkam sesuai pengaturan. Mikrofon bawaannya MATI
/// dan hanya dibuka selama rekaman jika pengguna menyalakannya.
/// </summary>
public sealed class RecordingService : IRecordingService, IDisposable
{
    private enum RecordingState
    {
        Idle,
        Starting,
        Recording,
        Stopping
    }

    // Perekaman tidak dimulai jika ruang kosong di drive tujuan kurang dari ini (500 MB).
    private const long MinFreeDiskBytes = 500L * 1024 * 1024;

    private readonly ILogger<RecordingService> _logger;
    private readonly INotificationService _notificationService;
    private readonly IHotkeyService _hotkeyService;
    private readonly ISettingsService _settingsService;
    private readonly Dispatcher _uiDispatcher;
    private readonly Stopwatch _stopwatch = new();

    private RecordingState _state = RecordingState.Idle;
    private RecordingSession? _session;
    private StorageFile? _outputFile;

    public event EventHandler? StateChanged;

    public bool IsRecording => _state == RecordingState.Recording;

    public TimeSpan Elapsed => IsRecording ? _stopwatch.Elapsed : TimeSpan.Zero;

    public bool IsMicrophoneActive => IsRecording && _session is { IsMicrophoneActive: true };

    public RecordingService(
        ILogger<RecordingService> logger,
        INotificationService notificationService,
        IHotkeyService hotkeyService,
        ISettingsService settingsService)
    {
        _logger = logger;
        _notificationService = notificationService;
        _hotkeyService = hotkeyService;
        _settingsService = settingsService;
        _uiDispatcher = Dispatcher.CurrentDispatcher;

        _hotkeyService.ToggleRecordingRequested += OnToggleRequested;

        // Dilepas lagi di Dispose (event statis harus selalu dilepas).
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    private void OnToggleRequested(object? sender, EventArgs e)
    {
        // Pesan pintasan tiba di dalam callback Windows. Tunda sampai callback selesai.
        _uiDispatcher.BeginInvoke(new Action(async () => await ToggleAsync()));
    }

    public async Task ToggleAsync()
    {
        if (_state == RecordingState.Recording)
        {
            await StopAsync();
        }
        else if (_state == RecordingState.Idle)
        {
            await StartAsync();
        }
    }

    public async Task StartAsync()
    {
        if (_state != RecordingState.Idle)
        {
            return;
        }

        // Pastikan Windows ini mendukung penangkapan layar.
        if (!GraphicsCaptureSession.IsSupported())
        {
            _notificationService.Show(
                "Recording unavailable",
                "This version of Windows does not support screen recording.",
                NotificationKind.Warning);
            return;
        }

        // Pastikan ruang disk cukup sebelum mulai.
        string folder = GetOutputFolder();
        if (!HasEnoughDiskSpace(folder))
        {
            _notificationService.Show(
                "Not enough disk space",
                "Free up at least 500 MB to record.",
                NotificationKind.Warning);
            return;
        }

        _state = RecordingState.Starting;

        try
        {
            IntPtr monitor = GetMonitorUnderCursor();
            _outputFile = await CreateOutputFileAsync(folder);

            var audio = new RecordingAudioOptions(
                _settingsService.Current.RecordSystemAudio,
                _settingsService.Current.RecordMicrophone);

            RecordingSession session = await RecordingSession.StartAsync(monitor, _outputFile, audio, _logger);
            session.EndedUnexpectedly += OnSessionEndedUnexpectedly;
            session.AudioWarning += OnSessionAudioWarning;
            _session = session;

            _stopwatch.Restart();
            _state = RecordingState.Recording;
            _logger.LogInformation("Recording to {Path}", _outputFile.Path);
            StateChanged?.Invoke(this, EventArgs.Empty);

            // Sumber suara yang diminta tetapi gagal dibuka: beri tahu, jangan diam-diam merekam tanpa suara.
            if (session.StartupWarnings.Count > 0)
            {
                _notificationService.Show(
                    "Audio unavailable",
                    string.Join(" ", session.StartupWarnings),
                    NotificationKind.Warning);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not start recording");
            await DiscardOutputFileAsync();

            _state = RecordingState.Idle;
            _notificationService.Show(
                "Recording failed",
                "Could not start recording. See the log for details.",
                NotificationKind.Warning);
        }
    }

    public async Task StopAsync()
    {
        if (_state != RecordingState.Recording || _session is null)
        {
            return;
        }

        RecordingSession session = _session;
        StorageFile? file = _outputFile;
        _session = null;

        // Pil "Merekam" langsung hilang. Menutup file MP4 butuh sekitar satu detik.
        _state = RecordingState.Stopping;
        StateChanged?.Invoke(this, EventArgs.Empty);

        try
        {
            session.EndedUnexpectedly -= OnSessionEndedUnexpectedly;
            session.AudioWarning -= OnSessionAudioWarning;
            await session.StopAsync();

            _logger.LogInformation("Recording saved: {Path}", file?.Path);
            _notificationService.Show("Recording saved", file?.Name ?? "Recording", NotificationKind.Success);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Recording did not finish cleanly");
            _notificationService.Show(
                "Recording failed",
                "The video could not be saved. See the log for details.",
                NotificationKind.Warning);
        }
        finally
        {
            session.Dispose();
            _outputFile = null;
            _state = RecordingState.Idle;
        }
    }

    /// <summary>Perekaman berakhir sendiri (monitor dicabut, encoder gagal): selesaikan seperti biasa.</summary>
    private void OnSessionEndedUnexpectedly(object? sender, EventArgs e)
    {
        _uiDispatcher.BeginInvoke(new Action(async () =>
        {
            if (_state == RecordingState.Recording)
            {
                _logger.LogWarning("Recording ended unexpectedly, finishing the file");
                await StopAsync();
            }
        }));
    }

    /// <summary>Masalah suara saat merekam (mikrofon hening total, perangkat dicabut). Datang dari thread lain.</summary>
    private void OnSessionAudioWarning(object? sender, string message)
    {
        _uiDispatcher.BeginInvoke(new Action(() =>
        {
            if (_state == RecordingState.Recording)
            {
                _notificationService.Show("Audio problem", message, NotificationKind.Warning);
            }
        }));
    }

    /// <summary>
    /// Laptop masuk sleep atau bangun dari sleep: sesi penangkap layar biasanya mati, jadi rekaman
    /// dihentikan dan file yang sudah terekam disimpan. Event ini datang dari thread lain,
    /// jadi pekerjaannya dipindah ke UI thread.
    /// </summary>
    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Suspend && e.Mode != PowerModes.Resume)
        {
            return;
        }

        PowerModes mode = e.Mode;
        _uiDispatcher.BeginInvoke(new Action(async () =>
        {
            if (_state == RecordingState.Recording)
            {
                _logger.LogInformation("Recording stopped because of {Mode}", mode);
                await StopAsync();
            }
        }));
    }

    // ---- Lokasi file ----

    /// <summary>
    /// Folder rekaman: Videos\Captures (folder yang sama dengan rekaman bawaan Windows). Dibuat jika belum ada.
    /// Lokasi Videos diambil dari Windows, jadi tetap benar walaupun pengguna memindahkan foldernya.
    /// </summary>
    private static string GetOutputFolder()
    {
        string videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        if (string.IsNullOrWhiteSpace(videos))
        {
            videos = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos");
        }

        string folder = Path.Combine(videos, "Captures");
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>
    /// Memeriksa sisa ruang di drive tujuan. Jika tidak bisa diperiksa (misalnya folder Videos
    /// dipindah ke jaringan), perekaman tetap diizinkan.
    /// </summary>
    private bool HasEnoughDiskSpace(string folder)
    {
        try
        {
            string? root = Path.GetPathRoot(folder);
            if (string.IsNullOrEmpty(root))
            {
                return true;
            }

            return new DriveInfo(root).AvailableFreeSpace >= MinFreeDiskBytes;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not check free disk space");
            return true;
        }
    }

    /// <summary>
    /// Membuat file kosong di folder tujuan.
    /// Jika nama sudah ada, Windows menambahkan angka di belakangnya.
    /// </summary>
    private static async Task<StorageFile> CreateOutputFileAsync(string folder)
    {
        // Nama file tidak boleh mengandung ":", jadi jam memakai titik.
        string fileName = $"Recording {DateTime.Now:yyyy-MM-dd HH.mm.ss}.mp4";

        StorageFolder storageFolder = await StorageFolder.GetFolderFromPathAsync(folder);
        return await storageFolder.CreateFileAsync(fileName, CreationCollisionOption.GenerateUniqueName);
    }

    /// <summary>Menghapus file kosong jika perekaman gagal dimulai.</summary>
    private async Task DiscardOutputFileAsync()
    {
        _session?.Dispose();
        _session = null;

        if (_outputFile is null)
        {
            return;
        }

        try
        {
            await _outputFile.DeleteAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not delete the empty recording file");
        }

        _outputFile = null;
    }

    private static IntPtr GetMonitorUnderCursor()
    {
        NativeMethods.GetCursorPos(out NativeMethods.NativePoint point);
        return NativeMethods.MonitorFromPoint(point, NativeMethods.MONITOR_DEFAULTTONEAREST);
    }

    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _hotkeyService.ToggleRecordingRequested -= OnToggleRequested;

        if (_session is null)
        {
            return;
        }

        // Aplikasi ditutup saat merekam: selesaikan file agar tidak rusak. Dijalankan di thread lain
        // supaya menunggunya tidak menahan UI thread.
        RecordingSession session = _session;
        _session = null;
        session.EndedUnexpectedly -= OnSessionEndedUnexpectedly;
        session.AudioWarning -= OnSessionAudioWarning;

        try
        {
            Task.Run(session.StopAsync).Wait(TimeSpan.FromSeconds(10));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not finish the recording while closing");
        }

        session.Dispose();
    }
}
