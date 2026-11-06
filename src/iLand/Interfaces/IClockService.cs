namespace iLand.Interfaces;

/// <summary>
/// Kontrak untuk service jam. ViewModel hanya mengenal interface ini,
/// bukan class aslinya. Jadi implementasinya mudah diganti atau di-test.
/// </summary>
public interface IClockService
{
    /// <summary>Waktu saat ini.</summary>
    DateTime Now { get; }

    /// <summary>Dipanggil setiap detik.</summary>
    event EventHandler? Ticked;
}
