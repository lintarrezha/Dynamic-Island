using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using iLand.Interfaces;

namespace iLand.ViewModels;

/// <summary>
/// ViewModel untuk island. Untuk Phase 2 baru menampilkan jam,
/// hanya untuk membuktikan MVVM + DI sudah bekerja.
/// </summary>
public partial class IslandViewModel : ObservableObject, IDisposable
{
    private readonly IClockService _clockService;

    // [ObservableProperty] otomatis membuat property publik bernama "CurrentTime"
    // (dari field _currentTime) lengkap dengan notifikasi perubahan ke UI.
    [ObservableProperty]
    private string _currentTime = string.Empty;

    public IslandViewModel(IClockService clockService)
    {
        _clockService = clockService;

        UpdateTime();
        _clockService.Ticked += OnClockTicked;
    }

    private void OnClockTicked(object? sender, EventArgs e)
    {
        UpdateTime();
    }

    private void UpdateTime()
    {
        CurrentTime = _clockService.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }

    // Berhenti mendengarkan event agar tidak terjadi memory leak.
    public void Dispose()
    {
        _clockService.Ticked -= OnClockTicked;
    }
}
