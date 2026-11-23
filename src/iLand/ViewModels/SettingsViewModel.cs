using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using iLand.Models;

namespace iLand.ViewModels;

/// <summary>
/// Cangkang jendela Settings: daftar halaman di menu samping dan halaman yang sedang tampil.
/// Pengaturan itu sendiri ada di ViewModel tiap halaman, yang dibuat HANYA saat halamannya pertama dibuka
/// (dan disimpan selama jendela terbuka, supaya nilai tidak hilang saat berpindah halaman).
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private readonly Dictionary<SettingsPage, object> _pages = new();

    /// <summary>Item menu samping, urut seperti tampil. MeetingAlerts menyusul di Prompt 7.</summary>
    public IReadOnlyList<SettingsNavItem> Items { get; } = new[]
    {
        new SettingsNavItem(SettingsPage.Island, "Island", "IconIsland"),
        new SettingsNavItem(SettingsPage.MusicLyrics, "Music & Lyrics", "IconMusic"),
        new SettingsNavItem(SettingsPage.MeetingAlerts, "Meeting & Alerts", "IconAlerts"),
        new SettingsNavItem(SettingsPage.Capture, "Capture", "IconCapture"),
        new SettingsNavItem(SettingsPage.General, "General", "IconSettings"),
        new SettingsNavItem(SettingsPage.About, "About", "IconInfo")
    };

    [ObservableProperty]
    private SettingsNavItem? _selectedItem;

    /// <summary>ViewModel halaman yang sedang tampil. SettingsWindow.xaml memilih tampilannya lewat DataTemplate.</summary>
    [ObservableProperty]
    private object? _currentPage;

    public SettingsViewModel(IServiceProvider services)
    {
        _services = services;
        SelectedItem = Items[0];
    }

    partial void OnSelectedItemChanged(SettingsNavItem? value)
    {
        CurrentPage = value is null ? null : GetPage(value.Page);
    }

    private object GetPage(SettingsPage page)
    {
        if (!_pages.TryGetValue(page, out object? viewModel))
        {
            viewModel = page switch
            {
                SettingsPage.Island => _services.GetRequiredService<IslandSettingsViewModel>(),
                SettingsPage.MusicLyrics => _services.GetRequiredService<MusicSettingsViewModel>(),
                SettingsPage.MeetingAlerts => _services.GetRequiredService<MeetingAlertsViewModel>(),
                SettingsPage.Capture => _services.GetRequiredService<CaptureSettingsViewModel>(),
                SettingsPage.General => _services.GetRequiredService<GeneralSettingsViewModel>(),
                SettingsPage.About => _services.GetRequiredService<AboutViewModel>(),
                _ => throw new ArgumentOutOfRangeException(nameof(page), page, "Halaman Settings belum tersedia.")
            };

            _pages[page] = viewModel;
        }

        return viewModel;
    }
}
