using System.ComponentModel;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iLand.Helpers;
using iLand.Interfaces;
using iLand.Models;

namespace iLand.ViewModels;

/// <summary>
/// Otak dari island: state (normal / mini / notifikasi / expanded), tab, jam, baterai,
/// musik, timer, dan aturan hover.
/// ViewModel tidak tahu apa pun tentang animasi. Animasi dikerjakan oleh View.
/// </summary>
public partial class IslandViewModel : ObservableObject, IDisposable
{
    private readonly IClockService _clockService;
    private readonly IBatteryService _batteryService;
    private readonly IMediaService _mediaService;
    private readonly IAudioLevelService _audioLevel;
    private readonly IAppVolumeService _appVolume;

    public IAudioLevelService AudioMeter => _audioLevel;

    // ---- Mode rapat (tanda atau tombol mic di pill idle) ----

    /// <summary>Tombol mic bisa ditekan (mode rapat aktif dan diizinkan di Settings).</summary>
    public bool ShowMeetingMicButton => _meeting.IsMeetingActive && _settingsService.Current.MeetingShowMicButton;

    /// <summary>Hanya tanda mikrofon kecil (mode rapat aktif tetapi tombol dimatikan di Settings).</summary>
    public bool ShowMeetingIndicator => _meeting.IsMeetingActive && !_settingsService.Current.MeetingShowMicButton;

    /// <summary>Mikrofon default sedang dibisukan (ikon berubah menjadi merah bergaris).</summary>
    public bool IsMicMuted => _meeting.IsMicMuted;

    /// <summary>Volume aplikasi pemutar media (slider di tab Music).</summary>
    public IAppVolumeService AppVolume => _appVolume;
    private readonly ITimerService _timerService;
    private readonly INotificationService _notificationService;
    private readonly IOverlayService _overlayService;
    private readonly IScrollVolumeService _scrollVolume;
    private readonly IMeetingService _meeting;
    private readonly IWindowContextService _windowContext;
    private readonly ISettingsService _settingsService;
    private readonly IWindowService _windowService;
    private readonly ICaptureService _captureService;
    private readonly IRecordingService _recordingService;

    // Timer penunda collapse. Selama timer berjalan dan mouse kembali masuk, collapse dibatalkan.
    private readonly DispatcherTimer _collapseTimer;

    // Dipakai agar perubahan dari service tidak dianggap sebagai klik pengguna pada pilihan mode timer.
    private bool _isSyncingTimer;

    // Apakah mouse sedang berada di atas island.
    private bool _isPointerInside;

    // Untuk mendeteksi saat baterai BARU SAJA menjadi rendah atau penuh (null = belum pernah dibaca).
    private bool? _wasBatteryLow;
    private bool? _wasBatteryFull;

    [ObservableProperty]
    private string _currentTime = string.Empty;

    [ObservableProperty]
    private string _currentDate = string.Empty;

    /// <summary>Sapaan kecil di tab Home sesuai jam ("Good morning", dst.).</summary>
    [ObservableProperty]
    private string _greetingText = string.Empty;

    /// <summary>Teks kedua kartu "Focus" di tab Home: "Start 25 min", "Resume", "In progress", atau "Timer in use".</summary>
    [ObservableProperty]
    private string _focusCardSubtitle = string.Empty;

    /// <summary>Teks di tengah pill collapsed: jam, atau sisa waktu jika timer sedang aktif.</summary>
    [ObservableProperty]
    private string _collapsedCenterText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMusicGlow))]
    private bool _isExpanded;

    /// <summary>
    /// True = island menyusut jadi notch kecil karena ada isi di bawahnya.
    /// Saat island di-hover (expanded) atau menampilkan notifikasi, nilainya kembali false.
    /// </summary>
    [ObservableProperty]
    private bool _isMini;

    /// <summary>True = island harus disembunyikan karena ada aplikasi fullscreen.</summary>
    [ObservableProperty]
    private bool _shouldHideForFullscreen;

    // ---- Tab yang sedang dipilih di expanded island ----
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMusicGlow))]
    private IslandTab _selectedTab = IslandTab.Home;

    /// <summary>True saat island sedang menerima ketikan keyboard (misalnya mengisi tugas baru).</summary>
    [ObservableProperty]
    private bool _isKeyboardActive;

    /// <summary>Isi tab Todo. Dikelola oleh TodoViewModel sendiri agar kelas ini tidak membengkak.</summary>
    public TodoViewModel Todo { get; }

    /// <summary>Tab Mixer: volume master dan per aplikasi.</summary>
    public MixerViewModel Mixer { get; }

    /// <summary>Tab Control: Wi-Fi, Bluetooth, mode gelap, mute, kunci layar, volume.</summary>
    public ControlViewModel ControlPanel { get; }

    /// <summary>Isi tab Shelf (file sementara). Dikelola oleh ShelfViewModel sendiri.</summary>
    public ShelfViewModel Shelf { get; }

    /// <summary>Isi tab Capture (screenshot). Dikelola oleh CaptureViewModel sendiri.</summary>
    public CaptureViewModel Capture { get; }
    /// <summary>Panel lirik di tab Music. Dikelola oleh LyricsViewModel sendiri.</summary>
    public LyricsViewModel Lyrics { get; }

    // ---- Perekaman layar ----

    /// <summary>True selama layar sedang direkam. Island menampilkan pil "Recording" dengan tombol stop.</summary>
    [ObservableProperty]
    private bool _isRecording;

    /// <summary>Lama perekaman, misalnya 00:42 atau 1:05:09.</summary>
    [ObservableProperty]
    private string _recordingTimeText = "00:00";

    // ---- Notifikasi ----
    [ObservableProperty]
    private bool _isNotificationVisible;

    [ObservableProperty]
    private string _notificationTitle = string.Empty;

    [ObservableProperty]
    private string _notificationMessage = string.Empty;

    [ObservableProperty]
    private string _notificationGlyph = string.Empty;

    /// <summary>Gambar kecil di notifikasi (misalnya pratinjau screenshot). Null = pakai ikon biasa.</summary>
    [ObservableProperty]
    private ImageSource? _notificationThumbnail;

    /// <summary>True jika notifikasi yang tampil membawa gambar kecil (menggantikan ikon bulat).</summary>
    public bool HasNotificationThumbnail => NotificationThumbnail is not null;

    partial void OnNotificationThumbnailChanged(ImageSource? value) => OnPropertyChanged(nameof(HasNotificationThumbnail));

    // ---- Overlay sementara (volume, kecerahan, perangkat, Caps Lock, dan notifikasi teks). IsNotificationVisible = ada overlay tampil ----

    /// <summary>Jenis tampilan overlay yang sedang aktif. Window memilih isi dan ukurannya dari sini.</summary>
    [ObservableProperty]
    private OverlayKind _activeOverlayKind = OverlayKind.Text;

    /// <summary>Nama resource ikon (Navigation.xaml) untuk overlay Level dan Chip.</summary>
    [ObservableProperty]
    private string _overlayIconKey = string.Empty;

    /// <summary>Isi bar 0 sampai 1 untuk overlay Level. Berubah terus saat volume digeser, jadi TIDAK memicu perubahan ukuran island.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OverlayPercent))]
    private double _overlayLevel;

    /// <summary>Isi dalam persen bulat. Dipakai cincin baterai di panel charging.</summary>
    public int OverlayPercent => (int)Math.Round(OverlayLevel * 100);

    [ObservableProperty]
    private string _overlayValueText = string.Empty;

    /// <summary>
    /// Naik setiap kali ukuran overlay yang sedang tampil harus dihitung ulang (jenis berganti, atau teks chip berganti).
    /// IslandWindow memanggil ApplyState saat nilai ini berubah. Update bar level biasa tidak menaikkannya.
    /// </summary>
    [ObservableProperty]
    private int _overlayRevision;

    // ---- Baterai ----
    [ObservableProperty]
    private bool _hasBattery;

    [ObservableProperty]
    private int _batteryPercent;

    [ObservableProperty]
    private string _batteryText = string.Empty;

    [ObservableProperty]
    private string _batteryStatusText = string.Empty;

    [ObservableProperty]
    private bool _isBatteryCharging;

    [ObservableProperty]
    private bool _isBatteryLow;

    // ---- Musik ----
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMusicGlow))]
    private bool _hasMedia;

    [ObservableProperty]
    private bool _showNoMediaMessage = true;

    [ObservableProperty]
    private string _mediaTitle = string.Empty;

    [ObservableProperty]
    private string _mediaArtist = string.Empty;

    [ObservableProperty]
    private string _mediaSourceApp = string.Empty;

    [ObservableProperty]
    private string _nowPlayingText = string.Empty;

    [ObservableProperty]
    private ImageSource? _mediaArtwork;

    [ObservableProperty]
    private bool _isMediaPlaying;

    // Glyph ikon Segoe MDL2 Assets: E768 = play, E769 = pause.
    [ObservableProperty]
    private string _playPauseGlyph = "\uE768";

    [ObservableProperty]
    private bool _canPlayPause;

    [ObservableProperty]
    private bool _canGoNext;

    [ObservableProperty]
    private bool _canGoPrevious;

    /// <summary>Progress lagu dari 0.0 sampai 1.0.</summary>
    [ObservableProperty]
    private double _mediaProgress;

    [ObservableProperty]
    private string _mediaElapsedText = "0:00";

    [ObservableProperty]
    private string _mediaDurationText = "0:00";

    /// <summary>Sisa waktu lagu, ditampilkan dengan tanda minus (misalnya "-1:23").</summary>
    [ObservableProperty]
    private string _mediaRemainingText = "-0:00";

    /// <summary>True jika pemutar mengizinkan loncat posisi (bar progres bisa diklik dan digeser).</summary>
    [ObservableProperty]
    private bool _canSeek;

    /// <summary>Ikon aplikasi pemutar (diambil dari aplikasinya sendiri). Null = tampilkan ikon nada.</summary>
    [ObservableProperty]
    private ImageSource? _mediaSourceIcon;

    // Identitas aplikasi yang ikonnya sedang dimuat atau sudah dimuat, agar pencarian tidak diulang tiap status berubah.
    private string _mediaSourceIconAppId = string.Empty;

    // ---- Timer ----
    [ObservableProperty]
    private string _timerDisplayText = "05:00";

    [ObservableProperty]
    private string _timerLabelText = string.Empty;

    [ObservableProperty]
    private bool _isTimerRunning;

    /// <summary>True jika timer sedang berjalan atau di-pause.</summary>
    [ObservableProperty]
    private bool _isTimerActive;

    [ObservableProperty]
    private string _timerPrimaryGlyph = "\uE768";

    /// <summary>Jenis timer hanya boleh diganti saat timer tidak aktif.</summary>
    [ObservableProperty]
    private bool _canChangeTimerMode = true;

    /// <summary>Pilihan 1/5/10 menit dan tombol +/- hanya tampil untuk Timer yang belum dimulai.</summary>
    [ObservableProperty]
    private bool _showCountdownPresets = true;
    private const int SessionDotCount = 8;

    [ObservableProperty] private double _timerProgress = 1;
    [ObservableProperty] private double _timerFontSize = 32;
    [ObservableProperty] private string _timerRingLabel = "Timer";
    [ObservableProperty] private int _timerModeIndex;   // 0 Timer, 1 Pomodoro, 2 Stopwatch
    [ObservableProperty] private string _timerPrimaryText = "Start";
    [ObservableProperty] private bool _canResetTimer;
    [ObservableProperty] private bool _showSessionDots;
    [ObservableProperty] private bool _showTimerStatusLabel;
    [ObservableProperty] private int _timerPulseCount;
    [ObservableProperty] private IReadOnlyList<bool> _sessionDots = new bool[SessionDotCount];
    [ObservableProperty] private string _sessionsTodayText = "0";
    private int _lastSessionsToday = -1;

    [ObservableProperty]
    private bool _isCountdownMode = true;

    [ObservableProperty]
    private bool _isPomodoroMode;

    [ObservableProperty]
    private bool _isStopwatchMode;

    public IslandViewModel(
        IClockService clockService,
        IBatteryService batteryService,
        IMediaService mediaService,
        IAudioLevelService audioLevel,
        IAppVolumeService appVolume,
        ITimerService timerService,
        INotificationService notificationService,
        IOverlayService overlayService,
        IScrollVolumeService scrollVolume,
        IMeetingService meeting,
        IWindowContextService windowContext,
        ISettingsService settingsService,
        IWindowService windowService,
        TodoViewModel todoViewModel,
        ShelfViewModel shelfViewModel,
        ICaptureService captureService,
        CaptureViewModel captureViewModel,
        IRecordingService recordingService,
        LyricsViewModel lyricsViewModel,
        MixerViewModel mixerViewModel,
        ControlViewModel controlViewModel)
    {
        _clockService = clockService;
        _batteryService = batteryService;
        _mediaService = mediaService;
        _audioLevel = audioLevel;
        _appVolume = appVolume;
        _timerService = timerService;
        _notificationService = notificationService;
        _overlayService = overlayService;
        _scrollVolume = scrollVolume;
        _meeting = meeting;
        _windowContext = windowContext;
        _settingsService = settingsService;
        _windowService = windowService;
        Todo = todoViewModel;
        // Jumlah tugas memengaruhi tinggi island saat tab Todo terbuka.
        Todo.Items.CollectionChanged += OnTodoItemsChanged;
        Shelf = shelfViewModel;
        _captureService = captureService;
        Capture = captureViewModel;
        _recordingService = recordingService;
        Lyrics = lyricsViewModel;
        Mixer = mixerViewModel;
        ControlPanel = controlViewModel;
        // Jumlah aplikasi yang bersuara memengaruhi tinggi island saat tab Mixer terbuka.
        Mixer.Apps.CollectionChanged += OnMixerAppsChanged;
        // Baris lirik yang berganti dan status panel lirik menentukan isi pill.
        Lyrics.PropertyChanged += OnLyricsPropertyChangedForPill;
        Lyrics.PropertyChanged += OnLyricsPropertyChanged;
        _collapseTimer = new DispatcherTimer();
        _collapseTimer.Tick += OnCollapseTimerTick;

        UpdateClock();
        UpdateBattery();
        UpdateMedia();
        UpdateTimer();
        UpdateModeFlags();
        UpdateLyricsContext();
        UpdateOverlaySuppression();

        _clockService.Ticked += OnClockTicked;
        _captureService.CaptureStarted += OnCaptureStarted;
        _recordingService.StateChanged += OnRecordingStateChanged;
        _batteryService.Changed += OnBatteryChanged;
        _mediaService.Changed += OnMediaChanged;
        _timerService.Changed += OnTimerChanged;
        _timerService.PhaseCompleted += OnTimerPhaseCompleted;
        _overlayService.Changed += OnOverlayChanged;
        _windowContext.Changed += OnContextChanged;
        _settingsService.SettingsChanged += OnSettingsChanged;
        _meeting.Changed += OnMeetingChanged;
        _lastMusicStyle = _settingsService.Current.MusicStyle;

        ApplyEnabledTabs(); // catat daftar tab yang menyala saat startup
    }

    // True selama window island tampil. Diisi IslandWindow (menyembunyikan lewat tray tidak terlihat oleh ViewModel).
    private bool _isWindowVisible = true;

    /// <summary>Dipanggil IslandWindow saat window ditampilkan atau disembunyikan (misalnya lewat menu tray).</summary>
    public void SetWindowVisible(bool visible)
    {
        if (_isWindowVisible == visible)
        {
            return;
        }

        _isWindowVisible = visible;
        UpdateAudioMeter();
        UpdateLyricsContext();
    }

    // ---- Menentukan mode island ----

    /// <summary>
    /// Meter suara hanya menyala jika visualizer benar-benar terlihat: di pill idle (mode normal, bukan mini,
    /// notifikasi, atau perekaman) atau di tab Music saat island terbuka, dan window sedang tampil.
    /// </summary>
    // ---- Aksen dari sampul album (isi progress dan visualizer) ----

    // Aksen bawaan (sampul monokrom atau tanpa sampul): putih.
    private static readonly Color DefaultAccent = Color.FromRgb(0xFF, 0xFF, 0xFF);

    // Kekuatan cahaya sampul di latar island (alpha 0..255) pada tiga titik gradien: tengah, sedang, tepi.
    private static readonly byte[] GlowAlpha = { 82, 34, 0 };

    private ImageSource? _accentSource;
    private Color? _accentExtracted;

    /// <summary>
    /// Warna aksen untuk isi progress, volume, dan visualizer. Satu objek brush yang warnanya dianimasikan, jadi
    /// semua elemen yang memakainya berubah bersamaan. Sampul monokrom atau tanpa sampul: putih.
    /// Brush ini sengaja TIDAK dibekukan karena warnanya dianimasikan.
    /// </summary>
    public SolidColorBrush MediaAccentBrush { get; } = new SolidColorBrush(DefaultAccent);

    /// <summary>
    /// Cahaya lembut warna sampul di latar island (gradien radial dari pojok kiri atas, memudar ke hitam).
    /// Dipasang sebagai Background lapisan "AccentGlow" di IslandWindow. Tanpa aksen (monokrom) semua titik transparan.
    /// Memakai satu brush gradien yang stop-nya dianimasikan (tanpa OpacityMask, supaya render tetap murah).
    /// </summary>
    public RadialGradientBrush MediaGlowBrush { get; } = CreateGlowBrush();

    /// <summary>Lapisan cahaya sampul hanya tampil di tab Music saat ada media dan island terbuka.</summary>
    public bool ShowMusicGlow => IsExpanded && SelectedTab == IslandTab.Music && HasMedia;

    private static RadialGradientBrush CreateGlowBrush()
    {
        var center = new System.Windows.Point(0.12, 0.15);
        var brush = new RadialGradientBrush
        {
            Center = center,
            GradientOrigin = center,
            RadiusX = 0.95,
            RadiusY = 1.25,
        };

        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.55));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1.0));
        return brush;
    }

    /// <summary>Menghitung ulang aksen hanya bila sampul berganti (murah: satu kali per lagu).</summary>
    private void UpdateAccent(ImageSource? artwork)
    {
        if (ReferenceEquals(artwork, _accentSource))
        {
            return;
        }

        _accentSource = artwork;

        Color? extracted = AccentColorExtractor.Extract(artwork as System.Windows.Media.Imaging.BitmapSource);
#if DEBUG
        Serilog.Log.Debug("Accent from cover: {Color}", extracted?.ToString() ?? "none (monochrome or no cover, using white)");
#endif
        if (extracted == _accentExtracted)
        {
            return;
        }

        _accentExtracted = extracted;

        Color bar = extracted ?? DefaultAccent;
        TimeSpan duration = MotionTokens.ReducedMotion ? TimeSpan.Zero : TimeSpan.FromMilliseconds(350);

        MediaAccentBrush.BeginAnimation(
            SolidColorBrush.ColorProperty,
            new ColorAnimation(bar, duration) { EasingFunction = MotionTokens.ContentInEase });

        // Cahaya latar: warna sampul dengan alpha berbeda per titik. Tanpa aksen, semuanya transparan.
        for (int i = 0; i < MediaGlowBrush.GradientStops.Count; i++)
        {
            byte alpha = extracted is null ? (byte)0 : GlowAlpha[i];
            Color glow = Color.FromArgb(alpha, bar.R, bar.G, bar.B);
            MediaGlowBrush.GradientStops[i].BeginAnimation(
                GradientStop.ColorProperty,
                new ColorAnimation(glow, duration) { EasingFunction = MotionTokens.ContentInEase });
        }
    }

    // ---- Isi pill saat tertutup ----

    // Setelah musik dijeda, pill tetap berada di mode media sebentar sebelum kembali ke jam + baterai.
    private const int MediaPillLingerSeconds = 2;

    private bool _mediaPillActive;
    private DispatcherTimer? _mediaPillTimer;

    [ObservableProperty]
    private PillLayoutMode _pillLayout = PillLayoutMode.Idle;

    /// <summary>
    /// True saat island disembunyikan karena "When idle: Hide". Dihitung ulang oleh UpdateIdleHidden setiap salah satu
    /// syaratnya berubah. IslandWindow mengubahnya menjadi garis pemicu tipis di tepi atas layar.
    /// </summary>
    [ObservableProperty]
    private bool _isIdleHidden;

    // Bagian-bagian pill yang tampil menurut PillLayout (diisi UpdatePill).
    [ObservableProperty]
    private bool _pillShowLeftText = true;

    [ObservableProperty]
    private bool _pillShowLeftBattery;

    [ObservableProperty]
    private bool _pillShowRightBattery;

    [ObservableProperty]
    private bool _pillShowCover;

    [ObservableProperty]
    private bool _pillShowLyrics;

    [ObservableProperty]
    private bool _pillShowVisualizer;

    partial void OnIsTimerActiveChanged(bool value) => UpdatePill();

    partial void OnHasBatteryChanged(bool value) => UpdatePill();

    private void OnLyricsPropertyChangedForPill(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LyricsViewModel.IsOpen)
            or nameof(LyricsViewModel.State)
            or nameof(LyricsViewModel.CurrentLineText))
        {
            UpdatePill();
        }
    }

    /// <summary>
    /// Menentukan isi pill:
    /// - Idle: tidak ada musik diputar (atau sudah dijeda lebih dari 2 detik). Jam di kiri, baterai + persen di kanan.
    /// - Media: musik diputar, lirik tidak tampil. Cincin baterai kiri (atau sisa timer bila timer aktif), sampul + visualizer kanan.
    /// - MediaLyrics: musik diputar dan lirik aktif (tombol lirik ditekan, lirik sudah dimuat dan ada baris aktif).
    ///   Sampul kiri, baris lirik tengah, visualizer kanan.
    /// </summary>
    private void UpdatePill()
    {
        bool playing = IsMediaPlaying && HasMedia;

        if (playing)
        {
            _mediaPillTimer?.Stop();
            _mediaPillActive = true;
        }
        else if (_mediaPillActive && _mediaPillTimer is not { IsEnabled: true })
        {
            _mediaPillTimer ??= CreateMediaPillTimer();
            _mediaPillTimer.Start();
        }

        bool lyricsShown = _mediaPillActive
                           && Lyrics.IsOpen
                           && Lyrics.State == LyricsViewState.Synced
                           && !string.IsNullOrEmpty(Lyrics.CurrentLineText);

        PillLayoutMode layout = !_mediaPillActive
            ? PillLayoutMode.Idle
            : lyricsShown ? PillLayoutMode.MediaLyrics : PillLayoutMode.Media;

        PillLayout = layout;
        PillShowLeftText = layout == PillLayoutMode.Idle || (layout == PillLayoutMode.Media && IsTimerActive);
        PillShowLeftBattery = layout == PillLayoutMode.Media && !IsTimerActive && HasBattery;
        PillShowRightBattery = layout == PillLayoutMode.Idle && HasBattery;
        PillShowCover = layout != PillLayoutMode.Idle;
        PillShowLyrics = layout == PillLayoutMode.MediaLyrics;
        PillShowVisualizer = layout != PillLayoutMode.Idle;

        UpdateIdleHidden();
    }

    /// <summary>
    /// Island disembunyikan hanya jika: pilihan "When idle: Hide" aktif untuk bentuk ini, pill sedang Idle (tanpa musik),
    /// tidak ada timer, dan island tidak terbuka, tidak menampilkan notifikasi, tidak merekam, serta pointer tidak sedang di atasnya.
    /// Salah satu syarat berubah (musik mulai, timer jalan, notifikasi masuk, pointer menyentuh garis tepi atas) = island muncul lagi.
    /// </summary>
    private void UpdateIdleHidden()
    {
        IsIdleHidden = _settingsService.Current.ActiveHideWhenIdle
                       && PillLayout == PillLayoutMode.Idle
                       && !IsTimerActive
                       && !IsExpanded
                       && !IsNotificationVisible
                       && !IsRecording
                       && !_isPointerInside;
    }

    private DispatcherTimer CreateMediaPillTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(MediaPillLingerSeconds) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!(IsMediaPlaying && HasMedia))
            {
                _mediaPillActive = false;
                UpdatePill();
            }
        };
        return timer;
    }

    private void UpdateAudioMeter()
    {
        bool pillVisible = !IsExpanded && !IsMini && !IsNotificationVisible && !IsRecording;
        bool musicTabVisible = IsExpanded && SelectedTab == IslandTab.Music;
        bool visible = _isWindowVisible && !ShouldHideForFullscreen && (pillVisible || musicTabVisible);

        _audioLevel.SetActive(IsMediaPlaying && visible, _mediaService.Current.SourceAppId);

        // Sesi audio untuk slider volume hanya dipegang selama tab Music terlihat dan ada media.
        // Attach murah bila aplikasinya sama (tidak melakukan apa pun), Detach melepas objek COM.
        bool wantsVolume = _isWindowVisible && !ShouldHideForFullscreen && musicTabVisible && HasMedia;
        Lyrics.SetPillVisible(_isWindowVisible && !ShouldHideForFullscreen && pillVisible);
        UpdatePill();
        if (wantsVolume)
        {
            _appVolume.Attach(_mediaService.Current.SourceAppId);
        }
        else
        {
            _appVolume.Detach();
        }

        // Mixer: sesi audio dan event hanya dipegang selama tab Mixer benar-benar terlihat.
        // Tab Control juga memakai volume master dari IMixerService, jadi Mixer dipasang saat salah satu tab itu terlihat.
        bool mixerTabVisible = IsExpanded && SelectedTab == IslandTab.Mixer;
        bool controlTabVisible = IsExpanded && SelectedTab == IslandTab.Control;
        bool islandShown = _isWindowVisible && !ShouldHideForFullscreen;
        Mixer.SetActive(islandShown && (mixerTabVisible || controlTabVisible));
        ControlPanel.SetActive(islandShown && controlTabVisible);
    }

    partial void OnIsRecordingChanged(bool value)
    {
        UpdateAudioMeter();
        UpdateIdleHidden();
    }

    partial void OnIsMediaPlayingChanged(bool value)
    {
        UpdateAudioMeter();
    }

    partial void OnShouldHideForFullscreenChanged(bool value)
    {
        UpdateAudioMeter();
        UpdateOverlaySuppression();
    }    
    // Dua method ini dibuat otomatis oleh [ObservableProperty]: dipanggil saat nilainya berubah.
    partial void OnIsExpandedChanged(bool value)
    {
        UpdateModeFlags();      // juga memperbarui meter suara (lewat UpdateModeFlags)
        UpdateLyricsContext();  // lirik hanya terlihat saat island terbuka di tab Music
        UpdateOverlaySuppression();
    }

    partial void OnIsNotificationVisibleChanged(bool value)
    {
        UpdateModeFlags();
    }

    // Dipanggil otomatis saat SelectedTab berubah.
    partial void OnSelectedTabChanged(IslandTab value)
    {
        UpdateLyricsContext();
        // Saat tab Shelf dibuka, periksa ulang file mana yang masih ada.
        if (value == IslandTab.Shelf)
        {
            Shelf.Refresh();
        }

        // Judul di header mengikuti tab yang aktif.
        OnPropertyChanged(nameof(SelectedTabTitle));

        UpdateAudioMeter();
    }

    /// <summary>Judul tab aktif yang ditampilkan di header island.</summary>
    public string SelectedTabTitle => SelectedTab switch
    {
        IslandTab.Home => "Home",
        IslandTab.Music => "Music",
        IslandTab.Mixer => "Mixer",
        IslandTab.Control => "Control",
        IslandTab.Timer => "Timer",
        IslandTab.Todo => "Tasks",
        IslandTab.Shelf => "Shelf",
        IslandTab.Capture => "Capture",
        _ => string.Empty
    };

    // ---- Informasi untuk TabLayout (tinggi dan lebar island mengikuti tab) ----

    /// <summary>
    /// Jumlah item yang memengaruhi tinggi tab aktif. Todo: jumlah tugas. Mixer: jumlah aplikasi yang bersuara.
    /// Tab lain 0.
    /// </summary>
    public int TabItemCount => SelectedTab switch
    {
        IslandTab.Todo => Todo.Items.Count,
        IslandTab.Mixer => Mixer.Apps.Count,
        _ => 0
    };

    /// <summary>True jika tab aktif meminta lebar penuh: tab Music dengan panel lirik terbuka.</summary>
    public bool WantsWideLayout => SelectedTab == IslandTab.Music && HasMedia && Lyrics.IsOpen && !IsGlassStyle;

    private bool IsGlassStyle => _settingsService.Current.MusicStyle == MusicTabStyle.Glass;

    public bool ShowNormalMusicLayout => HasMedia && !Lyrics.IsOpen && !IsGlassStyle;

    public bool ShowLyricsLayout => HasMedia && Lyrics.IsOpen && !IsGlassStyle;

    /// <summary>Tab Music tampil dengan gaya Glass (ada media dan gaya Glass dipilih di Settings).</summary>
    public bool ShowGlassMusicLayout => HasMedia && IsGlassStyle;

    /// <summary>Gaya Glass tetapi lirik masih dimatikan di Settings: kartu menawarkan "Turn on lyrics".</summary>
    public bool ShowGlassLyricsOff => IsGlassStyle && !_settingsService.Current.LyricsEnabled;

    private MusicTabStyle _lastMusicStyle;

    /// <summary>Latar Glass: sampul yang diperbesar dan di-blur (gambar kecil). Null bila bukan gaya Glass atau tanpa sampul.</summary>
    [ObservableProperty]
    private ImageSource? _glassBackdrop;

    // Sampul yang dipakai untuk membuat GlassBackdrop terakhir, agar tidak dibuat ulang untuk sampul yang sama.
    private ImageSource? _glassBackdropSource;

    /// <summary>Latar blur terlihat: island terbuka di tab Music dengan gaya Glass dan ada media.</summary>
    public bool ShowGlassBackdrop => SelectedTab == IslandTab.Music && ShowGlassMusicLayout;

    /// <summary>Membuat latar Glass dari sampul bila perlu (hanya gaya Glass dengan media). Di luar itu gambar dilepas.</summary>
    private void UpdateGlassBackdrop()
    {
        if (!(IsGlassStyle && HasMedia))
        {
            _glassBackdropSource = null;
            GlassBackdrop = null;
            return;
        }

        if (ReferenceEquals(_glassBackdropSource, MediaArtwork) && GlassBackdrop is not null)
        {
            return;
        }

        _glassBackdropSource = MediaArtwork;
        GlassBackdrop = CoverBlur.Create(MediaArtwork);
    }

    private void NotifyMusicStyleProperties()
    {
        OnPropertyChanged(nameof(WantsWideLayout));
        OnPropertyChanged(nameof(ShowNormalMusicLayout));
        OnPropertyChanged(nameof(ShowLyricsLayout));
        OnPropertyChanged(nameof(ShowGlassMusicLayout));
        OnPropertyChanged(nameof(ShowGlassLyricsOff));
        OnPropertyChanged(nameof(ShowGlassBackdrop));
        UpdateGlassBackdrop();
    }

    public bool ShowLyricsConfirm => HasMedia && Lyrics.IsConfirmVisible;

    partial void OnHasMediaChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowNormalMusicLayout));
        OnPropertyChanged(nameof(ShowLyricsLayout));
        OnPropertyChanged(nameof(ShowLyricsConfirm));
        OnPropertyChanged(nameof(WantsWideLayout));
        OnPropertyChanged(nameof(ShowGlassMusicLayout));
        OnPropertyChanged(nameof(ShowGlassBackdrop));
        UpdateGlassBackdrop();
        UpdateLyricsContext(); // gaya Glass: lirik otomatis dimuat saat media baru muncul di tab Music
    }

    private void OnLyricsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(LyricsViewModel.IsOpen):
                OnPropertyChanged(nameof(ShowNormalMusicLayout));
                OnPropertyChanged(nameof(ShowLyricsLayout));
                OnPropertyChanged(nameof(WantsWideLayout));
                break;

            case nameof(LyricsViewModel.IsConfirmVisible):
                OnPropertyChanged(nameof(ShowLyricsConfirm));
                break;
        }
    }

    /// <summary>Lirik hanya perlu diperbarui saat benar-benar terlihat: island terbuka di tab Music.</summary>
    private void UpdateLyricsContext()
    {
        bool visible = _isWindowVisible && IsExpanded && SelectedTab == IslandTab.Music;
        Lyrics.SetContextVisible(visible);
        OnPropertyChanged(nameof(ShowGlassBackdrop)); // tab berganti: latar ikut muncul atau memudar

        // Gaya Glass: lirik selalu tampil di kartunya, jadi dimuat otomatis saat tab Music terlihat,
        // tetapi hanya bila lirik sudah dinyalakan di Settings (OpenIfEnabled tidak melakukan apa pun bila belum).
        if (visible && HasMedia && IsGlassStyle)
        {
            Lyrics.OpenIfEnabled();
        }
    }

    private void OnTodoItemsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        // Hanya relevan jika tab Todo yang sedang terbuka.
        if (SelectedTab == IslandTab.Todo)
        {
            OnPropertyChanged(nameof(TabItemCount));
        }
    }

    private void OnMixerAppsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (SelectedTab == IslandTab.Mixer)
        {
            OnPropertyChanged(nameof(TabItemCount));
        }
    }

    // ---- Pindah tab lewat scroll mouse di tab bar ----

    // Urutan tab mengikuti urutan nilai di enum IslandTab (Home, Control, Music, Mixer, Timer, Todo, Shelf, Capture).
    private static readonly IslandTab[] TabOrder = Enum.GetValues<IslandTab>();

    // PreviousTab -> PreviousTabCommand, NextTab -> NextTabCommand (dibuat otomatis oleh [RelayCommand]).
    // Di tab pertama atau terakhir tidak terjadi apa-apa (tidak berputar).
    // Hanya tab yang menyala di Settings ("Tabs on island") yang dilewati saat berpindah.
    [RelayCommand]
    private void PreviousTab()
    {
        IslandTab[] tabs = GetEnabledTabs();
        int index = Array.IndexOf(tabs, SelectedTab);
        if (index > 0)
        {
            SelectedTab = tabs[index - 1];
        }
    }

    [RelayCommand]
    private void NextTab()
    {
        IslandTab[] tabs = GetEnabledTabs();
        int index = Array.IndexOf(tabs, SelectedTab);
        if (index >= 0 && index < tabs.Length - 1)
        {
            SelectedTab = tabs[index + 1];
        }
    }

    // ---- Tab yang menyala (pengaturan "Tabs on island") ----

    /// <summary>Tab yang tampil di island, berurutan seperti enum IslandTab. Home selalu ada.</summary>
    private IslandTab[] GetEnabledTabs()
    {
        AppSettings settings = _settingsService.Current;
        return TabOrder.Where(settings.IsTabEnabled).ToArray();
    }

    // Tanda tangan tab yang menyala saat ini, untuk mendeteksi perubahan (SettingsChanged dipanggil untuk semua pengaturan).
    private string _enabledTabsKey = string.Empty;

    public bool IsShelfTabEnabled => _settingsService.Current.IsTabEnabled(IslandTab.Shelf);

    public bool IsTimerTabEnabled => _settingsService.Current.IsTabEnabled(IslandTab.Timer);

    public bool IsCaptureTabEnabled => _settingsService.Current.IsTabEnabled(IslandTab.Capture);

    /// <summary>Kartu aksi cepat di Home hanya tampil jika tab yang dituju menyala.</summary>
    public bool HasQuickActions => IsTimerTabEnabled || IsCaptureTabEnabled;

    /// <summary>
    /// Dipanggil saat pengaturan berubah. Jika daftar tab berubah dan tab yang sedang dibuka dimatikan,
    /// island kembali ke Home. Juga memperbarui kartu Home yang bergantung pada tab.
    /// </summary>
    private void ApplyEnabledTabs()
    {
        AppSettings settings = _settingsService.Current;
        string key = string.Join(',', TabOrder.Where(settings.IsTabEnabled));
        if (key == _enabledTabsKey)
        {
            return;
        }

        _enabledTabsKey = key;

        if (!settings.IsTabEnabled(SelectedTab))
        {
            SelectedTab = IslandTab.Home;
        }

        OnPropertyChanged(nameof(IsShelfTabEnabled));
        OnPropertyChanged(nameof(IsTimerTabEnabled));
        OnPropertyChanged(nameof(IsCaptureTabEnabled));
        OnPropertyChanged(nameof(HasQuickActions));
    }

    private void OnContextChanged(object? sender, EventArgs e)
    {
        UpdateModeFlags();
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        ApplyEnabledTabs();
        NotifyMeetingProperties(); // "Show mic button" bisa berubah

        MusicTabStyle style = _settingsService.Current.MusicStyle;
        if (style != _lastMusicStyle)
        {
            _lastMusicStyle = style;
            if (style == MusicTabStyle.Classic)
            {
                Lyrics.CloseLyricsPanel(); // Glass membuka lirik otomatis. Classic kembali ke tombol lirik.
            }
        }

        NotifyMusicStyleProperties();
        UpdateLyricsContext();
        UpdateModeFlags();
    }

    private void OnMeetingChanged(object? sender, EventArgs e)
    {
        NotifyMeetingProperties();
    }

    private void NotifyMeetingProperties()
    {
        OnPropertyChanged(nameof(ShowMeetingMicButton));
        OnPropertyChanged(nameof(ShowMeetingIndicator));
        OnPropertyChanged(nameof(IsMicMuted));
    }

    /// <summary>Membisukan atau membunyikan mikrofon default sistem (tombol mic di pill saat rapat).</summary>
    [RelayCommand]
    private void ToggleMic()
    {
        _meeting.ToggleMic();
    }

    private void UpdateModeFlags()
    {
        var settings = _settingsService.Current;

        // Mini hanya jika: fitur aktif, ada isi di bawah island, dan island tidak sedang dibuka
        // atau menampilkan notifikasi.
        IsMini = settings.AutoMinimizeWhenWindowBelow
                 && _windowContext.IsContentBelowIsland
                 && !IsExpanded
                 && !IsNotificationVisible;

        ShouldHideForFullscreen = settings.HideWhenFullscreen
                                  && _windowContext.IsFullscreenAppActive;

        // Mode berubah (mini, notifikasi, terbuka): periksa apakah visualizer masih terlihat.
        UpdateAudioMeter();

        // Pengaturan "When idle" atau mode berubah: tentukan lagi apakah island disembunyikan.
        UpdateIdleHidden();
    }

    // ---- Aturan interaksi mouse (dipanggil oleh View) ----

    /// <summary>
    /// Scroll di atas island yang tertutup mengubah volume sistem, bila diaktifkan di Settings.
    /// Hanya bila "Hover to expand" mati: dengan hover aktif, island langsung terbuka saat pointer menyentuhnya, lalu scroll
    /// di tab bar memindahkan tab. Mengembalikan true bila scroll dipakai (agar tidak diteruskan ke aplikasi lain).
    /// </summary>
    public bool OnPillScrolled(int wheelDelta)
    {
        var settings = _settingsService.Current;
        if (!settings.ScrollVolumeEnabled || settings.HoverToExpand
            || IsExpanded || IsMini || IsIdleHidden || IsRecording)
        {
            return false;
        }

        _scrollVolume.Adjust(wheelDelta);
        return true;
    }

    /// <summary>Mouse masuk ke area island.</summary>
    public void OnPointerEntered()
    {
        _isPointerInside = true;
        UpdateIdleHidden(); // pointer menyentuh garis tepi atas: island yang tersembunyi muncul

        // Saat notifikasi tampil, hover hanya menahan notifikasi agar tidak hilang. Island tidak melebar.
        if (IsNotificationVisible)
        {
            _overlayService.Hold();
            return;
        }

        // Saat merekam dan island tertutup, hover TIDAK membuka island supaya tombol stop di pil
        // bisa diklik. Klik pada badan pil (bukan tombol stop) tetap membukanya.
        if (IsRecording && !IsExpanded)
        {
            _collapseTimer.Stop();
            return;
        }

        _collapseTimer.Stop(); // mouse kembali: batalkan rencana collapse

        if (_settingsService.Current.HoverToExpand)
        {
            IsExpanded = true;
        }
    }

    /// <summary>Mouse keluar dari area island.</summary>
    public void OnPointerLeft()
    {
        _isPointerInside = false;
        UpdateIdleHidden(); // pointer pergi dan island sedang diam: sembunyi lagi

        if (IsNotificationVisible)
        {
            _overlayService.Release(MotionTokens.OverlayAfterHoverMs);
            return;
        }

        // Selama pengguna sedang mengetik, island jangan menutup.
        if (!IsExpanded || !_settingsService.Current.AutoCollapse || IsKeyboardActive)
        {
            return;
        }

        // Jangan langsung menutup: tunggu sesuai delay di Settings.
        _collapseTimer.Interval = TimeSpan.FromMilliseconds(_settingsService.Current.CollapseDelayMs);
        _collapseTimer.Start();
    }

    /// <summary>Island diklik.</summary>
    public void OnPointerClicked()
    {
        // Klik pada notifikasi = tutup notifikasi.
        if (IsNotificationVisible)
        {
            DismissNotification();
            return;
        }

        _collapseTimer.Stop();

        if (!IsExpanded)
        {
            IsExpanded = true;
        }
        else if (!_settingsService.Current.AutoCollapse)
        {
            // Jika auto collapse mati, klik berikutnya menutup island.
            IsExpanded = false;
        }
    }

    // ---- Screenshot ----

    /// <summary>Saat pengambilan layar dimulai, island menutup agar tidak menghalangi.</summary>
    private void OnCaptureStarted(object? sender, EventArgs e)
    {
        _collapseTimer.Stop();
        IsExpanded = false;
    }

    // ---- Rekam layar ----

    /// <summary>Perekaman mulai atau berhenti.</summary>
    private void OnRecordingStateChanged(object? sender, EventArgs e)
    {
        bool recording = _recordingService.IsRecording;
        UpdateRecordingTime();

        if (recording && !IsRecording)
        {
            // Perekaman baru mulai: tutup island agar pil "Recording" terlihat.
            _collapseTimer.Stop();
            IsExpanded = false;
        }

        IsRecording = recording;
    }

    private void UpdateRecordingTime()
    {
        TimeSpan elapsed = _recordingService.Elapsed;

        RecordingTimeText = elapsed.TotalHours >= 1
            ? elapsed.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : elapsed.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }

    // StopRecordingAsync -> StopRecordingCommand. Dipakai tombol stop di pil "Recording".
    [RelayCommand]
    private async Task StopRecordingAsync()
    {
        await _recordingService.StopAsync();
    }

    // ---- Seret file ke Shelf ----

    /// <summary>
    /// Dipanggil terus-menerus selama file diseret di atas island.
    /// Island otomatis terbuka ke tab Shelf agar area drop terlihat.
    /// </summary>
    public void OnFileDragOver()
    {
        // Tab Shelf dimatikan: seret file ke island diabaikan.
        if (!IsShelfTabEnabled)
        {
            return;
        }

        _isPointerInside = true;
        _collapseTimer.Stop();

        if (IsNotificationVisible)
        {
            DismissNotification();
        }

        Shelf.IsDragOver = true;
        Shelf.SelectedSection = ShelfSection.File;
        SelectedTab = IslandTab.Shelf;
        IsExpanded = true;
    }

    /// <summary>File yang diseret keluar dari island tanpa dijatuhkan.</summary>
    public void OnFileDragLeft()
    {
        _isPointerInside = false;
        Shelf.IsDragOver = false;

        if (IsExpanded && _settingsService.Current.AutoCollapse && !IsKeyboardActive)
        {
            _collapseTimer.Interval = TimeSpan.FromMilliseconds(_settingsService.Current.CollapseDelayMs);
            _collapseTimer.Start();
        }
    }

    /// <summary>File dijatuhkan ke island: simpan ke Shelf.</summary>
    public void OnFilesDropped(string[] paths)
    {
        if (!IsShelfTabEnabled)
        {
            return;
        }

        Shelf.AddDroppedPaths(paths);
        Shelf.IsDragOver = false;

        // Mouse masih di dalam island setelah drop. Island menutup lagi saat mouse keluar.
        _isPointerInside = true;
    }

    /// <summary>
    /// Dipanggil oleh View saat island mulai atau selesai menerima ketikan keyboard.
    /// Saat selesai dan mouse sudah di luar island, island baru boleh menutup.
    /// </summary>
    public void SetKeyboardActive(bool active)
    {
        IsKeyboardActive = active;

        if (!active && IsExpanded && !_isPointerInside && _settingsService.Current.AutoCollapse)
        {
            _collapseTimer.Interval = TimeSpan.FromMilliseconds(_settingsService.Current.CollapseDelayMs);
            _collapseTimer.Start();
        }
    }

    /// <summary>
    /// Pengaman terakhir sebelum island menutup: diisi oleh View, mengembalikan true jika pointer
    /// ternyata masih di atas island (misalnya event "mouse keluar" datang lebih dulu dari kenyataannya).
    /// </summary>
    public Func<bool>? PointerInsideProbe { get; set; }

    private void OnCollapseTimerTick(object? sender, EventArgs e)
    {
        _collapseTimer.Stop();

        // Island TIDAK menutup selama: pengguna sedang mengetik, ada file yang sedang diseret di atasnya,
        // atau pointer ternyata masih di atas island. Penutupan dicoba lagi saat pointer keluar berikutnya.
        if (IsKeyboardActive || Shelf.IsDragOver || PointerInsideProbe?.Invoke() == true)
        {
            return;
        }

        IsExpanded = false;
    }

    // ---- Notifikasi ----

    /// <summary>
    /// Island yang sedang dibuka (pengguna sedang melihat) atau disembunyikan karena fullscreen tidak menampilkan overlay.
    /// Bila overlay sedang tampil saat island dibuka, antrean dikosongkan oleh OverlayService. Suara notifikasi
    /// (hanya untuk notifikasi teks) tetap diputar oleh NotificationService.
    /// </summary>
    private void UpdateOverlaySuppression()
    {
        _overlayService.IsSuppressed = IsExpanded || ShouldHideForFullscreen;
    }

    /// <summary>Overlay yang tampil berubah (muncul, diperbarui di tempat, diganti, atau hilang).</summary>
    private void OnOverlayChanged(object? sender, EventArgs e)
    {
        OverlayRequest? overlay = _overlayService.Current;
        if (overlay is null)
        {
            IsNotificationVisible = false;
            return;
        }

        // Ukuran island hanya perlu dihitung ulang bila jenis berganti atau teks chip berganti.
        // Bar level yang terus bergerak hanya mengubah OverlayLevel dan OverlayValueText.
        bool sizeChanged = overlay.Kind != ActiveOverlayKind
                           || (overlay.Kind == OverlayKind.Chip && overlay.Title != NotificationTitle);

        NotificationTitle = overlay.Title;
        NotificationMessage = overlay.Message;
        NotificationGlyph = overlay.Glyph;
        NotificationThumbnail = overlay.Thumbnail; // null untuk overlay tanpa gambar, jadi pratinjau lama tidak menempel
        OverlayIconKey = overlay.IconKey;
        OverlayLevel = overlay.Level;
        OverlayValueText = overlay.ValueText;
        ActiveOverlayKind = overlay.Kind;

        _collapseTimer.Stop();

        if (IsNotificationVisible)
        {
            if (sizeChanged)
            {
                OverlayRevision++;
            }
        }
        else
        {
            IsNotificationVisible = true;
        }
    }

    private void DismissNotification()
    {
        _overlayService.Dismiss();
    }

    // ---- Perintah tombol ----

    [RelayCommand]
    private void OpenSettings()
    {
        _windowService.ShowSettings();
    }

    // Nama command dibuat otomatis: TogglePlayPauseAsync -> TogglePlayPauseCommand, dst.
    [RelayCommand]
    private async Task TogglePlayPauseAsync()
    {
        await _mediaService.TogglePlayPauseAsync();
    }

    [RelayCommand]
    private async Task NextTrackAsync()
    {
        await _mediaService.NextTrackAsync();
    }

    [RelayCommand]
    private async Task PreviousTrackAsync()
    {
        await _mediaService.PreviousTrackAsync();
    }

    // Parameter: pecahan 0.0 sampai 1.0 dari SeekBar. Command yang dihasilkan: SeekCommand.
    [RelayCommand]
    private async Task SeekAsync(double fraction)
    {
        var media = _mediaService.Current;
        if (!media.CanSeek || media.Duration <= TimeSpan.Zero)
        {
            return;
        }

        fraction = Math.Clamp(fraction, 0, 1);
        TimeSpan target = TimeSpan.FromSeconds(media.Duration.TotalSeconds * fraction);

        // Tampilkan hasilnya langsung, jangan menunggu laporan dari pemutar.
        MediaProgress = fraction;
        MediaElapsedText = FormatMediaTime(target);
        MediaRemainingText = "-" + FormatMediaTime(media.Duration - target);

        await _mediaService.SeekAsync(target);
    }

    [RelayCommand]
    private void ToggleTimer()
    {
        if (IsTimerRunning)
        {
            _timerService.Pause();
        }
        else
        {
            _timerService.StartOrResume();
        }
    }

    [RelayCommand]
    private void ResetTimer()
    {
        _timerService.Reset();
    }

    /// <summary>
    /// Kartu "Focus" di tab Home: buka tab Timer, lalu mulai Pomodoro.
    /// Timer atau Stopwatch yang sedang aktif tidak diganggu (tab Timer hanya dibuka).
    /// Pomodoro yang sedang di-pause dilanjutkan, yang sedang berjalan dibiarkan.
    /// </summary>
    [RelayCommand]
    private void StartFocus()
    {
        if (!IsTimerTabEnabled)
        {
            return; // tab Timer dimatikan (kartunya juga disembunyikan di Home)
        }

        SelectedTab = IslandTab.Timer;

        if (_timerService.IsActive && _timerService.Mode != TimerMode.Pomodoro)
        {
            return;
        }

        // SetMode tidak berpengaruh jika sudah Pomodoro. Saat Idle atau Completed (Timer habis), mode diganti dan timer di-reset.
        _timerService.SetMode(TimerMode.Pomodoro);

        // Idle: mulai sesi fokus. Paused: lanjutkan. Running: tidak melakukan apa-apa.
        _timerService.StartOrResume();
    }

    // Parameter dikirim dari XAML sebagai teks, misalnya CommandParameter="5".
    [RelayCommand]
    private void SetCountdownMinutes(string? minutes)
    {
        if (int.TryParse(minutes, out int value))
        {
            _timerService.SetCountdownMinutes(value);
        }
    }

    [RelayCommand]
    private void AdjustCountdownMinutes(string? delta)
    {
        if (int.TryParse(delta, out int value))
        {
            _timerService.AdjustCountdownMinutes(value);
        }
    }

    partial void OnTimerModeIndexChanged(int value)
    {
        if (_isSyncingTimer)
        {
            return;
        }

        _timerService.SetMode((TimerMode)Math.Clamp(value, 0, 2));
        UpdateTimer(); // jika service menolak (timer aktif), kembalikan pilihan ke keadaan sebenarnya
    }

    // ---- Pilihan jenis timer (dari tombol radio) ----

    partial void OnIsCountdownModeChanged(bool value)
    {
        if (value && !_isSyncingTimer)
        {
            _timerService.SetMode(TimerMode.Countdown);
        }
    }

    partial void OnIsPomodoroModeChanged(bool value)
    {
        if (value && !_isSyncingTimer)
        {
            _timerService.SetMode(TimerMode.Pomodoro);
        }
    }

    partial void OnIsStopwatchModeChanged(bool value)
    {
        if (value && !_isSyncingTimer)
        {
            _timerService.SetMode(TimerMode.Stopwatch);
        }
    }

    // ---- Jam ----

    private void OnClockTicked(object? sender, EventArgs e)
    {
        UpdateClock();

        // Timer perekaman memakai detak jam yang sudah ada, jadi tidak perlu timer baru.
        if (IsRecording)
        {
            UpdateRecordingTime();
        }

        // Progress lagu hanya perlu bergerak saat tab Music sedang terlihat dan lagu diputar.
        // Memakai detak jam yang sudah ada, jadi tidak perlu timer baru.
        if (IsExpanded && SelectedTab == IslandTab.Music && HasMedia && IsMediaPlaying)
        {
            UpdateMediaProgress();
        }
    }

    private void UpdateClock()
    {
        var now = _clockService.Now;
        CurrentTime = now.ToString("HH:mm", CultureInfo.InvariantCulture);
        CurrentDate = now.ToString("dddd, d MMMM", CultureInfo.CurrentCulture);
        GreetingText = GetGreeting(now.Hour);
        UpdateCollapsedText();
    }

    // Sapaan menurut jam: 05-11 pagi, 12-16 siang, 17-20 sore/malam awal, 21-04 malam.
    private static string GetGreeting(int hour) => hour switch
    {
        >= 5 and < 12 => "Good morning",
        >= 12 and < 17 => "Good afternoon",
        >= 17 and < 21 => "Good evening",
        _ => "Good night"
    };

    private void UpdateCollapsedText()
    {
        CollapsedCenterText = IsTimerActive ? TimerDisplayText : CurrentTime;
    }

    // ---- Baterai ----

    private void OnBatteryChanged(object? sender, EventArgs e)
    {
        UpdateBattery();
    }

    private void UpdateBattery()
    {
        var battery = _batteryService.Current;

        HasBattery = battery.HasBattery;
        BatteryPercent = battery.Percent;
        BatteryText = $"{battery.Percent}%";
        IsBatteryCharging = battery.IsCharging;
        IsBatteryLow = battery.IsLow;

        BatteryStatusText = battery.IsFull ? "Fully charged"
            : battery.IsCharging ? "Charging"
            : battery.IsLow ? "Battery low"
            : battery.IsPluggedIn ? "Plugged in"
            : "On battery";

        // Notifikasi hanya muncul saat baterai BARU SAJA menjadi rendah atau penuh,
        // bukan setiap kali nilainya dibaca. Pembacaan pertama tidak memicu notifikasi.
        if (_wasBatteryLow == false && battery.IsLow)
        {
            _notificationService.Show(
                "Battery low",
                $"{battery.Percent}% left. Plug in your charger.",
                NotificationKind.Warning);
        }

        // Bila animasi charging menyala, "Fully charged" sudah ditampilkan sekali oleh ChargingOverlayService: jangan dobel.
        if (_wasBatteryFull == false && battery.IsFull && !_settingsService.Current.ChargingAnimationEnabled)
        {
            _notificationService.Show(
                "Battery full",
                "Fully charged. You can unplug now.",
                NotificationKind.BatteryFull);
        }

        _wasBatteryLow = battery.IsLow;
        _wasBatteryFull = battery.IsFull;
    }

    // ---- Musik ----

    private void OnMediaChanged(object? sender, EventArgs e)
    {
        UpdateMedia();
    }

    private void UpdateMedia()
    {
        var media = _mediaService.Current;

        HasMedia = media.HasSession;
        ShowNoMediaMessage = !media.HasSession;
        MediaTitle = string.IsNullOrWhiteSpace(media.Title) ? "Unknown title" : media.Title;
        MediaArtist = media.Artist;
        MediaSourceApp = media.SourceApp;
        MediaArtwork = media.Artwork;
        UpdateGlassBackdrop(); // sampul baru: latar Glass dibuat ulang (hanya bila gaya Glass)
        UpdateAccent(media.Artwork);
        IsMediaPlaying = media.IsPlaying;
        PlayPauseGlyph = media.IsPlaying ? "\uE769" : "\uE768";
        CanPlayPause = media.CanPlayPause;
        CanGoNext = media.CanNext;
        CanGoPrevious = media.CanPrevious;
        CanSeek = media.CanSeek;
        _ = UpdateSourceIconAsync(media.SourceAppId);

        NowPlayingText = !media.HasSession ? string.Empty
            : string.IsNullOrWhiteSpace(media.Artist) ? MediaTitle
            : $"{MediaTitle} \u2014 {media.Artist}";

        UpdateMediaProgress();
        UpdateAudioMeter();
    }

    private void UpdateMediaProgress()
    {
        var media = _mediaService.Current;

        if (!media.HasSession)
        {
            MediaProgress = 0;
            MediaElapsedText = "0:00";
            MediaDurationText = "0:00";
            MediaRemainingText = "-0:00";
            return;
        }

        TimeSpan position = media.GetEstimatedPosition();
        MediaElapsedText = FormatMediaTime(position);

        if (media.Duration > TimeSpan.Zero)
        {
            MediaProgress = Math.Clamp(position.TotalSeconds / media.Duration.TotalSeconds, 0, 1);
            MediaDurationText = FormatMediaTime(media.Duration);
            MediaRemainingText = "-" + FormatMediaTime(media.Duration - position);
        }
        else
        {
            // Siaran langsung atau pemutar yang tidak melaporkan durasi.
            MediaProgress = 0;
            MediaDurationText = "--:--";
            MediaRemainingText = "--:--";
        }
    }

    /// <summary>
    /// Memuat ikon aplikasi pemutar di thread latar (mencari proses bisa memakan beberapa milidetik),
    /// lalu memasangnya jika aplikasi itu masih yang sedang aktif. Hanya berjalan saat identitas aplikasi berubah.
    /// </summary>
    private async Task UpdateSourceIconAsync(string appId)
    {
        if (appId == _mediaSourceIconAppId)
        {
            return;
        }

        _mediaSourceIconAppId = appId;

        if (string.IsNullOrEmpty(appId))
        {
            MediaSourceIcon = null;
            return;
        }

        ImageSource? icon = await Task.Run(() => AppIconResolver.GetIcon(appId));

        if (appId == _mediaSourceIconAppId)
        {
            MediaSourceIcon = icon;
        }
    }

    private static string FormatMediaTime(TimeSpan time)
    {
        return $"{(int)time.TotalMinutes}:{time.Seconds:00}";
    }

    // ---- Timer ----

    private void OnTimerChanged(object? sender, EventArgs e)
    {
        UpdateTimer();
    }

    private void UpdateTimer()
    {
        // Selama menyalin nilai dari service, jangan anggap perubahan radio sebagai klik pengguna.
        _isSyncingTimer = true;
        try
        {
            var mode = _timerService.Mode;
            var status = _timerService.Status;

            IsCountdownMode = mode == TimerMode.Countdown;
            IsPomodoroMode = mode == TimerMode.Pomodoro;
            IsStopwatchMode = mode == TimerMode.Stopwatch;

            TimerDisplayText = FormatTimerTime(_timerService.DisplayTime);
            IsTimerRunning = status == TimerStatus.Running;
            IsTimerActive = _timerService.IsActive;
            TimerPrimaryGlyph = IsTimerRunning ? "\uE769" : "\uE768";

            CanChangeTimerMode = !IsTimerActive;
            ShowCountdownPresets = mode == TimerMode.Countdown && !IsTimerActive;
            TimerLabelText = BuildTimerLabel(mode, status);
            FocusCardSubtitle = BuildFocusCardSubtitle(mode, status);

            UpdateTimerView(mode, status);
        }
        finally
        {
            _isSyncingTimer = false;
        }

        UpdateCollapsedText();
    }

    private void UpdateTimerView(TimerMode mode, TimerStatus status)
    {
        TimeSpan time = _timerService.DisplayTime;

        TimerModeIndex = (int)mode;
        TimerFontSize = time.TotalHours >= 1 ? 24 : 32;
        CanResetTimer = status != TimerStatus.Idle;
        TimerPrimaryText = status switch
        {
            TimerStatus.Running => "Pause",
            TimerStatus.Paused => "Resume",
            _ => "Start"
        };

        ShowSessionDots = mode == TimerMode.Pomodoro;
        ShowTimerStatusLabel = !ShowCountdownPresets && mode != TimerMode.Pomodoro;

        if (mode == TimerMode.Stopwatch)
        {
            TimerProgress = time.TotalSeconds % 60 / 60.0;
        }
        else
        {
            double total = _timerService.TotalTime.TotalSeconds;
            TimerProgress = total > 0 ? Math.Clamp(time.TotalSeconds / total, 0, 1) : 0;
        }

        TimerRingLabel = status switch
        {
            TimerStatus.Paused => "Paused",
            TimerStatus.Completed => "Done",
            _ => mode switch
            {
                TimerMode.Countdown => "Timer",
                TimerMode.Stopwatch => "Stopwatch",
                _ => _timerService.Phase switch
                {
                    PomodoroPhase.Focus => $"Focus {_timerService.PomodoroSessionNumber}/{_timerService.PomodoroSessionsPerCycle}",
                    PomodoroPhase.ShortBreak => "Short break",
                    _ => "Long break"
                }
            }
        };

        int today = _timerService.FocusSessionsToday;
        if (today != _lastSessionsToday)
        {
            _lastSessionsToday = today;
            var dots = new bool[SessionDotCount];
            for (int i = 0; i < dots.Length; i++)
            {
                dots[i] = i < today;
            }
    
            SessionDots = dots;
            SessionsTodayText = today > SessionDotCount ? $"{SessionDotCount}+" : today.ToString();
        }
    }

    private void OnTimerPhaseCompleted(object? sender, EventArgs e)
    {
        TimerPulseCount++;
    }

    // Teks kartu "Focus" di tab Home mengikuti keadaan timer.
    private string BuildFocusCardSubtitle(TimerMode mode, TimerStatus status)
    {
        if (mode == TimerMode.Pomodoro && status == TimerStatus.Running)
        {
            return "In progress";
        }

        if (mode == TimerMode.Pomodoro && status == TimerStatus.Paused)
        {
            return "Resume";
        }

        if (_timerService.IsActive)
        {
            return "Timer in use"; // Timer atau Stopwatch milik pengguna sedang aktif, tidak disentuh
        }

        return $"Start {_timerService.PomodoroFocusMinutes} min";
    }

    private string BuildTimerLabel(TimerMode mode, TimerStatus status)
    {
        string statusText = status switch
        {
            TimerStatus.Running => "Running",
            TimerStatus.Paused => "Paused",
            TimerStatus.Completed => "Done",
            _ => "Ready"
        };

        return mode switch
        {
            TimerMode.Countdown => $"Timer \u00B7 {statusText}",
            TimerMode.Stopwatch => $"Stopwatch \u00B7 {statusText}",
            _ => $"{GetPhaseName(_timerService.Phase)} {_timerService.PomodoroSessionNumber}/{_timerService.PomodoroSessionsPerCycle} \u00B7 {statusText}"
        };
    }

    private static string GetPhaseName(PomodoroPhase phase)
    {
        return phase switch
        {
            PomodoroPhase.Focus => "Focus",
            PomodoroPhase.ShortBreak => "Short break",
            _ => "Long break"
        };
    }

    private static string FormatTimerTime(TimeSpan time)
    {
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes:00}:{time.Seconds:00}";
    }

    public void Dispose()
    {
        _collapseTimer.Stop();
        _collapseTimer.Tick -= OnCollapseTimerTick;

        _clockService.Ticked -= OnClockTicked;
        _batteryService.Changed -= OnBatteryChanged;
        _mediaService.Changed -= OnMediaChanged;
        _timerService.Changed -= OnTimerChanged;
        _timerService.PhaseCompleted -= OnTimerPhaseCompleted;
        _overlayService.Changed -= OnOverlayChanged;
        _captureService.CaptureStarted -= OnCaptureStarted;
        _recordingService.StateChanged -= OnRecordingStateChanged;
        _windowContext.Changed -= OnContextChanged;
        _settingsService.SettingsChanged -= OnSettingsChanged;
        _meeting.Changed -= OnMeetingChanged;
        Todo.Items.CollectionChanged -= OnTodoItemsChanged;
        Mixer.Apps.CollectionChanged -= OnMixerAppsChanged;
        Mixer.SetActive(false);
        ControlPanel.SetActive(false);
        Lyrics.PropertyChanged -= OnLyricsPropertyChanged;
    }
}
