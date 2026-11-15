using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using NAudio.CoreAudioApi;
using Serilog;
using iLand.Helpers;
using iLand.Interfaces;

namespace iLand.Services;

/// <summary>
/// Membaca peak meter WASAPI per sesi audio (sama seperti Volume Mixer Windows).
/// Semua akses COM terjadi di satu worker thread. Worker hanya hidup saat SetActive(true),
/// berhenti sendiri setelah level meluruh ke 0, lalu melepas objek COM.
/// </summary>
public sealed class AudioLevelService : ObservableObject, IAudioLevelService, IDisposable
{
    private const int TickMs = 50;
    private const int ResolveEveryTicks = 60;   // sekitar 3 detik

    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _worker;
    private bool _active;
    private string? _sourceAppId;
    private int _generation;

    private double _level;
    private bool _unavailable;

    public double Level => _level;
    public bool IsUnavailable => _unavailable;

    public void SetActive(bool active, string? sourceAppId)
    {
        lock (_gate)
        {
            _active = active;
            if (!string.Equals(_sourceAppId, sourceAppId, StringComparison.Ordinal))
            {
                _sourceAppId = sourceAppId;
                _generation++;   // worker re-resolve sesi di tick berikutnya
            }

            if (active && _worker is null)
            {
                _cts?.Dispose(); // worker sebelumnya sudah selesai, pembatal lamanya tidak dipakai lagi
                _cts = new CancellationTokenSource();
                var token = _cts.Token;
                _worker = Task.Run(() => RunAsync(token));
            }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        MMDeviceEnumerator? enumerator = null;
        MMDevice? device = null;
        var sessions = new List<(string Name, AudioSessionControl Session)>();
        var nameCache = new Dictionary<uint, string>();
        double smoothed = 0;
        int tick = ResolveEveryTicks;   // paksa resolve di awal
        int seenGeneration = -1;

        try
        {
            enumerator = new MMDeviceEnumerator();
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(TickMs));

            while (await timer.WaitForNextTickAsync(ct))
            {
                bool active;
                string? app;
                int generation;
                lock (_gate)
                {
                    active = _active;
                    app = _sourceAppId;
                    generation = _generation;
                }

                if (tick >= ResolveEveryTicks || generation != seenGeneration)
                {
                    tick = 0;
                    seenGeneration = generation;
                    DisposeAll(sessions);
                    DisposeObj(device);
                    device = null;
                    if (active)
                    {
                        device = Resolve(enumerator, app, sessions, nameCache);
                    }
                }

                tick++;

                double target = 0;
                if (active && device is not null)
                {
                    target = ReadPeak(device, sessions);
                    SetUnavailable(false);
                }
                else if (active)
                {
                    SetUnavailable(true);
                }

                // Attack cepat, release lebih lambat supaya batang tidak berkedip.
                double alpha = target > smoothed ? 0.55 : 0.2;
                smoothed += (target - smoothed) * alpha;

                if (smoothed < 0.01)
                {
                    smoothed = 0;
                }

                Post(smoothed);

                if (!active && smoothed == 0)
                {
                    break;   // sudah diam, hentikan worker
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Audio level worker stopped");
            SetUnavailable(true);
        }
        finally
        {
            DisposeAll(sessions);
            DisposeObj(device);
            DisposeObj(enumerator);
            Post(0);
            lock (_gate)
            {
                _worker = null;
                // Bila SetActive(true) datang tepat saat worker selesai, mulai lagi.
                if (_active && _cts is { IsCancellationRequested: false })
                {
                    var token = _cts.Token;
                    _worker = Task.Run(() => RunAsync(token));
                }
            }
        }
    }

    private static MMDevice? Resolve(
        MMDeviceEnumerator enumerator,
        string? appId,
        List<(string Name, AudioSessionControl Session)> sessions,
        Dictionary<uint, string> nameCache)
    {
        MMDevice? device = null;
        try
        {
            device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var wanted = ProcessNameFromAumid.Guess(appId);
            if (wanted is null)
            {
                return device;
            }

            var manager = device.AudioSessionManager;
            manager.RefreshSessions();
            var list = manager.Sessions;
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                bool keep = false;
                try
                {
                    if (s.IsSystemSoundsSession)
                    {
                        continue;
                    }

                    var pid = s.GetProcessID;
                    if (!nameCache.TryGetValue(pid, out var name))
                    {
                        try
                        {
                            using Process process = Process.GetProcessById((int)pid); // dilepas setelah nama dibaca
                            name = process.ProcessName;
                        }
                        catch
                        {
                            name = string.Empty;
                        }

                        nameCache[pid] = name;
                    }

                    // Nama terlalu pendek (kurang dari 3 huruf) ditolak agar pencocokan longgar tidak salah sasaran.
                    if (name.Length >= 3 && wanted.Length >= 3 &&
                        (name.Contains(wanted, StringComparison.OrdinalIgnoreCase) ||
                         wanted.Contains(name, StringComparison.OrdinalIgnoreCase)))
                    {
                        sessions.Add((name, s));
                        keep = true;
                    }
                }
                catch
                {
                    // sesi sudah hilang, lewati
                }
                finally
                {
                    if (!keep)
                    {
                        DisposeObj(s);
                    }
                }
            }

            return device;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Audio session resolve failed");
            DisposeObj(device);
            return null;
        }
    }

    private static double ReadPeak(MMDevice device, List<(string Name, AudioSessionControl Session)> sessions)
    {
        try
        {
            float peak = 0;
            if (sessions.Count > 0)
            {
                foreach (var (_, s) in sessions)
                {
                    try
                    {
                        peak = Math.Max(peak, s.AudioMeterInformation.MasterPeakValue);
                    }
                    catch
                    {
                        // sesi berakhir, akan diganti saat resolve berikutnya
                    }
                }
            }
            else
            {
                // Aplikasi tidak ketemu: pakai peak keseluruhan perangkat.
                peak = device.AudioMeterInformation.MasterPeakValue;
            }

            return Math.Clamp(peak, 0f, 1f);
        }
        catch
        {
            return 0;
        }
    }

    private void Post(double value)
    {
        if (Math.Abs(value - _level) < 0.005 && value != 0 && _level != 0)
        {
            return;
        }

        _level = value;
        var dispatcher = Application.Current?.Dispatcher;
        dispatcher?.BeginInvoke((Action)(() => OnPropertyChanged(nameof(Level))));
    }

    private void SetUnavailable(bool value)
    {
        if (_unavailable == value)
        {
            return;
        }

        _unavailable = value;
        Application.Current?.Dispatcher.BeginInvoke((Action)(() => OnPropertyChanged(nameof(IsUnavailable))));
    }

    private static void DisposeAll(List<(string Name, AudioSessionControl Session)> sessions)
    {
        foreach (var (_, s) in sessions)
        {
            DisposeObj(s);
        }

        sessions.Clear();
    }

    private static void DisposeObj(object? o)
    {
        try
        {
            (o as IDisposable)?.Dispose();
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _active = false;
            _cts?.Cancel();
        }
    }
}
