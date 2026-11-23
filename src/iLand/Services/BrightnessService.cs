using System.Management;
using System.Windows.Threading;
using iLand.Interfaces;
using Microsoft.Extensions.Logging;

namespace iLand.Services;

/// <summary>
/// Kecerahan layar internal lewat WMI namespace root\wmi. Semua pemanggilan WMI berjalan di thread latar
/// (bisa lambat, terutama yang pertama). Perubahan dari luar (tombol Fn, slider Windows) datang lewat
/// WmiMonitorBrightnessEvent, jadi tidak ada polling. Pendengar hanya terpasang antara Attach dan Detach.
/// </summary>
public sealed class BrightnessService : IBrightnessService
{
    private const string Scope = @"root\wmi";

    private readonly ILogger<BrightnessService> _logger;
    private readonly Dispatcher _ui;
    private readonly object _writeLock = new();

    private readonly HashSet<string> _owners = new();

    private ManagementEventWatcher? _watcher;
    private bool _attached;
    private int _attachVersion;

    private int _pendingPercent = -1;
    private bool _writing;

    public BrightnessService(ILogger<BrightnessService> logger)
    {
        _logger = logger;
        _ui = Dispatcher.CurrentDispatcher; // service ini dibuat di UI thread
    }

    public bool IsAvailable { get; private set; }

    public int Percent { get; private set; }

    public event EventHandler? Changed;

    public void Attach() => Attach("ui");

    public void Detach() => Detach("ui");

    public void Attach(string owner)
    {
        _owners.Add(owner);
        _logger.LogInformation("Brightness attach by {Owner}: owners={Count}, alreadyAttached={Attached}", owner, _owners.Count, _attached);

        if (_attached)
        {
            return;
        }

        _attached = true;
        int version = ++_attachVersion;

        // Baca dan pasang pendengar di thread latar; hasilnya dikirim kembali ke UI thread.
        _ = Task.Run(() =>
        {
            int? current = ReadCurrentPercent();
            ManagementEventWatcher? watcher = current.HasValue ? CreateWatcher() : null;

            _ui.BeginInvoke(new Action(() =>
            {
                // Dilepas saat menunggu: bersihkan, jangan memakai hasilnya.
                if (!_attached || version != _attachVersion)
                {
                    StopWatcher(watcher);
                    return;
                }

                _watcher = watcher;
                _logger.LogInformation("Brightness ready: available={Available}, listening={Listening}", current.HasValue, watcher != null);
                IsAvailable = current.HasValue;
                Percent = current ?? 0;
                Changed?.Invoke(this, EventArgs.Empty);
            }));
        });
    }

    public void Detach(string owner)
    {
        bool removed = _owners.Remove(owner);
        _logger.LogInformation("Brightness detach by {Owner}: removed={Removed}, ownersLeft={Count}, attached={Attached}", owner, removed, _owners.Count, _attached);

        // Pemilik lain masih memakai (misalnya overlay saat tab Control menutup): jangan dilepas.
        if (!removed || _owners.Count > 0 || !_attached)
        {
            return;
        }

        _attached = false;
        _attachVersion++;
        StopWatcher(_watcher);
        _watcher = null;
    }

    public void SetPercent(int percent)
    {
        if (!IsAvailable)
        {
            return;
        }

        percent = Math.Clamp(percent, 0, 100);
        Percent = percent;
        Changed?.Invoke(this, EventArgs.Empty);

        lock (_writeLock)
        {
            _pendingPercent = percent;
            if (_writing)
            {
                return;
            }

            _writing = true;
        }

        _ = Task.Run(WriteLoop);
    }

    // Menulis nilai terbaru berulang sampai tidak ada yang tertunda. Slider yang digeser cepat hanya menghasilkan
    // satu penulisan per sekitar 40 ms.
    private void WriteLoop()
    {
        while (true)
        {
            int value;
            lock (_writeLock)
            {
                if (_pendingPercent < 0)
                {
                    _writing = false;
                    return;
                }

                value = _pendingPercent;
                _pendingPercent = -1;
            }

            WritePercent(value);
            Thread.Sleep(40);
        }
    }

    private void WritePercent(int percent)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(Scope, "SELECT * FROM WmiMonitorBrightnessMethods");
            foreach (ManagementObject instance in searcher.Get())
            {
                using (instance)
                {
                    // WmiSetBrightness(Timeout uint32, Brightness uint8): timeout 1 = berlaku segera.
                    instance.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)percent });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not set the screen brightness");
        }
    }

    private int? ReadCurrentPercent()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(Scope, "SELECT * FROM WmiMonitorBrightness");
            foreach (ManagementObject instance in searcher.Get())
            {
                using (instance)
                {
                    // Instance pertama sudah cukup: layar internal laptop hanya satu.
                    return Convert.ToInt32(instance["CurrentBrightness"]);
                }
            }
        }
        catch (Exception ex)
        {
            // Tidak ada layar internal, atau akses ditolak: fitur diam, tanpa error ke pengguna.
            _logger.LogInformation(ex, "Screen brightness is not available through WMI");
        }

        return null;
    }

    private ManagementEventWatcher? CreateWatcher()
    {
        try
        {
            var watcher = new ManagementEventWatcher(
                new ManagementScope(Scope), new WqlEventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
            watcher.EventArrived += OnBrightnessEvent;
            watcher.Start();
            return watcher;
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "Could not listen to brightness changes");
            return null;
        }
    }

    // Datang dari thread WMI.
    private void OnBrightnessEvent(object sender, EventArrivedEventArgs e)
    {
        int percent;
        try
        {
            percent = Convert.ToInt32(e.NewEvent["Brightness"]);
        }
        catch (Exception)
        {
            return;
        }

        _ui.BeginInvoke(new Action(() =>
        {
            if (!_attached)
            {
                _logger.LogInformation("Brightness event {Percent} ignored: not attached", percent);
                return;
            }

            // Abaikan gema dari penulisan kita sendiri yang masih berjalan.
            lock (_writeLock)
            {
                if (_writing || _pendingPercent >= 0)
                {
                    _logger.LogInformation("Brightness event {Percent} ignored: own write in progress", percent);
                    return;
                }
            }

            _logger.LogInformation("Brightness event {Percent} accepted", percent);
            Percent = percent;
            Changed?.Invoke(this, EventArgs.Empty);
        }));
    }

    private static void StopWatcher(ManagementEventWatcher? watcher)
    {
        if (watcher == null)
        {
            return;
        }

        try
        {
            watcher.Stop();
            watcher.Dispose();
        }
        catch (Exception)
        {
            // Sudah berhenti.
        }
    }
}
