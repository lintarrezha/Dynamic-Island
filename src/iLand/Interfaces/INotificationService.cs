using System.Windows.Media;
using iLand.Models;

namespace iLand.Interfaces;

/// <summary>
/// Pintu masuk notifikasi teks. Bagian mana pun dari aplikasi (timer, baterai, screenshot, dst.) cukup memanggil Show.
/// Penampilannya diurus IOverlayService (antrean overlay), bukan lagi event di sini.
/// </summary>
public interface INotificationService
{
    /// <summary>Tampilkan notifikasi (dan putar suara jika diaktifkan di Settings).</summary>
    void Show(string title, string message, NotificationKind kind = NotificationKind.Info, ImageSource? thumbnail = null);
}
