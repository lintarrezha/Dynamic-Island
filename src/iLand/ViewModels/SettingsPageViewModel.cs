using CommunityToolkit.Mvvm.ComponentModel;
using iLand.Interfaces;
using iLand.Models;

namespace iLand.ViewModels;

/// <summary>
/// Dasar semua ViewModel halaman Settings yang mengubah AppSettings.
/// Setiap perubahan nilai langsung ditulis ke AppSettings dan island ikut berubah (tanpa tombol Save);
/// penyimpanan ke settings.json ditunda dan digabung oleh SettingsService.
/// </summary>
public abstract class SettingsPageViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;

    // True saat nilai awal sedang dimuat, supaya tidak dianggap sebagai perubahan dari pengguna.
    private bool _isLoading;

    protected SettingsPageViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    protected AppSettings Current => _settingsService.Current;

    protected bool IsLoading => _isLoading;

    /// <summary>Menjalankan pemuatan nilai awal tanpa memicu penulisan balik ke AppSettings.</summary>
    protected void Load(Action load)
    {
        _isLoading = true;
        try
        {
            load();
        }
        finally
        {
            _isLoading = false;
        }
    }

    /// <summary>Menulis satu perubahan ke AppSettings lalu memberi tahu island (langsung berlaku).</summary>
    protected void Update(Action<AppSettings> change)
    {
        if (_isLoading)
        {
            return;
        }

        change(_settingsService.Current);
        _settingsService.NotifyChanged();
    }
}
