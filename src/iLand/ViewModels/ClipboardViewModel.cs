using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iLand.Interfaces;

namespace iLand.ViewModels;

/// <summary>
/// ViewModel untuk bagian Clipboard di tab Shelf. Menampilkan salah satu dari tiga keadaan:
/// fitur mati (dengan tombol Turn on), nyala tapi kosong, atau daftar teks.
/// </summary>
public partial class ClipboardViewModel : ObservableObject, IDisposable
{
    private readonly IClipboardService _clipboardService;
    private readonly ISettingsService _settingsService;

    public ObservableCollection<ClipboardItemViewModel> Items { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDisabled))]
    private bool _isEnabled;

    /// <summary>Kebalikan IsEnabled, agar XAML cukup memakai BoolToVisibility.</summary>
    public bool IsDisabled => !IsEnabled;

    /// <summary>Fitur nyala dan ada item.</summary>
    [ObservableProperty]
    private bool _hasItems;

    /// <summary>Fitur nyala tetapi belum ada yang disalin.</summary>
    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    public ClipboardViewModel(IClipboardService clipboardService, ISettingsService settingsService)
    {
        _clipboardService = clipboardService;
        _settingsService = settingsService;

        IsEnabled = _settingsService.Current.ClipboardHistoryEnabled;
        Reload();

        _clipboardService.Changed += OnClipboardChanged;
        _settingsService.SettingsChanged += OnSettingsChanged;
    }

    /// <summary>Memperbarui teks waktu semua item. Dipanggil saat tab Shelf dibuka (tanpa timer).</summary>
    public void RefreshTimes()
    {
        foreach (ClipboardItemViewModel item in Items)
        {
            item.RefreshTime();
        }
    }

    // TurnOn -> TurnOnCommand, ClearAll -> ClearAllCommand (dibuat otomatis oleh [RelayCommand]).
    // Menyalakan fitur dari tombol "Turn on". Pengaturan yang sama bisa diubah di Settings.
    [RelayCommand]
    private void TurnOn()
    {
        _settingsService.Current.ClipboardHistoryEnabled = true;
        _settingsService.NotifyChanged();
    }

    [RelayCommand]
    private void ClearAll()
    {
        _clipboardService.Clear();
    }

    private void OnClipboardChanged(object? sender, EventArgs e)
    {
        Reload();
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        // Event ini juga datang saat slider di Settings digeser, jadi hanya bereaksi jika nilainya benar-benar berubah.
        bool enabled = _settingsService.Current.ClipboardHistoryEnabled;
        if (enabled == IsEnabled)
        {
            return;
        }

        IsEnabled = enabled;
        Reload();
    }

    private void Reload()
    {
        Items.Clear();

        foreach (var entry in _clipboardService.Items)
        {
            Items.Add(new ClipboardItemViewModel(entry, _clipboardService));
        }

        int count = Items.Count;
        HasItems = IsEnabled && count > 0;
        IsEmpty = IsEnabled && count == 0;
        SummaryText = HasItems ? (count == 1 ? "1 item" : $"{count} items") : string.Empty;
    }

    public void Dispose()
    {
        _clipboardService.Changed -= OnClipboardChanged;
        _settingsService.SettingsChanged -= OnSettingsChanged;
    }
}
