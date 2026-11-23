using System.Windows;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using Serilog;
using iLand.Interfaces;

namespace iLand.Services;

/// <summary>
/// Mendengarkan volume master dan mute lewat NAudio.CoreAudioApi (AudioEndpointVolume.OnVolumeNotification).
/// Polanya sama dengan MixerService, tetapi tanpa pemindaian sesi aplikasi:
/// - Semua pekerjaan COM berurutan di thread latar lewat satu penjadwal eksklusif.
/// - Callback dari Windows tidak memblokir dan tidak melepas objek COM: hanya mengantrekan pekerjaan.
/// - Pergantian perangkat output default (peran Multimedia) memasang ulang pendengar dan mencatat nilai acuan baru tanpa mengumumkan.
/// - Antar Stop dan Start, "generasi" naik sehingga hasil terlambat dari pekerjaan lama diabaikan.
/// Hanya aktif antara Start dan Stop. Di luar itu tidak ada event, thread, atau objek COM.
/// </summary>
public sealed class MasterVolumeWatcher : IMasterVolumeWatcher, IDisposable
{
    // Volume Windows bergerak per 1 persen (0,01). Selisih lebih kecil dari ini dianggap tidak berubah.
    private const double VolumeEpsilon = 0.004;

    // ---- Sisi UI ----
    private readonly Dispatcher _dispatcher;
    private bool _running;
    private bool _disposed;
    private volatile int _generation;
    private bool _hasBaseline;
    private double _lastVolume;
    private bool _lastMuted;

    // ---- Sisi COM (hanya lewat _worker) ----
    private readonly TaskFactory _worker = new(
        CancellationToken.None,
        TaskCreationOptions.DenyChildAttach,
        TaskContinuationOptions.None,
        new ConcurrentExclusiveSchedulerPair().ExclusiveScheduler);

    private MMDeviceEnumerator? _enumerator;
    private MMDeviceNotificationClient? _notifier;   // pembungkus event pergantian perangkat dari NAudio (publik)
    private MMDevice? _device;
    private AudioEndpointVolume? _endpointVolume;
    private int _workerGeneration;
    private int _rebindQueued;

    public MasterVolumeWatcher()
    {
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    public event EventHandler<MasterVolumeChangedEventArgs>? Changed;

    // =====================================================================
    // Start / Stop (thread UI)
    // =====================================================================

    public void Start()
    {
        if (_running || _disposed)
        {
            return;
        }

        _running = true;
        _hasBaseline = false;
        int generation = ++_generation;
        Enqueue(() => StartCore(generation));
    }

    public void Stop()
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        _generation++;
        Enqueue(StopCore);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _disposed = true;
    }

    // =====================================================================
    // Thread latar
    // =====================================================================

    private void StartCore(int generation)
    {
        _workerGeneration = generation;

        try
        {
            _enumerator = new MMDeviceEnumerator();
            // false = event datang di thread Windows (bukan UI). Handler kita hanya mengantrekan pekerjaan.
            _notifier = _enumerator.CreateNotificationClient(useSynchronizationContext: false);
            _notifier.DefaultDeviceChanged += OnDefaultDeviceChangedEvent;
            BindDevice();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Master volume watcher start failed");
        }
    }

    private void StopCore()
    {
        try
        {
            if (_notifier is not null)
            {
                _notifier.DefaultDeviceChanged -= OnDefaultDeviceChangedEvent;
                _notifier.Dispose();
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Master volume watcher: unregister notification failed");
        }

        UnbindDevice();
        DisposeQuietly(_enumerator);
        _enumerator = null;
        _notifier = null;
    }

    /// <summary>Memasang perangkat output default dan mencatat nilai awalnya sebagai acuan (tanpa mengumumkan).</summary>
    private void BindDevice()
    {
        UnbindDevice();

        try
        {
            _device = _enumerator!.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _endpointVolume = _device.AudioEndpointVolume;
            _endpointVolume.OnVolumeNotification += OnVolumeNotification;

            double volume = _endpointVolume.MasterVolumeLevelScalar;
            bool muted = _endpointVolume.Mute;
            PostToUi(_workerGeneration, () => SetBaseline(volume, muted));
        }
        catch (Exception ex)
        {
            // Tidak ada perangkat output: diam sampai perangkat default berganti.
            Log.Debug(ex, "Master volume watcher: no default output device");
            UnbindDevice();
        }
    }

    private void UnbindDevice()
    {
        if (_endpointVolume is not null)
        {
            _endpointVolume.OnVolumeNotification -= OnVolumeNotification;
            DisposeQuietly(_endpointVolume);
            _endpointVolume = null;
        }

        DisposeQuietly(_device);
        _device = null;
    }

    // =====================================================================
    // Callback dari Windows (tidak memblokir)
    // =====================================================================

    private void OnVolumeNotification(AudioVolumeNotificationData data)
    {
        double volume = data.MasterVolume;
        bool muted = data.Muted;
        PostToUi(_generation, () => HandleChange(volume, muted));
    }

    private void OnDefaultDeviceChangedEvent(object? sender, DefaultDeviceChangedEventArgs e)
    {
        if (e.Flow != DataFlow.Render || e.Role != Role.Multimedia)
        {
            return;
        }

        // Windows mengirim beberapa kali (per peran): gabungkan menjadi satu pemasangan ulang.
        if (Interlocked.Exchange(ref _rebindQueued, 1) == 1)
        {
            return;
        }

        Enqueue(() =>
        {
            Interlocked.Exchange(ref _rebindQueued, 0);
            if (_enumerator is null)
            {
                return;
            }

            // Perangkat baru punya volume sendiri: itu bukan perubahan yang disengaja pengguna.
            PostToUi(_workerGeneration, () => _hasBaseline = false);
            BindDevice();
        });
    }

    // =====================================================================
    // Thread UI
    // =====================================================================

    private void SetBaseline(double volume, bool muted)
    {
        _lastVolume = volume;
        _lastMuted = muted;
        _hasBaseline = true;
    }

    private void HandleChange(double volume, bool muted)
    {
        if (!_hasBaseline)
        {
            SetBaseline(volume, muted);
            return;
        }

        if (Math.Abs(volume - _lastVolume) < VolumeEpsilon && muted == _lastMuted)
        {
            return;
        }

        _lastVolume = volume;
        _lastMuted = muted;
        Changed?.Invoke(this, new MasterVolumeChangedEventArgs(volume, muted));
    }

    // =====================================================================
    // Bantu
    // =====================================================================

    private void Enqueue(Action action)
    {
        _worker.StartNew(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Master volume watcher worker action failed");
            }
        });
    }

    /// <summary>Menjalankan pekerjaan di thread UI, kecuali sudah Stop atau Start ulang sejak pekerjaan itu dijadwalkan.</summary>
    private void PostToUi(int generation, Action action)
    {
        _dispatcher.BeginInvoke(() =>
        {
            if (generation == _generation && _running)
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
