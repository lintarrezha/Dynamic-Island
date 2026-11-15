using iLand.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace iLand.Services;

/// <summary>
/// Start with Windows lewat registry milik pengguna (HKEY_CURRENT_USER\...\Run).
/// Cara ini resmi, tidak butuh izin administrator, dan bisa dilihat pengguna di
/// Task Manager > Startup apps.
/// </summary>
public sealed class StartupService : IStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "iLand";

    private readonly ILogger<StartupService> _logger;

    public StartupService(ILogger<StartupService> logger)
    {
        _logger = logger;
    }

    public bool IsEnabled
    {
        get
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                return key?.GetValue(ValueName) is string;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read startup registration");
                return false;
            }
        }
    }

    public void SetEnabled(bool enabled)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                                    ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

            if (enabled)
            {
                string? exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath))
                {
                    _logger.LogWarning("Cannot register startup: process path is unknown");
                    return;
                }

                // Tanda kutip dibutuhkan jika path mengandung spasi.
                key.SetValue(ValueName, $"\"{exePath}\"");
                _logger.LogInformation("Start with Windows enabled: {Path}", exePath);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                _logger.LogInformation("Start with Windows disabled");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update startup registration");
        }
    }
}
