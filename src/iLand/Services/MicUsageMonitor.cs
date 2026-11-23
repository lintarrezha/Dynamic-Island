using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using Serilog;
using iLand.Interfaces;

namespace iLand.Services;

/// <summary>
/// Memantau sesi capture (mikrofon) lewat NAudio.CoreAudioApi, dengan pola yang sama seperti MixerService:
/// - Semua pekerjaan COM berurutan di thread latar lewat satu penjadwal eksklusif.
/// - Callback dari Windows hanya mengantrekan pekerjaan dan tidak pernah melepas objek COM.
/// - Perangkat capture default (peran Communications dan Multimedia) dipasang. Pergantian perangkat default memasang ulang.
/// - Tiap sesi didengarkan perubahan statusnya (aktif atau tidak), jadi tidak ada polling.
/// - Nomor "generasi" naik di Stop dan Start sehingga hasil terlambat dari pekerjaan lama diabaikan.
///
/// Yang dibaca: status sesi dan nama proses. Tidak ada aliran audio yang dibuka. Nama proses tidak masuk log.
/// Hanya aktif antara Start dan Stop: di luar itu tidak ada event, thread, atau objek COM.
/// </summary>
public sealed class MicUsageMonitor : IMicUsageMonitor, IDisposable
{
    // ---- Sisi UI ----
    private readonly Dispatcher _dispatcher;
    private bool _running;
    private bool _disposed;
    private volatile int _generation;
    private IReadOnlyCollection<string> _activeApps = Array.Empty<string>();

    // ---- Sisi COM (hanya lewat _worker) ----
    private readonly TaskFactory _worker = new(
        CancellationToken.None,
        TaskCreationOptions.DenyChildAttach,
        TaskContinuationOptions.None,
        new ConcurrentExclusiveSchedulerPair().ExclusiveScheduler);

    private readonly List<DeviceBinding> _devices = new();
    private readonly List<Entry> _entries = new();
    private readonly HashSet<string> _knownSessions = new();
    private readonly Dictionary<uint, string> _names = new();
    private MMDeviceEnumerator? _enumerator;
    private MMDeviceNotificationClient? _notifier;
    private int _workerGeneration;
    private int _rebindQueued;
    private int _recomputeQueued;
    private bool _binding;
    private string _lastPosted = string.Empty;

    public MicUsageMonitor()
    {
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    public IReadOnlyCollection<string> ActiveApps => _activeApps;

    public event EventHandler? ActiveAppsChanged;

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
        _activeApps = Array.Empty<string>();
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
        _activeApps = Array.Empty<string>();
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
        _lastPosted = string.Empty;

        try
        {
            _enumerator = new MMDeviceEnumerator();
            _notifier = _enumerator.CreateNotificationClient(useSynchronizationContext: false);
            _notifier.DefaultDeviceChanged += OnDefaultDeviceChangedEvent;
            BindDevices();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Mic usage monitor start failed");
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
            Log.Debug(ex, "Mic usage monitor: unregister notification failed");
        }

        UnbindDevices();
        DisposeQuietly(_enumerator);
        _enumerator = null;
        _notifier = null;
        _lastPosted = string.Empty;
    }

    /// <summary>Memasang perangkat capture default dan semua sesinya. Hanya dari thread latar (bukan dari dalam callback).</summary>
    private void BindDevices()
    {
        UnbindDevices();
        _binding = true;

        try
        {
            foreach (Role role in new[] { Role.Communications, Role.Multimedia })
            {
                MMDevice? device = null;
                bool owned = false;

                try
                {
                    device = _enumerator!.GetDefaultAudioEndpoint(DataFlow.Capture, role);

                    if (_devices.Any(d => d.DeviceId == device.ID))
                    {
                        continue; // perangkat yang sama untuk dua peran: cukup satu (dibuang di blok finally)
                    }

                    var binding = new DeviceBinding(this, device);
                    owned = true;
                    _devices.Add(binding);

                    binding.Manager = device.AudioSessionManager;
                    binding.Manager.OnSessionCreated += binding.OnSessionCreated;
                    binding.Manager.RefreshSessions();

                    SessionCollection sessions = binding.Manager.Sessions;
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        AddSession(sessions[i]);
                    }
                }
                catch (Exception ex)
                {
                    // Tidak ada mikrofon untuk peran ini: lewati.
                    Log.Debug(ex, "Mic usage monitor: no capture device for a role");
                }
                finally
                {
                    if (!owned)
                    {
                        DisposeQuietly(device);
                    }
                }
            }
        }
        finally
        {
            _binding = false;
        }

        Recompute();
    }

    private void UnbindDevices()
    {
        foreach (Entry entry in _entries)
        {
            entry.Disposed = true;
            DisposeQuietly(entry.Control);
        }

        _entries.Clear();
        _knownSessions.Clear();
        _names.Clear();

        foreach (DeviceBinding binding in _devices)
        {
            if (binding.Manager is not null)
            {
                binding.Manager.OnSessionCreated -= binding.OnSessionCreated;
                DisposeQuietly(binding.Manager);
                binding.Manager = null;
            }

            DisposeQuietly(binding.Device);
        }

        _devices.Clear();
    }

    private void AddSession(AudioSessionControl control)
    {
        bool keep = false;

        try
        {
            if (control.IsSystemSoundsSession)
            {
                return;
            }

            uint pid = control.GetProcessID;
            if (pid == 0 || pid == (uint)Environment.ProcessId)
            {
                return; // sesi sistem atau sesi iLand sendiri
            }

            string instanceId = SafeInstanceId(control);
            if (instanceId.Length > 0 && !_knownSessions.Add(instanceId))
            {
                return; // sesi yang sama muncul dua kali (terdaftar lalu terbaca saat pemindaian)
            }

            var entry = new Entry(control, pid, instanceId);
            entry.Handler = new SessionHandler(this, entry);
            control.RegisterEventClient(entry.Handler);

            _entries.Add(entry);
            keep = true;

            if (!_binding)
            {
                Recompute();
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Mic usage monitor: failed to add session");
        }
        finally
        {
            if (!keep)
            {
                DisposeQuietly(control);
            }
        }
    }

    private void RemoveEntry(Entry entry)
    {
        if (entry.Disposed)
        {
            return;
        }

        entry.Disposed = true;
        _entries.Remove(entry);
        if (entry.InstanceId.Length > 0)
        {
            _knownSessions.Remove(entry.InstanceId);
        }

        DisposeQuietly(entry.Control);
        Recompute();
    }

    /// <summary>Menghitung ulang himpunan aplikasi yang membuka mikrofon dan mengumumkannya hanya bila berubah.</summary>
    private void Recompute()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Entry entry in _entries)
        {
            if (entry.Disposed)
            {
                continue;
            }

            try
            {
                if (entry.Control.State == AudioSessionState.AudioSessionStateActive)
                {
                    names.Add(ResolveName(entry.Pid));
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Mic usage monitor: reading session state failed");
            }
        }

        string key = string.Join('|', names);
        if (key == _lastPosted)
        {
            return;
        }

        _lastPosted = key;
        string[] snapshot = names.ToArray();

        PostToUi(_workerGeneration, () =>
        {
            _activeApps = snapshot;
            ActiveAppsChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private string ResolveName(uint pid)
    {
        if (_names.TryGetValue(pid, out string? cached))
        {
            return cached;
        }

        string name;
        try
        {
            using Process process = Process.GetProcessById((int)pid);
            name = process.ProcessName;
        }
        catch
        {
            name = "unknown";
        }

        _names[pid] = name;
        return name;
    }

    // =====================================================================
    // Callback dari Windows (tidak memblokir: hanya mengantrekan pekerjaan)
    // =====================================================================

    private void OnDefaultDeviceChangedEvent(object? sender, DefaultDeviceChangedEventArgs e)
    {
        if (e.Flow != DataFlow.Capture)
        {
            return;
        }

        if (Interlocked.Exchange(ref _rebindQueued, 1) == 1)
        {
            return; // Windows mengirim beberapa kali: gabungkan
        }

        Enqueue(() =>
        {
            Interlocked.Exchange(ref _rebindQueued, 0);
            if (_enumerator is not null)
            {
                BindDevices();
            }
        });
    }

    private void EnqueueRecompute()
    {
        if (Interlocked.Exchange(ref _recomputeQueued, 1) == 1)
        {
            return;
        }

        Enqueue(() =>
        {
            Interlocked.Exchange(ref _recomputeQueued, 0);
            Recompute();
        });
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
                Log.Debug(ex, "Mic usage monitor worker action failed");
            }
        });
    }

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

    private static string SafeInstanceId(AudioSessionControl control)
    {
        try
        {
            return control.GetSessionInstanceIdentifier ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
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

    // =====================================================================
    // Tipe pendukung
    // =====================================================================

    private sealed class DeviceBinding
    {
        private readonly MicUsageMonitor _owner;

        public DeviceBinding(MicUsageMonitor owner, MMDevice device)
        {
            _owner = owner;
            Device = device;
            DeviceId = device.ID;
        }

        public MMDevice Device { get; }
        public string DeviceId { get; }
        public AudioSessionManager? Manager { get; set; }

        /// <summary>Sesi baru: hanya diteruskan ke thread latar. Method group yang sama dipakai untuk berhenti berlangganan.</summary>
        public void OnSessionCreated(object sender, AudioSessionControl newSession) =>
            _owner.Enqueue(() => _owner.AddSession(newSession));
    }

    private sealed class Entry
    {
        public Entry(AudioSessionControl control, uint pid, string instanceId)
        {
            Control = control;
            Pid = pid;
            InstanceId = instanceId;
        }

        public AudioSessionControl Control { get; }
        public uint Pid { get; }
        public string InstanceId { get; }
        public SessionHandler? Handler { get; set; }   // disimpan agar callback tidak diambil garbage collector
        public bool Disposed { get; set; }
    }

    /// <summary>Pendengar event satu sesi. Semua metode hanya mengantrekan pekerjaan (aturan callback Windows).</summary>
    private sealed class SessionHandler : IAudioSessionEventsHandler
    {
        private readonly MicUsageMonitor _owner;
        private readonly Entry _entry;

        public SessionHandler(MicUsageMonitor owner, Entry entry)
        {
            _owner = owner;
            _entry = entry;
        }

        public void OnStateChanged(AudioSessionState state) => _owner.EnqueueRecompute();

        public void OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason) =>
            _owner.Enqueue(() => _owner.RemoveEntry(_entry));

        public void OnVolumeChanged(float volume, bool isMuted)
        {
        }

        public void OnDisplayNameChanged(string displayName)
        {
        }

        public void OnIconPathChanged(string iconPath)
        {
        }

        public void OnChannelVolumeChanged(uint channelCount, IntPtr newVolumes, uint channelIndex)
        {
        }

        public void OnGroupingParamChanged(ref Guid groupingId)
        {
        }
    }
}
