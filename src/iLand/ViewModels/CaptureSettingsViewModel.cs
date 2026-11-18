using CommunityToolkit.Mvvm.ComponentModel;
using iLand.Interfaces;

namespace iLand.ViewModels;

/// <summary>Halaman Capture: suara rekam layar dan penyembunyian island dari screenshot dan rekaman.</summary>
public sealed partial class CaptureSettingsViewModel : SettingsPageViewModel
{
    [ObservableProperty] private bool _recordSystemAudio;
    [ObservableProperty] private bool _recordMicrophone;
    [ObservableProperty] private bool _hideFromScreenshots;

    public CaptureSettingsViewModel(ISettingsService settingsService) : base(settingsService)
    {
        Load(() =>
        {
            RecordSystemAudio = Current.RecordSystemAudio;
            RecordMicrophone = Current.RecordMicrophone;
            HideFromScreenshots = Current.HideFromScreenshots;
        });
    }

    partial void OnRecordSystemAudioChanged(bool value) => Update(s => s.RecordSystemAudio = value);
    partial void OnRecordMicrophoneChanged(bool value) => Update(s => s.RecordMicrophone = value);
    partial void OnHideFromScreenshotsChanged(bool value) => Update(s => s.HideFromScreenshots = value);
}
