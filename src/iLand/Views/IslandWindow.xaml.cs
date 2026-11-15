using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using iLand.Helpers;
using iLand.Interfaces;
using iLand.Models;
using iLand.Native;
using iLand.ViewModels;

namespace iLand.Views;

/// <summary>
/// Window island. Tugas file ini hanya urusan tampilan:
/// posisi di monitor, animasi bentuk, input keyboard, menyembunyikan saat fullscreen,
/// dan meneruskan event mouse ke ViewModel.
/// </summary>
public partial class IslandWindow : Window
{
    // Ukuran bentuk hitam yang terlihat pada tiap mode.
    private const double CollapsedWidth = 200;

    // Lebar pill di mode media. Area deteksi smart collapse TETAP memakai CollapsedWidth (200), jadi lebar pill
    // yang berubah-ubah tidak membuat island bolak-balik antara normal dan mini.
    private const double PillMediaWidth = 240;
    private const double PillLyricMinWidth = 240;
    private const double PillLyricMaxWidth = 560;
    // Bagian pill di luar teks lirik: margin 16 x 2, sampul 22, jarak 12 x 2, visualizer 22, cadangan 6.
    private const double PillLyricChrome = 106;

    // Lebar tujuan terakhir pill normal. Dipakai agar animasi lebar tidak diulang bila tujuannya sama.
    private double _lastPillTarget = double.NaN;
    private const double CollapsedHeight = 34;

    // Mode mini: notch kecil menempel di tepi atas (sisi atas rata, hanya sudut bawah yang melengkung).
    private const double MiniWidth = 72;
    private const double MiniHeight = 10;
    private const double MiniBottomRadius = 7;
    // Mode notifikasi: pill sedang yang muncul sebentar lalu hilang.
    private const double NotificationWidth = 330;
    private const double NotificationHeight = 58;

    // Mode perekaman: pill sedang berisi titik merah, timer, dan tombol stop.
    private const double RecordingWidth = 250;
    private const double RecordingHeight = 44;

    // Celah antara tepi atas layar dan island (pada mode normal, notifikasi, dan expanded).
    private const double TopGap = 6;

    // CornerRadius tidak bisa dianimasikan langsung di WPF.
    // Solusinya: buat dua property angka sendiri (DependencyProperty) yang bisa dianimasikan,
    // lalu setiap kali nilainya berubah, kita isi CornerRadius milik Border.
    // Radius atas dan bawah dipisah supaya mode mini bisa berbentuk notch (atas rata).
    public static readonly DependencyProperty IslandTopRadiusProperty =
        DependencyProperty.Register(
            nameof(IslandTopRadius),
            typeof(double),
            typeof(IslandWindow),
            new PropertyMetadata(CollapsedHeight / 2, OnIslandRadiusChanged));

    public static readonly DependencyProperty IslandBottomRadiusProperty =
        DependencyProperty.Register(
            nameof(IslandBottomRadius),
            typeof(double),
            typeof(IslandWindow),
            new PropertyMetadata(CollapsedHeight / 2, OnIslandRadiusChanged));

    public double IslandTopRadius
    {
        get => (double)GetValue(IslandTopRadiusProperty);
        set => SetValue(IslandTopRadiusProperty, value);
    }

    public double IslandBottomRadius
    {
        get => (double)GetValue(IslandBottomRadiusProperty);
        set => SetValue(IslandBottomRadiusProperty, value);
    }

    private static void OnIslandRadiusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var window = (IslandWindow)d;
        double top = window.IslandTopRadius;
        double bottom = window.IslandBottomRadius;

        // Urutan CornerRadius: kiri-atas, kanan-atas, kanan-bawah, kiri-bawah.
        window.Island.CornerRadius = new CornerRadius(top, top, bottom, bottom);
        window.UpdateIslandClip();
    }

    /// <summary>
    /// Menyamakan bentuk klip dengan bentuk island. Dipanggil dari dua tempat: saat ukuran island berubah
    /// (setiap frame animasi ukuran) dan saat radius berubah. RectangleGeometry hanya punya satu radius,
    /// jadi dipakai yang lebih kecil antara atas dan bawah. Beda keduanya hanya terjadi di mode mini, yang isinya tidak tampil.
    /// </summary>
    private void UpdateIslandClip()
    {
        // Dipanggil sangat sering saat animasi (ukuran dan radius berubah tiap frame). Setiap penugasan ke
        // RectangleGeometry membuat WPF menggambar ulang, jadi hanya diisi bila nilainya benar-benar berubah.
        var bounds = new Rect(0, 0, Island.ActualWidth, Island.ActualHeight);
        if (_islandClip.Rect != bounds)
        {
            _islandClip.Rect = bounds;
        }

        CornerRadius radius = Island.CornerRadius;
        double clipRadius = Math.Min(radius.TopLeft, radius.BottomLeft);
        if (_islandClip.RadiusX != clipRadius)
        {
            _islandClip.RadiusX = clipRadius;
            _islandClip.RadiusY = clipRadius;
        }
    }

    private readonly IslandViewModel _viewModel;
    private readonly ISettingsService _settingsService;
    private readonly IMonitorService _monitorService;
    private readonly IWindowContextService _windowContext;

    private string _appliedMonitorName = "\u0000"; // nilai awal yang pasti berbeda dari pengaturan mana pun
    private bool? _appliedHideFromCapture; // null = belum pernah diterapkan

    // Lebar bingkai hover tambahan di kiri, kanan, dan bawah island yang sedang expanded (piksel).
    // Harus muat di window: lebar island maksimum 640 + 2 x 12 = 664 <= 720, tinggi 290 + 6 + 12 = 308 <= 340.
    private const double HoverHaloPx = 12;

    // true = pointer keluar dari island selagi tombol mouse masih ditekan, dan keputusan "pointer keluar"
    // ditunda sampai tombol dilepas (misalnya saat menggeser slider sampai ke luar island).
    private bool _pointerLeftWhilePressed;
    private bool _hiddenByFullscreen;
    private bool _isKeyboardInputEnabled;

    // Tab yang sedang ditampilkan. Dipakai untuk tahu panel mana yang harus memudar keluar saat tab berpindah.
    private IslandTab _shownTab;

    // Nomor "versi animasi" tiap panel tab (indeks sama dengan enum IslandTab). Naik setiap kali panel itu mendapat
    // animasi baru atau di-reset. Handler "selesai" milik animasi lama membandingkan nomornya, dan diabaikan jika
    // sudah ada animasi yang lebih baru. Inilah yang membuat klik tab berulang cepat tidak meninggalkan panel tersisa.
    private int[] _tabPanelVersion = Array.Empty<int>();

    // Elemen utama isi tiap panel yang sedang dianimasikan "muncul bertahap", dan nomor versi animasinya.
    // Indeks = indeks panel (sama dengan enum IslandTab).
    private List<FrameworkElement>[] _staggerTargets = Array.Empty<List<FrameworkElement>>();
    private int[] _staggerVersion = Array.Empty<int>();

    // Apakah ApplyState terakhir dalam keadaan expanded (untuk tahu kapan island BARU dibuka atau ditutup).
    private bool _wasExpandedApplied;

    // Judul tab yang sedang tampil di header (untuk crossfade ke judul baru).
    private string _shownTitle = string.Empty;

    // Bentuk klip island: persegi bersudut membulat yang selalu mengikuti ukuran dan radius island.
    private readonly RectangleGeometry _islandClip = new();

    /// <summary>Durasi nyata sebuah gerak: durasi dasar dari MotionTokens dikali pengali dari Settings.</summary>
    private TimeSpan Scaled(double baseMs)
    {
        return MotionTokens.Scale(baseMs, _settingsService.Current.AnimationDurationMs);
    }

    public IslandWindow(
        IslandViewModel viewModel,
        ISettingsService settingsService,
        IMonitorService monitorService,
        IWindowContextService windowContext)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _settingsService = settingsService;
        _monitorService = monitorService;
        _windowContext = windowContext;
        DataContext = viewModel;

        _tabPanelVersion = new int[TabHost.Children.Count];
        _staggerVersion = new int[TabHost.Children.Count];
        _staggerTargets = new List<FrameworkElement>[TabHost.Children.Count];
        for (int i = 0; i < _staggerTargets.Length; i++)
        {
            _staggerTargets[i] = new List<FrameworkElement>();
        }

        // Awalnya hanya panel tab yang terpilih yang terlihat.
        _shownTab = viewModel.SelectedTab;
        _shownTitle = viewModel.SelectedTabTitle;
        ShowOnlyTab(_shownTab); // juga menaruh indikator tab di item aktif

        // ClipToBounds pada Border hanya memotong berbentuk persegi, jadi isi bisa bocor di sudut membulat.
        // Clip berbentuk persegi membulat dipasang dan diperbarui tiap ukuran atau radius island berubah.
        Island.Clip = _islandClip;
        Island.SizeChanged += (_, _) => UpdateIslandClip();

        Island.Opacity = _settingsService.Current.Opacity;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.Lyrics.PropertyChanged += OnLyricsPropertyChanged;
        _settingsService.SettingsChanged += OnSettingsChanged;
        _monitorService.MonitorsChanged += OnMonitorsChanged;

        // Kotak teks (misalnya "Add a task") butuh keyboard. Window island sengaja tidak mengambil
        // fokus agar tidak mengganggu aplikasi lain, jadi kita aktifkan HANYA saat kotak teks diklik.
        // Parameter true = tetap dipanggil walaupun event sudah ditandai "handled" oleh kontrol lain.
        AddHandler(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnWindowPreviewMouseDown), true);
        Deactivated += OnWindowDeactivated;

        // Tombol mouse dilepas atau capture mouse hilang: selesaikan keputusan "pointer keluar" yang tertunda.
        // Parameter true = tetap dipanggil walaupun event sudah ditandai "handled" oleh kontrol lain.
        AddHandler(PreviewMouseUpEvent, new MouseButtonEventHandler(OnWindowPreviewMouseUp), true);
        LostMouseCapture += OnWindowLostMouseCapture;

        // Window disembunyikan atau ditampilkan (misalnya lewat menu tray): meter suara dan lirik ikut berhenti atau lanjut.
        IsVisibleChanged += OnWindowIsVisibleChanged;

        // ViewModel memakai ini sebagai pengaman terakhir sebelum menutup island.
        _viewModel.PointerInsideProbe = IsPointerOverIslandArea;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Handle window baru tersedia di sini. Sembunyikan dari Alt+Tab dan cegah pencurian fokus.
        WindowStyleHelper.ApplyOverlayStyle(this);

        // Terapkan pilihan "Hide island from screenshots" dari Settings (bawaan: island disembunyikan dari tangkapan layar).
        ApplyCaptureVisibility();

        PositionOnMonitor();
        ApplyState();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        PositionOnMonitor();
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.Lyrics.PropertyChanged -= OnLyricsPropertyChanged;
        _settingsService.SettingsChanged -= OnSettingsChanged;
        _monitorService.MonitorsChanged -= OnMonitorsChanged;
        Deactivated -= OnWindowDeactivated;
        LostMouseCapture -= OnWindowLostMouseCapture;
        IsVisibleChanged -= OnWindowIsVisibleChanged;
        _viewModel.PointerInsideProbe = null;
        base.OnClosed(e);
    }

    private void OnWindowIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _viewModel.SetWindowVisible(IsVisible);
    }

    // ---- Input keyboard (hanya saat kotak teks diklik) ----

    private void OnWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindParent<TextBoxBase>(source) is TextBoxBase textBox)
        {
            EnableKeyboardInput(textBox);
        }
    }

    private void EnableKeyboardInput(TextBoxBase textBox)
    {
        if (!_isKeyboardInputEnabled)
        {
            _isKeyboardInputEnabled = true;
            _viewModel.SetKeyboardActive(true);

            // Izinkan window menjadi aktif, lalu aktifkan. Ini diizinkan Windows karena
            // iLand baru saja menerima klik dari pengguna.
            WindowStyleHelper.SetActivatable(this, true);
            Activate();
        }

        // Fokus keyboard dipindahkan setelah window benar-benar aktif.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsActive)
            {
                // Windows menolak mengaktifkan window: kembalikan ke keadaan semula agar island tidak "macet".
                OnWindowDeactivated(this, EventArgs.Empty);
                return;
            }

            textBox.Focus();
            Keyboard.Focus(textBox);
        }), DispatcherPriority.Input);
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        if (!_isKeyboardInputEnabled)
        {
            return;
        }

        _isKeyboardInputEnabled = false;

        // Kembalikan ke perilaku overlay: tidak mengambil fokus dari aplikasi lain.
        WindowStyleHelper.SetActivatable(this, false);
        _viewModel.SetKeyboardActive(false);
    }

    /// <summary>Mencari elemen induk bertipe T dari sebuah elemen (naik ke atas pohon tampilan).</summary>
    private static T? FindParent<T>(DependencyObject? start) where T : DependencyObject
    {
        DependencyObject? current = start;

        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = current is Visual
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return null;
    }

    // ---- Posisi di monitor ----

    /// <summary>
    /// Taruh window di tengah atas monitor yang dipilih di Settings (kosong = monitor utama).
    /// Posisi dihitung dalam PIKSEL fisik dan dipasang lewat SetWindowPos,
    /// karena itu cara paling andal untuk setup banyak monitor.
    /// </summary>
    private void PositionOnMonitor()
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        string monitorName = _settingsService.Current.MonitorDeviceName;
        MonitorInfo monitor = _monitorService.GetMonitorOrPrimary(monitorName);
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;

        double windowWidthPx = Width * scale;
        int x = (int)Math.Round(monitor.Bounds.Left + (monitor.Bounds.Width - windowWidthPx) / 2);
        int y = (int)Math.Round(monitor.Bounds.Top);

        NativeMethods.SetWindowPos(
            handle, IntPtr.Zero, x, y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);

        _appliedMonitorName = monitorName;

        // Beri tahu service: di sinilah pill island NORMALNYA berada.
        // Area ini selalu sama (ukuran pill normal), apa pun mode island saat ini.
        // Dengan begitu mode mini tidak "mengecek dirinya sendiri" dan tidak terjadi bolak-balik.
        double pillWidth = CollapsedWidth * scale;
        double pillHeight = CollapsedHeight * scale;
        var pillArea = new Rect(
            monitor.Bounds.Left + (monitor.Bounds.Width - pillWidth) / 2,
            monitor.Bounds.Top + TopGap * scale,
            pillWidth,
            pillHeight);
        _windowContext.SetIslandZone(pillArea);
    }

    private void OnMonitorsChanged(object? sender, EventArgs e)
    {
        PositionOnMonitor();
    }

    // ---- Reaksi terhadap perubahan state ----

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(IslandViewModel.IsExpanded):
            case nameof(IslandViewModel.IsMini):
            case nameof(IslandViewModel.IsNotificationVisible):
            case nameof(IslandViewModel.IsRecording):
                ApplyState();
                break;

            case nameof(IslandViewModel.ShouldHideForFullscreen):
                ApplyFullscreenRule();
                break;

            case nameof(IslandViewModel.SelectedTab):
                ChangeTab(_viewModel.SelectedTab);
                // Tinggi (dan lebar) island mengikuti tab baru. Saat tertutup, ukuran diterapkan ketika island dibuka.
                if (_viewModel.IsExpanded)
                {
                    ApplyExpandedSize();
                }
                else
                {
                    FreezeExpandedContentSize();
                }
                break;

            case nameof(IslandViewModel.TabItemCount):
                // Jumlah item pada tab dinamis (misalnya tugas bertambah) mengubah tinggi island.
                if (_viewModel.IsExpanded)
                {
                    ApplyExpandedSize();
                }
                else
                {
                    FreezeExpandedContentSize();
                }
                break;

            case nameof(IslandViewModel.PillLayout):
                // Isi pill berganti (idle, media, lirik): lebar pill ikut menyesuaikan.
                ApplyPillWidth();
                break;

            case nameof(IslandViewModel.WantsWideLayout):
                // Panel lirik dibuka atau ditutup: lebar island ikut berubah.
                if (_viewModel.IsExpanded)
                {
                    ApplyExpandedSize();
                }
                else
                {
                    FreezeExpandedContentSize();
                }
                break;
        }
    }

    // ---- Lebar pill (island tertutup) ----

    private void OnLyricsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LyricsViewModel.CurrentLineText)
            || _viewModel.PillLayout != PillLayoutMode.MediaLyrics)
        {
            return;
        }

        // Baris lirik berganti saat pill menampilkannya: pill menyesuaikan lebar dan teks baru masuk dari bawah.
        ApplyPillWidth();
        AnimatePillLyricIn();
    }

    /// <summary>Lebar pill normal menurut isinya. Mode lirik: sepanjang baris lirik (tidak dipotong), dibatasi min dan maks.</summary>
    private double GetPillWidth()
    {
        switch (_viewModel.PillLayout)
        {
            case PillLayoutMode.Media:
                return PillMediaWidth;

            case PillLayoutMode.MediaLyrics:
                double wanted = MeasurePillLyricWidth(_viewModel.Lyrics.CurrentLineText) + PillLyricChrome;
                return Math.Clamp(wanted, PillLyricMinWidth, PillLyricMaxWidth);

            default:
                return CollapsedWidth;
        }
    }

    /// <summary>Mengukur lebar teks lirik dengan huruf yang sama seperti PillLyricText (tanpa menunggu binding).</summary>
    private double MeasurePillLyricWidth(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(PillLyricText.FontFamily, PillLyricText.FontStyle, PillLyricText.FontWeight, PillLyricText.FontStretch),
            PillLyricText.FontSize,
            Brushes.White,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        return Math.Ceiling(formatted.WidthIncludingTrailingWhitespace);
    }

    /// <summary>Hanya mengubah LEBAR pill yang sedang tampil normal. Mode lain (expanded, notifikasi, rekam, mini) tidak disentuh.</summary>
    private void ApplyPillWidth()
    {
        bool isNormalPill = !_viewModel.IsExpanded
                            && !_viewModel.IsNotificationVisible
                            && !_viewModel.IsRecording
                            && !_viewModel.IsMini;
        if (!isNormalPill)
        {
            return;
        }

        double target = GetPillWidth();

        // Lebar teks dikunci ke ukuran barisnya SEBELUM pill melebar. Tanpa ini, teks baru yang lebih panjang dari
        // kolom yang masih sempit terpotong menjadi "..." lalu muncul utuh setelah pill selesai melebar (terlihat patah).
        UpdatePillLyricWidth();

        if (Math.Abs(target - _lastPillTarget) < 0.5)
        {
            return; // tujuan sama: biarkan animasi yang sedang berjalan selesai
        }

        _lastPillTarget = target;

        TimeSpan duration = MotionTokens.ReducedMotion ? TimeSpan.Zero : Scaled(MotionTokens.PillWidthMs);
        IslandHost.BeginAnimation(FrameworkElement.WidthProperty,
            new DoubleAnimation(target, duration) { EasingFunction = MotionTokens.PillWidthEase });
    }

    /// <summary>
    /// Mengunci lebar TextBlock lirik pill sesuai panjang barisnya (dibatasi lebar maksimum pill).
    /// Teks yang lebih lebar dari kolom saat animasi dipotong oleh tepi kolom, bukan diganti "...".
    /// </summary>
    private void UpdatePillLyricWidth()
    {
        if (_viewModel.PillLayout != PillLayoutMode.MediaLyrics)
        {
            PillLyricText.ClearValue(FrameworkElement.WidthProperty);
            return;
        }

        double textWidth = MeasurePillLyricWidth(_viewModel.Lyrics.CurrentLineText) + 2;
        PillLyricText.Width = Math.Min(textWidth, PillLyricMaxWidth - PillLyricChrome);
    }

    /// <summary>Baris lirik baru masuk: naik 6 px sambil memudar masuk (durasi mengikuti pengali animasi di Settings).</summary>
    private void AnimatePillLyricIn()
    {
        if (MotionTokens.ReducedMotion)
        {
            return;
        }

        TimeSpan duration = Scaled(MotionTokens.ContentInMs);
        TimeSpan delay = Scaled(MotionTokens.PillTextDelayMs);

        // Animasi ber-BeginTime: nilai awal diatur lokal dulu, karena selama jeda yang tampil nilai dasar.
        PillLyricText.BeginAnimation(UIElement.OpacityProperty, null);
        PillLyricText.Opacity = 0;
        PillLyricShift.BeginAnimation(TranslateTransform.YProperty, null);
        PillLyricShift.Y = 6;

        PillLyricText.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, duration) { BeginTime = delay, EasingFunction = MotionTokens.ContentInEase });
        PillLyricShift.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(6, 0, duration) { BeginTime = delay, EasingFunction = MotionTokens.ContentInEase });
    }

    // ---- Animasi perpindahan tab ----

    /// <summary>Panel tab di dalam TabHost berurutan sama dengan nilai enum IslandTab.</summary>
    private UIElement? GetTabPanel(IslandTab tab)
    {
        int index = (int)tab;
        return index >= 0 && index < TabHost.Children.Count ? TabHost.Children[index] : null;
    }

    /// <summary>Menampilkan hanya satu panel tab, langsung tanpa animasi. Semua sisa animasi dibersihkan.</summary>
    private void ShowOnlyTab(IslandTab tab)
    {
        for (int i = 0; i < TabHost.Children.Count; i++)
        {
            UIElement panel = TabHost.Children[i];
            ResetPanel(i, panel);
            panel.Visibility = i == (int)tab ? Visibility.Visible : Visibility.Collapsed;
        }

        ResetTabTitle();
        MoveTabIndicator(tab, animate: false);
    }

    /// <summary>
    /// Menggeser bentuk putih di tab bar ke item tab yang aktif. Posisi dihitung dari indeks enum,
    /// karena setiap item berukuran tetap dan tanpa jarak (TabLayout.TabBarItemWidth).
    /// Berpindah dekat memakai kurva dengan pantulan kecil. Berpindah jauh memakai kurva tanpa pantulan,
    /// supaya indikator tidak menonjol keluar dari pill di ujung tab bar.
    /// </summary>
    private void MoveTabIndicator(IslandTab tab, bool animate)
    {
        var shift = (TranslateTransform)TabIndicator.RenderTransform;
        double targetX = (int)tab * TabLayout.TabBarItemWidth;

        // Mode gerak berkurang: indikator pindah langsung (ikonnya tetap crossfade).
        if (!animate || MotionTokens.ReducedMotion)
        {
            shift.BeginAnimation(TranslateTransform.XProperty, null); // lepas animasi dulu, baru atur nilai
            shift.X = targetX;
            return;
        }

        // shift.X membaca posisi saat ini (termasuk yang sedang beranimasi), jadi jarak benar walau klik berulang cepat.
        double travelInTabs = Math.Abs(targetX - shift.X) / TabLayout.TabBarItemWidth;
        IEasingFunction ease = travelInTabs <= MotionTokens.IndicatorOvershootMaxTabs
            ? MotionTokens.IndicatorEase
            : MotionTokens.ResizeEase;

        shift.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(targetX, Scaled(MotionTokens.IndicatorMs)) { EasingFunction = ease });
    }

    /// <summary>
    /// Mengembalikan panel ke keadaan normal: tanpa animasi, terlihat penuh, bisa diklik.
    /// Menaikkan nomor versi sehingga handler selesai milik animasi lama diabaikan.
    /// </summary>
    private void ResetPanel(int index, UIElement panel, bool cancelStagger = true)
    {
        _tabPanelVersion[index]++;

        // Saat panel selesai memudar masuk, animasi bertahap isinya masih boleh lanjut (cancelStagger = false).
        if (cancelStagger)
        {
            CancelContentStagger(index);
        }

        panel.BeginAnimation(UIElement.OpacityProperty, null); // lepas animasi dulu, baru atur nilai
        panel.Opacity = 1;
        panel.IsHitTestVisible = true;
    }

    /// <summary>
    /// Tab berpindah: panel lama memudar keluar, panel baru memudar masuk sedikit lebih lambat dan tumpang tindih.
    /// Hanya perubahan transparansi (tanpa geser) supaya terasa tenang. Jika island sedang tertutup,
    /// panel diganti langsung tanpa animasi.
    /// </summary>
    private void ChangeTab(IslandTab newTab)
    {
        if (newTab == _shownTab)
        {
            return;
        }

        IslandTab oldTab = _shownTab;
        _shownTab = newTab;

        string oldTitle = _shownTitle;
        _shownTitle = _viewModel.SelectedTabTitle;

        if (!_viewModel.IsExpanded)
        {
            ShowOnlyTab(newTab);
            return;
        }

        FadeOutPanel((int)oldTab);
        FadeInPanel((int)newTab);
        AnimateTabTitle(oldTitle);
        MoveTabIndicator(newTab, animate: true);
    }

    private void FadeOutPanel(int index)
    {
        UIElement? panel = GetTabPanel((IslandTab)index);
        if (panel is null)
        {
            return;
        }

        int version = ++_tabPanelVersion[index];

        panel.IsHitTestVisible = false; // panel yang sedang pergi tidak boleh bisa diklik

        // Tanpa nilai awal: memudar dimulai dari transparansi panel saat ini,
        // jadi tetap mulus walaupun panel ini sedang di tengah gerak masuk.
        // Mode gerak berkurang: crossfade pendek yang sama untuk semua perpindahan.
        TimeSpan duration = MotionTokens.ReducedMotion ? MotionTokens.ReducedFade : Scaled(MotionTokens.ContentOutMs);

        var fade = new DoubleAnimation(0, duration)
        {
            EasingFunction = MotionTokens.ContentOutEase
        };

        fade.Completed += (_, _) =>
        {
            // Diabaikan jika sudah ada animasi lebih baru, atau panel ini dipilih lagi selagi memudar.
            if (_tabPanelVersion[index] != version || index == (int)_shownTab)
            {
                return;
            }

            ResetPanel(index, panel);
            panel.Visibility = Visibility.Collapsed;
        };

        panel.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private void FadeInPanel(int index)
    {
        UIElement? panel = GetTabPanel((IslandTab)index);
        if (panel is null)
        {
            return;
        }

        bool reduced = MotionTokens.ReducedMotion;
        TimeSpan? delay = reduced ? null : Scaled(MotionTokens.ContentInDelayMs);

        // Panel yang masih terlihat berarti sedang di tengah gerak keluar (pengguna kembali ke tab itu).
        // Memudar masuk dilanjutkan dari transparansi saat ini tanpa lompat dan tanpa jeda.
        // Panel yang tersembunyi dimulai dari transparan.
        bool wasVisible = panel.Visibility == Visibility.Visible;

        int version = ++_tabPanelVersion[index];

        if (!wasVisible)
        {
            panel.BeginAnimation(UIElement.OpacityProperty, null); // lepas animasi lama dulu, baru atur nilai awal
            panel.Opacity = 0;
            panel.Visibility = Visibility.Visible;

            // Elemen utama isi tab muncul bertahap (naik 8 px sambil memudar masuk), dimulai bersama panel.
            StartContentStagger(index, panel, delay ?? TimeSpan.Zero);
        }
        else
        {
            delay = null;
        }

        panel.IsHitTestVisible = true;

        var fade = new DoubleAnimation(1, reduced ? MotionTokens.ReducedFade : Scaled(MotionTokens.ContentInMs))
        {
            BeginTime = delay,
            EasingFunction = MotionTokens.ContentInEase
        };

        fade.Completed += (_, _) =>
        {
            if (_tabPanelVersion[index] != version || index != (int)_shownTab)
            {
                return;
            }

            ResetPanel(index, panel, cancelStagger: false); // lepas animasi panel, animasi bertahap isinya dibiarkan selesai
        };

        panel.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    // ---- Isi tab muncul bertahap ----

    /// <summary>
    /// Mencari elemen utama isi sebuah panel (maksimal MotionTokens.StaggerMaxElements). Aturannya: dari panel, turun
    /// lewat rantai yang hanya punya satu anak yang terlihat, sampai menemukan induk dengan dua anak terlihat atau lebih.
    /// Anak-anak itu (urutan tampilan) adalah elemen utamanya. Contoh: Home = kolom kiri dan kanan, Timer = tiga baris.
    /// Elemen yang sudah punya RenderTransform atau Opacity sendiri (atau Opacity-nya terikat ke data) dilewati.
    /// </summary>
    private static List<FrameworkElement> FindStaggerTargets(UIElement panel)
    {
        DependencyObject? container = panel;

        for (int depth = 0; depth < 4 && container is not null; depth++)
        {
            List<FrameworkElement> children = GetVisibleChildren(container);

            if (children.Count >= 2)
            {
                return children
                    .Take(MotionTokens.StaggerMaxElements)
                    .Where(e => e.RenderTransform == Transform.Identity
                                && e.Opacity == 1.0
                                && !BindingOperations.IsDataBound(e, UIElement.OpacityProperty))
                    .ToList();
            }

            container = children.Count == 1 ? children[0] : null;
        }

        return new List<FrameworkElement>();
    }

    private static List<FrameworkElement> GetVisibleChildren(DependencyObject parent)
    {
        IEnumerable<object?> candidates = parent switch
        {
            Panel panel => panel.Children.Cast<object?>(),
            ContentControl control => new[] { control.Content },
            Decorator decorator => new object?[] { decorator.Child },
            _ => Array.Empty<object?>()
        };

        return candidates
            .OfType<FrameworkElement>()
            .Where(e => e.Visibility == Visibility.Visible)
            .ToList();
    }

    /// <summary>
    /// Elemen utama isi panel muncul satu per satu: tiap elemen naik dari 8 px di bawah sambil memudar masuk,
    /// dengan jeda 30 ms antar elemen. Dibatalkan otomatis oleh CancelContentStagger (saat panel selesai pergi,
    /// island menutup, atau tab ditampilkan ulang). Tidak berjalan di mode gerak berkurang.
    /// </summary>
    private void StartContentStagger(int index, UIElement panel, TimeSpan baseDelay)
    {
        CancelContentStagger(index);

        if (MotionTokens.ReducedMotion || MotionTokens.StaggerMaxElements <= 0)
        {
            return;
        }

        List<FrameworkElement> targets = FindStaggerTargets(panel);
        _staggerTargets[index] = targets;
        int version = ++_staggerVersion[index];

        TimeSpan duration = Scaled(MotionTokens.ContentInMs);
        TimeSpan step = Scaled(MotionTokens.StaggerDelayMs);

        for (int i = 0; i < targets.Count; i++)
        {
            FrameworkElement element = targets[i];
            var shift = new TranslateTransform(0, MotionTokens.StaggerOffset);

            element.RenderTransform = shift;
            element.BeginAnimation(UIElement.OpacityProperty, null); // lepas animasi lama dulu, baru atur nilai awal
            element.Opacity = 0;

            TimeSpan begin = baseDelay + step * i;
            var fade = new DoubleAnimation(1, duration) { BeginTime = begin, EasingFunction = MotionTokens.ContentInEase };
            var slide = new DoubleAnimation(0, duration) { BeginTime = begin, EasingFunction = MotionTokens.ContentInEase };

            fade.Completed += (_, _) =>
            {
                if (_staggerVersion[index] == version)
                {
                    ReleaseStaggerElement(element);
                }
            };

            element.BeginAnimation(UIElement.OpacityProperty, fade);
            shift.BeginAnimation(TranslateTransform.YProperty, slide);
        }
    }

    /// <summary>Membatalkan animasi bertahap sebuah panel dan mengembalikan semua elemennya ke keadaan normal.</summary>
    private void CancelContentStagger(int index)
    {
        _staggerVersion[index]++;

        foreach (FrameworkElement element in _staggerTargets[index])
        {
            ReleaseStaggerElement(element);
        }

        _staggerTargets[index] = new List<FrameworkElement>();
    }

    private static void ReleaseStaggerElement(FrameworkElement element)
    {
        if (element.RenderTransform is TranslateTransform shift)
        {
            shift.BeginAnimation(TranslateTransform.YProperty, null);
        }

        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.ClearValue(UIElement.RenderTransformProperty);
        element.ClearValue(UIElement.OpacityProperty);
    }

    // ---- Judul tab di header: crossfade + geser vertikal ----

    /// <summary>Judul lama naik sambil memudar, judul baru naik dari bawah sambil muncul.</summary>
    private void AnimateTabTitle(string oldTitle)
    {
        bool reduced = MotionTokens.ReducedMotion;
        TimeSpan duration = reduced ? MotionTokens.ReducedFade : Scaled(MotionTokens.TitleMs);
        double offset = reduced ? 0 : MotionTokens.TitleOffset; // mode gerak berkurang: crossfade tanpa geser
        var newShift = (TranslateTransform)TabTitle.RenderTransform;
        var oldShift = (TranslateTransform)TabTitleOld.RenderTransform;

        // Judul lama mulai dari keadaan judul yang sedang tampil (bisa saja masih setengah muncul).
        double startOpacity = TabTitle.Opacity;
        double startY = newShift.Y;

        TabTitleOld.Text = oldTitle;
        TabTitleOld.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(startOpacity, 0, duration) { EasingFunction = MotionTokens.ContentOutEase });
        oldShift.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(startY, -offset, duration) { EasingFunction = MotionTokens.ContentOutEase });

        TabTitle.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, duration) { EasingFunction = MotionTokens.ContentInEase });
        newShift.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(offset, 0, duration) { EasingFunction = MotionTokens.ContentInEase });
    }

    /// <summary>Judul kembali normal tanpa animasi (dipakai saat island tertutup).</summary>
    private void ResetTabTitle()
    {
        TabTitle.BeginAnimation(UIElement.OpacityProperty, null);
        TabTitle.Opacity = 1;
        ((TranslateTransform)TabTitle.RenderTransform).BeginAnimation(TranslateTransform.YProperty, null);
        ((TranslateTransform)TabTitle.RenderTransform).Y = 0;

        TabTitleOld.BeginAnimation(UIElement.OpacityProperty, null);
        TabTitleOld.Opacity = 0;
        ((TranslateTransform)TabTitleOld.RenderTransform).BeginAnimation(TranslateTransform.YProperty, null);
        ((TranslateTransform)TabTitleOld.RenderTransform).Y = 0;
    }

    /// <summary>
    /// Menerapkan pilihan "Hide island from screenshots".
    /// Hanya memanggil Windows jika pilihannya berubah (bukan setiap slider Settings digeser).
    /// Deteksi isi di belakang island (smart collapse) hanya valid jika island benar-benar tidak ikut terekam,
    /// jadi ia menyala hanya saat island disembunyikan dan Windows menerima perintahnya.
    /// </summary>
    private void ApplyCaptureVisibility()
    {
        bool hide = _settingsService.Current.HideFromScreenshots;
        if (_appliedHideFromCapture == hide)
        {
            return;
        }

        bool succeeded = WindowStyleHelper.SetCaptureExclusion(this, hide);
        _appliedHideFromCapture = hide;
        _windowContext.IsContentDetectionAvailable = hide && succeeded;
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        Island.Opacity = _settingsService.Current.Opacity;

        ApplyCaptureVisibility();

        // Pindah monitor hanya jika pilihan monitor memang berubah (bukan setiap slider digeser).
        if (_settingsService.Current.MonitorDeviceName != _appliedMonitorName)
        {
            PositionOnMonitor();
        }

        // Terapkan ukuran, radius, dan durasi baru.
        ApplyState();
    }

    /// <summary>Sembunyikan island saat ada aplikasi fullscreen, tampilkan lagi setelah selesai.</summary>
    private void ApplyFullscreenRule()
    {
        if (_viewModel.ShouldHideForFullscreen)
        {
            if (IsVisible)
            {
                Hide();
                _hiddenByFullscreen = true; // kita yang menyembunyikan, jadi kita yang menampilkan lagi
            }
        }
        else if (_hiddenByFullscreen)
        {
            _hiddenByFullscreen = false;
            Show();
        }
    }

    // ---- Animasi ----

    /// <summary>Lebar dan tinggi island expanded untuk tab yang sedang aktif.</summary>
    private TabSize GetExpandedSize(AppSettings settings)
    {
        var request = new TabLayoutRequest(
            ItemCount: _viewModel.TabItemCount,
            WideLayout: _viewModel.WantsWideLayout);

        return TabLayout.GetSize(_viewModel.SelectedTab, settings.ExpandedWidth, request);
    }

    /// <summary>
    /// Mengunci ukuran isi expanded sama dengan ukuran AKHIR island untuk tab aktif.
    ///
    /// Mengapa: sebelumnya ExpandedContent ikut membesar-mengecil bersama island. Akibatnya, pada SETIAP frame animasi
    /// WPF mengukur ulang dan menata ulang seluruh isinya (header, tab bar, panel tab, lirik, dan seterusnya).
    /// Itu pekerjaan di UI thread yang membuat frame terlewat. Dengan ukuran terkunci, tata letak isi hanya dihitung
    /// satu kali di awal. Selama animasi, island (yang berukuran lebih kecil) hanya memotong isi lewat Clip,
    /// sehingga tampak seperti "terbuka" dan itu murah.
    ///
    /// Isi diletakkan di tengah-atas supaya terbuka simetris. Pada keadaan diam ukuran island = ukuran terkunci,
    /// jadi tampilan akhirnya sama persis dengan sebelumnya.
    /// </summary>
    private int _freezeVersion;

    /// <summary>
    /// Melepas kunci ukuran: isi expanded kembali mengikuti ukuran island (meregang). Dipakai SELAMA animasi yang
    /// mengubah LEBAR (misalnya panel lirik dibuka). Bila isi tetap dikunci pada lebar akhir sementara island baru
    /// setengah melebar, isi yang terpusat terpotong di sisi kiri dan kanan.
    /// </summary>
    private void UnfreezeExpandedContentSize()
    {
        ExpandedContent.ClearValue(FrameworkElement.WidthProperty);
        ExpandedContent.ClearValue(FrameworkElement.HeightProperty);
        ExpandedContent.HorizontalAlignment = HorizontalAlignment.Stretch;
        ExpandedContent.VerticalAlignment = VerticalAlignment.Stretch;
    }

    private void FreezeExpandedContentSize()
    {
        TabSize size = GetExpandedSize(_settingsService.Current);

        ExpandedContent.HorizontalAlignment = HorizontalAlignment.Center;
        ExpandedContent.VerticalAlignment = VerticalAlignment.Top;

        if (ExpandedContent.Width != size.Width)
        {
            ExpandedContent.Width = size.Width;
        }

        if (ExpandedContent.Height != size.Height)
        {
            ExpandedContent.Height = size.Height;
        }
    }

#if DEBUG
    // ---- Pengukur frame animasi (HANYA build Debug, tidak ada di Release) ----
    // Menghitung selisih waktu antar frame selama sebuah animasi island, lalu menulis ringkasan ke log
    // (%APPDATA%\iLand\logs). Frame sehat pada 60 Hz = sekitar 16,7 ms. Frame di atas 25 ms terasa patah.
    private bool _frameProbeRunning;

    private void StartFrameProbe(string label, TimeSpan animationDuration)
    {
        if (_frameProbeRunning)
        {
            return;
        }

        _frameProbeRunning = true;

        var clock = System.Diagnostics.Stopwatch.StartNew();
        TimeSpan last = TimeSpan.Zero;
        TimeSpan stopAfter = animationDuration + TimeSpan.FromMilliseconds(150);
        int frames = 0;
        double worst = 0;
        int slowFrames = 0;
        double total = 0;

        EventHandler? handler = null;
        handler = (_, _) =>
        {
            TimeSpan now = clock.Elapsed;
            if (frames > 0)
            {
                double gap = (now - last).TotalMilliseconds;
                total += gap;
                worst = Math.Max(worst, gap);
                if (gap > 25)
                {
                    slowFrames++;
                }
            }

            frames++;
            last = now;

            if (now >= stopAfter)
            {
                CompositionTarget.Rendering -= handler;
                _frameProbeRunning = false;
                double average = frames > 1 ? total / (frames - 1) : 0;
                Serilog.Log.Debug(
                    "Anim probe [{Label}] durasi={Duration:0}ms frame={Frames} rata-rata={Avg:0.0}ms terburuk={Worst:0.0}ms lambat(>25ms)={Slow}",
                    label, animationDuration.TotalMilliseconds, frames, average, worst, slowFrames);
            }
        };

        CompositionTarget.Rendering += handler;
    }
#endif

    /// <summary>
    /// Hanya mengubah ukuran island yang sedang expanded (saat pindah tab atau jumlah item berubah).
    /// Isi, radius, dan mode tidak disentuh, jadi isi tab tidak berkedip.
    /// </summary>
    private void ApplyExpandedSize()
    {
        // Mode gerak berkurang: ukuran berubah langsung (durasi nol), isi tetap crossfade.
        TimeSpan duration = MotionTokens.ReducedMotion ? TimeSpan.Zero : Scaled(MotionTokens.ResizeMs);
        TabSize size = GetExpandedSize(_settingsService.Current);
#if DEBUG
        StartFrameProbe("ganti tab / ukuran", duration);
#endif

        // Jika LEBAR berubah (panel lirik dibuka atau ditutup), isi dibiarkan meregang selama animasi lalu dikunci
        // lagi setelah selesai. Jika hanya tinggi yang berubah, isi langsung dikunci (murah, tanpa tata letak per frame).
        int version = ++_freezeVersion;
        double lockedWidth = ExpandedContent.Width;
        bool widthChanges = !double.IsNaN(lockedWidth) && Math.Abs(lockedWidth - size.Width) > 0.5;

        var widthAnimation = new DoubleAnimation(size.Width, duration) { EasingFunction = MotionTokens.ResizeEase };

        if (widthChanges && duration > TimeSpan.Zero)
        {
            UnfreezeExpandedContentSize();
            widthAnimation.Completed += (_, _) =>
            {
                // Hanya animasi terbaru yang boleh mengunci (animasi yang digantikan diabaikan).
                if (version == _freezeVersion)
                {
                    FreezeExpandedContentSize();
                }
            };
        }
        else
        {
            FreezeExpandedContentSize();
        }

        // Perubahan ukuran saat ganti tab tanpa pantulan, supaya isi tab tidak keluar dari bingkai.
        IslandHost.BeginAnimation(FrameworkElement.WidthProperty, widthAnimation);
        IslandHost.BeginAnimation(FrameworkElement.HeightProperty,
            new DoubleAnimation(size.Height + TopGap, duration) { EasingFunction = MotionTokens.ResizeEase });
    }

    private void ApplyState()
    {
        var settings = _settingsService.Current;

        // Ukuran isi expanded dikunci SELALU (juga saat tertutup), supaya tata letaknya sudah siap sebelum island dibuka.
        _freezeVersion++; // membatalkan penguncian tertunda dari ApplyExpandedSize
        FreezeExpandedContentSize();

        // Prioritas mode: expanded, lalu notifikasi, lalu perekaman, lalu mini, lalu normal.
        bool isExpanded = _viewModel.IsExpanded;

        // Bingkai hover tambahan hanya saat expanded. Saat tertutup, area hover = ukuran pill saja,
        // jadi tidak ada area tak terlihat yang menahan klik ke aplikasi di sekitar pill.
        HoverZone.Padding = isExpanded
            ? new Thickness(HoverHaloPx, 0, HoverHaloPx, HoverHaloPx)
            : new Thickness(0);

        bool isNotification = !isExpanded && _viewModel.IsNotificationVisible;
        bool isRecording = !isExpanded && !isNotification && _viewModel.IsRecording;
        bool isMini = !isExpanded && !isNotification && !isRecording && _viewModel.IsMini;

        // Gaya gerak ala Apple: island yang membesar sedikit "memantul" di akhir (kurva membuka, durasi lebih panjang),
        // sedangkan saat mengecil geraknya halus tanpa pantulan (kurva menutup, durasi lebih pendek). Lihat MotionTokens.
        bool isOpening = isExpanded || isNotification || isRecording;
        // Mode gerak berkurang: ukuran dan bentuk berubah langsung (durasi nol), isi crossfade pendek (lihat SetContentVisible).
        TimeSpan duration = MotionTokens.ReducedMotion
            ? TimeSpan.Zero
            : Scaled(isOpening ? MotionTokens.OpenMs : MotionTokens.CloseMs);
        IEasingFunction easing = isOpening ? MotionTokens.OpenEase : MotionTokens.CloseEase;
#if DEBUG
        StartFrameProbe(isOpening ? "membuka" : "menutup", duration);
#endif

        double visibleWidth;
        double visibleHeight;
        double topRadius;
        double bottomRadius;
        double topGap;

        if (isExpanded)
        {
            // Ukuran expanded mengikuti tab yang aktif (lihat TabLayout), bukan satu nilai tetap dari Settings.
            TabSize tabSize = GetExpandedSize(settings);
            visibleWidth = tabSize.Width;
            visibleHeight = tabSize.Height;
            topRadius = settings.ExpandedCornerRadius;
            bottomRadius = settings.ExpandedCornerRadius;
            topGap = TopGap;
        }
        else if (isNotification)
        {
            visibleWidth = NotificationWidth;
            visibleHeight = NotificationHeight;
            topRadius = NotificationHeight / 2;
            bottomRadius = NotificationHeight / 2;
            topGap = TopGap;
        }
        else if (isRecording)
        {
            visibleWidth = RecordingWidth;
            visibleHeight = RecordingHeight;
            topRadius = RecordingHeight / 2;
            bottomRadius = RecordingHeight / 2;
            topGap = TopGap;
        }
        else if (isMini)
        {
            visibleWidth = MiniWidth;
            visibleHeight = MiniHeight;
            topRadius = 0;                  // sisi atas rata: menempel di tepi layar seperti notch
            bottomRadius = MiniBottomRadius;
            topGap = 0;                     // tanpa celah: menempel di tepi atas layar
        }
        else
        {
            visibleWidth = GetPillWidth();
            _lastPillTarget = visibleWidth;
            UpdatePillLyricWidth();
            visibleHeight = CollapsedHeight;
            topRadius = CollapsedHeight / 2;
            bottomRadius = CollapsedHeight / 2;
            topGap = TopGap;
        }

        // Semua animasi dimulai dari nilai saat ini, jadi tetap mulus walaupun pengguna
        // berpindah mode di tengah animasi.
        IslandHost.BeginAnimation(FrameworkElement.WidthProperty,
            new DoubleAnimation(visibleWidth, duration) { EasingFunction = easing });
        IslandHost.BeginAnimation(FrameworkElement.HeightProperty,
            new DoubleAnimation(visibleHeight + topGap, duration) { EasingFunction = easing });
        Island.BeginAnimation(FrameworkElement.MarginProperty,
            new ThicknessAnimation(new Thickness(0, topGap, 0, 0), duration) { EasingFunction = easing });
        BeginAnimation(IslandTopRadiusProperty,
            new DoubleAnimation(topRadius, duration) { EasingFunction = easing });
        BeginAnimation(IslandBottomRadiusProperty,
            new DoubleAnimation(bottomRadius, duration) { EasingFunction = easing });   

        // Isi: collapsed hanya pada mode normal, expanded hanya pada mode expanded,
        // notifikasi hanya pada mode notifikasi, perekaman hanya pada mode perekaman, dan mode mini tidak menampilkan apa-apa.
        SetContentVisible(CollapsedContent, !isExpanded && !isNotification && !isRecording && !isMini, duration);
        SetContentVisible(ExpandedContent, isExpanded, duration);
        SetContentVisible(NotificationContent, isNotification, duration);
        SetContentVisible(RecordingContent, isRecording, duration);

        // Isi tab yang aktif muncul bertahap saat island BARU dibuka. Saat island menutup, sisa animasi dibatalkan.
        if (isExpanded && !_wasExpandedApplied)
        {
            int tabIndex = (int)_viewModel.SelectedTab;
            if (GetTabPanel(_viewModel.SelectedTab) is UIElement activePanel)
            {
                StartContentStagger(tabIndex, activePanel, duration * 0.5); // bersamaan dengan isi expanded mulai terlihat
            }
        }
        else if (!isExpanded && _wasExpandedApplied)
        {
            for (int i = 0; i < _staggerTargets.Length; i++)
            {
                CancelContentStagger(i);
            }
        }

        _wasExpandedApplied = isExpanded;
    }

    /// <summary>Isi yang hilang memudar cepat, isi yang muncul menunggu island cukup besar.</summary>
    private static void SetContentVisible(UIElement content, bool visible, TimeSpan islandDuration)
    {
        // Mode gerak berkurang: isi lama dan baru bertukar dengan crossfade pendek yang sama, tanpa jeda.
        DoubleAnimation animation = MotionTokens.ReducedMotion
            ? new DoubleAnimation(visible ? 1 : 0, MotionTokens.ReducedFade)
            : visible
                ? new DoubleAnimation(1, islandDuration * 0.4) { BeginTime = islandDuration * 0.5 }
                : new DoubleAnimation(0, islandDuration * 0.3);

        content.BeginAnimation(UIElement.OpacityProperty, animation);
        content.IsHitTestVisible = visible; // hanya isi yang terlihat yang boleh menerima klik
    }

    // ---- Seret file ke island (Shelf) ----

    /// <summary>
    /// Dipanggil saat file diseret di atas island. Hanya file/folder (FileDrop) yang diterima.
    /// Kita hanya menjawab "Copy" (atau "Link"), TIDAK PERNAH "Move". Jika menjawab Move,
    /// File Explorer akan menghapus file asli setelah drop.
    /// </summary>
    private void Island_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        if ((e.AllowedEffects & DragDropEffects.Copy) != 0)
        {
            e.Effects = DragDropEffects.Copy;
        }
        else if ((e.AllowedEffects & DragDropEffects.Link) != 0)
        {
            e.Effects = DragDropEffects.Link;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
        _viewModel.OnFileDragOver();
    }

    private void Island_PreviewDragLeave(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        Point p = e.GetPosition(HoverZone);
        if (p.X >= 0 && p.Y >= 0 && p.X < HoverZone.ActualWidth && p.Y < HoverZone.ActualHeight)
        {
            return;
        }

        _viewModel.OnFileDragLeft();
    }

    private void Island_PreviewDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            _viewModel.OnFilesDropped(paths);
        }

        e.Handled = true;
    }

    // ---- Scroll mouse di tab bar memindahkan tab ----

    /// <summary>Putar ke atas = tab sebelumnya (kiri), putar ke bawah = tab berikutnya (kanan). Berhenti di ujung.</summary>
    private void TabBar_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Delta > 0)
        {
            _viewModel.PreviousTabCommand.Execute(null);
        }
        else if (e.Delta < 0)
        {
            _viewModel.NextTabCommand.Execute(null);
        }

        e.Handled = true;
    }

    // ---- Event mouse: diteruskan ke ViewModel ----

    private void Island_MouseEnter(object sender, MouseEventArgs e)
    {
        _pointerLeftWhilePressed = false; // pointer kembali: batalkan keputusan keluar yang tertunda
        _viewModel.OnPointerEntered();
    }

    private void Island_MouseLeave(object sender, MouseEventArgs e)
    {
        // Selama tombol mouse masih ditekan (menggeser slider, menyeret sesuatu), island jangan dianggap ditinggalkan.
        // Keputusannya ditunda sampai tombol dilepas.
        if (IsAnyMouseButtonPressed())
        {
            _pointerLeftWhilePressed = true;
            return;
        }

        _viewModel.OnPointerLeft();
    }

    // ---- Penundaan "pointer keluar" saat tombol mouse ditekan ----

    private static bool IsAnyMouseButtonPressed()
    {
        return Mouse.LeftButton == MouseButtonState.Pressed
            || Mouse.RightButton == MouseButtonState.Pressed
            || Mouse.MiddleButton == MouseButtonState.Pressed;
    }

    private void OnWindowPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        // Diselesaikan setelah event ini selesai supaya status tombol mouse sudah final.
        Dispatcher.BeginInvoke(new Action(ResolveDeferredPointerLeave), DispatcherPriority.Input);
    }

    private void OnWindowLostMouseCapture(object sender, MouseEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(ResolveDeferredPointerLeave), DispatcherPriority.Input);
    }

    /// <summary>
    /// Jika pointer sempat keluar selagi tombol ditekan, dan sekarang tombol sudah dilepas serta pointer memang
    /// di luar island, barulah ViewModel diberi tahu bahwa pointer keluar (timer collapse mulai berjalan).
    /// </summary>
    private void ResolveDeferredPointerLeave()
    {
        if (!_pointerLeftWhilePressed || IsAnyMouseButtonPressed())
        {
            return;
        }

        _pointerLeftWhilePressed = false;

        if (!IsPointerOverIslandArea())
        {
            _viewModel.OnPointerLeft();
        }
    }

    /// <summary>
    /// Apakah pointer sekarang benar-benar di atas area island (termasuk bingkai hover saat expanded)?
    /// Dihitung dari posisi pointer, bukan dari event Enter/Leave, jadi akurat walaupun event itu terlewat.
    /// </summary>
    private bool IsPointerOverIslandArea()
    {
        Point position = Mouse.GetPosition(HoverZone);
        return position.X >= 0 && position.Y >= 0
            && position.X <= HoverZone.ActualWidth
            && position.Y <= HoverZone.ActualHeight;
    }

    private void Island_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Klik pada tombol atau kotak teks adalah milik kontrol itu, bukan klik pada island.
        if (e.OriginalSource is DependencyObject source
            && (FindParent<ButtonBase>(source) is not null || FindParent<TextBoxBase>(source) is not null))
        {
            return;
        }

        _viewModel.OnPointerClicked();
    }
}
