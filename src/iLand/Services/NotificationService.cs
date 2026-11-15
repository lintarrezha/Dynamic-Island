using System.Media;
using System.Windows.Media;
using iLand.Interfaces;
using iLand.Models;

namespace iLand.Services;

public sealed class NotificationService : INotificationService
{
    private readonly ISettingsService _settingsService;

    public event EventHandler<NotificationEventArgs>? NotificationRequested;

    public NotificationService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public void Show(string title, string message, NotificationKind kind = NotificationKind.Info, ImageSource? thumbnail = null)
    {
        if (_settingsService.Current.PlayNotificationSound)
        {
            // Suara bawaan Windows, tidak ada file suara tambahan.
            SystemSound sound = kind == NotificationKind.Warning
                ? SystemSounds.Exclamation
                : SystemSounds.Asterisk;
            sound.Play();
        }

        NotificationRequested?.Invoke(this, new NotificationEventArgs(title, message, kind, thumbnail));
    }
}
