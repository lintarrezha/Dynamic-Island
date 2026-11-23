using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iLand.Interfaces;
using iLand.Models;

namespace iLand.ViewModels;

/// <summary>
/// Halaman Music &amp; Lyrics. Sumber kebenaran tunggal pengaturan lirik (LyricsEnabled dan LyricsOffsetMs),
/// ditambah scroll volume.
/// </summary>
public sealed partial class MusicSettingsViewModel : SettingsPageViewModel
{
    // Rentang geser waktu lirik (detik). Disimpan di AppSettings.LyricsOffsetMs dalam milidetik, kelipatan 100.
    public const double OffsetLimitSeconds = 2.0;

    /// <summary>Gaya tab Music: 0 = Classic, 1 = Glass (indeks SegmentedControl).</summary>
    [ObservableProperty] private int _musicStyleIndex;

    [ObservableProperty] private bool _lyricsEnabled;

    /// <summary>Geser waktu lirik dalam detik. Negatif = lirik tampil lebih lambat (Later), positif = lebih cepat (Earlier).</summary>
    [ObservableProperty] private double _lyricsOffsetSeconds;

    [ObservableProperty] private bool _scrollVolumeEnabled;

    public MusicSettingsViewModel(ISettingsService settingsService) : base(settingsService)
    {
        Load(() =>
        {
            MusicStyleIndex = Current.MusicStyle == MusicTabStyle.Glass ? 1 : 0;
            LyricsEnabled = Current.LyricsEnabled;
            LyricsOffsetSeconds = Current.LyricsOffsetMs / 1000.0;
            ScrollVolumeEnabled = Current.ScrollVolumeEnabled;
        });
    }

    partial void OnMusicStyleIndexChanged(int value) =>
        Update(s => s.MusicStyle = value == 1 ? MusicTabStyle.Glass : MusicTabStyle.Classic);

    partial void OnLyricsEnabledChanged(bool value) => Update(s => s.LyricsEnabled = value);

    partial void OnLyricsOffsetSecondsChanged(double value)
    {
        // Kelipatan 100 ms dan dijepit ke rentang. AppSettings.Validate() juga menjepit saat file dibaca.
        int ms = (int)Math.Round(Math.Clamp(value, -OffsetLimitSeconds, OffsetLimitSeconds) * 10.0) * 100;
        Update(s => s.LyricsOffsetMs = ms);
    }

    partial void OnScrollVolumeEnabledChanged(bool value) => Update(s => s.ScrollVolumeEnabled = value);

    [RelayCommand]
    private void ResetLyricsOffset() => LyricsOffsetSeconds = 0;
}
