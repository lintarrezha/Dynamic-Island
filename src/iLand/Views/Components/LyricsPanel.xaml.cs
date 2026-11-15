using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using iLand.Helpers;
using iLand.ViewModels;

namespace iLand.Views.Components;

/// <summary>
/// Panel lirik (lihat LyricsPanel.xaml). Tugas kode di sini hanya satu: menggulir daftar lirik tersinkron
/// ke baris aktif secara halus, sehingga baris yang sedang dinyanyikan berada di tengah.
///
/// Cara kerja gulir: ScrollViewer tidak punya properti offset yang bisa dianimasikan, jadi panel ini punya
/// properti sendiri (AnimatedOffset) yang meneruskan nilainya ke ScrollToVerticalOffset. Animasi (kurva
/// ResizeEase) berjalan pada properti itu dan dilepas dengan BeginAnimation(properti, null) setelah selesai.
/// Gulir dengan roda mouse dimatikan pada lirik tersinkron karena daftar mengikuti lagu.
/// </summary>
public partial class LyricsPanel : UserControl
{
    private static readonly DependencyProperty AnimatedOffsetProperty = DependencyProperty.Register(
        nameof(AnimatedOffset), typeof(double), typeof(LyricsPanel),
        new PropertyMetadata(0.0, OnAnimatedOffsetChanged));

    // Naik setiap kali ada gulir baru. Penyelesaian animasi lama yang sudah tertimpa diabaikan.
    private int _scrollVersion;

    private LyricsViewModel? _viewModel;

    public LyricsPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private double AnimatedOffset
    {
        get => (double)GetValue(AnimatedOffsetProperty);
        set => SetValue(AnimatedOffsetProperty, value);
    }

    private static void OnAnimatedOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((LyricsPanel)d).SyncedScroller.ScrollToVerticalOffset((double)e.NewValue);
    }

    // ---- Mengikuti ViewModel ----

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = e.NewValue as LyricsViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(LyricsViewModel.Lines):
                // Daftar baru: tunggu sampai baris-barisnya selesai dibuat, lalu langsung ke posisi (tanpa animasi).
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => ScrollToActive(animate: false)));
                break;

            case nameof(LyricsViewModel.ActiveIndex):
                ScrollToActive(animate: true);
                break;
        }
    }

    // ---- Gulir ----

    /// <summary>Menggulir agar baris aktif berada di tengah. Jika belum ada baris aktif, kembali ke atas.</summary>
    private void ScrollToActive(bool animate)
    {
        // Tidak terlihat (island tertutup, tab lain, atau bukan mode tersinkron): tidak ada yang perlu digulir.
        // Saat panel terlihat lagi, IsVisibleChanged dan SizeChanged memanggil metode ini.
        if (_viewModel is null || !SyncedScroller.IsVisible)
        {
            return;
        }

        double target;
        int index = _viewModel.ActiveIndex;

        if (index < 0)
        {
            target = 0;
        }
        else
        {
            if (LinesList.ItemContainerGenerator.ContainerFromIndex(index) is not FrameworkElement container
                || container.ActualHeight <= 0)
            {
                return; // barisnya belum diukur. SizeChanged akan memanggil lagi.
            }

            try
            {
                double top = container.TransformToAncestor(LinesList).Transform(new Point(0, 0)).Y;
                target = top + container.ActualHeight / 2 - SyncedScroller.ViewportHeight / 2;
            }
            catch (InvalidOperationException)
            {
                return; // belum terhubung ke pohon tampilan
            }
        }

        target = Math.Clamp(target, 0, Math.Max(0, SyncedScroller.ScrollableHeight));

        int version = ++_scrollVersion;

        if (!animate || MotionTokens.ReducedMotion || Math.Abs(SyncedScroller.VerticalOffset - target) < 0.5)
        {
            BeginAnimation(AnimatedOffsetProperty, null);
            AnimatedOffset = target;
            return;
        }

        var animation = new DoubleAnimation(SyncedScroller.VerticalOffset, target, _viewModel.GetScrollDuration())
        {
            EasingFunction = MotionTokens.ResizeEase
        };

        animation.Completed += (_, _) =>
        {
            if (version != _scrollVersion)
            {
                return; // sudah ada gulir yang lebih baru
            }

            // Nilai dasar disamakan dulu, baru animasi dilepas, supaya tidak ada lompatan.
            AnimatedOffset = target;
            BeginAnimation(AnimatedOffsetProperty, null);
        };

        BeginAnimation(AnimatedOffsetProperty, animation);
    }

    private void SyncedScroller_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Lebar island berubah (panel baru terbuka) mengubah pemenggalan baris, jadi posisi dihitung ulang.
        ScrollToActive(animate: false);
    }

    private void SyncedScroller_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (SyncedScroller.IsVisible)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => ScrollToActive(animate: false)));
        }
    }

    private void SyncedScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Daftar mengikuti lagu: gulir manual dimatikan supaya tidak berebut dengan gulir otomatis.
        e.Handled = true;
    }
}
