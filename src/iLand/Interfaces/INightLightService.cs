namespace iLand.Interfaces;

/// <summary>
/// Night light Windows. TIDAK ada API resmi untuk menyalakannya, jadi ini membaca dan menulis nilai biner di registri
/// (CloudStore) yang dipakai Windows. Format nilai itu tidak didokumentasikan Microsoft (hanya oleh proyek komunitas) dan
/// bisa berubah di versi Windows lain. Karena itu penulisan hanya dilakukan jika bentuk nilainya dikenali persis;
/// jika tidak, IsSupported false dan pemanggil membuka halaman Settings sebagai gantinya.
/// </summary>
public interface INightLightService
{
    /// <summary>True jika nilai registrinya ada dan dikenali.</summary>
    bool IsSupported { get; }

    /// <summary>True jika Night light menyala (dibaca ulang setiap kali).</summary>
    bool IsOn { get; }

    /// <summary>Menyalakan atau mematikan. Mengembalikan false jika tidak didukung atau hasilnya tidak terverifikasi.</summary>
    Task<bool> SetAsync(bool on);
}
