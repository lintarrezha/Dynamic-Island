using System.Windows.Controls;

namespace iLand.Views.Components;

/// <summary>
/// Tampilan tab Timer. Tidak punya DataContext sendiri: memakai IslandViewModel yang diwarisi dari TabHost
/// (properti Timer* dan perintah ToggleTimerCommand, ResetTimerCommand, AdjustCountdownMinutesCommand, SetCountdownMinutesCommand).
/// </summary>
public partial class TimerView : UserControl
{
    public TimerView()
    {
        InitializeComponent();
    }
}
