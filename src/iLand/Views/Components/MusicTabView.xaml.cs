using System.Windows.Controls;

namespace iLand.Views.Components;

/// <summary>Isi tab Music (lihat MusicTabView.xaml). Tanpa logika sendiri: semuanya lewat binding ke IslandViewModel.</summary>
public partial class MusicTabView : UserControl
{
    public MusicTabView()
    {
        InitializeComponent();
    }
}
