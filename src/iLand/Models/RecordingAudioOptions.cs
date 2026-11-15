namespace iLand.Models;

/// <summary>Suara apa saja yang ikut direkam bersama video.</summary>
/// <param name="SystemAudio">Suara yang keluar dari speaker atau headphone (semua aplikasi).</param>
/// <param name="Microphone">Suara dari mikrofon bawaan Windows.</param>
public readonly record struct RecordingAudioOptions(bool SystemAudio, bool Microphone)
{
    /// <summary>True jika ada setidaknya satu sumber suara yang diminta.</summary>
    public bool Any => SystemAudio || Microphone;

    /// <summary>Hanya gambar, tanpa suara.</summary>
    public static RecordingAudioOptions None => new(false, false);
}
