using System.Windows;
using iLand.Interfaces;
using iLand.Views;
using Microsoft.Extensions.DependencyInjection;

namespace iLand.Services;

public sealed class WindowService : IWindowService
{
    private readonly IServiceProvider _serviceProvider;
    private SettingsWindow? _settingsWindow;

    public WindowService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public void ShowSettings()
    {
        // Jika Settings sudah terbuka, cukup bawa ke depan (jangan buka dua kali).
        if (_settingsWindow is not null)
        {
            if (_settingsWindow.WindowState == WindowState.Minimized)
            {
                _settingsWindow.WindowState = WindowState.Normal;
            }
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = _serviceProvider.GetRequiredService<SettingsWindow>();
        _settingsWindow.Closed += OnSettingsWindowClosed;
        _settingsWindow.Show();
    }

    public void ExitApplication()
    {
        Application.Current.Shutdown();
    }

        public void ToggleIsland()
    {
        // Island diambil dari DI saat dibutuhkan (bukan lewat constructor)
        // agar tidak terjadi lingkaran ketergantungan.
        var island = _serviceProvider.GetRequiredService<IslandWindow>();

        if (island.IsVisible)
        {
            island.Hide();
        }
        else
        {
            island.Show();
        }
    }

    private void OnSettingsWindowClosed(object? sender, EventArgs e)
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Closed -= OnSettingsWindowClosed;
            _settingsWindow = null;
        }
    }
}
