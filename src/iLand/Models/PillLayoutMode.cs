namespace iLand.Models;

/// <summary>Isi pill saat island tertutup (mode normal).</summary>
public enum PillLayoutMode
{
    /// <summary>Tidak ada musik: jam (atau sisa timer) di kiri, cincin baterai + persen di kanan.</summary>
    Idle,

    /// <summary>Musik diputar, lirik tidak tampil: cincin baterai di kiri, sampul + visualizer di kanan.</summary>
    Media,

    /// <summary>Musik diputar dan lirik aktif: sampul di kiri, baris lirik di tengah, visualizer di kanan.</summary>
    MediaLyrics
}
