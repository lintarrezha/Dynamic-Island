using System.ComponentModel;

namespace iLand.Interfaces;

/// <summary>
/// Tingkat suara (0..1) dari aplikasi media yang sedang diputar. Hanya membaca peak meter
/// milik Windows, tidak merekam atau menyimpan audio.
/// </summary>
public interface IAudioLevelService : INotifyPropertyChanged
{
    /// <summary>Level 0..1 yang sudah dihaluskan. Selalu diubah di UI thread.</summary>
    double Level { get; }

    /// <summary>True bila meter tidak bisa dibaca (visualizer digambar statis).</summary>
    bool IsUnavailable { get; }

    /// <summary>Nyalakan atau matikan pembacaan. Saat false, tidak ada timer atau thread yang berjalan.</summary>
    void SetActive(bool active, string? sourceAppId);
}
