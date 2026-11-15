using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iLand.Interfaces;
using iLand.Models;

namespace iLand.ViewModels;

/// <summary>
/// ViewModel tab Mixer: meneruskan data dari IMixerService ke tampilan.
/// IslandViewModel memanggil SetActive(true) saat tab Mixer terlihat dan SetActive(false) saat tidak.
/// </summary>
public sealed partial class MixerViewModel : ObservableObject
{
    private readonly IMixerService _mixer;

    public MixerViewModel(IMixerService mixer)
    {
        _mixer = mixer;
        _mixer.PropertyChanged += OnMixerPropertyChanged;
        _mixer.Apps.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowEmpty));
    }

    public ObservableCollection<MixerApp> Apps => _mixer.Apps;

    /// <summary>True jika pemindaian selesai dan tidak ada aplikasi yang bersuara.</summary>
    public bool ShowEmpty => _mixer.IsReady && _mixer.Apps.Count == 0;

    public bool IsMasterAvailable => _mixer.IsMasterAvailable;

    public double MasterVolume
    {
        get => _mixer.MasterVolume;
        set => _mixer.MasterVolume = value;
    }

    public string MasterPercentText => ((int)Math.Round(_mixer.MasterVolume * 100)).ToString();

    /// <summary>True jika master dibisukan atau volumenya nol (ikon speaker berganti ke bisu).</summary>
    public bool IsMasterSilent => _mixer.IsMasterMuted || _mixer.MasterVolume <= 0.001;

    [RelayCommand]
    private void ToggleMasterMute()
    {
        _mixer.ToggleMasterMute();
    }

    /// <summary>Dipanggil IslandViewModel: true saat tab Mixer benar-benar terlihat.</summary>
    public void SetActive(bool active)
    {
        if (active)
        {
            _mixer.Attach();
        }
        else
        {
            _mixer.Detach();
        }
    }

    private void OnMixerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(IMixerService.MasterVolume):
                OnPropertyChanged(nameof(MasterVolume));
                OnPropertyChanged(nameof(MasterPercentText));
                OnPropertyChanged(nameof(IsMasterSilent));
                break;

            case nameof(IMixerService.IsMasterMuted):
                OnPropertyChanged(nameof(IsMasterSilent));
                break;

            case nameof(IMixerService.IsMasterAvailable):
                OnPropertyChanged(nameof(IsMasterAvailable));
                break;

            case nameof(IMixerService.IsReady):
                OnPropertyChanged(nameof(ShowEmpty));
                break;
        }
    }
}
