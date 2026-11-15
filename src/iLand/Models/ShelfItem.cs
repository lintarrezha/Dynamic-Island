namespace iLand.Models;

/// <summary>
/// Satu entri di Shelf. Yang disimpan hanyalah LOKASI file (referensi), bukan salinan isinya,
/// jadi Shelf tidak memakan ruang disk.
/// </summary>
public class ShelfItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Path { get; set; } = string.Empty;

    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.Now;
}
