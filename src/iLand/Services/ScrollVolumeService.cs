using NAudio.CoreAudioApi;
using Serilog;
using iLand.Interfaces;

namespace iLand.Services;

/// <summary>
/// Mengubah volume master lewat NAudio.CoreAudioApi (AudioEndpointVolume), seperti MasterVolumeWatcher tetapi hanya menulis.
/// - Tidak ada objek COM atau event yang dipegang antar scroll: setiap pekerjaan membuka perangkat output default, mengubah
///   volume, dan melepasnya lagi. Saat tidak ada yang scroll, layanan ini tidak memakai apa pun.
/// - Scroll beruntun digabung: hanya satu pekerjaan yang mengantre sekaligus, dan semua delta yang terkumpul dipakai sekali.
/// - Naik saat sedang mute: mute dilepas. Turun: mute tidak diubah.
/// </summary>
public sealed class ScrollVolumeService : IScrollVolumeService
{
    private const double PercentPerNotch = 2.0;

    private readonly IOverlayService _overlayService;

    private readonly TaskFactory _worker = new(
        CancellationToken.None,
        TaskCreationOptions.DenyChildAttach,
        TaskContinuationOptions.None,
        new ConcurrentExclusiveSchedulerPair().ExclusiveScheduler);

    private readonly object _lock = new();
    private double _pendingPercent;   // dilindungi _lock
    private bool _drainQueued;        // dilindungi _lock
    private double _remainder;        // hanya diakses thread latar (penjadwal eksklusif)

    public ScrollVolumeService(IOverlayService overlayService)
    {
        _overlayService = overlayService;
    }

    public void Adjust(int wheelDelta)
    {
        if (wheelDelta == 0)
        {
            return;
        }

        bool shouldQueue;
        lock (_lock)
        {
            _pendingPercent += wheelDelta / 120.0 * PercentPerNotch;
            shouldQueue = !_drainQueued;
            _drainQueued = true;
        }

        if (shouldQueue)
        {
            _worker.StartNew(Drain);
        }
    }

    private void Drain()
    {
        double percent;
        lock (_lock)
        {
            percent = _pendingPercent;
            _pendingPercent = 0;
            _drainQueued = false;
        }

        // Roda presisi tinggi mengirim delta kecil: kumpulkan sampai genap 1 persen.
        _remainder += percent;
        int whole = (int)Math.Truncate(_remainder);
        if (whole == 0)
        {
            return;
        }

        _remainder -= whole;

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using MMDevice device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            using AudioEndpointVolume endpoint = device.AudioEndpointVolume;

            bool muted = endpoint.Mute;
            double current = Math.Round(endpoint.MasterVolumeLevelScalar * 100.0);

            if (whole > 0 && muted)
            {
                endpoint.Mute = false;
                muted = false;
            }

            double next = Math.Clamp(current + whole, 0, 100);
            endpoint.MasterVolumeLevelScalar = (float)(next / 100.0);

            _overlayService.Show(LevelOverlayService.CreateVolumeRequest(next / 100.0, muted));
        }
        catch (Exception ex)
        {
            // Tidak ada perangkat output atau akses ditolak: diam.
            Log.Debug(ex, "Scroll volume failed");
        }
    }
}
