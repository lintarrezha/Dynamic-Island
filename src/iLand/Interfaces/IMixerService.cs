using System.Collections.ObjectModel;
using System.ComponentModel;
using iLand.Models;

namespace iLand.Interfaces;

/// <summary>
/// Volume master dan volume per aplikasi (seperti Volume Mixer Windows) untuk tab Mixer.
/// Berbeda dengan IAppVolumeService yang hanya mengurus satu aplikasi pemutar di tab Music.
/// Semua anggota dibaca dan dipanggil dari thread UI. Seluruh pekerjaan COM berjalan di thread latar.
/// </summary>
public interface IMixerService : INotifyPropertyChanged
{
    /// <summary>Aplikasi yang sedang bersuara (atau baru berhenti kurang dari 8 detik lalu). Hanya diubah di thread UI.</summary>
    ObservableCollection<MixerApp> Apps { get; }

    /// <summary>False sampai pemindaian pertama selesai setelah Attach. Dipakai agar "tidak ada aplikasi" tidak berkedip.</summary>
    bool IsReady { get; }

    /// <summary>False jika tidak ada perangkat output default.</summary>
    bool IsMasterAvailable { get; }

    /// <summary>Volume master 0..1. Penulisan diteruskan ke perangkat output default (digabung, sekitar 30 ms).</summary>
    double MasterVolume { get; set; }

    bool IsMasterMuted { get; }

    void ToggleMasterMute();

    /// <summary>Mulai membaca sesi audio dan mendengarkan perubahan. Dipanggil saat tab Mixer terlihat. Aman dipanggil berulang.</summary>
    void Attach();

    /// <summary>Hentikan semua pendengar dan lepas objek COM. Dipanggil saat tab Mixer tidak terlihat.</summary>
    void Detach();
}
