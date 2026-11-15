using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iLand.Helpers;
using iLand.Interfaces;
using iLand.Models;

namespace iLand.ViewModels;

/// <summary>ViewModel untuk tab Shelf: daftar file sementara dan status saat file diseret ke island.</summary>
public partial class ShelfViewModel : ObservableObject, IDisposable
{
    private readonly IShelfService _shelfService;
    private readonly PropertyChangedEventHandler _clipboardChangedHandler;

    // Timer sekali-jalan: menghapus tile yang sudah selesai animasi keluar. Hanya hidup selama ada tile yang keluar.
    private DispatcherTimer? _leaveTimer;

    // Ikon disimpan agar tidak diambil ulang dari Windows setiap Shelf dimuat ulang.
    private readonly Dictionary<string, ImageSource?> _iconCache = new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<ShelfItemViewModel> Items { get; } = new();

    /// <summary>Isi bagian Clipboard. Dikelola oleh ClipboardViewModel sendiri.</summary>
    public ClipboardViewModel Clipboard { get; }

    /// <summary>Bagian yang sedang dibuka: File (daftar file sementara) atau Clipboard (riwayat teks).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFileSection))]
    [NotifyPropertyChangedFor(nameof(IsClipboardSection))]
    [NotifyPropertyChangedFor(nameof(HeaderSummary))]
    [NotifyPropertyChangedFor(nameof(HasCurrentItems))]
    [NotifyPropertyChangedFor(nameof(SelectedSectionIndex))]
    private ShelfSection _selectedSection = ShelfSection.File;

    /// <summary>
    /// Bagian yang dibuka sebagai angka (0 = File, 1 = Clipboard), untuk SegmentedControl di header
    /// (kontrol itu bekerja dengan indeks). Urutan segmen di XAML HARUS sama dengan urutan enum ShelfSection.
    /// </summary>
    public int SelectedSectionIndex
    {
        get => (int)SelectedSection;
        set
        {
            if (Enum.IsDefined(typeof(ShelfSection), value))
            {
                SelectedSection = (ShelfSection)value;
            }
        }
    }

    public bool IsFileSection => SelectedSection == ShelfSection.File;

    public bool IsClipboardSection => SelectedSection == ShelfSection.Clipboard;

    /// <summary>Ringkasan di samping judul: jumlah item pada bagian yang sedang dibuka.</summary>
    public string HeaderSummary => IsFileSection ? SummaryText : Clipboard.SummaryText;

    /// <summary>True jika bagian yang sedang dibuka punya item (untuk menampilkan tombol Clear all).</summary>
    public bool HasCurrentItems => IsFileSection ? HasItems : Clipboard.HasItems;

    /// <summary>True saat ada file yang sedang diseret di atas island (untuk menyorot area drop).</summary>
    [ObservableProperty]
    private bool _isDragOver;

    /// <summary>
    /// Bertambah setiap kali ada file BARU yang berhasil dijatuhkan ke Shelf. ShelfDropZone memakainya untuk
    /// memutar riak biru sekali (perubahan angka = ada drop yang berhasil).
    /// </summary>
    [ObservableProperty]
    private int _dropCounter;

    [ObservableProperty]
    private bool _isEmpty = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrentItems))]
    private bool _hasItems;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderSummary))]
    private string _summaryText = string.Empty;

    public ShelfViewModel(IShelfService shelfService, ClipboardViewModel clipboardViewModel)
    {
        _shelfService = shelfService;
        Clipboard = clipboardViewModel;

        Sync(animate: false);
        _shelfService.Changed += OnShelfChanged;

        // Ringkasan dan tombol Clear all di header ikut berubah saat isi Clipboard berubah.
        _clipboardChangedHandler = (_, e) =>
        {
            if (e.PropertyName is nameof(ClipboardViewModel.SummaryText) or nameof(ClipboardViewModel.HasItems))
            {
                OnPropertyChanged(nameof(HeaderSummary));
                OnPropertyChanged(nameof(HasCurrentItems));
            }
        };
        Clipboard.PropertyChanged += _clipboardChangedHandler;
    }

    /// <summary>Dipanggil View saat file dijatuhkan ke island.</summary>
    public void AddDroppedPaths(IEnumerable<string> paths)
    {
        int before = _shelfService.Items.Count;

        _shelfService.AddPaths(paths); // memicu Changed, jadi tile baru dibuat dengan animasi masuk

        if (_shelfService.Items.Count > before)
        {
            DropCounter++;
        }
    }

    /// <summary>
    /// Muat ulang daftar. Dipanggil saat tab Shelf dibuka agar file yang sudah dihapus
    /// atau dipindah dari luar langsung ditandai "Missing".
    /// </summary>
    public void Refresh()
    {
        Sync(animate: false);
        Clipboard.RefreshTimes();
    }

    // ClearCurrent -> ClearCurrentCommand (dibuat otomatis oleh [RelayCommand]).
    // Tombol "Clear all" mengosongkan bagian yang sedang dibuka: file di Shelf, atau riwayat clipboard.
    [RelayCommand]
    private void ClearCurrent()
    {
        if (IsFileSection)
        {
            _shelfService.Clear();
        }
        else
        {
            Clipboard.ClearAllCommand.Execute(null);
        }
    }

    // Saat berpindah ke Clipboard, teks waktu ("5 min ago") diperbarui.
    partial void OnSelectedSectionChanged(ShelfSection value)
    {
        if (value == ShelfSection.Clipboard)
        {
            Clipboard.RefreshTimes();
        }
    }

    private void OnShelfChanged(object? sender, EventArgs e)
    {
        Sync(animate: true);
    }

    /// <summary>
    /// Menyamakan daftar tile dengan isi ShelfService TANPA membuat ulang tile yang sudah ada.
    /// Tile baru ditandai IsEntering (animasi masuk), tile yang dihapus ditandai IsLeaving (animasi keluar)
    /// lalu dihapus oleh timer. ShelfService hanya menambah di akhir atau menghapus, jadi urutan tile tetap benar.
    /// animate = false (muat awal, tab dibuka ulang, gerak berkurang): perubahan langsung tanpa animasi.
    /// </summary>
    private void Sync(bool animate)
    {
        bool animated = animate && !MotionTokens.ReducedMotion;
        var wantedIds = new HashSet<Guid>(_shelfService.Items.Select(i => i.Id));

        // 1. Tile yang sudah tidak ada di Shelf.
        bool anyLeaving = false;
        foreach (ShelfItemViewModel tile in Items.ToList())
        {
            if (tile.IsLeaving || wantedIds.Contains(tile.Id))
            {
                continue;
            }

            if (animated)
            {
                tile.IsLeaving = true;
                anyLeaving = true;
            }
            else
            {
                Items.Remove(tile);
            }
        }

        if (anyLeaving)
        {
            StartLeaveTimer();
        }

        // 2. Perbarui tile yang ada, tambahkan yang baru.
        var existing = Items.Where(t => !t.IsLeaving).ToDictionary(t => t.Id);
        int enterIndex = 0;

        foreach (var item in _shelfService.Items)
        {
            bool exists = File.Exists(item.Path) || Directory.Exists(item.Path);

            ImageSource? icon = null;
            if (exists && !_iconCache.TryGetValue(item.Path, out icon))
            {
                icon = FileIconHelper.GetIcon(item.Path);
                _iconCache[item.Path] = icon;
            }

            if (existing.TryGetValue(item.Id, out ShelfItemViewModel? current))
            {
                current.Update(exists, icon);
            }
            else
            {
                Items.Add(new ShelfItemViewModel(item, exists, icon, _shelfService)
                {
                    IsEntering = animated,
                    EnterIndex = enterIndex++
                });
            }
        }

        // Buang ikon milik item yang sudah tidak ada di Shelf.
        var livePaths = new HashSet<string>(_shelfService.Items.Select(i => i.Path), StringComparer.OrdinalIgnoreCase);
        foreach (string cachedPath in _iconCache.Keys.Where(p => !livePaths.Contains(p)).ToList())
        {
            _iconCache.Remove(cachedPath);
        }

        // Hitungan memakai isi ShelfService (bukan Items), jadi keadaan kosong langsung muncul
        // sambil tile terakhir masih animasi keluar.
        int count = _shelfService.Items.Count;
        IsEmpty = count == 0;
        HasItems = count > 0;
        SummaryText = count == 0 ? string.Empty : count == 1 ? "1 item" : $"{count} items";
    }

    private void StartLeaveTimer()
    {
        if (_leaveTimer is null)
        {
            _leaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(MotionTokens.RowMs + 30) };
            _leaveTimer.Tick += OnLeaveTimerTick;
        }

        _leaveTimer.Stop();
        _leaveTimer.Start();
    }

    private void OnLeaveTimerTick(object? sender, EventArgs e)
    {
        _leaveTimer?.Stop();

        foreach (ShelfItemViewModel tile in Items.Where(t => t.IsLeaving).ToList())
        {
            Items.Remove(tile);
        }
    }

    public void Dispose()
    {
        if (_leaveTimer is not null)
        {
            _leaveTimer.Stop();
            _leaveTimer.Tick -= OnLeaveTimerTick;
        }

        _shelfService.Changed -= OnShelfChanged;
        Clipboard.PropertyChanged -= _clipboardChangedHandler;
    }
}
