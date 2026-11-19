using System.Diagnostics;
using iLand.Native;

namespace iLand.Helpers;

/// <summary>Aksi sistem kecil untuk tab Control.</summary>
public static class SystemShell
{
    /// <summary>Mengunci layar Windows. Mengembalikan false jika Windows menolak.</summary>
    public static bool LockScreen() => ControlNative.LockWorkStation();

    /// <summary>
    /// Membuka halaman aplikasi Settings Windows lewat alamat "ms-settings:..." (cara resmi Microsoft).
    /// Hanya alamat yang diawali "ms-settings:" yang diterima.
    /// </summary>
    public static bool TryOpenSettingsPage(string uri)
    {
        if (!uri.StartsWith("ms-settings:", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
