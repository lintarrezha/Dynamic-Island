using System.Media;
using System.Windows.Media;
using iLand.Helpers;
using iLand.Interfaces;
using iLand.Models;

namespace iLand.Services;

/// <summary>
/// Notifikasi teks (timer selesai, screenshot tersimpan, baterai rendah, dan sebagainya).
/// Memutar suara bila diaktifkan, lalu meneruskannya ke antrean overlay dengan prioritas tertinggi (Alert).
/// Overlay lain (volume, kecerahan, charging, perangkat, Caps Lock) TIDAK lewat sini dan tidak memutar suara.
/// </summary>
public sealed class NotificationService : INotificationService
{
    private readonly ISettingsService _settingsService;
    private readonly IOverlayService _overlayService;

    public NotificationService(ISettingsService settingsService, IOverlayService overlayService)
    {
        _settingsService = settingsService;
        _overlayService = overlayService;
    }

    public void Show(string title, string message, NotificationKind kind = NotificationKind.Info, ImageSource? thumbnail = null)
    {
        // Peringatan baterai rendah (Warning) dianggap kritis dan tetap tampil saat mode rapat. Lainnya ditahan, termasuk suaranya.
        bool critical = kind == NotificationKind.Warning;
        if (_overlayService.IsMeetingMode && !critical)
        {
            return;
        }

        if (_settingsService.Current.PlayNotificationSound)
        {
            // Suara bawaan Windows, tidak ada file suara tambahan.
            SystemSound sound = kind == NotificationKind.Warning
                ? SystemSounds.Exclamation
                : SystemSounds.Asterisk;
            sound.Play();
        }

        // Key unik: dua notifikasi beruntun tidak saling menimpa, yang kedua antre.
        _overlayService.Show(new OverlayRequest(
            "alert:" + Guid.NewGuid().ToString("N"),
            OverlayKind.Text,
            OverlayPriority.Alert,
            MotionTokens.OverlayAlertMs)
        {
            Title = title,
            Message = message,
            Glyph = GetGlyph(kind),
            Thumbnail = thumbnail,
            IsCritical = critical
        });
    }

    // Karakter simbol dari font "Segoe UI Symbol" yang sudah ada di Windows (dipindah dari IslandViewModel).
    private static string GetGlyph(NotificationKind kind)
    {
        return kind switch
        {
            NotificationKind.Success => "\u2713",     // centang
            NotificationKind.Warning => "\u26A0",     // segitiga peringatan
            NotificationKind.BatteryFull => "\u26A1", // petir
            _ => "\u2139"                             // huruf i
        };
    }
}
