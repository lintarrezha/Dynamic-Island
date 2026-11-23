using iLand.Models;

namespace iLand.Interfaces;

public interface IMediaService
{
    /// <summary>Kondisi pemutar media terbaru.</summary>
    MediaState Current { get; }

    /// <summary>Dipanggil saat lagu, status putar, atau posisi berubah.</summary>
    event EventHandler? Changed;

    /// <summary>Mulai mendengarkan sesi media Windows. Panggil sekali saat aplikasi mulai.</summary>
    Task InitializeAsync();

    /// <summary>Play jika sedang pause, pause jika sedang play.</summary>
    Task TogglePlayPauseAsync();

    /// <summary>Menjeda pemutaran (SMTC TryPauseAsync). False bila tidak ada sesi atau pemutar menolak.</summary>
    Task<bool> PauseAsync();

    /// <summary>Melanjutkan pemutaran (SMTC TryPlayAsync). False bila tidak ada sesi atau pemutar menolak.</summary>
    Task<bool> PlayAsync();

    Task NextTrackAsync();

    Task PreviousTrackAsync();

    /// <summary>Loncat ke posisi lagu tertentu. Hanya berefek jika MediaState.CanSeek bernilai true.</summary>
    Task SeekAsync(TimeSpan position);
}
