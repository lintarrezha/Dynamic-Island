using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using iLand.Helpers;
using iLand.ViewModels;

namespace iLand.Views;

public partial class SettingsWindow : Window
{
    // Jarak antar item menu samping: tinggi item 36 + jarak 2. Harus sama dengan SettingsNavItemStyle di SettingsTheme.xaml.
    private const double NavItemPitch = 38;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Handle window baru ada setelah SourceInitialized, jadi title bar gelap dipasang di sana.
        SourceInitialized += OnSourceInitialized;

        // Posisi awal indikator dipasang langsung (tanpa gerak) setelah item menu selesai dibuat.
        Loaded += (_, _) => MoveNavIndicator(animate: false);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        SourceInitialized -= OnSourceInitialized;
        WindowStyleHelper.ApplyDarkTitleBar(this);
    }

    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        MoveNavIndicator(animate: IsLoaded);
    }

    /// <summary>Menggeser pil putih ke item terpilih. Tanpa gerak jika gerak dikurangi atau jendela belum tampil.</summary>
    private void MoveNavIndicator(bool animate)
    {
        int index = Nav.SelectedIndex;
        if (index < 0)
        {
            NavIndicator.Visibility = Visibility.Collapsed;
            return;
        }

        NavIndicator.Visibility = Visibility.Visible;
        double target = index * NavItemPitch;

        if (!animate || MotionTokens.ReducedMotion)
        {
            NavIndicatorShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null); // lepas animasi dulu, baru atur nilai
            NavIndicatorShift.Y = target;
            return;
        }

        NavIndicatorShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(target, TimeSpan.FromMilliseconds(MotionTokens.IndicatorMs))
            {
                EasingFunction = MotionTokens.ResizeEase
            });
    }
}
