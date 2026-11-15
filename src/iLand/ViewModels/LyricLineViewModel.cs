using CommunityToolkit.Mvvm.ComponentModel;

namespace iLand.ViewModels;

/// <summary>
/// Satu baris lirik di panel lirik. Opacity diatur LyricsViewModel: baris aktif 1,0, baris di sebelahnya 0,6,
/// dua baris dari baris aktif 0,35, sisanya 0,2 (supaya baris di tepi panel nyaris tak terlihat dan tidak terpotong tajam).
/// </summary>
public sealed partial class LyricLineViewModel : ObservableObject
{
    // Baris kosong di file LRC berarti jeda instrumental: ditampilkan sebagai not musik.
    private const string BreakText = "♪";

    public LyricLineViewModel(TimeSpan time, string text)
    {
        Time = time;
        Text = string.IsNullOrWhiteSpace(text) ? BreakText : text;
    }

    public TimeSpan Time { get; }

    public string Text { get; }

    [ObservableProperty]
    private double _opacity = 0.2;
}
