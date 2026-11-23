using System.Windows;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using Serilog;
using iLand.Interfaces;
using iLand.Models;

namespace iLand.Services;

/// <summary>
/// Mode rapat otomatis.
///
/// Mulai: ada aplikasi lain (selain yang di daftar "Ignore these apps") yang membuka mikrofon terus-menerus selama
/// MeetingStartDelaySeconds, supaya panggilan sesaat tidak memicu. Selesai: tidak ada lagi selama MeetingEndDelaySeconds.
///
/// Saat aktif:
/// - Musik dijeda lewat SMTC HANYA bila diizinkan di Settings dan sedang diputar. Setelah rapat selesai musik dilanjutkan
///   HANYA bila iLand yang menjedanya, pemutar masih dalam keadaan jeda, dan lagunya masih sama. Bila pengguna menekan play atau
///   mengganti lagu selama rapat, iLand tidak melanjutkan apa pun.
/// - Overlay peringatan iLand ditahan (IOverlayService.IsMeetingMode), kecuali yang kritis.
/// - Mikrofon default sistem dipantau (hanya selama rapat) agar tombol mic di island tersinkron bila diubah dari luar.
///
/// Hanya aktif bila MeetingModeEnabled menyala (MATI bawaan). Mati = pemantau mikrofon dihentikan dan semua dilepas.
/// Hanya status pemakaian dan nama proses yang dibaca, tidak pernah audio. Nama proses tidak ditulis ke log.
/// Semua anggota publik dipanggil dari UI thread.
/// </summary>
public sealed class MeetingService : IMeetingService, IDisposable
{
    private const int MeetingStartDelaySeconds = 6;
    private const int MeetingEndDelaySeconds = 6;

    private enum TimerMode
    {
        None,
        Starting,
        Ending
    }

    private readonly ISettingsService _settingsService;
    private readonly IMicUsageMonitor _monitor;
    private readonly IMediaService _media;
    private readonly IOverlayService _overlayService;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timer;

    private bool _initialized;
    private bool _running;
    private bool _active;
    private TimerMode _mode;

    // Musik
    private bool _pausedByIland;
    private string _pausedKey = string.Empty;

    // Mikrofon (COM hanya di thread latar)
    private readonly TaskFactory _worker = new(
        CancellationToken.None,
        TaskCreationOptions.DenyChildAttach,
        TaskContinuationOptions.None,
        new ConcurrentExclusiveSchedulerPair().ExclusiveScheduler);

    private MMDeviceEnumerator? _micEnumerator;
    private MMDevice? _micDevice;
    private AudioEndpointVolume? _micEndpoint;
    private volatile int _micGeneration;
    private bool _micAvailable;
    private bool _micMuted;

    public MeetingService(
        ISettingsService settingsService,
        IMicUsageMonitor monitor,
        IMediaService media,
        IOverlayService overlayService)
    {
        _settingsService = settingsService;
        _monitor = monitor;
        _media = media;
        _overlayService = overlayService;

        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _timer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher);
        _timer.Tick += OnTimerTick;
    }

    public bool IsMeetingActive => _active;

    public bool IsMicMuted => _active && _micAvailable && _micMuted;

    public event EventHandler? Changed;

    // =====================================================================
    // Mulai dan berhenti mengikuti pengaturan
    // =====================================================================

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
        _timer.Tick -= OnTimerTick;
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        Apply();

        // Daftar aplikasi yang diabaikan bisa berubah saat mode rapat berjalan.
        if (_running)
        {
            Evaluate();
        }
    }

    private void Apply()
    {
        bool wanted = _settingsService.Current.MeetingModeEnabled;
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
        _running = true;
        _monitor.ActiveAppsChanged += OnActiveAppsChanged;
        _monitor.Start();
        Log.Information("Meeting mode: watching microphone use");
    }

    private void Stop()
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        _monitor.ActiveAppsChanged -= OnActiveAppsChanged;
        _monitor.Stop();
        StopTimer();

        if (_active)
        {
            EndMeeting();
        }

        Log.Information("Meeting mode: stopped watching");
    }

    // =====================================================================
    // Deteksi rapat (UI thread)
    // =====================================================================

    private void OnActiveAppsChanged(object? sender, EventArgs e)
    {
        Evaluate();
    }

    private bool HasMeetingApp()
    {
        HashSet<string> ignored = new(_settingsService.Current.MeetingIgnoredApps, StringComparer.OrdinalIgnoreCase);
        return _monitor.ActiveApps.Any(app => !ignored.Contains(app));
    }

    /// <summary>Menyesuaikan timer mulai atau selesai dengan keadaan sekarang.</summary>
    private void Evaluate()
    {
        bool anyApp = HasMeetingApp();

        if (anyApp)
        {
            if (_active)
            {
                if (_mode == TimerMode.Ending)
                {
                    StopTimer(); // rapat berlanjut: batalkan penutupan
                }
            }
            else if (_mode != TimerMode.Starting)
            {
                StartTimer(MeetingStartDelaySeconds, TimerMode.Starting);
            }
        }
        else
        {
            if (!_active)
            {
                if (_mode == TimerMode.Starting)
                {
                    StopTimer(); // panggilan sesaat: batalkan
                }
            }
            else if (_mode != TimerMode.Ending)
            {
                StartTimer(MeetingEndDelaySeconds, TimerMode.Ending);
            }
        }
    }

    private void StartTimer(int seconds, TimerMode mode)
    {
        _timer.Stop();
        _mode = mode;
        _timer.Interval = TimeSpan.FromSeconds(seconds);
        _timer.Start();
    }

    private void StopTimer()
    {
        _timer.Stop();
        _mode = TimerMode.None;
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        TimerMode mode = _mode;
        StopTimer();

        if (!_running)
        {
            return;
        }

        bool anyApp = HasMeetingApp();

        if (mode == TimerMode.Starting && anyApp && !_active)
        {
            BeginMeeting();
        }
        else if (mode == TimerMode.Ending && !anyApp && _active)
        {
            EndMeeting();
        }
    }

    // =====================================================================
    // Mulai dan selesai rapat
    // =====================================================================

    private void BeginMeeting()
    {
        _active = true;
        _overlayService.IsMeetingMode = true;
        _media.Changed += OnMediaChanged;

        if (_settingsService.Current.MeetingPauseMedia)
        {
            _ = PauseMediaAsync();
        }

        BindMic();
        Log.Information("Meeting mode: active");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void EndMeeting()
    {
        _active = false;
        _overlayService.IsMeetingMode = false;
        _media.Changed -= OnMediaChanged;

        _ = ResumeMediaAsync();
        UnbindMic();

        Log.Information("Meeting mode: ended");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // =====================================================================
    // Musik
    // =====================================================================

    private static string KeyOf(MediaState state) => state.SourceAppId + "|" + state.Title + "|" + state.Artist;

    private async Task PauseMediaAsync()
    {
        try
        {
            MediaState state = _media.Current;
            if (!state.HasSession || !state.IsPlaying)
            {
                return;
            }

            bool paused = await _media.PauseAsync();
            if (paused && _active)
            {
                _pausedByIland = true;
                _pausedKey = KeyOf(state);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Meeting mode: pause media failed");
        }
    }

    private async Task ResumeMediaAsync()
    {
        try
        {
            if (!_pausedByIland)
            {
                return;
            }

            _pausedByIland = false;

            MediaState state = _media.Current;
            if (state.HasSession && !state.IsPlaying && KeyOf(state) == _pausedKey)
            {
                await _media.PlayAsync();
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Meeting mode: resume media failed");
        }
    }

    /// <summary>Pengguna memutar sendiri atau mengganti lagu selama rapat: jangan lanjutkan apa pun yang kita jeda.</summary>
    private void OnMediaChanged(object? sender, EventArgs e)
    {
        if (!_pausedByIland)
        {
            return;
        }

        MediaState state = _media.Current;
        if (state.IsPlaying || KeyOf(state) != _pausedKey)
        {
            _pausedByIland = false;
        }
    }

    // =====================================================================
    // Mikrofon default (hanya selama rapat aktif)
    // =====================================================================

    public void ToggleMic()
    {
        if (!_active || !_micAvailable)
        {
            return;
        }

        int generation = _micGeneration;
        EnqueueMic(() =>
        {
            if (_micEndpoint is null)
            {
                return;
            }

            bool next = !_micEndpoint.Mute;
            _micEndpoint.Mute = next;
            PostMic(generation, () => SetMicState(true, next));
        });
    }

    private void BindMic()
    {
        int generation = ++_micGeneration;
        EnqueueMic(() => BindMicCore(generation));
    }

    private void UnbindMic()
    {
        _micGeneration++;
        EnqueueMic(ReleaseMicCore);
        SetMicState(false, false);
    }

    private void BindMicCore(int generation)
    {
        try
        {
            ReleaseMicCore();

            _micEnumerator = new MMDeviceEnumerator();
            try
            {
                _micDevice = _micEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            }
            catch
            {
                _micDevice = _micEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
            }

            _micEndpoint = _micDevice.AudioEndpointVolume;
            _micEndpoint.OnVolumeNotification += OnMicNotification;

            bool muted = _micEndpoint.Mute;
            PostMic(generation, () => SetMicState(true, muted));
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Meeting mode: no microphone to control");
            ReleaseMicCore();
            PostMic(generation, () => SetMicState(false, false));
        }
    }

    private void ReleaseMicCore()
    {
        if (_micEndpoint is not null)
        {
            _micEndpoint.OnVolumeNotification -= OnMicNotification;
            DisposeQuietly(_micEndpoint);
            _micEndpoint = null;
        }

        DisposeQuietly(_micDevice);
        _micDevice = null;
        DisposeQuietly(_micEnumerator);
        _micEnumerator = null;
    }

    /// <summary>Callback dari Windows: hanya mengantrekan pembaruan ke UI thread.</summary>
    private void OnMicNotification(AudioVolumeNotificationData data)
    {
        bool muted = data.Muted;
        PostMic(_micGeneration, () => SetMicState(true, muted));
    }

    private void SetMicState(bool available, bool muted)
    {
        if (_micAvailable == available && _micMuted == muted)
        {
            return;
        }

        _micAvailable = available;
        _micMuted = muted;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void EnqueueMic(Action action)
    {
        _worker.StartNew(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Meeting mode: microphone action failed");
            }
        });
    }

    private void PostMic(int generation, Action action)
    {
        _dispatcher.BeginInvoke(() =>
        {
            if (generation == _micGeneration && _active)
            {
                action();
            }
        });
    }

    private static void DisposeQuietly(object? o)
    {
        try
        {
            (o as IDisposable)?.Dispose();
        }
        catch
        {
        }
    }
}
