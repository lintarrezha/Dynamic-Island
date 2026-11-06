using System.IO;
namespace iLand.Helpers;

/// <summary>
/// Satu tempat untuk semua lokasi folder/file milik iLand.
/// Dengan begitu, path tidak tersebar dan tidak ditulis ulang di banyak file.
/// </summary>
public static class AppPaths
{
    public const string AppName = "iLand";

    /// <summary>Contoh: C:\Users\NamaAnda\AppData\Roaming\iLand</summary>
    public static string AppDataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppName);

    public static string LogsFolder { get; } = Path.Combine(AppDataFolder, "logs");

    /// <summary>Serilog otomatis menambahkan tanggal, hasilnya: iland-20261106.log</summary>
    public static string LogFilePattern { get; } = Path.Combine(LogsFolder, "iland-.log");

    public static void EnsureFoldersExist()
    {
        Directory.CreateDirectory(AppDataFolder);
        Directory.CreateDirectory(LogsFolder);
    }
}
