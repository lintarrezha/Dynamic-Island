using System.Windows.Media;

namespace iLand.Models;

/// <summary>Isi satu notifikasi yang akan ditampilkan di island.</summary>
public sealed class NotificationEventArgs : EventArgs
{
    /// <param name="thumbnail">Gambar kecil opsional di sisi kiri notifikasi (misalnya pratinjau screenshot). Null = ikon biasa.</param>
    public NotificationEventArgs(string title, string message, NotificationKind kind, ImageSource? thumbnail = null)
    {
        Title = title;
        Message = message;
        Kind = kind;
        Thumbnail = thumbnail;
    }

    public string Title { get; }

    public string Message { get; }

    public NotificationKind Kind { get; }

    /// <summary>Gambar kecil beku (frozen) yang hanya hidup di memori. Null jika notifikasi memakai ikon biasa.</summary>
    public ImageSource? Thumbnail { get; }
}
