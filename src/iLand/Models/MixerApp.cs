using System.Diagnostics;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace iLand.Models;

/// <summary>
/// Satu baris aplikasi di tab Mixer (satu proses atau satu kelompok proses, misalnya semua proses Chrome).
/// Objek ini hanya dibuat dan diubah di thread UI oleh MixerService.
/// Volume bersifat dua arah: slider di UI menulis ke property Volume (diteruskan ke Windows oleh layanan),
/// sedangkan perubahan dari luar masuk lewat SetVolumeFromSystem (tidak dianggap tulisan pengguna).
/// </summary>
public sealed class MixerApp : ObservableObject
{
    private readonly Action<string, double> _userVolumeChanged;

    private string _name = string.Empty;
    private ImageSource? _icon;
    private double _volume;
    private bool _isLeaving;

    public MixerApp(string key, Action<string, double> userVolumeChanged)
    {
        Key = key;
        _userVolumeChanged = userVolumeChanged;
    }

    /// <summary>Kunci kelompok: nama proses huruf kecil (misalnya "chrome").</summary>
    public string Key { get; }

    /// <summary>Saat terakhir pengguna menggeser slider baris ini (Stopwatch ticks). Dipakai layanan agar tidak menimpa geseran.</summary>
    public long LastUserChangeTicks { get; private set; }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    /// <summary>Ikon dari file exe aplikasi. Null = tampilkan ikon speaker kecil.</summary>
    public ImageSource? Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value);
    }

    /// <summary>Volume 0..1. Penulisan dari UI diteruskan ke sesi audio aplikasi.</summary>
    public double Volume
    {
        get => _volume;
        set
        {
            double clamped = Math.Clamp(value, 0, 1);
            if (Math.Abs(clamped - _volume) < 0.0005)
            {
                return;
            }

            _volume = clamped;
            OnPropertyChanged(nameof(Volume));
            OnPropertyChanged(nameof(PercentText));
            LastUserChangeTicks = Stopwatch.GetTimestamp();
            _userVolumeChanged(Key, clamped);
        }
    }

    public string PercentText => ((int)Math.Round(_volume * 100)).ToString();

    /// <summary>True saat baris sedang animasi keluar (tinggi dan fade). Setelah selesai baris dihapus dari daftar.</summary>
    public bool IsLeaving
    {
        get => _isLeaving;
        set => SetProperty(ref _isLeaving, value);
    }

    /// <summary>Volume yang dibaca dari sistem. Tidak memicu penulisan balik ke Windows.</summary>
    public void SetVolumeFromSystem(double volume)
    {
        double clamped = Math.Clamp(volume, 0, 1);
        if (Math.Abs(clamped - _volume) < 0.0005)
        {
            return;
        }

        _volume = clamped;
        OnPropertyChanged(nameof(Volume));
        OnPropertyChanged(nameof(PercentText));
    }
}
