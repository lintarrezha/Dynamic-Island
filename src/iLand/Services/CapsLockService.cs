using System.Windows.Threading;
using Serilog;
using iLand.Helpers;
using iLand.Interfaces;
using iLand.Models;
using iLand.Native;

namespace iLand.Services;

/// <summary>
/// Memantau status Caps Lock dengan membaca GetKeyState(VK_CAPITAL) tiap 100 ms, hanya saat fitur dinyalakan.
///
/// Yang dilakukan kode ini, seluruhnya:
/// - Memanggil GetKeyState dengan SATU kode tombol (VK_CAPITAL) dan memakai bit rendahnya (status toggle: menyala atau mati).
/// - Menyimpan satu nilai bool (menyala atau mati) di memori.
/// Yang TIDAK dilakukan: hook keyboard, raw input, GetAsyncKeyState, membaca tombol lain, mencatat ketikan, atau menulis
/// status tombol ke log atau disk. Log hanya mencatat "mulai" dan "berhenti".
///
/// Biaya: satu DispatcherTimer berprioritas Normal yang bangun 10 kali per detik selama fitur menyala. Mati = tidak ada timer.
/// Prioritas Normal (bukan Background) agar pemeriksaan tidak tertunda saat UI thread sibuk dengan animasi.
/// Nilai pertama setelah fitur dinyalakan hanya acuan: tidak memunculkan overlay.
/// Semua anggota dipanggil dari UI thread.
/// </summary>
public sealed class CapsLockService : ICapsLockService, IDisposable
{
    private const string CapsKey = "caps";
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    private readonly ISettingsService _settingsService;
    private readonly IOverlayService _overlayService;
    private readonly DispatcherTimer _timer;

    private bool _initialized;
    private bool _running;
    private bool _lastOn;

    public CapsLockService(ISettingsService settingsService, IOverlayService overlayService)
    {
        _settingsService = settingsService;
        _overlayService = overlayService;

        _timer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = PollInterval
        };
        _timer.Tick += OnTick;
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
        _timer.Tick -= OnTick;
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        Apply(); // murah: hanya membandingkan satu nilai bool
    }

    private void Apply()
    {
        bool wanted = _settingsService.Current.CapsLockOverlay;
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
        _lastOn = ReadCapsLock(); // acuan, tanpa overlay
        _timer.Start();
        Log.Information("Caps Lock indicator: start");
    }

    private void Stop()
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        _timer.Stop();
        Log.Information("Caps Lock indicator: stop");
    }

    private void OnTick(object? sender, EventArgs e)
    {
        bool on = ReadCapsLock();
        if (on == _lastOn)
        {
            return;
        }

        _lastOn = on;

        _overlayService.Show(new OverlayRequest(CapsKey, OverlayKind.Chip, OverlayPriority.CapsLock, MotionTokens.OverlayCapsLockMs)
        {
            IconKey = "IconCapsLock",
            Title = on ? "Caps Lock On" : "Caps Lock Off"
        });
    }

    /// <summary>Bit rendah GetKeyState(VK_CAPITAL) = status toggle Caps Lock. Hanya tombol ini yang pernah dibaca.</summary>
    private static bool ReadCapsLock()
    {
        return (NativeMethods.GetKeyState(NativeMethods.VK_CAPITAL) & 0x0001) != 0;
    }
}
