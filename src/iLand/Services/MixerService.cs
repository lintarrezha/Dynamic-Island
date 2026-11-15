using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using Serilog;
using iLand.Helpers;
using iLand.Interfaces;
using iLand.Models;

namespace iLand.Services;

/// <summary>
/// Volume master dan volume per aplikasi lewat NAudio.CoreAudioApi.
///
/// Cara kerja dan biaya:
/// - Hanya aktif antara Attach dan Detach (tab Mixer terlihat). Di luar itu tidak ada event, timer, atau objek COM.
/// - TIDAK ada polling. Perubahan datang dari event Windows: sesi baru, volume berubah dari luar, status sesi,
///   volume master, dan pergantian perangkat output default.
/// - Semua pekerjaan COM berjalan berurutan di thread pool lewat satu penjadwal eksklusif (tanpa kunci COM).
/// - Callback dari Windows harus tidak memblokir dan tidak boleh melepas objek COM (aturan IAudioSessionEvents),
///   jadi setiap callback hanya mengantrekan pekerjaan ke penjadwal itu.
/// - Penulisan volume digabung (sekitar 30 ms): geseran cepat hanya menulis nilai terakhir.
/// - Satu timer sekali-jalan di sisi UI hanya hidup selama ada baris yang menunggu dihapus (8 detik setelah sesi diam).
///
/// Sesi dikelompokkan per nama proses (semua proses Chrome menjadi satu baris). Sesi iLand sendiri dan suara sistem diabaikan.
/// Keterbatasan: aplikasi Store kadang bersuara atas nama proses lain, sehingga nama atau ikonnya bisa meleset.
/// </summary>
public sealed class MixerService : ObservableObject, IMixerService, IDisposable
{
    private const int WriteThrottleMs = 30;
    private static readonly TimeSpan RetainSpan = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan LeaveSpan = TimeSpan.FromMilliseconds(MotionTokens.RowMs);
    private static readonly long UserQuietTicks = Stopwatch.Frequency * 400 / 1000;

    // ---- Sisi UI (hanya thread UI) ----
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<string, MixerApp> _rows = new();
    private readonly Dictionary<string, (DateTime Due, bool Leaving)> _schedule = new();
    private DispatcherTimer? _leaveTimer;
    private bool _attached;
    private bool _disposed;
    private bool _isReady;
    private bool _isMasterAvailable;
    private double _masterVolume;
    private bool _isMasterMuted;
    private long _lastMasterUserTicks;
    private volatile int _generation;

    // ---- Penulisan volume (antrean kecil lintas thread) ----
    private readonly object _pendingGate = new();
    private readonly Dictionary<string, double> _pendingApps = new();
    private double? _pendingMaster;
    private bool _flushScheduled;

    // ---- Sisi COM (hanya lewat _worker) ----
    private readonly TaskFactory _worker = new(
        CancellationToken.None,
        TaskCreationOptions.DenyChildAttach,
        TaskContinuationOptions.None,
        new ConcurrentExclusiveSchedulerPair().ExclusiveScheduler);

    private readonly Dictionary<string, Group> _groups = new();
    private readonly Dictionary<uint, ProcessInfo?> _processCache = new();
    private readonly Dictionary<string, ImageSource?> _iconCache = new();
    private readonly HashSet<string> _knownSessions = new();
    private MMDeviceEnumerator? _enumerator;
    private MMDeviceNotificationClient? _notifier;   // pembungkus event pergantian perangkat dari NAudio (publik)
    private MMDevice? _device;
    private AudioEndpointVolume? _endpointVolume;
    private AudioSessionManager? _manager;
    private int _workerGeneration;
    private int _rebindQueued;

    public MixerService()
    {
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    public ObservableCollection<MixerApp> Apps { get; } = new();

    public bool IsReady => _isReady;

    public bool IsMasterAvailable => _isMasterAvailable;

    public bool IsMasterMuted => _isMasterMuted;

    public double MasterVolume
    {
        get => _masterVolume;
        set
        {
            double clamped = Math.Clamp(value, 0, 1);
            if (Math.Abs(clamped - _masterVolume) < 0.0005)
            {
                return;
            }

            _masterVolume = clamped;
            OnPropertyChanged(nameof(MasterVolume));
            _lastMasterUserTicks = Stopwatch.GetTimestamp();

            lock (_pendingGate)
            {
                _pendingMaster = clamped;
            }

            ScheduleFlush();
        }
    }

    // =====================================================================
    // Attach / Detach (thread UI)
    // =====================================================================

    public void Attach()
    {
        if (_attached || _disposed)
        {
            return;
        }

        _attached = true;
        int generation = ++_generation;
        Enqueue(() => AttachCore(generation));
    }

    public void Detach()
    {
        if (!_attached)
        {
            return;
        }

        _attached = false;
        _generation++;

        ClearRows();
        _isReady = false;
        OnPropertyChanged(nameof(IsReady));

        Enqueue(DetachCore);
    }

    public void ToggleMasterMute()
    {
        Enqueue(() =>
        {
            if (_endpointVolume is not null)
            {
                _endpointVolume.Mute = !_endpointVolume.Mute;
            }
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _attached = false;
        _generation++;
        _leaveTimer?.Stop();
        Enqueue(DetachCore);
    }

    // =====================================================================
    // Thread latar: memasang dan melepas
    // =====================================================================

    private void AttachCore(int generation)
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
            Log.Debug(ex, "Mixer attach failed");
        }

        PostToUi(generation, () =>
        {
            _isReady = true;
            OnPropertyChanged(nameof(IsReady));
        });
    }

    private void DetachCore()
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
            Log.Debug(ex, "Mixer unregister notification failed");
        }

        UnbindDevice();
        DisposeQuietly(_enumerator);
        _enumerator = null;
        _notifier = null;
        _processCache.Clear();
        _iconCache.Clear();
    }

    /// <summary>Memasang perangkat output default: volume master, notifikasi, dan semua sesi yang sudah ada.</summary>
    private void BindDevice()
    {
        UnbindDevice();

        try
        {
            _device = _enumerator!.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Mixer: no default output device");
            _device = null;
            PostToUi(_workerGeneration, () => ApplyMaster(false, 0, false));
            return;
        }

        _endpointVolume = _device.AudioEndpointVolume;
        _endpointVolume.OnVolumeNotification += OnMasterNotification;
        float masterVolume = _endpointVolume.MasterVolumeLevelScalar;
        bool masterMuted = _endpointVolume.Mute;
        PostToUi(_workerGeneration, () => ApplyMaster(true, masterVolume, masterMuted));

        _manager = _device.AudioSessionManager;
        _manager.OnSessionCreated += OnSessionCreated;
        _manager.RefreshSessions();

        SessionCollection sessions = _manager.Sessions;
        for (int i = 0; i < sessions.Count; i++)
        {
            AddSession(sessions[i]);
        }
    }

    /// <summary>Melepas semua sesi, pendengar, dan perangkat. Hanya dari thread latar (bukan dari dalam callback).</summary>
    private void UnbindDevice()
    {
        foreach (Group group in _groups.Values)
        {
            foreach (Entry entry in group.Entries)
            {
                entry.Disposed = true;
                DisposeQuietly(entry.Control);
            }
        }

        _groups.Clear();
        _knownSessions.Clear();

        if (_manager is not null)
        {
            _manager.OnSessionCreated -= OnSessionCreated;
            DisposeQuietly(_manager);
            _manager = null;
        }

        if (_endpointVolume is not null)
        {
            _endpointVolume.OnVolumeNotification -= OnMasterNotification;
            DisposeQuietly(_endpointVolume);
            _endpointVolume = null;
        }

        DisposeQuietly(_device);
        _device = null;
    }

    // =====================================================================
    // Callback dari Windows (tidak memblokir: hanya mengantrekan pekerjaan)
    // =====================================================================

    private void OnSessionCreated(object sender, AudioSessionControl newSession)
    {
        // NAudio 3.x sudah membungkus sesi baru menjadi AudioSessionControl (publik), jadi tinggal diteruskan.
        Enqueue(() => AddSession(newSession));
    }

    private void OnMasterNotification(AudioVolumeNotificationData data)
    {
        float volume = data.MasterVolume;
        bool muted = data.Muted;
        PostToUi(_generation, () => ApplyMaster(true, volume, muted));
    }

    /// <summary>Event dari NAudio. Hanya perangkat output default untuk peran Multimedia yang relevan.</summary>
    private void OnDefaultDeviceChangedEvent(object? sender, DefaultDeviceChangedEventArgs e)
    {
        if (e.Flow == DataFlow.Render && e.Role == Role.Multimedia)
        {
            OnDefaultDeviceChanged();
        }
    }

    private void OnDefaultDeviceChanged()
    {
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

            PostToUi(_workerGeneration, ClearRows);
            BindDevice();
        });
    }

    // =====================================================================
    // Thread latar: sesi
    // =====================================================================

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
                return;
            }

            // Sesi yang sama bisa muncul dua kali (terdaftar lalu juga terbaca saat pemindaian): lewati duplikat.
            string instanceId = SafeInstanceId(control);
            if (instanceId.Length > 0 && !_knownSessions.Add(instanceId))
            {
                return;
            }

            ProcessInfo? info = ResolveProcess(pid);
            if (info is null)
            {
                _knownSessions.Remove(instanceId);
                return;
            }

            if (!_groups.TryGetValue(info.Key, out Group? group))
            {
                group = new Group(info.Key, info.DisplayName, LoadIcon(info.ExePath));
                _groups[info.Key] = group;
            }

            var entry = new Entry(control, group, instanceId)
            {
                Volume = control.SimpleAudioVolume.Volume,
                Muted = control.SimpleAudioVolume.Mute,
                State = control.State
            };

            entry.Handler = new SessionHandler(this, entry);
            control.RegisterEventClient(entry.Handler);

            group.Entries.Add(entry);
            keep = true;
            Publish(group);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Mixer: failed to add session");
        }
        finally
        {
            if (!keep)
            {
                DisposeQuietly(control);
            }
        }
    }

    private void HandleVolume(Entry entry, float volume, bool muted)
    {
        if (entry.Disposed)
        {
            return;
        }

        entry.Volume = volume;
        entry.Muted = muted;
        Publish(entry.Group);
    }

    private void HandleState(Entry entry, AudioSessionState state)
    {
        if (entry.Disposed)
        {
            return;
        }

        entry.State = state;
        Publish(entry.Group);
    }

    private void RemoveEntry(Entry entry)
    {
        if (entry.Disposed)
        {
            return;
        }

        entry.Disposed = true;
        Group group = entry.Group;
        group.Entries.Remove(entry);
        _knownSessions.Remove(entry.InstanceId);
        DisposeQuietly(entry.Control); // aman: ini thread latar, bukan di dalam callback

        if (group.Entries.Count == 0)
        {
            _groups.Remove(group.Key);
            string key = group.Key;
            PostToUi(_workerGeneration, () => RemoveGroupRow(key));
        }
        else
        {
            Publish(group);
        }
    }

    /// <summary>Menghitung status kelompok dari semua sesinya lalu mengirimnya ke UI.</summary>
    private void Publish(Group group)
    {
        if (group.Entries.Count == 0)
        {
            return;
        }

        double volume = 0;
        bool active = false;
        foreach (Entry entry in group.Entries)
        {
            volume = Math.Max(volume, entry.Muted ? 0 : entry.Volume);
            active |= entry.State == AudioSessionState.AudioSessionStateActive;
        }

        string key = group.Key;
        string name = group.Name;
        ImageSource? icon = group.Icon;
        PostToUi(_workerGeneration, () => ApplyGroup(key, name, icon, volume, active));
    }

    // =====================================================================
    // Thread latar: proses dan ikon
    // =====================================================================

    private ProcessInfo? ResolveProcess(uint pid)
    {
        if (_processCache.TryGetValue(pid, out ProcessInfo? cached))
        {
            return cached;
        }

        ProcessInfo? info = null;
        try
        {
            using Process process = Process.GetProcessById((int)pid);
            string name = process.ProcessName;

            string? path = null;
            try
            {
                path = process.MainModule?.FileName; // ditolak untuk proses administrator
            }
            catch
            {
            }

            string display = string.Empty;
            if (path is not null)
            {
                try
                {
                    display = FileVersionInfo.GetVersionInfo(path).FileDescription ?? string.Empty;
                }
                catch
                {
                }
            }

            if (string.IsNullOrWhiteSpace(display))
            {
                display = name.Length > 0 ? char.ToUpperInvariant(name[0]) + name[1..] : name;
            }

            if (name.Length > 0)
            {
                info = new ProcessInfo(name.ToLowerInvariant(), display.Trim(), path);
            }
        }
        catch
        {
            // proses sudah berakhir
        }

        _processCache[pid] = info;
        return info;
    }

    private ImageSource? LoadIcon(string? exePath)
    {
        if (exePath is null)
        {
            return null;
        }

        if (!_iconCache.TryGetValue(exePath, out ImageSource? icon))
        {
            icon = FileIconHelper.GetIcon(exePath); // hasilnya sudah dibekukan, aman dipakai di UI
            _iconCache[exePath] = icon;
        }

        return icon;
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

    // =====================================================================
    // Menulis volume (digabung)
    // =====================================================================

    private void OnUserAppVolume(string key, double value)
    {
        lock (_pendingGate)
        {
            _pendingApps[key] = value;
        }

        ScheduleFlush();
    }

    private void ScheduleFlush()
    {
        lock (_pendingGate)
        {
            if (_flushScheduled)
            {
                return;
            }

            _flushScheduled = true;
        }

        _ = Task.Delay(WriteThrottleMs).ContinueWith(_ => Enqueue(FlushWrites), TaskScheduler.Default);
    }

    private void FlushWrites()
    {
        Dictionary<string, double> apps;
        double? master;

        lock (_pendingGate)
        {
            apps = new Dictionary<string, double>(_pendingApps);
            _pendingApps.Clear();
            master = _pendingMaster;
            _pendingMaster = null;
            _flushScheduled = false;
        }

        foreach (KeyValuePair<string, double> pair in apps)
        {
            if (!_groups.TryGetValue(pair.Key, out Group? group))
            {
                continue;
            }

            foreach (Entry entry in group.Entries)
            {
                try
                {
                    SimpleAudioVolume volume = entry.Control.SimpleAudioVolume;
                    volume.Volume = (float)pair.Value;
                    if (pair.Value > 0 && volume.Mute)
                    {
                        volume.Mute = false; // menggeser slider di atas nol membunyikan kembali
                    }

                    entry.Volume = (float)pair.Value;
                    entry.Muted = false;
                }
                catch
                {
                    // sesi berakhir: akan dibersihkan lewat event disconnect
                }
            }
        }

        if (master is double masterValue && _endpointVolume is not null)
        {
            try
            {
                _endpointVolume.MasterVolumeLevelScalar = (float)masterValue;
                if (masterValue > 0 && _endpointVolume.Mute)
                {
                    _endpointVolume.Mute = false;
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Mixer: failed to write master volume");
            }
        }
    }

    // =====================================================================
    // Thread UI: menerapkan hasil
    // =====================================================================

    private void ApplyMaster(bool available, float volume, bool muted)
    {
        if (_isMasterAvailable != available)
        {
            _isMasterAvailable = available;
            OnPropertyChanged(nameof(IsMasterAvailable));
        }

        // Jangan menimpa geseran pengguna dengan event yang datang terlambat dari tulisan kita sendiri.
        if (Stopwatch.GetTimestamp() - _lastMasterUserTicks > UserQuietTicks
            && Math.Abs(volume - _masterVolume) >= 0.0005)
        {
            _masterVolume = volume;
            OnPropertyChanged(nameof(MasterVolume));
        }

        if (_isMasterMuted != muted)
        {
            _isMasterMuted = muted;
            OnPropertyChanged(nameof(IsMasterMuted));
        }
    }

    private void ApplyGroup(string key, string name, ImageSource? icon, double volume, bool active)
    {
        if (!_rows.TryGetValue(key, out MixerApp? row))
        {
            // Aplikasi yang diam dan belum pernah tampil tidak ditambahkan.
            if (!active)
            {
                return;
            }

            row = new MixerApp(key, OnUserAppVolume) { Name = name, Icon = icon };
            row.SetVolumeFromSystem(volume);
            _rows[key] = row;
            Apps.Add(row);
        }
        else
        {
            row.Name = name;
            row.Icon = icon;

            if (Stopwatch.GetTimestamp() - row.LastUserChangeTicks > UserQuietTicks)
            {
                row.SetVolumeFromSystem(volume);
            }
        }

        if (active)
        {
            // Aktif lagi: batalkan penghapusan (termasuk yang sudah mulai animasi keluar).
            _schedule.Remove(key);
            row.IsLeaving = false;
            ArmTimer();
        }
        else if (!_schedule.ContainsKey(key))
        {
            _schedule[key] = (DateTime.UtcNow + RetainSpan, false);
            ArmTimer();
        }
    }

    private void RemoveGroupRow(string key)
    {
        if (_rows.TryGetValue(key, out MixerApp? row) && !row.IsLeaving)
        {
            row.IsLeaving = true;
            _schedule[key] = (DateTime.UtcNow + LeaveSpan, true);
            ArmTimer();
        }
    }

    private void ClearRows()
    {
        _leaveTimer?.Stop();
        _schedule.Clear();
        _rows.Clear();
        Apps.Clear();
    }

    /// <summary>Timer sekali-jalan menuju jadwal terdekat. Tidak berjalan jika tidak ada baris yang menunggu.</summary>
    private void ArmTimer()
    {
        if (_schedule.Count == 0)
        {
            _leaveTimer?.Stop();
            return;
        }

        DateTime next = DateTime.MaxValue;
        foreach ((DateTime due, bool _) in _schedule.Values)
        {
            if (due < next)
            {
                next = due;
            }
        }

        TimeSpan wait = next - DateTime.UtcNow;
        if (wait < TimeSpan.FromMilliseconds(15))
        {
            wait = TimeSpan.FromMilliseconds(15);
        }

        if (_leaveTimer is null)
        {
            _leaveTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher);
            _leaveTimer.Tick += OnLeaveTimerTick;
        }

        _leaveTimer.Stop();
        _leaveTimer.Interval = wait;
        _leaveTimer.Start();
    }

    private void OnLeaveTimerTick(object? sender, EventArgs e)
    {
        _leaveTimer?.Stop();
        DateTime now = DateTime.UtcNow;

        foreach (KeyValuePair<string, (DateTime Due, bool Leaving)> item in _schedule.ToList())
        {
            if (item.Value.Due > now)
            {
                continue;
            }

            if (!_rows.TryGetValue(item.Key, out MixerApp? row))
            {
                _schedule.Remove(item.Key);
                continue;
            }

            if (!item.Value.Leaving)
            {
                // Waktu tunggu habis: mulai animasi keluar, hapus dari daftar setelah animasi selesai.
                row.IsLeaving = true;
                _schedule[item.Key] = (now + LeaveSpan, true);
            }
            else
            {
                Apps.Remove(row);
                _rows.Remove(item.Key);
                _schedule.Remove(item.Key);
            }
        }

        ArmTimer();
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
                Log.Debug(ex, "Mixer worker action failed");
            }
        });
    }

    /// <summary>Menjalankan pekerjaan di thread UI, kecuali sudah Detach atau Attach ulang sejak pekerjaan itu dijadwalkan.</summary>
    private void PostToUi(int generation, Action action)
    {
        _dispatcher.BeginInvoke(() =>
        {
            if (generation == _generation && _attached)
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

    // =====================================================================
    // Tipe bantu
    // =====================================================================

    private sealed record ProcessInfo(string Key, string DisplayName, string? ExePath);

    private sealed class Group
    {
        public Group(string key, string name, ImageSource? icon)
        {
            Key = key;
            Name = name;
            Icon = icon;
        }

        public string Key { get; }
        public string Name { get; }
        public ImageSource? Icon { get; }
        public List<Entry> Entries { get; } = new();
    }

    private sealed class Entry
    {
        public Entry(AudioSessionControl control, Group group, string instanceId)
        {
            Control = control;
            Group = group;
            InstanceId = instanceId;
        }

        public AudioSessionControl Control { get; }
        public Group Group { get; }
        public string InstanceId { get; }
        public SessionHandler? Handler { get; set; }   // disimpan agar callback tidak diambil garbage collector
        public float Volume { get; set; }
        public bool Muted { get; set; }
        public AudioSessionState State { get; set; }
        public bool Disposed { get; set; }
    }

    /// <summary>Pendengar event satu sesi. Semua metode hanya mengantrekan pekerjaan (aturan callback Windows).</summary>
    private sealed class SessionHandler : IAudioSessionEventsHandler
    {
        private readonly MixerService _owner;
        private readonly Entry _entry;

        public SessionHandler(MixerService owner, Entry entry)
        {
            _owner = owner;
            _entry = entry;
        }

        public void OnVolumeChanged(float volume, bool isMuted) =>
            _owner.Enqueue(() => _owner.HandleVolume(_entry, volume, isMuted));

        public void OnStateChanged(AudioSessionState state) =>
            _owner.Enqueue(() => _owner.HandleState(_entry, state));

        public void OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason) =>
            _owner.Enqueue(() => _owner.RemoveEntry(_entry));

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
