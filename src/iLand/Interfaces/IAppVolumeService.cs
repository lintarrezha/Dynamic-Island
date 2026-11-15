using System.ComponentModel;

namespace iLand.Interfaces;

/// <summary>
/// Volume aplikasi pemutar media (sesi audio Spotify, browser, dan sebagainya), sama seperti slider aplikasi
/// di Volume Mixer Windows. Ini BUKAN volume sistem (master): hanya sesi milik aplikasi yang sedang diputar.
/// </summary>
public interface IAppVolumeService : INotifyPropertyChanged
{
    /// <summary>
    /// Volume 0..1. Dibaca saat Attach, dan DITULIS oleh slider di UI (penulisan diteruskan ke sesi audio
    /// di thread latar, digabung supaya geseran cepat tidak membanjiri Windows).
    /// </summary>
    double Volume { get; set; }

    /// <summary>False bila sesi audio aplikasi tidak ditemukan (slider dimatikan).</summary>
    bool IsAvailable { get; }

    /// <summary>True bila volume nol (ikon speaker berganti ke bisu).</summary>
    bool IsMuted { get; }

    /// <summary>Cari sesi audio milik aplikasi pemutar dan baca volumenya. Aman dipanggil berulang untuk aplikasi yang sama.</summary>
    void Attach(string? sourceAppId);

    /// <summary>Lepaskan sesi audio (objek COM). Dipanggil saat slider tidak terlihat.</summary>
    void Detach();
}
