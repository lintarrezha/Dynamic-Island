namespace iLand.Models;

/// <summary>
/// Tab yang tersedia di expanded island.
/// URUTAN PENTING: panel di TabHost (IslandWindow.xaml) dan item tab bar harus berurutan sama dengan nilai di sini.
/// Menambah tab berarti menyisipkan satu nilai di sini, satu item tab bar, dan satu panel di TabHost pada posisi yang sama.
/// </summary>
public enum IslandTab
{
    Home,
    Music,
    Mixer,
    Timer,
    Todo,
    Shelf,
    Capture
}
