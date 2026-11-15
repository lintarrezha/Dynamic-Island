using System.Windows.Media;

namespace iLand.Models;

/// <summary>
/// Potret kondisi pemutar media (Spotify, YouTube di browser, dll) pada satu waktu.
/// "record" membuat salinan dengan perubahan kecil menjadi mudah: state with { IsPlaying = true }.
/// </summary>
public sealed record MediaState(
    bool HasSession,
    string Title,
    string Artist,
    string Album,
    string SourceApp,
    bool IsPlaying,
    bool CanPlayPause,
    bool CanNext,
    bool CanPrevious,
    TimeSpan Position,
    TimeSpan Duration,
    DateTimeOffset PositionUpdatedAt,
    double PlaybackRate,
    ImageSource? Artwork,
    bool CanSeek = false,
    string SourceAppId = "")
{
    /// <summary>Tidak ada pemutar media yang aktif.</summary>
    public static MediaState None { get; } = new(
        false, string.Empty, string.Empty, string.Empty, string.Empty,
        false, false, false, false,
        TimeSpan.Zero, TimeSpan.Zero, DateTimeOffset.MinValue, 1.0, null);

    /// <summary>
    /// Perkiraan posisi lagu SAAT INI. Windows hanya memberi tahu posisi pada saat tertentu
    /// (PositionUpdatedAt), jadi saat lagu diputar kita tambahkan waktu yang sudah berlalu.
    /// Dengan cara ini progress bar bergerak mulus tanpa menanyai pemutar setiap detik.
    /// </summary>
    public TimeSpan GetEstimatedPosition()
    {
        if (!HasSession)
        {
            return TimeSpan.Zero;
        }

        TimeSpan position = Position;

        if (IsPlaying && PositionUpdatedAt != DateTimeOffset.MinValue)
        {
            TimeSpan elapsed = DateTimeOffset.UtcNow - PositionUpdatedAt;
            if (elapsed > TimeSpan.Zero)
            {
                position += TimeSpan.FromSeconds(elapsed.TotalSeconds * PlaybackRate);
            }
        }

        if (Duration > TimeSpan.Zero && position > Duration)
        {
            position = Duration;
        }

        return position < TimeSpan.Zero ? TimeSpan.Zero : position;
    }
}
