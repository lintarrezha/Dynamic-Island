namespace iLand.Interfaces;

/// <summary>
/// Mengubah volume master sistem saat pengguna men-scroll di atas pill idle (opsi "Scroll over the island to change volume").
/// Tanpa pendengar permanen: perangkat output dibuka seperlunya saat scroll, diubah, lalu dilepas lagi.
/// Umpan baliknya memakai overlay volume yang sama dengan Tahap B (key yang sama, jadi bar diperbarui di tempat).
/// </summary>
public interface IScrollVolumeService
{
    /// <summary>
    /// Satu atau lebih "klik" roda mouse. wheelDelta positif (scroll ke atas) menaikkan volume, negatif menurunkan.
    /// Satu klik roda standar (120) = 2 persen. Roda presisi tinggi (delta kecil) dikumpulkan sampai genap 1 persen.
    /// Aman dipanggil dari UI thread: pekerjaan COM berjalan di thread latar.
    /// </summary>
    void Adjust(int wheelDelta);
}
