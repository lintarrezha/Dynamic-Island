using System.IO;
using System.Text.Json;
using iLand.Helpers;
using iLand.Interfaces;
using iLand.Models;
using Microsoft.Extensions.Logging;

namespace iLand.Services;

/// <summary>
/// Menyimpan daftar file Shelf di %APPDATA%\iLand\shelf.json.
/// Hanya path yang disimpan. File aslinya tidak pernah disalin, dipindah, atau dihapus oleh iLand.
/// </summary>
public sealed class ShelfService : IShelfService
{
    private const int MaxItems = 60;
    private const int MaxPathLength = 1024;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly ILogger<ShelfService> _logger;
    private readonly List<ShelfItem> _items;

    public IReadOnlyList<ShelfItem> Items => _items;

    public event EventHandler? Changed;

    public ShelfService(ILogger<ShelfService> logger)
    {
        _logger = logger;
        _items = Load();
    }

    public void AddPaths(IEnumerable<string> paths)
    {
        bool changed = false;

        foreach (string rawPath in paths)
        {
            if (_items.Count >= MaxItems)
            {
                break;
            }

            string? fullPath = NormalizePath(rawPath);
            if (fullPath is null)
            {
                continue; // tidak valid atau tidak ada
            }

            bool alreadyExists = _items.Any(item =>
                string.Equals(item.Path, fullPath, StringComparison.OrdinalIgnoreCase));
            if (alreadyExists)
            {
                continue;
            }

            _items.Add(new ShelfItem { Path = fullPath });
            changed = true;
        }

        if (changed)
        {
            SaveAndNotify();
        }
    }

    public void Remove(Guid id)
    {
        if (_items.RemoveAll(item => item.Id == id) > 0)
        {
            SaveAndNotify();
        }
    }

    public void Clear()
    {
        if (_items.Count > 0)
        {
            _items.Clear();
            SaveAndNotify();
        }
    }

    /// <summary>Memvalidasi input pengguna: path harus wajar dan benar-benar ada.</summary>
    private static string? NormalizePath(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath) || rawPath.Length > MaxPathLength)
        {
            return null;
        }

        try
        {
            string fullPath = Path.GetFullPath(rawPath);
            return File.Exists(fullPath) || Directory.Exists(fullPath) ? fullPath : null;
        }
        catch (Exception)
        {
            return null; // karakter tidak valid, dll.
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
            string tempPath = AppPaths.ShelfFilePath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(_items, JsonOptions));
            File.Move(tempPath, AppPaths.ShelfFilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save shelf");
        }
    }

    private List<ShelfItem> Load()
    {
        try
        {
            if (File.Exists(AppPaths.ShelfFilePath))
            {
                string json = File.ReadAllText(AppPaths.ShelfFilePath);
                var loaded = JsonSerializer.Deserialize<List<ShelfItem>>(json);
                if (loaded is not null)
                {
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read shelf, starting with an empty shelf");
        }

        return new List<ShelfItem>();
    }
}
