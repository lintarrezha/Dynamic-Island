namespace iLand.Models;

/// <summary>
/// Hasil pencarian lirik. "Memuat" sengaja tidak ada di sini: itu keadaan tampilan yang diatur ViewModel
/// selama menunggu hasil.
/// </summary>
public enum LyricsStatus
{
    /// <summary>Fitur lirik dimatikan di Settings. Tidak ada permintaan jaringan.</summary>
    Disabled,

    /// <summary>Lirik tersinkron ditemukan (Lines terisi).</summary>
    Synced,

    /// <summary>Hanya lirik biasa tanpa waktu (PlainText terisi). Ditampilkan statis.</summary>
    PlainOnly,

    /// <summary>Lagu ditandai instrumental dan tidak ada lirik.</summary>
    Instrumental,

    /// <summary>Tidak ditemukan di LRCLIB.</summary>
    NotFound,

    /// <summary>Gagal (tanpa internet, waktu habis, server bermasalah). Hasil ini tidak disimpan di cache.</summary>
    Error
}

/// <summary>Hasil pencarian lirik untuk satu lagu.</summary>
public sealed record LyricsResult(LyricsStatus Status, IReadOnlyList<LyricLine> Lines, string PlainText)
{
    public static LyricsResult Disabled { get; } = new(LyricsStatus.Disabled, Array.Empty<LyricLine>(), string.Empty);

    public static LyricsResult NotFound { get; } = new(LyricsStatus.NotFound, Array.Empty<LyricLine>(), string.Empty);

    public static LyricsResult Instrumental { get; } = new(LyricsStatus.Instrumental, Array.Empty<LyricLine>(), string.Empty);

    public static LyricsResult Error { get; } = new(LyricsStatus.Error, Array.Empty<LyricLine>(), string.Empty);

    /// <summary>True jika ada lirik yang bisa ditampilkan (tersinkron atau biasa).</summary>
    public bool HasLyrics => Status is LyricsStatus.Synced or LyricsStatus.PlainOnly;
}
