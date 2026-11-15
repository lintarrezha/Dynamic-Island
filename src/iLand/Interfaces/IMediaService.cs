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

    Task NextTrackAsync();

    Task PreviousTrackAsync();

    /// <summary>Loncat ke posisi lagu tertentu. Hanya berefek jika MediaState.CanSeek bernilai true.</summary>
    Task SeekAsync(TimeSpan position);
}
