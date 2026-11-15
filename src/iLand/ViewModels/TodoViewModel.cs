using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iLand.Helpers;
using iLand.Interfaces;
using iLand.Models;

namespace iLand.ViewModels;

/// <summary>ViewModel untuk tab Todo: daftar tugas, kotak tambah tugas, dan ringkasan.</summary>
public partial class TodoViewModel : ObservableObject, IDisposable
{
    // Jeda sebelum baris yang dicentang pindah ke bagian selesai, dan sebelum baris yang dihapus benar-benar dibuang.
    // Sedikit lebih lama dari animasi baris (MotionTokens.RowMs) agar animasi selalu selesai dulu.
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(MotionTokens.RowMs + 160);

    private readonly ITodoService _todoService;

    // Timer sekali jalan. Dibuat saat pertama dibutuhkan dan berhenti setelah dipakai, jadi tidak ada timer yang menganggur.
    private DispatcherTimer? _settleTimer;

    public ObservableCollection<TodoItemViewModel> Items { get; } = new();

    [ObservableProperty]
    private string _newTaskTitle = string.Empty;

    [ObservableProperty]
    private bool _isEmpty = true;

    [ObservableProperty]
    private bool _hasCompleted;

    /// <summary>Ringkasan di header, contoh "2/5 done". Kosong bila belum ada tugas.</summary>
    [ObservableProperty]
    private string _summaryText = string.Empty;

    public TodoViewModel(ITodoService todoService)
    {
        _todoService = todoService;

        Reload(initial: true);
        _todoService.Changed += OnTodosChanged;
    }

    [RelayCommand]
    private void AddTask()
    {
        if (string.IsNullOrWhiteSpace(NewTaskTitle))
        {
            return;
        }

        _todoService.Add(NewTaskTitle);
        NewTaskTitle = string.Empty;
    }

    [RelayCommand]
    private void ClearCompleted()
    {
        _todoService.ClearCompleted();
    }

    private void OnTodosChanged(object? sender, EventArgs e)
    {
        Reload(initial: false);
    }

    /// <summary>Urutan yang benar: tugas belum selesai di atas, yang selesai di bawah, sesama jenis menurut waktu dibuat.</summary>
    private List<TodoItem> GetSortedSource()
    {
        return _todoService.Items
            .OrderBy(item => item.IsDone)
            .ThenBy(item => item.CreatedAt)
            .ToList();
    }

    /// <summary>
    /// Menyelaraskan daftar tampilan dengan data di service tanpa membangun ulang semuanya:
    /// baris yang sudah ada dipertahankan (hanya IsDone yang diperbarui), baris baru disisipkan dengan animasi masuk,
    /// baris yang hilang ditandai IsLeaving lalu dibuang setelah animasi keluar selesai.
    /// </summary>
    private void Reload(bool initial)
    {
        List<TodoItem> source = GetSortedSource();

        if (initial)
        {
            Items.Clear();
            foreach (var item in source)
            {
                Items.Add(new TodoItemViewModel(item, _todoService));
            }
        }
        else
        {
            bool needSettle = false;
            var existing = Items.Where(row => !row.IsLeaving).ToDictionary(row => row.Id);
            var sourceIds = source.Select(item => item.Id).ToHashSet();

            // 1. Baris yang sudah tidak ada di data: mulai animasi keluar.
            foreach (var leaving in existing.Values.Where(row => !sourceIds.Contains(row.Id)))
            {
                leaving.IsLeaving = true;
                needSettle = true;
            }

            // 2. Perbarui yang ada, sisipkan yang baru tepat setelah baris sebelumnya menurut urutan yang benar.
            TodoItemViewModel? previous = null;
            foreach (var item in source)
            {
                if (existing.TryGetValue(item.Id, out TodoItemViewModel? vm))
                {
                    if (vm.IsDone != item.IsDone)
                    {
                        vm.IsDone = item.IsDone; // memicu animasi centang atau coret di tampilan
                        needSettle = true;       // lalu baris pindah ke bagian yang sesuai setelah jeda
                    }
                }
                else
                {
                    vm = new TodoItemViewModel(item, _todoService) { IsNew = true };
                    int index = previous is null ? 0 : Items.IndexOf(previous) + 1;
                    Items.Insert(index, vm);
                    needSettle = true;
                }

                previous = vm;
            }

            if (needSettle)
            {
                ScheduleSettle();
            }
        }

        int total = _todoService.Items.Count;
        int done = _todoService.Items.Count(item => item.IsDone);

        IsEmpty = total == 0;
        HasCompleted = done > 0;
        SummaryText = total == 0 ? string.Empty : $"{done}/{total} done";
    }

    private void ScheduleSettle()
    {
        // Mode gerak berkurang: tidak ada animasi yang perlu ditunggu.
        if (MotionTokens.ReducedMotion)
        {
            Settle();
            return;
        }

        _settleTimer ??= CreateSettleTimer();
        _settleTimer.Stop();
        _settleTimer.Start(); // dimulai ulang bila ada perubahan lagi sebelum selesai
    }

    private DispatcherTimer CreateSettleTimer()
    {
        var timer = new DispatcherTimer { Interval = SettleDelay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Settle();
        };
        return timer;
    }

    /// <summary>Membuang baris yang sudah selesai keluar, mengurutkan ulang, dan menghapus tanda IsNew.</summary>
    private void Settle()
    {
        for (int i = Items.Count - 1; i >= 0; i--)
        {
            if (Items[i].IsLeaving)
            {
                Items.RemoveAt(i);
            }
        }

        // Urutkan ulang: yang belum selesai di atas, yang selesai di bawah.
        var byId = Items.ToDictionary(row => row.Id);
        int target = 0;
        foreach (var item in GetSortedSource())
        {
            if (!byId.TryGetValue(item.Id, out TodoItemViewModel? vm))
            {
                continue;
            }

            int current = Items.IndexOf(vm);
            if (current != target)
            {
                Items.Move(current, target);
            }

            target++;
        }

        foreach (var vm in Items)
        {
            vm.IsNew = false;
        }
    }

    public void Dispose()
    {
        _todoService.Changed -= OnTodosChanged;
        _settleTimer?.Stop();
    }
}
