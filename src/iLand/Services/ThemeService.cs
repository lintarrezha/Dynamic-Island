using iLand.Interfaces;
using iLand.Native;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace iLand.Services;

/// <summary>Lihat IThemeService. Nilai 0 = gelap, 1 = terang (AppsUseLightTheme dan SystemUsesLightTheme).</summary>
public sealed class ThemeService : IThemeService
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsValue = "AppsUseLightTheme";
    private const string SystemValue = "SystemUsesLightTheme";

    private readonly ILogger<ThemeService> _logger;

    public ThemeService(ILogger<ThemeService> logger)
    {
        _logger = logger;
    }

    public bool IsDarkMode
    {
        get
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
                // Tanpa nilai: Windows memakai tema terang.
                return key?.GetValue(AppsValue) is int apps && apps == 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read the Windows theme");
                return false;
            }
        }
    }

    public async Task<bool> SetDarkModeAsync(bool dark)
    {
        try
        {
            int value = dark ? 0 : 1;
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(PersonalizeKey, writable: true))
            {
                key.SetValue(AppsValue, value, RegistryValueKind.DWord);
                key.SetValue(SystemValue, value, RegistryValueKind.DWord);
            }

            // Pemberitahuan ke semua window bisa lama bila ada yang macet: jalankan di thread latar.
            await Task.Run(ControlNative.BroadcastThemeChange);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not change the Windows theme");
            return false;
        }
    }
}
