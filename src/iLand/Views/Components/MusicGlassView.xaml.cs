using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using iLand.ViewModels;

namespace iLand.Views.Components;

/// <summary>
/// Tampilan tab Music gaya Glass (lihat MusicGlassView.xaml). Satu-satunya tugas kode di sini: menutup menu timing lirik
/// (tombol titik tiga) saat pengguna menekan di luar menu dan di luar tombolnya.
/// </summary>
public partial class MusicGlassView : UserControl
{
    public MusicGlassView()
    {
        InitializeComponent();

        // handledEventsToo: klik pada tombol (yang menandai event sebagai handled) tetap diperiksa.
        AddHandler(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnPreviewMouseLeftButtonDown), true);
    }

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not IslandViewModel viewModel || !viewModel.Lyrics.IsTimingMenuOpen)
        {
            return;
        }

        if (e.OriginalSource is DependencyObject source
            && (IsInside(source, TimingMenu) || IsInside(source, TimingMenuButton)))
        {
            return;
        }

        viewModel.Lyrics.CloseTimingMenu();
    }

    private static bool IsInside(DependencyObject? element, DependencyObject container)
    {
        while (element is not null)
        {
            if (ReferenceEquals(element, container))
            {
                return true;
            }

            element = element is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }

        return false;
    }
}
