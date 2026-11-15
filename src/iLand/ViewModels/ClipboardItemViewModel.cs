using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iLand.Interfaces;
using iLand.Models;

namespace iLand.ViewModels;

/// <summary>Satu baris di daftar Clipboard: teks satu baris, waktu salin, dan perintah salin ulang atau hapus.</summary>
public partial class ClipboardItemViewModel : ObservableObject
{
    private const int PreviewMaxLength = 200;
    private const int TooltipMaxLength = 400;

    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);

    private readonly ClipboardEntry _entry;
    private readonly IClipboardService _clipboardService;

    public Guid Id => _entry.Id;

    /// <summary>Teks dijadikan satu baris (baris baru dan spasi berulang diringkas) agar rapi di daftar.</summary>
    public string Preview { get; }

    /// <summary>Teks lengkap (dipotong jika sangat panjang) untuk tooltip.</summary>
    public string TooltipText { get; }

    /// <summary>Misalnya "Just now", "5 min ago", atau "Copied" sesaat setelah diklik.</summary>
    [ObservableProperty]
    private string _timeText = string.Empty;

    /// <summary>True sesaat setelah kartu diklik dan teksnya disalin ulang. Tampilan memakainya untuk kedipan aksen.</summary>
    [ObservableProperty]
    private bool _isCopied;

    public ClipboardItemViewModel(ClipboardEntry entry, IClipboardService clipboardService)
    {
        _entry = entry;
        _clipboardService = clipboardService;

        string singleLine = WhitespaceRun.Replace(entry.Text.Trim(), " ");
        Preview = singleLine.Length > PreviewMaxLength ? singleLine[..PreviewMaxLength] : singleLine;
        TooltipText = entry.Text.Length > TooltipMaxLength ? entry.Text[..TooltipMaxLength] + "..." : entry.Text;

        RefreshTime();
    }

    /// <summary>Menghitung ulang teks waktu. Dipanggil saat Shelf dibuka, tanpa timer.</summary>
    public void RefreshTime()
    {
        TimeSpan age = DateTime.Now - _entry.CopiedAt;

        TimeText = age.TotalMinutes < 1 ? "Just now"
            : age.TotalHours < 1 ? $"{(int)age.TotalMinutes} min ago"
            : age.TotalDays < 1 ? $"{(int)age.TotalHours} h ago"
            : $"{(int)age.TotalDays} d ago";
    }

    // CopyAsync -> CopyCommand, Remove -> RemoveCommand (dibuat otomatis oleh [RelayCommand]).
    [RelayCommand]
    private async Task CopyAsync()
    {
        if (!_clipboardService.CopyBack(Id))
        {
            return;
        }

        // Umpan balik singkat: kartu berkedip aksen dan teks waktu berubah menjadi "Copied" sebentar.
        IsCopied = true;
        TimeText = "Copied";
        await Task.Delay(1200);
        IsCopied = false;
        RefreshTime();
    }

    [RelayCommand]
    private void Remove()
    {
        _clipboardService.Remove(Id);
    }
}
