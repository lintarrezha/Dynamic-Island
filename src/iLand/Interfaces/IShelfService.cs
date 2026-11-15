using iLand.Models;

namespace iLand.Interfaces;

public interface IShelfService
{
    IReadOnlyList<ShelfItem> Items { get; }

    /// <summary>Dipanggil setiap isi Shelf berubah.</summary>
    event EventHandler? Changed;

    /// <summary>
    /// Tambahkan file atau folder ke Shelf. Path yang tidak ada, tidak valid, atau sudah ada
    /// di Shelf diabaikan.
    /// </summary>
    void AddPaths(IEnumerable<string> paths);

    void Remove(Guid id);

    void Clear();
}
