using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iLand.Interfaces;

namespace iLand.ViewModels;

/// <summary>
/// ViewModel untuk tab Capture: tombol screenshot (layar penuh, area, jendela),
/// tombol rekam layar, pilihan suara untuk rekaman, dan teks pintasannya.
/// </summary>
public partial class CaptureViewModel : ObservableObject, IDisposable
{
    private readonly ICaptureService _captureService;
    private readonly IRecordingService _recordingService;
    private readonly ISettingsService _settingsService;
    private readonly IClockService _clockService;

    public string ShortcutText { get; }

    /// <summary>Teks tooltip chip pintasan: menyebut pintasan screenshot area dan rekam layar.</summary>
    public string ShortcutTooltipText { get; }

    public string RecordShortcutText { get; }

    /// <summary>True selama layar sedang direkam. Dipakai tile rekam untuk berubah menjadi tombol stop.</summary>
    public bool IsRecording => _recordingService.IsRecording;

    /// <summary>
    /// Teks kartu rekam: "Record screen", atau "Stop · 00:12" selama merekam. Waktunya sama dengan pil "Recording"
    /// (format sama dengan IslandViewModel.RecordingTimeText) dan diperbarui oleh detak jam yang sudah ada.
    /// </summary>
    public string RecordTileText => IsRecording
        ? "Stop \u00B7 " + FormatElapsed(_recordingService.Elapsed)
        : "Record screen";

    /// <summary>
    /// Ikut merekam suara dari speaker (semua aplikasi). Pilihan ini disimpan di pengaturan dan
    /// dipakai oleh tombol maupun pintasan Ctrl + Alt + R.
    /// </summary>
    public bool RecordSystemAudio
    {
        get => _settingsService.Current.RecordSystemAudio;
        set
        {
            if (_settingsService.Current.RecordSystemAudio == value)
            {
                return;
            }

            _settingsService.Current.RecordSystemAudio = value;
            _settingsService.NotifyChanged();
            OnPropertyChanged();
        }
    }

    /// <summary>Ikut merekam mikrofon. Mati secara bawaan dan hanya dibuka selama rekaman jika dinyalakan.</summary>
    public bool RecordMicrophone
    {
        get => _settingsService.Current.RecordMicrophone;
        set
        {
            if (_settingsService.Current.RecordMicrophone == value)
            {
                return;
            }

            _settingsService.Current.RecordMicrophone = value;
            _settingsService.NotifyChanged();
            OnPropertyChanged();
        }
    }

    public CaptureViewModel(
        ICaptureService captureService,
        IRecordingService recordingService,
        IHotkeyService hotkeyService,
        ISettingsService settingsService,
        IClockService clockService)
    {
        _captureService = captureService;
        _recordingService = recordingService;
        _settingsService = settingsService;
        _clockService = clockService;
        ShortcutText = hotkeyService.CaptureAreaShortcutText;
        RecordShortcutText = hotkeyService.ToggleRecordingShortcutText;
        ShortcutTooltipText = $"Area screenshot: {ShortcutText}\nRecord screen: {RecordShortcutText}";

        _recordingService.StateChanged += OnRecordingStateChanged;
        _clockService.Ticked += OnClockTicked;
    }

    // Selama merekam, waktu di kartu rekam ikut berjalan lewat detak jam yang sudah ada (tanpa timer baru).
    private void OnClockTicked(object? sender, EventArgs e)
    {
        if (IsRecording)
        {
            OnPropertyChanged(nameof(RecordTileText));
        }
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        return elapsed.TotalHours >= 1
            ? elapsed.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : elapsed.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }

    private void OnRecordingStateChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsRecording));
        OnPropertyChanged(nameof(RecordTileText));
    }

    // Nama command dibuat otomatis: CaptureFullScreen -> CaptureFullScreenCommand, dst.
    [RelayCommand]
    private void CaptureFullScreen()
    {
        _captureService.CaptureFullScreen();
    }

    [RelayCommand]
    private void CaptureArea()
    {
        _captureService.CaptureArea();
    }

    [RelayCommand]
    private void CaptureWindow()
    {
        _captureService.CaptureWindow();
    }

    // ToggleRecordingAsync -> ToggleRecordingCommand. Mulai merekam, atau berhenti jika sedang merekam.
    [RelayCommand]
    private async Task ToggleRecordingAsync()
    {
        await _recordingService.ToggleAsync();
    }

    public void Dispose()
    {
        _recordingService.StateChanged -= OnRecordingStateChanged;
        _clockService.Ticked -= OnClockTicked;
    }
}
