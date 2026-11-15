namespace iLand.Interfaces;

public interface IStartupService
{
    /// <summary>Apakah iLand terdaftar untuk berjalan otomatis saat Windows dinyalakan.</summary>
    bool IsEnabled { get; }

    /// <summary>Daftarkan atau batalkan pendaftaran start with Windows.</summary>
    void SetEnabled(bool enabled);
}
