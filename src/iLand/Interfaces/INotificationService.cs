using System.Windows.Media;
using iLand.Models;

namespace iLand.Interfaces;

/// <summary>
/// Pusat notifikasi. Bagian mana pun dari aplikasi (timer, baterai, dst.) cukup memanggil Show,
/// dan island yang menampilkannya.
/// </summary>
public interface INotificationService
{
    /// <summary>Dipanggil saat ada notifikasi baru untuk ditampilkan.</summary>
    event EventHandler<NotificationEventArgs>? NotificationRequested;

    /// <summary>Tampilkan notifikasi (dan putar suara jika diaktifkan di Settings).</summary>
    void Show(string title, string message, NotificationKind kind = NotificationKind.Info, ImageSource? thumbnail = null);
}
