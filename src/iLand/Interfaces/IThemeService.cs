namespace iLand.Interfaces;

/// <summary>
/// Mode gelap atau terang Windows. Windows tidak punya fungsi resmi untuk mengganti tema, jadi ini mengubah dua nilai
/// pengguna di registri (HKCU, tanpa hak administrator) lalu memberi tahu aplikasi lain. Teknik ini umum dipakai
/// tetapi tidak didokumentasikan resmi oleh Microsoft.
/// </summary>
public interface IThemeService
{
    /// <summary>True jika aplikasi Windows memakai tema gelap saat ini (dibaca ulang setiap kali).</summary>
    bool IsDarkMode { get; }

    /// <summary>Mengganti tema aplikasi dan sistem. Mengembalikan false jika registri tidak bisa ditulis.</summary>
    Task<bool> SetDarkModeAsync(bool dark);
}
