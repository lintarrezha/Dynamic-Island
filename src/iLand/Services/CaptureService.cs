using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using iLand.Helpers;
using iLand.Interfaces;
using iLand.Models;
using iLand.Native;
using iLand.Views;
using Microsoft.Extensions.Logging;

namespace iLand.Services;

/// <summary>
/// Mengambil screenshot (layar penuh, area, jendela), menyimpannya ke Pictures\iLand, dan menyalinnya
/// ke clipboard. Gambar hanya disimpan atas perintah pengguna dan tidak pernah dikirim ke mana pun.
///
/// Untuk area dan jendela, layar "dibekukan" dulu (diambil sebelum overlay tampil), jadi hasilnya
/// persis seperti yang terlihat saat pintasan ditekan dan tidak ada overlay yang ikut terambil.
/// Window island sudah dikecualikan dari tangkapan layar, jadi island tidak ikut terambil.
/// </summary>
public sealed class CaptureService : ICaptureService
{
    // Ukuran pratinjau di notifikasi (piksel). Tampil 40 x 40 di island, dibuat 2x agar tajam di layar 200% DPI.
    private const int NotificationThumbnailPx = 80;

    private readonly ILogger<CaptureService> _logger;
    private readonly IMonitorService _monitorService;
    private readonly INotificationService _notificationService;
    private readonly Dispatcher _uiDispatcher;

    private bool _isCapturing;

    public event EventHandler? CaptureStarted;

    public CaptureService(
        ILogger<CaptureService> logger,
        IMonitorService monitorService,
        INotificationService notificationService,
        IHotkeyService hotkeyService)
    {
        _logger = logger;
        _monitorService = monitorService;
        _notificationService = notificationService;
        _uiDispatcher = Dispatcher.CurrentDispatcher;

        // Pintasan ditangani setelah callback Windows selesai (bukan di dalamnya),
        // karena pengambilan area membuka window dan menjalankan message loop sendiri.
        hotkeyService.CaptureAreaRequested += (_, _) => _uiDispatcher.BeginInvoke(new Action(CaptureArea));
    }

    public void CaptureFullScreen()
    {
        RunCapture(() =>
        {
            MonitorInfo monitor = GetMonitorUnderCursor();
            return ScreenGrabber.Grab(monitor.Bounds);
        });
    }

    public void CaptureArea()
    {
        RunCapture(() => SelectRegion(CaptureSelectionMode.Area));
    }

    public void CaptureWindow()
    {
        RunCapture(() => SelectRegion(CaptureSelectionMode.Window));
    }

    // ---- Alur umum ----

    /// <summary>Menjalankan satu pengambilan layar. Hanya satu yang boleh berjalan pada satu waktu.</summary>
    private void RunCapture(Func<BitmapSource?> capture)
    {
        if (_isCapturing)
        {
            return;
        }

        _isCapturing = true;
        try
        {
            CaptureStarted?.Invoke(this, EventArgs.Empty);

            BitmapSource? image = capture();
            if (image is null)
            {
                return; // dibatalkan pengguna atau gagal diambil
            }

            SaveAndNotify(image);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Screenshot failed");
            _notificationService.Show("Screenshot failed", "Something went wrong. See the log for details.", NotificationKind.Warning);
        }
        finally
        {
            _isCapturing = false;
        }
    }

    /// <summary>Membekukan layar, menampilkan overlay pemilihan, lalu memotong hasilnya.</summary>
    private BitmapSource? SelectRegion(CaptureSelectionMode mode)
    {
        MonitorInfo monitor = GetMonitorUnderCursor();

        BitmapSource? frozen = ScreenGrabber.Grab(monitor.Bounds);
        if (frozen is null)
        {
            _notificationService.Show("Screenshot failed", "Could not read the screen.", NotificationKind.Warning);
            return null;
        }

        var overlay = new SelectionOverlayWindow(frozen, monitor.Bounds, mode, WindowFinder.GetWindowBoundsAt);
        overlay.ShowDialog();

        if (overlay.SelectedRegion is not Int32Rect region)
        {
            return null; // pengguna membatalkan (Esc atau klik kanan)
        }

        var cropped = new CroppedBitmap(frozen, region);
        cropped.Freeze();
        return cropped;
    }

    private MonitorInfo GetMonitorUnderCursor()
    {
        if (NativeMethods.GetCursorPos(out NativeMethods.NativePoint point))
        {
            MonitorInfo? match = _monitorService.GetMonitors()
                .FirstOrDefault(m => m.Bounds.Contains(point.X, point.Y));
            if (match is not null)
            {
                return match;
            }
        }

        return _monitorService.GetMonitorOrPrimary(null);
    }

    // ---- Menyimpan hasil ----

    private void SaveAndNotify(BitmapSource image)
    {
        string? filePath = SaveToPictures(image);
        CopyToClipboard(image);

        if (filePath is null)
        {
            _notificationService.Show("Screenshot failed", "The image could not be saved.", NotificationKind.Warning);
        }
        else
        {
            // Pratinjau kecil hanya hidup di memori (tidak ditulis ke disk dan tidak masuk log).
            BitmapSource? thumbnail = ThumbnailHelper.CreateSquare(image, NotificationThumbnailPx);
            _notificationService.Show("Screenshot saved", Path.GetFileName(filePath), NotificationKind.Success, thumbnail);
        }
    }

    private string? SaveToPictures(BitmapSource image)
    {
        try
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "iLand");
            Directory.CreateDirectory(folder);

            // Nama file tidak boleh mengandung ":", jadi jam memakai titik.
            string fileName = $"Screenshot {DateTime.Now:yyyy-MM-dd HH.mm.ss}.png";
            string filePath = Path.Combine(folder, fileName);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));

            using FileStream stream = File.Create(filePath);
            encoder.Save(stream);

            _logger.LogInformation("Screenshot saved: {Path}", filePath);
            return filePath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save screenshot");
            return null;
        }
    }

    private void CopyToClipboard(BitmapSource image)
    {
        try
        {
            Clipboard.SetImage(image);
        }
        catch (ExternalException ex)
        {
            // Clipboard sedang dipakai aplikasi lain. Screenshot tetap tersimpan sebagai file.
            _logger.LogDebug(ex, "Could not copy screenshot to clipboard");
        }
    }
}
