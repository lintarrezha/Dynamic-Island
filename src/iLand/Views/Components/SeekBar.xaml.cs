using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace iLand.Views.Components;

/// <summary>
/// Bar progres yang bisa diklik dan digeser (lihat SeekBar.xaml).
/// Property: Value (0.0 sampai 1.0), IsSeekEnabled, dan SeekCommand (menerima pecahan 0.0 sampai 1.0).
/// Selama bar digeser, nilai dari luar diabaikan supaya tidak berebut dengan jari/mouse pengguna.
/// </summary>
public partial class SeekBar : UserControl
{
    private const double ThumbSize = 12;

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(SeekBar), new PropertyMetadata(0.0, OnStateChanged));

    public static readonly DependencyProperty IsSeekEnabledProperty = DependencyProperty.Register(
        nameof(IsSeekEnabled), typeof(bool), typeof(SeekBar), new PropertyMetadata(false, OnStateChanged));

    public static readonly DependencyProperty SeekCommandProperty = DependencyProperty.Register(
        nameof(SeekCommand), typeof(ICommand), typeof(SeekBar), new PropertyMetadata(null));

    public static readonly DependencyProperty FillBrushProperty = DependencyProperty.Register(
        nameof(FillBrush), typeof(System.Windows.Media.Brush), typeof(SeekBar), new PropertyMetadata(null));

    private bool _isDragging;
    private double _dragValue;

    public SeekBar()
    {
        InitializeComponent();

        // Warna isi bawaan = putih. Pemakai bisa menggantinya (misalnya aksen dari sampul album).
        SetCurrentValue(FillBrushProperty, (System.Windows.Media.Brush)FindResource("TextPrimaryBrush"));
    }

    /// <summary>Brush untuk bagian bar yang sudah terisi.</summary>
    public System.Windows.Media.Brush? FillBrush
    {
        get => (System.Windows.Media.Brush?)GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    /// <summary>Posisi bar, 0.0 sampai 1.0.</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>True jika pemutar mengizinkan loncat posisi.</summary>
    public bool IsSeekEnabled
    {
        get => (bool)GetValue(IsSeekEnabledProperty);
        set => SetValue(IsSeekEnabledProperty, value);
    }

    /// <summary>Dijalankan saat pengguna melepas bar. Parameternya pecahan 0.0 sampai 1.0 (double).</summary>
    public ICommand? SeekCommand
    {
        get => (ICommand?)GetValue(SeekCommandProperty);
        set => SetValue(SeekCommandProperty, value);
    }

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((SeekBar)d).UpdateVisual();
    }

    private void Root_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateVisual();

    private void Root_MouseChanged(object sender, MouseEventArgs e) => UpdateVisual();

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsSeekEnabled)
        {
            return;
        }

        _isDragging = true;
        _dragValue = FractionAt(e);
        Root.CaptureMouse();
        UpdateVisual();
        e.Handled = true; // klik ini milik bar, bukan klik pada island
    }

    private void Root_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        _dragValue = FractionAt(e);
        UpdateVisual();
    }

    private void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        double fraction = FractionAt(e);
        _isDragging = false;
        _dragValue = fraction;
        Root.ReleaseMouseCapture();
        UpdateVisual();
        e.Handled = true;

        ICommand? command = SeekCommand;
        if (command is not null && command.CanExecute(fraction))
        {
            command.Execute(fraction);
        }
    }

    private void Root_LostMouseCapture(object sender, MouseEventArgs e)
    {
        // Capture hilang tanpa tombol dilepas di sini (misalnya jendela kehilangan fokus): batalkan geseran.
        if (_isDragging)
        {
            _isDragging = false;
            UpdateVisual();
        }
    }

    private double FractionAt(MouseEventArgs e)
    {
        double width = Root.ActualWidth;
        if (width <= 0)
        {
            return 0;
        }

        return Math.Clamp(e.GetPosition(Root).X / width, 0, 1);
    }

    private void UpdateVisual()
    {
        double width = Root.ActualWidth;
        double value = _isDragging ? _dragValue : Value;
        if (double.IsNaN(value))
        {
            value = 0;
        }

        value = Math.Clamp(value, 0, 1);

        FillBar.Width = width * value;
        ThumbShift.X = Math.Clamp(width * value - ThumbSize / 2, 0, Math.Max(0, width - ThumbSize));

        // Thumb memakai Opacity (bukan warna) supaya konsisten dengan aturan hover di seluruh island.
        ThumbDot.Opacity = IsSeekEnabled && (_isDragging || Root.IsMouseOver) ? 1 : 0;

        if (IsSeekEnabled)
        {
            Root.Cursor = Cursors.Hand;
        }
        else
        {
            Root.ClearValue(CursorProperty);
        }
    }
}
