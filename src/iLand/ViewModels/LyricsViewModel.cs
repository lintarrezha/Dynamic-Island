using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iLand.Helpers;
using iLand.Interfaces;
using iLand.Models;
using Microsoft.Extensions.Logging;

namespace iLand.ViewModels;

/// <summary>Keadaan tampilan panel lirik.</summary>
public enum LyricsViewState
{
    /// <summary>Belum ada yang dimuat untuk lagu ini.</summary>
    None,
    Loading,
    Synced,
    Plain,
    NotFound,
    Instrumental,
    Error
}

/// <summary>
/// Otak panel lirik di tab Music: tombol lirik, konfirmasi menyalakan fitur, memuat lirik, dan baris aktif.
///
/// Privasi dan performa:
/// - Tidak ada permintaan jaringan sebelum pengguna menyalakan lirik dan menekan tombol lirik.
/// - Lirik hanya dimuat saat panel lirik terbuka. Lagu berganti saat panel tertutup hanya mengosongkan hasil lama.
/// - Timer 100 ms (maksimal 10 pembaruan per detik) dibuat saat pertama dibutuhkan dan hanya berjalan selama
///   panel terbuka, island terbuka di tab Music, lirik tersinkron, dan lagu sedang diputar.
/// </summary>
public partial class LyricsViewModel : ObservableObject, IDisposable
{
    // Jeda sebelum pencarian dimulai. Saat lagu dilewati cepat-cepat, hanya lagu terakhir yang dicari.
    private const int LoadDelayMs = 400;

    private const int PositionIntervalMs = 100;

    private readonly ILyricsService _lyricsService;
    private readonly IMediaService _mediaService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<LyricsViewModel> _logger;

    private DispatcherTimer? _positionTimer;
    private bool _isPillVisible;
    private CancellationTokenSource? _loadCts;

    // Daftar baris untuk pencarian posisi (binary search). Sejajar dengan Lines.
    private IReadOnlyList<LyricLine> _syncedLines = Array.Empty<LyricLine>();

    // Identitas lagu yang sedang diputar, untuk mengetahui kapan lagu berganti.
    private string _trackKey = string.Empty;

    // True saat island terbuka di tab Music (diisi IslandViewModel).
    private bool _isContextVisible;

    /// <summary>False saat island menutup atau tab Music tidak terlihat. Panel lirik memakai ini untuk berhenti ikut layout selama animasi tutup.</summary>
    public bool IsContextVisible => _isContextVisible;

    /// <summary>True = panel lirik terbuka (island melebar, lirik di sisi kanan).</summary>
    [ObservableProperty]
    private bool _isOpen;

    /// <summary>True = kartu "Turn on lyrics?" tampil di dalam island.</summary>
    [ObservableProperty]
    private bool _isConfirmVisible;

    /// <summary>True = menu geser waktu lirik (titik tiga di gaya Glass) terbuka.</summary>
    [ObservableProperty]
    private bool _isTimingMenuOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowStatus))]
    [NotifyPropertyChangedFor(nameof(ShowSynced))]
    [NotifyPropertyChangedFor(nameof(ShowPlain))]
    private LyricsViewState _state = LyricsViewState.None;

    /// <summary>Teks keadaan: "Loading lyrics...", "No lyrics found", dan sebagainya.</summary>
    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>Baris lirik tersinkron. Diganti seluruhnya (bukan diubah satu per satu) setiap lagu baru.</summary>
    [ObservableProperty]
    private IReadOnlyList<LyricLineViewModel> _lines = Array.Empty<LyricLineViewModel>();

    /// <summary>Lirik biasa tanpa waktu (jika LRCLIB hanya punya itu).</summary>
    [ObservableProperty]
    private string _plainText = string.Empty;

    /// <summary>Indeks baris yang sedang dinyanyikan. -1 = belum sampai baris pertama.</summary>
    [ObservableProperty]
    private int _activeIndex = -1;

    /// <summary>True jika pemutar mengizinkan loncat posisi (klik baris lirik = loncat ke waktunya).</summary>
    [ObservableProperty]
    private bool _canSeek;

    public bool ShowStatus => State is LyricsViewState.Loading or LyricsViewState.NotFound
        or LyricsViewState.Instrumental or LyricsViewState.Error;

    public bool ShowSynced => State == LyricsViewState.Synced;

    public bool ShowPlain => State == LyricsViewState.Plain;

    // ---- Geser waktu lirik (stepper di tab Music) ----

    private const int OffsetStepMs = 500;
    private const int OffsetLimitMs = 2000;

    private TimeSpan LyricsOffset => TimeSpan.FromMilliseconds(_settingsService.Current.LyricsOffsetMs);

    /// <summary>Teks stepper: "0.0s", "+0.5s", "-1.5s".</summary>
    public string OffsetText
    {
        get
        {
            double seconds = _settingsService.Current.LyricsOffsetMs / 1000.0;
            return seconds == 0
                ? "0.0s"
                : seconds.ToString("+0.0;-0.0", System.Globalization.CultureInfo.InvariantCulture) + "s";
        }
    }

    /// <summary>Baris lirik yang sedang dinyanyikan (dipakai pill saat island tertutup). Kosong bila belum ada.</summary>
    public string CurrentLineText
    {
        get
        {
            IReadOnlyList<LyricLineViewModel> lines = Lines;
            return State == LyricsViewState.Synced && ActiveIndex >= 0 && ActiveIndex < lines.Count
                ? lines[ActiveIndex].Text
                : string.Empty;
        }
    }

    /// <summary>Tombol "-": lirik tampil lebih lambat 0,5 detik (batas -5 detik).</summary>
    [RelayCommand]
    private void OffsetDown() => ChangeOffset(-OffsetStepMs);

    /// <summary>Tombol "+": lirik tampil lebih cepat 0,5 detik (batas +5 detik).</summary>
    [RelayCommand]
    private void OffsetUp() => ChangeOffset(OffsetStepMs);

    /// <summary>Tombol "Reset" di menu timing (gaya Glass): kembali ke 0,0 detik.</summary>
    [RelayCommand]
    private void OffsetReset()
    {
        if (_settingsService.Current.LyricsOffsetMs == 0)
        {
            return;
        }

        _settingsService.Current.LyricsOffsetMs = 0;
        _settingsService.NotifyChanged();
    }

    [RelayCommand]
    private void ToggleTimingMenu() => IsTimingMenuOpen = !IsTimingMenuOpen;

    public void CloseTimingMenu() => IsTimingMenuOpen = false;

    /// <summary>
    /// Gaya Glass: membuka panel lirik otomatis, tetapi HANYA bila lirik sudah dinyalakan di Settings.
    /// Bila belum, tidak ada yang dimuat dan tidak ada permintaan jaringan (kartu menawarkan "Turn on lyrics").
    /// </summary>
    public void OpenIfEnabled()
    {
        if (IsOpen || !_settingsService.Current.LyricsEnabled)
        {
            return;
        }

        Open();
    }

    /// <summary>Menutup panel lirik (dipakai saat pengguna kembali ke gaya Classic).</summary>
    public void CloseLyricsPanel()
    {
        IsOpen = false;
        IsTimingMenuOpen = false;
    }

    private void ChangeOffset(int deltaMs)
    {
        int next = Math.Clamp(_settingsService.Current.LyricsOffsetMs + deltaMs, -OffsetLimitMs, OffsetLimitMs);
        if (next == _settingsService.Current.LyricsOffsetMs)
        {
            return;
        }

        _settingsService.Current.LyricsOffsetMs = next;
        _settingsService.NotifyChanged(); // memicu OnSettingsChanged: teks stepper dan baris aktif diperbarui
    }

    public LyricsViewModel(
        ILyricsService lyricsService,
        IMediaService mediaService,
        ISettingsService settingsService,
        ILogger<LyricsViewModel> logger)
    {
        _lyricsService = lyricsService;
        _mediaService = mediaService;
        _settingsService = settingsService;
        _logger = logger;

        MediaState media = _mediaService.Current;
        _trackKey = media.HasSession ? BuildKey(media) : string.Empty;
        CanSeek = media.CanSeek;

        _mediaService.Changed += OnMediaChanged;
        _settingsService.SettingsChanged += OnSettingsChanged;
    }

    // ---- Hook dari [ObservableProperty] ----

    partial void OnIsOpenChanged(bool value) => UpdateTimer();

    partial void OnActiveIndexChanged(int value) => OnPropertyChanged(nameof(CurrentLineText));

    partial void OnLinesChanged(IReadOnlyList<LyricLineViewModel> value) => OnPropertyChanged(nameof(CurrentLineText));

    partial void OnStateChanged(LyricsViewState value) => UpdateTimer();

    // ---- Dipanggil IslandViewModel ----

    /// <summary>Island sedang terbuka di tab Music (lirik benar-benar terlihat) atau tidak.</summary>
    public void SetContextVisible(bool visible)
    {
        if (_isContextVisible == visible)
        {
            return;
        }

        _isContextVisible = visible;
        OnPropertyChanged(nameof(IsContextVisible));

        if (!visible)
        {
            IsTimingMenuOpen = false; // menu ikut tertutup saat island menutup atau tab berganti
        }

        if (visible)
        {
            UpdatePosition(); // posisi bisa sudah berubah selama island tertutup
        }

        UpdateTimer();
    }

    /// <summary>
    /// Pill (island tertutup) sedang terlihat atau tidak (diisi IslandViewModel). Posisi lirik tetap diperbarui
    /// selama pill menampilkan baris lirik, walaupun panel lirik tidak terlihat.
    /// </summary>
    public void SetPillVisible(bool visible)
    {
        if (_isPillVisible == visible)
        {
            return;
        }

        _isPillVisible = visible;

        if (visible)
        {
            UpdatePosition();
        }

        UpdateTimer();
    }

    /// <summary>Durasi gulir halus baris lirik, mengikuti pengali animasi di Settings.</summary>
    public TimeSpan GetScrollDuration()
    {
        return MotionTokens.Scale(MotionTokens.ResizeMs, _settingsService.Current.AnimationDurationMs);
    }

    // ---- Perintah ----

    /// <summary>Tombol lirik: buka atau tutup panel. Jika fitur belum menyala, tampilkan konfirmasi dulu.</summary>
    [RelayCommand]
    private void Toggle()
    {
        if (IsConfirmVisible)
        {
            IsConfirmVisible = false; // menekan tombol lagi menutup konfirmasi
            return;
        }

        if (!_settingsService.Current.LyricsEnabled)
        {
            IsConfirmVisible = true;
            return;
        }

        if (IsOpen)
        {
            IsOpen = false;
        }
        else
        {
            Open();
        }
    }

    /// <summary>"Turn on" di konfirmasi: menyalakan lirik di Settings, lalu membuka panel.</summary>
    [RelayCommand]
    private void Confirm()
    {
        _settingsService.Current.LyricsEnabled = true;
        _settingsService.NotifyChanged();

        IsConfirmVisible = false;
        Open();
    }

    [RelayCommand]
    private void CancelConfirm()
    {
        IsConfirmVisible = false;
    }

    /// <summary>Klik sebuah baris lirik: loncat ke waktunya (hanya jika pemutar mengizinkan seek).</summary>
    [RelayCommand]
    private async Task SeekToLineAsync(LyricLineViewModel? line)
    {
        if (line is null || !_mediaService.Current.CanSeek)
        {
            return;
        }

        // Baris tampil saat posisi + offset >= waktunya, jadi pemutar harus loncat ke waktu dikurangi offset.
        TimeSpan target = line.Time - LyricsOffset;
        await _mediaService.SeekAsync(target < TimeSpan.Zero ? TimeSpan.Zero : target);
    }

    private void Open()
    {
        IsOpen = true;

        // Muat jika belum ada hasil untuk lagu ini. Setelah Error, membuka lagi berarti mencoba ulang.
        // NotFound tidak diulang: hasilnya sudah ada di cache.
        if (_mediaService.Current.HasSession && State is LyricsViewState.None or LyricsViewState.Error)
        {
            BeginLoad();
        }
    }

    // ---- Mengikuti pemutar dan pengaturan ----

    private void OnMediaChanged(object? sender, EventArgs e)
    {
        MediaState media = _mediaService.Current;
        CanSeek = media.CanSeek;

        if (!media.HasSession)
        {
            if (_trackKey.Length > 0 || IsOpen || IsConfirmVisible)
            {
                _trackKey = string.Empty;
                CancelLoad();
                ResetContent();
                IsOpen = false;
                IsConfirmVisible = false;
            }

            UpdateTimer();
            return;
        }

        string key = BuildKey(media);
        if (key != _trackKey)
        {
            // Lagu berganti: buang lirik lama. Lirik lagu baru hanya dicari jika panel sedang terbuka.
            _trackKey = key;
            CancelLoad();
            ResetContent();

            if (IsOpen)
            {
                BeginLoad();
            }
        }
        else
        {
            UpdatePosition(); // jeda, lanjut, atau loncat posisi
        }

        UpdateTimer();
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(OffsetText));
        UpdatePosition(); // offset mungkin berubah: cari ulang baris aktif

        // Lirik dimatikan dari Settings: tutup panel dan buang semuanya.
        if (!_settingsService.Current.LyricsEnabled
            && (IsOpen || IsConfirmVisible || State != LyricsViewState.None))
        {
            CancelLoad();
            ResetContent();
            IsOpen = false;
            IsConfirmVisible = false;
            UpdateTimer();
        }
    }

    private static string BuildKey(MediaState media)
    {
        return $"{media.SourceAppId}|{media.Title}|{media.Artist}|{media.Album}";
    }

    // ---- Memuat lirik ----

    private void BeginLoad()
    {
        CancelLoad();

        _loadCts = new CancellationTokenSource();
        SetState(LyricsViewState.Loading, "Loading lyrics...");

        _ = LoadAsync(_trackKey, _loadCts.Token);
    }

    private async Task LoadAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            // Jeda singkat: lagu yang dilewati cepat-cepat tidak sempat memicu permintaan jaringan.
            await Task.Delay(LoadDelayMs, cancellationToken);

            // Baca kondisi terbaru setelah jeda. Durasi dan album kadang baru lengkap beberapa saat setelah lagu berganti.
            MediaState media = _mediaService.Current;
            if (!media.HasSession || BuildKey(media) != key)
            {
                return; // lagu sudah berganti lagi: pemuatan untuk lagu baru dimulai oleh OnMediaChanged
            }

            LyricsResult result = await _lyricsService.GetLyricsAsync(
                media.Title, media.Artist, media.Album, media.Duration, cancellationToken);

            if (cancellationToken.IsCancellationRequested || key != _trackKey)
            {
                return;
            }

            ApplyResult(result);
        }
        catch (OperationCanceledException)
        {
            // Lagu berganti atau panel ditutup selagi memuat: bukan kesalahan.
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Lyrics could not be loaded ({ErrorType})", ex.GetType().Name);

            if (!cancellationToken.IsCancellationRequested && key == _trackKey)
            {
                SetState(LyricsViewState.Error, "Couldn't load lyrics");
            }
        }
    }

    private void ApplyResult(LyricsResult result)
    {
        switch (result.Status)
        {
            case LyricsStatus.Synced:
                _syncedLines = result.Lines;
                Lines = result.Lines.Select(line => new LyricLineViewModel(line.Time, line.Text)).ToList();
                ActiveIndex = -1;
                RefreshOpacityAround(-1, -1);
                SetState(LyricsViewState.Synced, string.Empty);
                UpdatePosition();
                break;

            case LyricsStatus.PlainOnly:
                PlainText = result.PlainText;
                SetState(LyricsViewState.Plain, string.Empty);
                break;

            case LyricsStatus.Instrumental:
                SetState(LyricsViewState.Instrumental, "Instrumental");
                break;

            case LyricsStatus.NotFound:
                SetState(LyricsViewState.NotFound, "No lyrics found");
                break;

            case LyricsStatus.Disabled:
                // Dimatikan di tengah jalan: panel ditutup oleh OnSettingsChanged.
                ResetContent();
                IsOpen = false;
                break;

            default:
                SetState(LyricsViewState.Error, "Couldn't load lyrics");
                break;
        }
    }

    private void CancelLoad()
    {
        // Cukup dibatalkan. CancellationTokenSource tanpa timer tidak punya sumber daya yang perlu dilepas.
        _loadCts?.Cancel();
        _loadCts = null;
    }

    private void ResetContent()
    {
        _syncedLines = Array.Empty<LyricLine>();
        Lines = Array.Empty<LyricLineViewModel>();
        PlainText = string.Empty;
        ActiveIndex = -1;
        SetState(LyricsViewState.None, string.Empty);
    }

    private void SetState(LyricsViewState state, string statusText)
    {
        StatusText = statusText;
        State = state;
    }

    // ---- Baris aktif ----

    private void OnPositionTick(object? sender, EventArgs e)
    {
        UpdatePosition();
    }

    /// <summary>Mencari baris yang sedang dinyanyikan (binary search). Murah, aman dipanggil sering.</summary>
    private void UpdatePosition()
    {
        if (State != LyricsViewState.Synced || _syncedLines.Count == 0)
        {
            return;
        }

        MediaState media = _mediaService.Current;
        if (!media.HasSession)
        {
            return;
        }

        int index = LrcParser.FindLineIndex(_syncedLines, media.GetEstimatedPosition() + LyricsOffset);
        if (index != ActiveIndex)
        {
            int previous = ActiveIndex;
            ActiveIndex = index;
            RefreshOpacityAround(previous, index);
            RefreshOpacityAround(index, index);
        }
    }

    /// <summary>Menyetel transparansi baris di sekitar "center" menurut jaraknya ke baris aktif.</summary>
    private void RefreshOpacityAround(int center, int active)
    {
        IReadOnlyList<LyricLineViewModel> lines = Lines;
        int first = Math.Max(0, center - 3);
        int last = Math.Min(lines.Count - 1, center + 3);

        for (int i = first; i <= last; i++)
        {
            lines[i].Opacity = OpacityFor(i - active);
        }
    }

    private static double OpacityFor(int distance)
    {
        return Math.Abs(distance) switch
        {
            0 => 1.0,
            1 => 0.6,
            2 => 0.35,
            _ => 0.2
        };
    }

    // ---- Timer posisi ----

    private void UpdateTimer()
    {
        // Timer berjalan saat lirik terlihat di panel (island terbuka di tab Music) ATAU di pill (island tertutup).
        bool shouldRun = IsOpen
                         && (_isContextVisible || _isPillVisible)
                         && State == LyricsViewState.Synced
                         && _mediaService.Current.IsPlaying;

        if (shouldRun)
        {
            if (_positionTimer is null)
            {
                _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PositionIntervalMs) };
                _positionTimer.Tick += OnPositionTick;
            }

            _positionTimer.Start();
        }
        else
        {
            _positionTimer?.Stop();
        }
    }

    public void Dispose()
    {
        _mediaService.Changed -= OnMediaChanged;
        _settingsService.SettingsChanged -= OnSettingsChanged;

        CancelLoad();

        if (_positionTimer is not null)
        {
            _positionTimer.Stop();
            _positionTimer.Tick -= OnPositionTick;
        }
    }
}
