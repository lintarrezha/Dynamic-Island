using System.Windows;
using iLand.Helpers;
using iLand.ViewModels;

namespace iLand.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Handle window baru ada setelah SourceInitialized, jadi title bar gelap dipasang di sana.
        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        SourceInitialized -= OnSourceInitialized;
        WindowStyleHelper.ApplyDarkTitleBar(this);
    }
}
