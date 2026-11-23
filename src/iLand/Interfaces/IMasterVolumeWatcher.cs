namespace iLand.Interfaces;

/// <summary>Perubahan volume master yang sebenarnya (setelah nilai awal dicatat).</summary>
public sealed class MasterVolumeChangedEventArgs : EventArgs
{
    public MasterVolumeChangedEventArgs(double volume, bool isMuted)
    {
        Volume = volume;
        IsMuted = isMuted;
    }

    /// <summary>Volume master 0..1.</summary>
    public double Volume { get; }

    public bool IsMuted { get; }
}

/// <summary>
/// Pendengar ringan volume master dan mute perangkat output default, khusus untuk overlay.
/// Berbeda dari IMixerService yang juga memindai semua sesi aplikasi: di sini hanya satu pendengar event
/// AudioEndpointVolume dan pemberitahuan pergantian perangkat. Tanpa polling dan tanpa timer.
/// Nilai pertama setelah Start (dan setelah perangkat output berganti) hanya dicatat sebagai acuan, TIDAK diumumkan,
/// sehingga overlay tidak muncul saat aplikasi mulai atau saat berpindah speaker/headset.
/// Start dan Stop dipanggil dari UI thread. Changed diangkat di UI thread.
/// </summary>
public interface IMasterVolumeWatcher
{
    event EventHandler<MasterVolumeChangedEventArgs>? Changed;

    /// <summary>Mulai mendengarkan. Aman dipanggil berulang.</summary>
    void Start();

    /// <summary>Berhenti dan melepas semua objek COM. Aman dipanggil berulang.</summary>
    void Stop();
}
