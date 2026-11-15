using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using iLand.Interfaces;
using Microsoft.Extensions.Logging;

namespace iLand.Services;

/// <summary>
/// Ikon iLand di system tray (pojok kanan bawah) beserta menu klik kanannya.
/// Memakai package H.NotifyIcon.Wpf karena WPF tidak punya tray icon bawaan.
/// </summary>
public sealed class TrayService : ITrayService, IDisposable
{
    private readonly IWindowService _windowService;
    private readonly ILogger<TrayService> _logger;
    private TaskbarIcon? _taskbarIcon;

    public TrayService(IWindowService windowService, ILogger<TrayService> logger)
    {
        _windowService = windowService;
        _logger = logger;
    }

    public void Initialize()
    {
        if (_taskbarIcon is not null)
        {
            return; // sudah dibuat
        }

        _taskbarIcon = new TaskbarIcon
        {
            ToolTipText = "iLand",
            IconSource = new BitmapImage(new Uri("pack://application:,,,/Assets/iLand.ico")),
            ContextMenu = BuildMenu()
        };

        // Icon dibuat lewat kode (bukan XAML), jadi harus dibuat paksa.
        // Parameter false = jangan aktifkan "efficiency mode" karena iLand punya window sendiri.
        _taskbarIcon.ForceCreate(false);

        _logger.LogInformation("Tray icon created");
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();

        var settingsItem = new MenuItem { Header = "Settings" };
        settingsItem.Click += (_, _) => _windowService.ShowSettings();

        var toggleItem = new MenuItem { Header = "Show / hide island" };
        toggleItem.Click += (_, _) => _windowService.ToggleIsland();

        var exitItem = new MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => _windowService.ExitApplication();

        menu.Items.Add(settingsItem);
        menu.Items.Add(toggleItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);
        return menu;
    }

    // Ikon harus dihapus saat aplikasi ditutup, kalau tidak akan tertinggal "ikon hantu" di tray.
    public void Dispose()
    {
        _taskbarIcon?.Dispose();
        _taskbarIcon = null;
    }
}
