namespace iLand.Models;

/// <summary>
/// Halaman (kategori) di menu samping jendela Settings.
/// Menambah halaman baru = tambah nilai di sini, satu SettingsNavItem di SettingsViewModel, satu ViewModel halaman
/// (didaftarkan di App.xaml.cs), dan satu DataTemplate di SettingsWindow.xaml.
/// MeetingAlerts disiapkan untuk Prompt 7 dan belum ada di menu.
/// </summary>
public enum SettingsPage
{
    Island,
    MusicLyrics,
    MeetingAlerts,
    Capture,
    General,
    About
}
