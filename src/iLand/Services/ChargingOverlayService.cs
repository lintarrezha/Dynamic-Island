using System.Globalization;
using iLand.Helpers;
using iLand.Interfaces;
using iLand.Models;

namespace iLand.Services;

/// <summary>
/// Mengubah perubahan status baterai menjadi overlay charging.
/// - Muncul saat charger BARU dicolok, atau saat baterai BARU menjadi penuh (sekali). Dicabut: tidak ada overlay,
///   pill idle sudah menampilkan keadaannya.
/// - Pembacaan pertama hanya acuan, jadi aplikasi yang dijalankan saat charger sudah tersambung tidak memunculkan panel.
/// - Colok pada 100%: satu panel "Fully charged" (colok dan penuh terjadi bersamaan, hanya satu overlay).
/// - Perangkat tanpa baterai: diam.
/// - Hanya terpasang selama ChargingAnimationEnabled menyala.
/// Semua anggota dipanggil dari UI thread (BatteryService mengangkat event di UI thread).
/// </summary>
public sealed class ChargingOverlayService : IChargingOverlayService, IDisposable
{
    private const string ChargingKey = "charging";

    private readonly ISettingsService _settingsService;
    private readonly IBatteryService _batteryService;
    private readonly IOverlayService _overlayService;

    private bool _initialized;
    private bool _running;
    private bool? _wasPluggedIn;
    private bool? _wasFull;

    public ChargingOverlayService(
        ISettingsService settingsService,
        IBatteryService batteryService,
        IOverlayService overlayService)
    {
        _settingsService = settingsService;
        _batteryService = batteryService;
        _overlayService = overlayService;
    }

    public void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        _settingsService.SettingsChanged += OnSettingsChanged;
        Apply();
    }

    public void Dispose()
    {
        if (_initialized)
        {
            _settingsService.SettingsChanged -= OnSettingsChanged;
        }

        Stop();
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        Apply(); // murah: hanya membandingkan satu nilai bool
    }

    private void Apply()
    {
        bool wanted = _settingsService.Current.ChargingAnimationEnabled;
        if (wanted == _running)
        {
            return;
        }

        if (wanted)
        {
            Start();
        }
        else
        {
            Stop();
        }
    }

    private void Start()
    {
        _running = true;

        // Keadaan saat ini menjadi acuan: menyalakan toggle saat charger sudah tersambung tidak memunculkan panel.
        BatteryInfo current = _batteryService.Current;
        _wasPluggedIn = current.HasBattery ? current.IsPluggedIn : null;
        _wasFull = current.HasBattery ? current.IsFull : null;

        _batteryService.Changed += OnBatteryChanged;
    }

    private void Stop()
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        _batteryService.Changed -= OnBatteryChanged;
        _wasPluggedIn = null;
        _wasFull = null;
    }

    private void OnBatteryChanged(object? sender, EventArgs e)
    {
        BatteryInfo battery = _batteryService.Current;

        if (!battery.HasBattery)
        {
            _wasPluggedIn = null;
            _wasFull = null;
            return;
        }

        if (_wasPluggedIn is null || _wasFull is null)
        {
            // Pembacaan pertama yang valid: hanya acuan.
            _wasPluggedIn = battery.IsPluggedIn;
            _wasFull = battery.IsFull;
            return;
        }

        bool justPluggedIn = !_wasPluggedIn.Value && battery.IsPluggedIn;
        bool justFull = !_wasFull.Value && battery.IsFull;

        _wasPluggedIn = battery.IsPluggedIn;
        _wasFull = battery.IsFull;

        if (justPluggedIn || justFull)
        {
            ShowPanel(battery);
        }
    }

    private void ShowPanel(BatteryInfo battery)
    {
        string caption = battery.IsFull ? "Fully charged"
            : battery.IsCharging ? "Charging"
            : "Plugged in";

        _overlayService.Show(new OverlayRequest(ChargingKey, OverlayKind.Charging, OverlayPriority.Charging, MotionTokens.OverlayChargingMs)
        {
            Title = caption,
            Level = battery.Percent / 100.0,
            ValueText = battery.Percent.ToString(CultureInfo.InvariantCulture) + "%"
        });
    }
}
