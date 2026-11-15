using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using NAudio.CoreAudioApi;
using Serilog;
using iLand.Helpers;
using iLand.Interfaces;

namespace iLand.Services;

/// <summary>
/// Membaca dan mengubah volume sesi audio aplikasi pemutar lewat NAudio.CoreAudioApi (SimpleAudioVolume).
///
/// Cara kerja dan biaya:
/// - TIDAK ada timer atau polling. Sesi dicari sekali saat Attach (thread latar), disimpan selama slider terlihat,
///   dan dilepas saat Detach.
/// - Semua akses COM dikunci oleh satu gate dan berjalan di thread latar, jadi UI tidak tersendat.
/// - Penulisan volume digabung: bila pengguna menggeser cepat, hanya nilai terakhir yang ditulis.
/// - Pencocokan sesi memakai nama proses hasil tebakan dari SourceAppUserModelId (sama seperti AudioLevelService).
///   Browser sering punya beberapa sesi: semuanya diatur bersama, jadi volume SEMUA tab browser itu ikut berubah.
///
/// Keterbatasan jujur: perubahan volume dari luar (Volume Mixer Windows) tidak disinkronkan saat island terbuka.
/// Nilainya dibaca ulang saat tab Music dibuka kembali.
/// </summary>
public sealed class AppVolumeService : ObservableObject, IAppVolumeService, IDisposable
{
    // Jika sesi tidak ditemukan, percobaan berikutnya baru dilakukan setelah jeda ini.
    private static readonly long RetryTicks = Stopwatch.Frequency * 2;

    private readonly object _comGate = new();     // semua akses COM
    private readonly object _stateGate = new();   // status kecil (appId, antrean tulis)
    private readonly List<AudioSessionControl> _sessions = new();
    private readonly Dictionary<uint, string> _nameCache = new();

    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _device;

    private string? _appId;
    private bool _attached;
    private int _version;
    private long _lastAttemptTicks;

    private double _volume;
    private bool _available;

    private double _pending = -1;
    private bool _applying;

    public double Volume
    {
        get => _volume;
        set
        {
            double clamped = Math.Clamp(value, 0, 1);
            if (Math.Abs(clamped - _volume) < 0.0005)
            {
                return;
            }

            _volume = clamped;
            OnPropertyChanged(nameof(Volume));
            OnPropertyChanged(nameof(IsMuted));
            QueueWrite(clamped);
        }
    }

    public bool IsAvailable => _available;

    public bool IsMuted => _volume <= 0.001;

    public void Attach(string? sourceAppId)
    {
        int version;
        lock (_stateGate)
        {
            bool sameApp = _attached && string.Equals(_appId, sourceAppId, StringComparison.Ordinal);
            bool recentlyTried = Stopwatch.GetTimestamp() - _lastAttemptTicks < RetryTicks;

            // Sudah terpasang untuk aplikasi yang sama: tidak ada yang perlu dilakukan,
            // kecuali sesi belum ditemukan dan jeda percobaan ulang sudah lewat.
            if (sameApp && (_available || recentlyTried))
            {
                return;
            }

            _attached = true;
            _appId = sourceAppId;
            _lastAttemptTicks = Stopwatch.GetTimestamp();
            version = ++_version;
        }

        string? appId = sourceAppId;
        _ = Task.Run(() => ResolveAndRead(appId, version));
    }

    public void Detach()
    {
        lock (_stateGate)
        {
            if (!_attached)
            {
                return;
            }

            _attached = false;
            _appId = null;
            _version++;
        }

        _ = Task.Run(ReleaseSessions);
    }

    // ---- Mencari sesi dan membaca volume (thread latar) ----

    private void ResolveAndRead(string? appId, int version)
    {
        double volume = 0;
        bool found = false;

        try
        {
            lock (_comGate)
            {
                ReleaseSessionsCore();
                found = ResolveCore(appId);

                if (found)
                {
                    foreach (AudioSessionControl session in _sessions)
                    {
                        try
                        {
                            volume = Math.Max(volume, session.SimpleAudioVolume.Volume);
                        }
                        catch
                        {
                            // sesi sudah hilang, lewati
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "App volume resolve failed");
            found = false;
        }

        lock (_stateGate)
        {
            if (version != _version)
            {
                return; // sudah ada Attach/Detach yang lebih baru
            }
        }

        PostToUi(() =>
        {
            _available = found;
            OnPropertyChanged(nameof(IsAvailable));

            if (found)
            {
                // Nilai dari sistem: dipasang langsung ke field supaya TIDAK dianggap tulisan pengguna.
                _volume = Math.Clamp(volume, 0, 1);
                OnPropertyChanged(nameof(Volume));
                OnPropertyChanged(nameof(IsMuted));
            }
        });
    }

    /// <summary>Mengisi _sessions dengan semua sesi yang cocok. Harus dipanggil di dalam _comGate.</summary>
    private bool ResolveCore(string? appId)
    {
        string? wanted = ProcessNameFromAumid.Guess(appId);
        if (wanted is null || wanted.Length < 3)
        {
            return false;
        }

        _enumerator ??= new MMDeviceEnumerator();
        _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

        AudioSessionManager manager = _device.AudioSessionManager;
        manager.RefreshSessions();
        SessionCollection list = manager.Sessions;

        for (int i = 0; i < list.Count; i++)
        {
            AudioSessionControl session = list[i];
            bool keep = false;

            try
            {
                if (session.IsSystemSoundsSession)
                {
                    continue;
                }

                uint pid = session.GetProcessID;
                if (!_nameCache.TryGetValue(pid, out string? name))
                {
                    try
                    {
                        using Process process = Process.GetProcessById((int)pid);
                        name = process.ProcessName;
                    }
                    catch
                    {
                        name = string.Empty;
                    }

                    _nameCache[pid] = name;
                }

                // Nama terlalu pendek ditolak agar pencocokan longgar tidak salah sasaran.
                if (name.Length >= 3 &&
                    (name.Contains(wanted, StringComparison.OrdinalIgnoreCase) ||
                     wanted.Contains(name, StringComparison.OrdinalIgnoreCase)))
                {
                    _sessions.Add(session);
                    keep = true;
                }
            }
            catch
            {
                // sesi hilang di tengah jalan, lewati
            }
            finally
            {
                if (!keep)
                {
                    DisposeQuietly(session);
                }
            }
        }

        return _sessions.Count > 0;
    }

    // ---- Menulis volume (thread latar, digabung) ----

    private void QueueWrite(double value)
    {
        lock (_stateGate)
        {
            _pending = value;
            if (_applying)
            {
                return; // loop yang sedang berjalan akan mengambil nilai terbaru
            }

            _applying = true;
        }

        _ = Task.Run(ApplyLoop);
    }

    private void ApplyLoop()
    {
        while (true)
        {
            double value;
            string? appId;
            lock (_stateGate)
            {
                if (_pending < 0)
                {
                    _applying = false;
                    return;
                }

                value = _pending;
                _pending = -1;
                appId = _appId;
            }

            try
            {
                lock (_comGate)
                {
                    // Belum ada sesi (misalnya pemutar baru mulai bersuara): coba cari sekali.
                    if (_sessions.Count == 0 && appId is not null)
                    {
                        ResolveCore(appId);
                    }

                    foreach (AudioSessionControl session in _sessions)
                    {
                        try
                        {
                            session.SimpleAudioVolume.Volume = (float)value;
                        }
                        catch
                        {
                            // sesi berakhir: akan ditemukan ulang pada Attach berikutnya
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "App volume write failed");
            }
        }
    }

    // ---- Melepas COM ----

    private void ReleaseSessions()
    {
        lock (_comGate)
        {
            ReleaseSessionsCore();
            DisposeQuietly(_device);
            _device = null;
            DisposeQuietly(_enumerator);
            _enumerator = null;
            _nameCache.Clear();
        }
    }

    /// <summary>Melepas sesi dan perangkat lama. Harus dipanggil di dalam _comGate.</summary>
    private void ReleaseSessionsCore()
    {
        foreach (AudioSessionControl session in _sessions)
        {
            DisposeQuietly(session);
        }

        _sessions.Clear();
        DisposeQuietly(_device);
        _device = null;
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

    private static void PostToUi(Action action)
    {
        Application.Current?.Dispatcher.BeginInvoke(action);
    }

    public void Dispose()
    {
        lock (_stateGate)
        {
            _attached = false;
            _version++;
        }

        ReleaseSessions();
    }
}
