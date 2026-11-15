using iLand.Models;

namespace iLand.Interfaces;

public interface ITodoService
{
    /// <summary>Semua tugas (urutan dibuat).</summary>
    IReadOnlyList<TodoItem> Items { get; }

    /// <summary>Dipanggil setiap daftar tugas berubah.</summary>
    event EventHandler? Changed;

    void Add(string title);

    /// <summary>Tandai selesai, atau batalkan tanda selesai.</summary>
    void Toggle(Guid id);

    void Remove(Guid id);

    void ClearCompleted();
}
