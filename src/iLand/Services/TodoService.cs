using System.IO;
using System.Text.Json;
using iLand.Helpers;
using iLand.Interfaces;
using iLand.Models;
using Microsoft.Extensions.Logging;

namespace iLand.Services;

/// <summary>
/// Menyimpan daftar tugas di %APPDATA%\iLand\todos.json.
/// Semua data hanya ada di komputer pengguna dan tidak dikirim ke mana pun.
/// </summary>
public sealed class TodoService : ITodoService
{
    private const int MaxItems = 200;
    private const int MaxTitleLength = 200;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly ILogger<TodoService> _logger;
    private readonly List<TodoItem> _items;

    public IReadOnlyList<TodoItem> Items => _items;

    public event EventHandler? Changed;

    public TodoService(ILogger<TodoService> logger)
    {
        _logger = logger;
        _items = Load();
    }

    public void Add(string title)
    {
        string cleanTitle = (title ?? string.Empty).Trim();
        if (cleanTitle.Length == 0 || _items.Count >= MaxItems)
        {
            return;
        }

        if (cleanTitle.Length > MaxTitleLength)
        {
            cleanTitle = cleanTitle[..MaxTitleLength];
        }

        _items.Add(new TodoItem { Title = cleanTitle });
        SaveAndNotify();
    }

    public void Toggle(Guid id)
    {
        TodoItem? item = _items.FirstOrDefault(i => i.Id == id);
        if (item is null)
        {
            return;
        }

        item.IsDone = !item.IsDone;
        item.CompletedAt = item.IsDone ? DateTimeOffset.Now : null;
        SaveAndNotify();
    }

    public void Remove(Guid id)
    {
        if (_items.RemoveAll(i => i.Id == id) > 0)
        {
            SaveAndNotify();
        }
    }

    public void ClearCompleted()
    {
        if (_items.RemoveAll(i => i.IsDone) > 0)
        {
            SaveAndNotify();
        }
    }

    private void SaveAndNotify()
    {
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.AppDataFolder);

            // Tulis ke file sementara dulu, lalu timpa file asli, agar file tidak rusak
            // jika aplikasi mati di tengah penulisan.
            string tempPath = AppPaths.TodosFilePath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(_items, JsonOptions));
            File.Move(tempPath, AppPaths.TodosFilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save todos");
        }
    }

    private List<TodoItem> Load()
    {
        try
        {
            if (File.Exists(AppPaths.TodosFilePath))
            {
                string json = File.ReadAllText(AppPaths.TodosFilePath);
                var loaded = JsonSerializer.Deserialize<List<TodoItem>>(json);
                if (loaded is not null)
                {
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read todos, starting with an empty list");

            // Simpan salinan file yang rusak agar data pengguna tidak hilang diam-diam.
            try
            {
                File.Copy(AppPaths.TodosFilePath, AppPaths.TodosFilePath + ".corrupt", overwrite: true);
            }
            catch (Exception copyEx)
            {
                _logger.LogDebug(copyEx, "Could not back up the corrupt todos file");
            }
        }

        return new List<TodoItem>();
    }
}
