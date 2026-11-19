using System.Windows.Threading;
using iLand.Interfaces;
using iLand.Models;
using iLand.Native;
using Microsoft.Extensions.Logging;
using Windows.System.Power;

namespace iLand.Services;

/// <summary>Lihat IBatterySaverService. Status dari PowerManager (resmi), penyalaan lewat ambang di paket daya aktif.</summary>
public sealed class BatterySaverService : IBatterySaverService
{
    // GUID dari dokumentasi Microsoft (Energy Saver settings dan Battery threshold).
    private static Guid _subEnergySaver = new("de830923-a562-41af-a086-e3a2c6bad2da");
    private static Guid _batteryThreshold = new("e69653ca-cf7f-4f05-aa73-cb833fa90ad4");

    private const uint AlwaysOn = 100;
    private const uint DefaultThreshold = 20; // dipakai hanya jika nilai asli tidak diketahui

    private readonly ISettingsService _settings;
    private readonly ILogger<BatterySaverService> _logger;
    private readonly Dispatcher _ui;
    private bool _attached;

    public BatterySaverService(ISettingsService settings, ILogger<BatterySaverService> logger)
    {
        _settings = settings;
        _logger = logger;
        _ui = Dispatcher.CurrentDispatcher;
    }

    public event EventHandler? Changed;

    public BatterySaverState State => PowerManager.EnergySaverStatus switch
    {
        EnergySaverStatus.On => BatterySaverState.On,
        EnergySaverStatus.Off => BatterySaverState.Off,
        _ => BatterySaverState.Unavailable
    };

    public void Attach()
    {
        if (_attached)
        {
            return;
        }

        _attached = true;
        PowerManager.EnergySaverStatusChanged += OnStatusChanged;

        // Rumah tangga: jika iLand dulu menaikkan ambang ke 100 tetapi Battery saver kini sudah mati
        // (dimatikan dari Windows, atau iLand tertutup mendadak), kembalikan ambang asli pengguna.
        RestoreOriginalIfStale();
    }

    public void Detach()
    {
        if (!_attached)
        {
            return;
        }

        _attached = false;
        PowerManager.EnergySaverStatusChanged -= OnStatusChanged;
    }

    public async Task<bool> SetAsync(bool on)
    {
        try
        {
            return await Task.Run(() => on ? TurnOn() : TurnOff());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not change Battery saver");
            return false;
        }
    }

    private void OnStatusChanged(object? sender, object e) =>
        _ui.BeginInvoke(new Action(() =>
        {
            if (_attached)
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }));

    private bool TurnOn()
    {
        if (!TryReadThreshold(out uint current))
        {
            return false;
        }

        // Ambang sudah 100: pengguna memang memakai "selalu". Tidak ada yang perlu diubah (dan kita tidak akan
        // menurunkannya saat mematikan, karena itu pilihan mereka).
        if (current == AlwaysOn)
        {
            return true;
        }

        // Simpan nilai asli SEBELUM mengubah, dan langsung ke disk agar selamat dari penutupan mendadak.
        _ui.Invoke(() =>
        {
            _settings.Current.BatterySaverOriginalThreshold = (int)current;
            _settings.Save();
        });

        return WriteThreshold(AlwaysOn);
    }

    private bool TurnOff()
    {
        int? saved = null;
        _ui.Invoke(() => saved = _settings.Current.BatterySaverOriginalThreshold);

        if (saved is null)
        {
            // Kita tidak pernah mengubah ambangnya, jadi tidak ada cara aman untuk mematikan dari sini.
            return false;
        }

        if (!WriteThreshold((uint)saved.Value))
        {
            return false;
        }

        _ui.Invoke(() =>
        {
            _settings.Current.BatterySaverOriginalThreshold = null;
            _settings.Save();
        });

        return true;
    }

    private void RestoreOriginalIfStale()
    {
        int? saved = _settings.Current.BatterySaverOriginalThreshold;
        if (saved is null || State == BatterySaverState.On)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            if (WriteThreshold((uint)saved.Value))
            {
                _ui.BeginInvoke(new Action(() =>
                {
                    _settings.Current.BatterySaverOriginalThreshold = null;
                    _settings.Save();
                }));
            }
        });
    }

    // ---- Paket daya aktif (powrprof.dll) ----

    private bool TryReadThreshold(out uint value)
    {
        value = 0;
        IntPtr schemePtr = IntPtr.Zero;
        try
        {
            if (ControlNative.PowerGetActiveScheme(IntPtr.Zero, out schemePtr) != 0 || schemePtr == IntPtr.Zero)
            {
                return false;
            }

            Guid scheme = System.Runtime.InteropServices.Marshal.PtrToStructure<Guid>(schemePtr);
            return ControlNative.PowerReadDCValueIndex(
                IntPtr.Zero, ref scheme, ref _subEnergySaver, ref _batteryThreshold, out value) == 0;
        }
        finally
        {
            if (schemePtr != IntPtr.Zero)
            {
                ControlNative.LocalFree(schemePtr);
            }
        }
    }

    private bool WriteThreshold(uint value)
    {
        IntPtr schemePtr = IntPtr.Zero;
        try
        {
            if (ControlNative.PowerGetActiveScheme(IntPtr.Zero, out schemePtr) != 0 || schemePtr == IntPtr.Zero)
            {
                return false;
            }

            Guid scheme = System.Runtime.InteropServices.Marshal.PtrToStructure<Guid>(schemePtr);
            if (ControlNative.PowerWriteDCValueIndex(
                    IntPtr.Zero, ref scheme, ref _subEnergySaver, ref _batteryThreshold, value) != 0)
            {
                return false;
            }

            // Menerapkan paket daya aktif agar perubahan berlaku sekarang.
            return ControlNative.PowerSetActiveScheme(IntPtr.Zero, ref scheme) == 0;
        }
        finally
        {
            if (schemePtr != IntPtr.Zero)
            {
                ControlNative.LocalFree(schemePtr);
            }
        }
    }
}
