using System.Windows.Threading;
using iLand.Interfaces;
using iLand.Models;
using iLand.Native;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Windows.Devices.Power;

namespace iLand.Services;

/// <summary>
/// Membaca baterai asli Windows.
/// - Nilai dibaca lewat Windows API GetSystemPowerStatus.
/// - Pembaruan datang dari EVENT (bukan polling): laporan baterai WinRT dan event power Windows.
///   Jadi saat tidak ada perubahan, service ini tidak memakai CPU.
/// </summary>
public sealed class BatteryService : IBatteryService, IDisposable
{
    private readonly ILogger<BatteryService> _logger;
    private readonly Dispatcher _uiDispatcher;
    private Battery? _battery;

    public BatteryInfo Current { get; private set; }

    public event EventHandler? Changed;

    public BatteryService(ILogger<BatteryService> logger)
    {
        _logger = logger;
        _uiDispatcher = Dispatcher.CurrentDispatcher; // service ini dibuat di UI thread
        Current = ReadBatteryInfo();

        try
        {
            _battery = Battery.AggregateBattery;
            _battery.ReportUpdated += OnReportUpdated;
        }
        catch (Exception ex)
        {
            // Tidak fatal: kita masih punya event power Windows di bawah.
            _logger.LogWarning(ex, "Battery report events are not available");
        }

        // Event ini juga muncul saat laptop bangun dari sleep (Resume).
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        _logger.LogInformation("Battery: has={Has}, percent={Percent}, charging={Charging}",
            Current.HasBattery, Current.Percent, Current.IsCharging);
    }

    // Event WinRT datang dari thread lain, jadi pindahkan ke UI thread dulu.
    private void OnReportUpdated(Battery sender, object args)
    {
        _uiDispatcher.BeginInvoke(new Action(Refresh));
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        _uiDispatcher.BeginInvoke(new Action(Refresh));
    }

    private void Refresh()
    {
        BatteryInfo latest = ReadBatteryInfo();
        if (latest == Current)
        {
            return; // tidak ada perubahan, jangan ganggu UI
        }

        Current = latest;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static BatteryInfo ReadBatteryInfo()
    {
        if (!NativeMethods.GetSystemPowerStatus(out NativeMethods.SystemPowerStatus status))
        {
            return BatteryInfo.None;
        }

        // BatteryFlag: 128 = tidak ada baterai, 255 = tidak diketahui.
        // BatteryLifePercent: 0-100, atau 255 = tidak diketahui.
        bool hasBattery = (status.BatteryFlag & 128) == 0
                          && status.BatteryFlag != 255
                          && status.BatteryLifePercent <= 100;
        if (!hasBattery)
        {
            return BatteryInfo.None;
        }

        bool isPluggedIn = status.ACLineStatus == 1;
        bool isCharging = (status.BatteryFlag & 8) != 0;
        return new BatteryInfo(true, status.BatteryLifePercent, isCharging, isPluggedIn);
    }

    // Berhenti mendengarkan event agar tidak terjadi memory leak.
    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;

        if (_battery is not null)
        {
            _battery.ReportUpdated -= OnReportUpdated;
            _battery = null;
        }
    }
}
