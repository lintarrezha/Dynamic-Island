using CommunityToolkit.Mvvm.ComponentModel;
using iLand.Interfaces;

namespace iLand.ViewModels;

/// <summary>Halaman Music &amp; Lyrics. Sumber kebenaran tunggal pengaturan lirik (LyricsEnabled); Prompt 7 menambah isi halaman ini.</summary>
public sealed partial class MusicSettingsViewModel : SettingsPageViewModel
{
    [ObservableProperty] private bool _lyricsEnabled;

    public MusicSettingsViewModel(ISettingsService settingsService) : base(settingsService)
    {
        Load(() => LyricsEnabled = Current.LyricsEnabled);
    }

    partial void OnLyricsEnabledChanged(bool value) => Update(s => s.LyricsEnabled = value);
}
