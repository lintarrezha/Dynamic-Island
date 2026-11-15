using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using iLand.Interfaces;
using iLand.Models;
using Microsoft.Extensions.Logging;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace iLand.Services;

/// <summary>
/// Terhubung ke Windows Media Session (GlobalSystemMediaTransportControls), yaitu API resmi
/// yang sama dengan panel media di Windows. Semua aplikasi yang tampil di panel itu
/// (Spotify, YouTube/Chrome/Edge, pemutar video, dll) otomatis didukung.
///
/// Event-driven: Windows memberi tahu saat lagu atau status berubah, jadi tidak ada polling.
/// Catatan: API ini tidak menyediakan kontrol volume per aplikasi, jadi volume belum didukung.
/// </summary>
public sealed class MediaService : IMediaService, IDisposable
{
    // Jika thumbnail belum tersedia saat lagu berganti, coba ambil lagi maksimal sekian kali.
    private const int MaxArtworkAttempts = 3;

    private readonly ILogger<MediaService> _logger;
    private readonly Dispatcher _uiDispatcher;

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _currentSession;

    // Mencegah dua pembacaan berjalan bersamaan. Semua kode ini berjalan di UI thread.
    private bool _isRefreshing;
    private bool _refreshAgain;

    // Cover lagu disimpan agar tidak diambil ulang setiap kali status berubah.
    private string _artworkKey = string.Empty;
    private ImageSource? _artwork;
    private int _artworkAttempts;

    public MediaState Current { get; private set; } = MediaState.None;

    public event EventHandler? Changed;

    public MediaService(ILogger<MediaService> logger)
    {
        _logger = logger;
        _uiDispatcher = Dispatcher.CurrentDispatcher; // service ini dibuat di UI thread
    }

    public async Task InitializeAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += OnCurrentSessionChanged;

            AttachToSession(_manager.GetCurrentSession());
            await RefreshAsync();

            _logger.LogInformation("Media session manager ready");
        }
        catch (Exception ex)
        {
            // Tidak fatal: iLand tetap berjalan, hanya tanpa fitur musik.
            _logger.LogWarning(ex, "Media session manager is not available");
        }
    }

    // ---- Kontrol pemutar ----

    public async Task TogglePlayPauseAsync()
    {
        var session = _currentSession;
        if (session is null)
        {
            return;
        }

        try
        {
            await session.TryTogglePlayPauseAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to toggle play/pause");
        }
    }

    public async Task NextTrackAsync()
    {
        var session = _currentSession;
        if (session is null)
        {
            return;
        }

        try
        {
            await session.TrySkipNextAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to skip to next track");
        }
    }

    public async Task PreviousTrackAsync()
    {
        var session = _currentSession;
        if (session is null)
        {
            return;
        }

        try
        {
            await session.TrySkipPreviousAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to skip to previous track");
        }
    }

    public async Task SeekAsync(TimeSpan position)
    {
        var session = _currentSession;
        if (session is null || !Current.CanSeek)
        {
            return;
        }

        try
        {
            var timeline = session.GetTimelineProperties();
            TimeSpan duration = GetDuration(timeline);

            if (position < TimeSpan.Zero)
            {
                position = TimeSpan.Zero;
            }

            if (duration > TimeSpan.Zero && position > duration)
            {
                position = duration;
            }

            // Posisi dari Windows dihitung mulai StartTime (lihat GetPosition), jadi StartTime ditambahkan lagi di sini.
            long ticks = (timeline.StartTime + position).Ticks;
            bool accepted = await session.TryChangePlaybackPositionAsync(ticks);

            // Perbarui posisi seketika agar bar tidak melompat balik sambil menunggu laporan dari pemutar.
            if (accepted && session == _currentSession)
            {
                SetState(Current with { Position = position, PositionUpdatedAt = DateTimeOffset.UtcNow });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to seek");
        }
    }

    // ---- Berpindah sesi (misalnya dari Spotify ke YouTube) ----

    private void OnCurrentSessionChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        CurrentSessionChangedEventArgs args)
    {
        // Event WinRT datang dari thread lain, jadi pindahkan ke UI thread.
        _uiDispatcher.BeginInvoke(new Action(() =>
        {
            if (_manager is null)
            {
                return;
            }

            AttachToSession(_manager.GetCurrentSession());
            _ = RefreshAsync();
        }));
    }

    private void AttachToSession(GlobalSystemMediaTransportControlsSession? session)
    {
        DetachFromSession();

        _currentSession = session;
        if (session is null)
        {
            return;
        }

        session.MediaPropertiesChanged += OnMediaPropertiesChanged;
        session.PlaybackInfoChanged += OnPlaybackInfoChanged;
        session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
    }

    private void DetachFromSession()
    {
        if (_currentSession is null)
        {
            return;
        }

        _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
        _currentSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        _currentSession.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
        _currentSession = null;
    }

    private void OnMediaPropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        _uiDispatcher.BeginInvoke(new Action(() =>
        {
            if (sender == _currentSession)
            {
                _ = RefreshAsync();
            }
        }));
    }

    private void OnPlaybackInfoChanged(
        GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
    {
        _uiDispatcher.BeginInvoke(new Action(() =>
        {
            if (sender == _currentSession)
            {
                _ = RefreshAsync();
            }
        }));
    }

    private void OnTimelinePropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
    {
        _uiDispatcher.BeginInvoke(new Action(() =>
        {
            if (sender == _currentSession)
            {
                RefreshTimelineOnly();
            }
        }));
    }

    // ---- Membaca kondisi pemutar ----

    private async Task RefreshAsync()
    {
        if (_isRefreshing)
        {
            _refreshAgain = true; // ada perubahan baru saat membaca: ulangi setelah selesai
            return;
        }

        _isRefreshing = true;
        try
        {
            do
            {
                _refreshAgain = false;
                SetState(await ReadFullStateAsync());
            }
            while (_refreshAgain);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read media state");
            SetState(MediaState.None);
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private async Task<MediaState> ReadFullStateAsync()
    {
        var session = _currentSession;
        if (session is null)
        {
            return MediaState.None;
        }

        var properties = await session.TryGetMediaPropertiesAsync();
        var playback = session.GetPlaybackInfo();
        var timeline = session.GetTimelineProperties();

        string title = properties.Title ?? string.Empty;
        string artist = properties.Artist ?? string.Empty;
        string album = properties.AlbumTitle ?? string.Empty;
        string appId = session.SourceAppUserModelId ?? string.Empty;

        // Ambil cover hanya saat lagu berganti (atau jika sebelumnya belum tersedia).
        string artworkKey = $"{appId}|{title}|{artist}|{album}";
        if (artworkKey != _artworkKey)
        {
            _artworkKey = artworkKey;
            _artwork = null;
            _artworkAttempts = 0;
        }

        if (_artwork is null && _artworkAttempts < MaxArtworkAttempts)
        {
            _artworkAttempts++;
            _artwork = await LoadArtworkAsync(properties.Thumbnail);
        }

        var controls = playback.Controls;

        return new MediaState(
            HasSession: true,
            Title: title,
            Artist: artist,
            Album: album,
            SourceApp: GetFriendlyAppName(appId),
            IsPlaying: IsPlaying(playback),
            CanPlayPause: controls.IsPlayEnabled || controls.IsPauseEnabled,
            CanNext: controls.IsNextEnabled,
            CanPrevious: controls.IsPreviousEnabled,
            Position: GetPosition(timeline),
            Duration: GetDuration(timeline),
            PositionUpdatedAt: timeline.LastUpdatedTime,
            PlaybackRate: playback.PlaybackRate ?? 1.0,
            Artwork: _artwork,
            CanSeek: controls.IsPlaybackPositionEnabled && GetDuration(timeline) > TimeSpan.Zero,
            SourceAppId: appId);
    }

    /// <summary>
    /// Pembaruan ringan khusus posisi dan status putar. Dibuat terpisah dari ReadFullStateAsync
    /// karena event posisi bisa sering datang dan kita tidak perlu mengambil judul/cover ulang.
    /// </summary>
    private void RefreshTimelineOnly()
    {
        var session = _currentSession;
        if (session is null || !Current.HasSession)
        {
            return;
        }

        try
        {
            var timeline = session.GetTimelineProperties();
            var playback = session.GetPlaybackInfo();

            SetState(Current with
            {
                IsPlaying = IsPlaying(playback),
                Position = GetPosition(timeline),
                Duration = GetDuration(timeline),
                PositionUpdatedAt = timeline.LastUpdatedTime,
                PlaybackRate = playback.PlaybackRate ?? 1.0,
                CanSeek = playback.Controls.IsPlaybackPositionEnabled && GetDuration(timeline) > TimeSpan.Zero
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read media timeline");
        }
    }

    private void SetState(MediaState state)
    {
        Current = state;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // ---- Fungsi bantu ----

    private static bool IsPlaying(GlobalSystemMediaTransportControlsSessionPlaybackInfo playback)
    {
        return playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
    }

    private static TimeSpan GetPosition(GlobalSystemMediaTransportControlsSessionTimelineProperties timeline)
    {
        TimeSpan position = timeline.Position - timeline.StartTime;
        return position < TimeSpan.Zero ? TimeSpan.Zero : position;
    }

    private static TimeSpan GetDuration(GlobalSystemMediaTransportControlsSessionTimelineProperties timeline)
    {
        TimeSpan duration = timeline.EndTime - timeline.StartTime;
        return duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
    }

    /// <summary>Mengubah id aplikasi dari Windows (misalnya "Spotify.exe") menjadi nama yang enak dibaca.</summary>
    private static string GetFriendlyAppName(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId))
        {
            return "Media";
        }

        string name = appId;

        // Aplikasi Microsoft Store memakai format "NamaPaket!NamaAplikasi".
        int separatorIndex = name.IndexOf('!');
        if (separatorIndex >= 0 && separatorIndex < name.Length - 1)
        {
            name = name[(separatorIndex + 1)..];
        }

        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        return name.Length == 0 ? "Media" : char.ToUpperInvariant(name[0]) + name[1..];
    }

    /// <summary>Mengubah thumbnail dari Windows menjadi gambar WPF yang kecil dan siap dipakai UI.</summary>
    private async Task<ImageSource?> LoadArtworkAsync(IRandomAccessStreamReference? thumbnail)
    {
        if (thumbnail is null)
        {
            return null;
        }

        try
        {
            using IRandomAccessStreamWithContentType winRtStream = await thumbnail.OpenReadAsync();
            using Stream stream = winRtStream.AsStreamForRead();

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad; // baca semua sekarang, lalu stream boleh ditutup
            image.DecodePixelWidth = 224;                 // sampul tampil 112 px: 224 px tetap tajam di layar DPI tinggi, hanya sekitar 200 KB
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze(); // gambar yang dibekukan lebih ringan dan aman dipakai antar thread
            return image;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not load media artwork");
            return null;
        }
    }

    // Berhenti mendengarkan event agar tidak terjadi memory leak.
    public void Dispose()
    {
        DetachFromSession();

        if (_manager is not null)
        {
            _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
            _manager = null;
        }
    }
}
