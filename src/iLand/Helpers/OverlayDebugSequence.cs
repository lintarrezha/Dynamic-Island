#if DEBUG
using System.Globalization;
using iLand.Interfaces;
using iLand.Models;
using Microsoft.Extensions.DependencyInjection;

namespace iLand.Helpers;

/// <summary>
/// HANYA build Debug. Menjalankan urutan uji antrean overlay sekali, 4 detik setelah aplikasi mulai,
/// bila variabel lingkungan ILAND_OVERLAY_TEST bernilai 1. Di build Release file ini tidak ikut terkompilasi.
/// Cara pakai (PowerShell):  $env:ILAND_OVERLAY_TEST = "1"; dotnet run --project src/iLand
/// Urutan dan hasil yang diharapkan ada di komentar tiap langkah. Tidak ada data pribadi yang ditulis ke log.
/// </summary>
public static class OverlayDebugSequence
{
    public static void RunIfRequested(IServiceProvider services)
    {
        if (Environment.GetEnvironmentVariable("ILAND_OVERLAY_TEST") != "1")
        {
            return;
        }

        // Dipanggil di UI thread, jadi await di bawah juga melanjutkan di UI thread.
        _ = RunAsync(services);
    }

    private static OverlayRequest Level(string key, string icon, double level) =>
        new(key, OverlayKind.Level, OverlayPriority.Level, MotionTokens.OverlayLevelMs)
        {
            IconKey = icon,
            Level = level,
            ValueText = Math.Round(level * 100).ToString(CultureInfo.InvariantCulture)
        };

    private static OverlayRequest Chip(string key, OverlayPriority priority, string icon, string text) =>
        new(key, OverlayKind.Chip, priority, MotionTokens.OverlayChipMs)
        {
            IconKey = icon,
            Title = text
        };

    private static async Task RunAsync(IServiceProvider services)
    {
        var overlay = services.GetRequiredService<IOverlayService>();
        var notifications = services.GetRequiredService<INotificationService>();

        await Task.Delay(4000);

        // 1. Update di tempat: SATU pill bar volume, bar naik 0 sampai 100. Hilang sekitar 1,5 detik setelah update terakhir.
        for (int i = 0; i <= 10; i++)
        {
            overlay.Show(Level("level:volume", "IconVolumeOn", i / 10.0));
            await Task.Delay(120);
        }

        await Task.Delay(3000);

        // 2. Level lain menggantikan langsung: bar volume lalu bar kecerahan (ikon matahari), tanpa pill baru yang menumpuk.
        overlay.Show(Level("level:volume", "IconVolumeOn", 0.5));
        await Task.Delay(500);
        overlay.Show(Level("level:brightness", "IconSun", 0.8));
        await Task.Delay(3000);

        // 3. Caps Lock saat bar level tampil: DIBUANG (prioritas lebih rendah, tidak boleh antre). Tidak ada chip sesudahnya.
        overlay.Show(Level("level:volume", "IconVolumeOn", 0.4));
        await Task.Delay(300);
        overlay.Show(Chip("caps", OverlayPriority.CapsLock, "IconLock", "Caps Lock On"));
        await Task.Delay(3500);

        // 4. Caps Lock sendiri: chip tampil, lebarnya mengikuti teks. Lalu "Off" memperbarui di tempat (lebar ikut berubah).
        overlay.Show(Chip("caps", OverlayPriority.CapsLock, "IconLock", "Caps Lock On"));
        await Task.Delay(1200);
        overlay.Show(Chip("caps", OverlayPriority.CapsLock, "IconLock", "Caps Lock Off"));
        await Task.Delay(3500);

        // 5. Perangkat disela charging: charging tampil dulu (menyela), chip perangkat menunggu dan tampil sesudahnya.
        overlay.Show(Chip("device:test", OverlayPriority.Device, "IconBluetooth", "Test Earbuds connected"));
        await Task.Delay(800);
        overlay.Show(Chip("charging", OverlayPriority.Charging, "IconBatterySaver", "Charging"));
        await Task.Delay(9000);

        // 6. Dua alert beruntun lewat NotificationService: yang kedua antre. Level selama alert DIBUANG.
        notifications.Show("Alert one", "First message", NotificationKind.Info);
        await Task.Delay(500);
        notifications.Show("Alert two", "Second message", NotificationKind.Success);
        await Task.Delay(500);
        overlay.Show(Level("level:volume", "IconVolumeOn", 0.7));
    }
}
#endif
