using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iLand.Interfaces;
using iLand.Models;

namespace iLand.ViewModels;

/// <summary>
/// Satu baris tugas di daftar. Objek ini dipertahankan selama tugasnya ada (tidak dibuat ulang tiap daftar berubah),
/// supaya animasi centang dan coret hanya berjalan pada baris yang berubah.
/// </summary>
public sealed partial class TodoItemViewModel : ObservableObject
{
    private readonly ITodoService _todoService;

    public Guid Id { get; }

    public string Title { get; }

    /// <summary>Tugas selesai. Berubah saat pengguna mencentang, dan tampilan menganimasikan perubahannya.</summary>
    [ObservableProperty]
    private bool _isDone;

    /// <summary>True sementara baris sedang keluar (tinggi dan fade ke nol). Baris dihapus dari daftar setelah animasi selesai.</summary>
    [ObservableProperty]
    private bool _isLeaving;

    /// <summary>True untuk baris yang baru ditambahkan (memicu animasi masuk). Baris yang sudah ada saat tampilan dibuka tidak dianimasikan.</summary>
    [ObservableProperty]
    private bool _isNew;

    public TodoItemViewModel(TodoItem item, ITodoService todoService)
    {
        _todoService = todoService;
        Id = item.Id;
        Title = item.Title;
        _isDone = item.IsDone;
    }

    // Nama command dibuat otomatis: Toggle -> ToggleCommand, Delete -> DeleteCommand.
    [RelayCommand]
    private void Toggle()
    {
        _todoService.Toggle(Id);
    }

    [RelayCommand]
    private void Delete()
    {
        _todoService.Remove(Id);
    }
}
