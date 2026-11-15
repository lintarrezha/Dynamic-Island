namespace iLand.Interfaces;

/// <summary>
/// Tempat membuka dan menutup window. ViewModel memakai interface ini,
/// jadi ViewModel tidak perlu tahu cara membuat window.
/// </summary>
public interface IWindowService
{
    /// <summary>Buka jendela Settings. Jika sudah terbuka, tampilkan yang sudah ada.</summary>
    void ShowSettings();

    /// <summary>Tutup seluruh aplikasi.</summary>
    void ExitApplication();
    
        /// <summary>Tampilkan island jika sedang tersembunyi, sembunyikan jika sedang tampil.</summary>
    void ToggleIsland();
}
