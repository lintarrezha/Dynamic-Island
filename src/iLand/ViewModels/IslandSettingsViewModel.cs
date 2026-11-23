using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using iLand.Interfaces;
using iLand.Models;

namespace iLand.ViewModels;

/// <summary>Halaman Island: layar, ukuran dan tampilan, perilaku buka dan tutup, otomatis, dan Shelf.</summary>
public sealed partial class IslandSettingsViewModel : SettingsPageViewModel
{
    // ---- Display ----
    [ObservableProperty] private string _selectedMonitorDevice = string.Empty;

    // ---- Appearance ----

    // Bentuk island: 0 = Dynamic, 1 = Notch (dipakai SegmentedControl).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewShape))]
    private int _shapeIndex;

    // Skala bentuk yang sedang dipilih (0,8 sampai 1,15). Tiap bentuk menyimpan skalanya sendiri.
    [ObservableProperty] private double _scale = 1.0;

    [ObservableProperty] private double _opacity;

    // True saat slider skala sedang diisi dari pengaturan (bukan digeser pengguna), supaya tidak ditulis balik.
    private bool _isSyncingScale;

    // "When idle": 0 = Always visible, 1 = Hide. Tiap bentuk menyimpan pilihannya sendiri (seperti skala).
    [ObservableProperty] private int _hideWhenIdleIndex;

    // True saat pilihan "When idle" sedang diisi dari pengaturan (bukan diklik pengguna), supaya tidak ditulis balik.
    private bool _isSyncingIdle;

    // ---- Opening and closing ----
    [ObservableProperty] private bool _hoverToExpand;
    [ObservableProperty] private bool _autoCollapse;
    [ObservableProperty] private double _collapseDelayMs;

    // ---- Automatic ----
    [ObservableProperty] private bool _autoMinimizeWhenWindowBelow;
    [ObservableProperty] private bool _hideWhenFullscreen;

    // ---- Shelf ----
    [ObservableProperty] private bool _clipboardHistoryEnabled;

    // ---- Tabs on island (Home selalu aktif, jadi tidak ada di daftar) ----
    [ObservableProperty] private bool _controlTabEnabled = true;
    [ObservableProperty] private bool _musicTabEnabled = true;
    [ObservableProperty] private bool _mixerTabEnabled = true;
    [ObservableProperty] private bool _timerTabEnabled = true;
    [ObservableProperty] private bool _todoTabEnabled = true;
    [ObservableProperty] private bool _shelfTabEnabled = true;
    [ObservableProperty] private bool _captureTabEnabled = true;

    /// <summary>Bentuk yang dipilih, untuk pratinjau.</summary>
    public IslandShape PreviewShape => ShapeIndex == 1 ? IslandShape.Notch : IslandShape.Dynamic;

    /// <summary>Pilihan untuk dropdown "Display on". Pilihan pertama = monitor utama.</summary>
    public IReadOnlyList<MonitorInfo> MonitorOptions { get; }

    public IslandSettingsViewModel(ISettingsService settingsService, IMonitorService monitorService)
        : base(settingsService)
    {
        var options = new List<MonitorInfo>
        {
            new(string.Empty, "Primary monitor (default)", Rect.Empty, true)
        };
        options.AddRange(monitorService.GetMonitors());
        MonitorOptions = options;

        Load(() =>
        {
            AppSettings current = Current;
            SelectedMonitorDevice = current.MonitorDeviceName;
            ShapeIndex = current.IslandShape == IslandShape.Notch ? 1 : 0;
            Scale = current.ActiveScale;
            HideWhenIdleIndex = current.ActiveHideWhenIdle ? 1 : 0;
            Opacity = current.Opacity;
            HoverToExpand = current.HoverToExpand;
            AutoCollapse = current.AutoCollapse;
            CollapseDelayMs = current.CollapseDelayMs;
            AutoMinimizeWhenWindowBelow = current.AutoMinimizeWhenWindowBelow;
            HideWhenFullscreen = current.HideWhenFullscreen;
            ClipboardHistoryEnabled = current.ClipboardHistoryEnabled;
            ControlTabEnabled = current.IsTabEnabled(IslandTab.Control);
            MusicTabEnabled = current.IsTabEnabled(IslandTab.Music);
            MixerTabEnabled = current.IsTabEnabled(IslandTab.Mixer);
            TimerTabEnabled = current.IsTabEnabled(IslandTab.Timer);
            TodoTabEnabled = current.IsTabEnabled(IslandTab.Todo);
            ShelfTabEnabled = current.IsTabEnabled(IslandTab.Shelf);
            CaptureTabEnabled = current.IsTabEnabled(IslandTab.Capture);
        });
    }

    partial void OnSelectedMonitorDeviceChanged(string value) => Update(s => s.MonitorDeviceName = value ?? string.Empty);

    partial void OnShapeIndexChanged(int value)
    {
        IslandShape shape = value == 1 ? IslandShape.Notch : IslandShape.Dynamic;
        Update(s => s.IslandShape = shape);

        // Slider kini menunjukkan skala bentuk yang baru dipilih. Mengisinya tidak boleh dianggap geseran pengguna.
        _isSyncingScale = true;
        Scale = shape == IslandShape.Notch ? Current.NotchScale : Current.DynamicScale;
        _isSyncingScale = false;

        // Pilihan "When idle" juga milik bentuk yang baru dipilih.
        _isSyncingIdle = true;
        HideWhenIdleIndex = (shape == IslandShape.Notch ? Current.NotchHideWhenIdle : Current.DynamicHideWhenIdle) ? 1 : 0;
        _isSyncingIdle = false;
    }

    partial void OnHideWhenIdleIndexChanged(int value)
    {
        if (_isSyncingIdle)
        {
            return;
        }

        bool hide = value == 1;

        // Disimpan untuk bentuk yang sedang dipilih saja.
        Update(s =>
        {
            if (s.IslandShape == IslandShape.Notch)
            {
                s.NotchHideWhenIdle = hide;
            }
            else
            {
                s.DynamicHideWhenIdle = hide;
            }
        });
    }

    partial void OnScaleChanged(double value)
    {
        if (_isSyncingScale)
        {
            return;
        }

        // Skala disimpan untuk bentuk yang sedang dipilih saja.
        Update(s =>
        {
            if (s.IslandShape == IslandShape.Notch)
            {
                s.NotchScale = value;
            }
            else
            {
                s.DynamicScale = value;
            }
        });
    }

    partial void OnOpacityChanged(double value) => Update(s => s.Opacity = value);
    partial void OnHoverToExpandChanged(bool value) => Update(s => s.HoverToExpand = value);
    partial void OnAutoCollapseChanged(bool value) => Update(s => s.AutoCollapse = value);
    partial void OnCollapseDelayMsChanged(double value) => Update(s => s.CollapseDelayMs = value);
    partial void OnAutoMinimizeWhenWindowBelowChanged(bool value) => Update(s => s.AutoMinimizeWhenWindowBelow = value);
    partial void OnHideWhenFullscreenChanged(bool value) => Update(s => s.HideWhenFullscreen = value);
    partial void OnClipboardHistoryEnabledChanged(bool value) => Update(s => s.ClipboardHistoryEnabled = value);
    partial void OnControlTabEnabledChanged(bool value) => Update(s => s.SetTabEnabled(IslandTab.Control, value));
    partial void OnMusicTabEnabledChanged(bool value) => Update(s => s.SetTabEnabled(IslandTab.Music, value));
    partial void OnMixerTabEnabledChanged(bool value) => Update(s => s.SetTabEnabled(IslandTab.Mixer, value));
    partial void OnTimerTabEnabledChanged(bool value) => Update(s => s.SetTabEnabled(IslandTab.Timer, value));
    partial void OnTodoTabEnabledChanged(bool value) => Update(s => s.SetTabEnabled(IslandTab.Todo, value));
    partial void OnShelfTabEnabledChanged(bool value) => Update(s => s.SetTabEnabled(IslandTab.Shelf, value));
    partial void OnCaptureTabEnabledChanged(bool value) => Update(s => s.SetTabEnabled(IslandTab.Capture, value));
}
