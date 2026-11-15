namespace iLand.Models;

/// <summary>Satu tugas. Objek ini disimpan apa adanya ke todos.json.</summary>
public class TodoItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = string.Empty;

    public bool IsDone { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public DateTimeOffset? CompletedAt { get; set; }
}
