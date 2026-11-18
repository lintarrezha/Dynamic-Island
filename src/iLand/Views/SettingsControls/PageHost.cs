using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using iLand.Helpers;

namespace iLand.Views.SettingsControls;

/// <summary>
/// Tempat halaman Settings tampil. Saat Page berganti, halaman lama memudar keluar sambil halaman baru memudar masuk
/// selama MotionTokens.SettingsPageFadeMs (tanpa geser). Tampilan halaman dipilih lewat DataTemplate di jendela
/// (tipe ViewModel halaman menentukan tampilannya).
///
/// Dua lapis ContentPresenter bergantian. Hanya halaman yang tampil (dan halaman lama selama 150 ms) yang ada di memori:
/// tampilan halaman dibuat saat dibuka dan dilepas setelah ditinggalkan. Tanpa gerak jika gerak dikurangi.
/// Aturan animasi: nilai akhir dipasang sebagai nilai lokal, animasi memakai FillBehavior.Stop dengan nilai awal eksplisit.
/// </summary>
public sealed class PageHost : Grid
{
    public static readonly DependencyProperty PageProperty = DependencyProperty.Register(
        nameof(Page), typeof(object), typeof(PageHost),
        new PropertyMetadata(null, (d, e) => ((PageHost)d).OnPageChanged(e.NewValue)));

    private readonly ContentPresenter[] _layers = { new(), new() };
    private int _front;
    private int _version;

    public PageHost()
    {
        foreach (ContentPresenter layer in _layers)
        {
            Children.Add(layer);
        }
    }

    /// <summary>ViewModel halaman yang sedang tampil.</summary>
    public object? Page
    {
        get => GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    private void OnPageChanged(object? page)
    {
        int version = ++_version;

        ContentPresenter oldLayer = _layers[_front];
        _front = 1 - _front;
        ContentPresenter newLayer = _layers[_front];

        // Hentikan animasi yang masih berjalan (perpindahan sebelumnya yang belum selesai).
        oldLayer.BeginAnimation(OpacityProperty, null);
        newLayer.BeginAnimation(OpacityProperty, null);

        newLayer.Content = page;
        newLayer.Opacity = 1;
        newLayer.IsHitTestVisible = true;
        oldLayer.IsHitTestVisible = false;

        bool animate = IsLoaded && IsVisible && !MotionTokens.ReducedMotion
                       && oldLayer.Content is not null && page is not null;
        if (!animate)
        {
            oldLayer.Content = null;
            return;
        }

        TimeSpan duration = MotionTokens.SettingsPageFadeDuration.TimeSpan;

        oldLayer.Opacity = 0; // nilai akhirnya; animasi hanya efek perjalanan 1 ke 0
        oldLayer.BeginAnimation(OpacityProperty, MakeFade(1, 0, duration, () =>
        {
            if (version == _version)
            {
                oldLayer.Content = null; // lepas halaman lama setelah selesai memudar
            }
        }));
        newLayer.BeginAnimation(OpacityProperty, MakeFade(0, 1, duration, null));
    }

    private static DoubleAnimation MakeFade(double from, double to, TimeSpan duration, Action? completed)
    {
        var animation = new DoubleAnimation(from, to, duration)
        {
            EasingFunction = MotionTokens.ContentInEase,
            FillBehavior = FillBehavior.Stop
        };

        if (completed is not null)
        {
            animation.Completed += (_, _) => completed();
        }

        return animation;
    }
}
