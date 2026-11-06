using System.Windows;
using iLand.ViewModels;

namespace iLand.Views;

public partial class IslandWindow : Window
{
    // ViewModel dikirim oleh DI container lewat constructor ini.
    public IslandWindow(IslandViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel; // menghubungkan View dengan ViewModel (binding)
    }
}
