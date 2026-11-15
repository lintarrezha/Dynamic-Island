using iLand.Models;

namespace iLand.Interfaces;

public interface ILyricsService
{
    /// <summary>Nilai AppSettings.LyricsEnabled saat ini. Jika false, tidak ada permintaan jaringan dan tidak ada akses cache.</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Mencari lirik sebuah lagu: cache lokal dulu, lalu LRCLIB. Tidak pernah memblokir UI.
    /// Jika cancellationToken dibatalkan (misalnya lagu berganti), OperationCanceledException dilempar,
    /// pemanggil cukup mengabaikannya. Kegagalan lain (tanpa internet, waktu habis 5 detik) dikembalikan
    /// sebagai LyricsStatus.Error, bukan exception.
    /// </summary>
    Task<LyricsResult> GetLyricsAsync(
        string title,
        string artist,
        string album,
        TimeSpan duration,
        CancellationToken cancellationToken);
}
