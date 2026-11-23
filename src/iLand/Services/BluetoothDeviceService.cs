using Serilog;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using iLand.Helpers;
using iLand.Interfaces;
using iLand.Models;

namespace iLand.Services;

/// <summary>
/// Pengamat perangkat Bluetooth yang sudah dipasangkan (DeviceWatcher, AssociationEndpoint) dengan properti System.Devices.Aep.IsConnected.
///
/// Aturan:
/// - Daftar awal (sampai EnumerationCompleted) dan perangkat yang baru dipasangkan hanya DICATAT. Perangkat yang sudah tersambung
///   sebelum aplikasi mulai tidak memunculkan overlay.
/// - Overlay muncul hanya saat IsConnected berubah dari false menjadi true, dan hanya untuk perangkat audio
///   (BluetoothDevice.ClassOfDevice.MajorClass = AudioVideo: headset, headphone, speaker). Mouse dan keyboard diabaikan.
/// - Hanya nama perangkat yang ditampilkan. Sisa baterai tidak (tidak ada API resmi yang andal untuk perangkat Classic).
/// - Dua pembaruan beruntun untuk perangkat yang sama dalam beberapa detik hanya menghasilkan satu overlay.
/// - Nama dan ID perangkat TIDAK ditulis ke log dan TIDAK disimpan ke disk. Daftar hanya ada di memori selama pengamat hidup.
/// - Hanya terpasang selama BluetoothConnectedOverlay menyala. Dimatikan = event dilepas, pengamat dihentikan, daftar dikosongkan.
/// Event DeviceWatcher datang dari thread lain. Daftar dilindungi kunci, dan OverlayService.Show aman dipanggil dari thread mana pun.
/// </summary>
public sealed class BluetoothDeviceService : IBluetoothDeviceService, IDisposable
{
    private const string IsConnectedProperty = "System.Devices.Aep.IsConnected";
    private const string DeviceAddressProperty = "System.Devices.Aep.DeviceAddress";
    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(5);

    private sealed class DeviceState
    {
        public string Name = string.Empty;
        public bool Connected;
        public bool? IsAudio;
        public DateTime LastShownUtc = DateTime.MinValue;
    }

    private readonly ISettingsService _settingsService;
    private readonly IOverlayService _overlayService;
    private readonly object _lock = new();
    private readonly Dictionary<string, DeviceState> _devices = new();

    private DeviceWatcher? _watcher;
    private bool _initialized;
    private bool _running;
    private volatile bool _enumerationCompleted;

    public BluetoothDeviceService(ISettingsService settingsService, IOverlayService overlayService)
    {
        _settingsService = settingsService;
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
        bool wanted = _settingsService.Current.BluetoothConnectedOverlay;
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

    // =====================================================================
    // Start / Stop (UI thread)
    // =====================================================================

    private void Start()
    {
        _running = true;
        _enumerationCompleted = false;

        try
        {
            // Selector resmi untuk perangkat yang SUDAH dipasangkan: tidak ada pemindaian perangkat sekitar.
            string selector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);

            DeviceWatcher watcher = DeviceInformation.CreateWatcher(
                selector,
                new[] { IsConnectedProperty, DeviceAddressProperty },
                DeviceInformationKind.AssociationEndpoint);

            watcher.Added += OnAdded;
            watcher.Updated += OnUpdated;
            watcher.Removed += OnRemoved;
            watcher.EnumerationCompleted += OnEnumerationCompleted;

            _watcher = watcher;
            watcher.Start();
        }
        catch (Exception ex)
        {
            // Tidak ada adapter Bluetooth atau layanan tidak tersedia: diam.
            Log.Information(ex, "Bluetooth device watcher could not start");
            StopWatcher();
        }
    }

    private void Stop()
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        StopWatcher();
    }

    private void StopWatcher()
    {
        DeviceWatcher? watcher = _watcher;
        _watcher = null;

        if (watcher is not null)
        {
            watcher.Added -= OnAdded;
            watcher.Updated -= OnUpdated;
            watcher.Removed -= OnRemoved;
            watcher.EnumerationCompleted -= OnEnumerationCompleted;

            try
            {
                if (watcher.Status == DeviceWatcherStatus.Started || watcher.Status == DeviceWatcherStatus.EnumerationCompleted)
                {
                    watcher.Stop();
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Bluetooth device watcher stop failed");
            }
        }

        _enumerationCompleted = false;
        lock (_lock)
        {
            _devices.Clear();
        }
    }

    // =====================================================================
    // Event DeviceWatcher (bukan thread UI)
    // =====================================================================

    private void OnAdded(DeviceWatcher sender, DeviceInformation info)
    {
        if (!ReferenceEquals(sender, _watcher))
        {
            return;
        }

        // Daftar awal dan perangkat yang baru dipasangkan: hanya dicatat sebagai acuan.
        bool connected = info.Properties.TryGetValue(IsConnectedProperty, out object? value) && value is bool b && b;

        lock (_lock)
        {
            _devices[info.Id] = new DeviceState { Name = info.Name ?? string.Empty, Connected = connected };
        }
    }

    private void OnUpdated(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        if (!ReferenceEquals(sender, _watcher))
        {
            return;
        }

        if (!update.Properties.TryGetValue(IsConnectedProperty, out object? value))
        {
            return; // bukan perubahan status tersambung
        }

        bool connected = value is bool b && b;
        bool justConnected;

        lock (_lock)
        {
            if (!_devices.TryGetValue(update.Id, out DeviceState? state))
            {
                return;
            }

            justConnected = !state.Connected && connected;
            state.Connected = connected;
        }

        if (justConnected && _enumerationCompleted)
        {
            _ = NotifyAsync(update.Id);
        }
    }

    private void OnRemoved(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        if (!ReferenceEquals(sender, _watcher))
        {
            return;
        }

        lock (_lock)
        {
            _devices.Remove(update.Id);
        }
    }

    private void OnEnumerationCompleted(DeviceWatcher sender, object args)
    {
        if (!ReferenceEquals(sender, _watcher))
        {
            return;
        }

        _enumerationCompleted = true;

        int count;
        lock (_lock)
        {
            count = _devices.Count;
        }

        Log.Information("Bluetooth watcher ready: {Count} paired device(s)", count); // hanya jumlah, tanpa nama atau ID
    }

    // =====================================================================
    // Menampilkan overlay
    // =====================================================================

    private async Task NotifyAsync(string id)
    {
        try
        {
            string name;
            bool? knownAudio;

            lock (_lock)
            {
                if (!_devices.TryGetValue(id, out DeviceState? state))
                {
                    return;
                }

                name = state.Name;
                knownAudio = state.IsAudio;
            }

            bool isAudio = knownAudio ?? await IsAudioDeviceAsync(id);

            DateTime now = DateTime.UtcNow;
            lock (_lock)
            {
                if (!_devices.TryGetValue(id, out DeviceState? state))
                {
                    return;
                }

                state.IsAudio = isAudio;

                if (!isAudio || now - state.LastShownUtc < MinInterval)
                {
                    return;
                }

                state.LastShownUtc = now;
            }

            if (!_running)
            {
                return; // dimatikan saat pencarian kelas perangkat berjalan
            }

            string label = string.IsNullOrWhiteSpace(name) ? "Bluetooth device" : name;

            _overlayService.Show(new OverlayRequest(
                "device:" + id,
                OverlayKind.Chip,
                OverlayPriority.Device,
                MotionTokens.OverlayDeviceMs)
            {
                IconKey = "IconBluetooth",
                Title = label + " connected"
            });
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Bluetooth connected overlay failed");
        }
    }

    /// <summary>Perangkat audio (headset, headphone, speaker) menurut Bluetooth Class of Device. Gagal membaca dianggap bukan audio.</summary>
    private static async Task<bool> IsAudioDeviceAsync(string id)
    {
        BluetoothDevice? device = null;
        try
        {
            device = await BluetoothDevice.FromIdAsync(id);
            return device?.ClassOfDevice.MajorClass == BluetoothMajorClass.AudioVideo;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Bluetooth class of device lookup failed");
            return false;
        }
        finally
        {
            device?.Dispose();
        }
    }
}
