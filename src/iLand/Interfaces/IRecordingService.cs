namespace iLand.Interfaces;

/// <summary>
/// Perekam layar. Merekam seluruh layar pada monitor tempat kursor berada menjadi file MP4
/// di folder Videos\Captures, dengan suara sistem dan atau mikrofon sesuai pengaturan.
/// </summary>
public interface IRecordingService
{
    /// <summary>Dipanggil setiap status perekaman berubah (mulai atau berhenti). Selalu di UI thread.</summary>
    event EventHandler? StateChanged;

    /// <summary>True selama perekaman sedang berjalan.</summary>
    bool IsRecording { get; }

    /// <summary>Lama perekaman sejak dimulai. Nol jika tidak sedang merekam.</summary>
    TimeSpan Elapsed { get; }

    /// <summary>True selama mikrofon ikut direkam. Dipakai island untuk menampilkan ikon mikrofon.</summary>
    bool IsMicrophoneActive { get; }

    /// <summary>Mulai merekam jika belum, atau berhenti jika sedang merekam.</summary>
    Task ToggleAsync();

    /// <summary>Mulai merekam. Tidak melakukan apa-apa jika sudah merekam.</summary>
    Task StartAsync();

    /// <summary>Berhenti merekam dan menyimpan file. Tidak melakukan apa-apa jika tidak sedang merekam.</summary>
    Task StopAsync();
}
