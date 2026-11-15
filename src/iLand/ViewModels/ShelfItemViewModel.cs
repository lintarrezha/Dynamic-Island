using System.Diagnostics;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iLand.Interfaces;
using iLand.Models;

namespace iLand.ViewModels;

/// <summary>
/// Satu tile file di Shelf. Objeknya dipertahankan selama file masih ada di Shelf (tidak dibuat ulang tiap daftar berubah),
/// sehingga hanya tile BARU yang dianimasikan masuk dan hanya tile yang dihapus yang dianimasikan keluar.
/// </summary>
public sealed partial class ShelfItemViewModel : ObservableObject
{
    private readonly IShelfService _shelfService;

    public Guid Id { get; }

    public string FullPath { get; }

    public string Name { get; }

    /// <summary>False jika file atau folder aslinya sudah dipindah atau dihapus.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMissing))]
    private bool _exists;

    public bool IsMissing => !Exists;

    [ObservableProperty]
    private ImageSource? _icon;

    /// <summary>
    /// True untuk tile yang baru saja dijatuhkan: tile disembunyikan sampai animasi masuk berjalan (diurus ShelfView),
    /// lalu ShelfView mengembalikannya ke false.
    /// </summary>
    [ObservableProperty]
    private bool _isEntering;

    /// <summary>Urutan tile dalam satu kali drop (0, 1, 2, ...), untuk jeda antar tile saat animasi masuk.</summary>
    public int EnterIndex { get; set; }

    /// <summary>True saat tile sedang animasi keluar. Setelah selesai, ShelfViewModel menghapusnya dari daftar.</summary>
    [ObservableProperty]
    private bool _isLeaving;

    public ShelfItemViewModel(ShelfItem item, bool exists, ImageSource? icon, IShelfService shelfService)
    {
        _shelfService = shelfService;

        Id = item.Id;
        FullPath = item.Path;
        Exists = exists;
        Icon = icon;

        string fileName = System.IO.Path.GetFileName(item.Path.TrimEnd('\\', '/'));
        Name = string.IsNullOrEmpty(fileName) ? item.Path : fileName; // contoh: "D:\" tidak punya nama
    }

    /// <summary>Memperbarui status ada/hilang dan ikon (dipanggil saat tab Shelf dibuka ulang).</summary>
    public void Update(bool exists, ImageSource? icon)
    {
        Exists = exists;
        Icon = icon;
    }

    // Nama command dibuat otomatis: Remove -> RemoveCommand, Open -> OpenCommand.
    [RelayCommand]
    private void Remove()
    {
        _shelfService.Remove(Id);
    }

    /// <summary>Membuka file dengan aplikasi bawaannya (sama seperti klik dua kali di File Explorer).</summary>
    [RelayCommand]
    private void Open()
    {
        if (!Exists)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(FullPath) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // File tidak bisa dibuka (tidak ada aplikasi terkait, akses ditolak, dll.): abaikan.
        }
    }
}
