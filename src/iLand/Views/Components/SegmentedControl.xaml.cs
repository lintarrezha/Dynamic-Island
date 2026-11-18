using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using iLand.Helpers;

namespace iLand.Views.Components;

/// <summary>
/// Kontrol pilihan segmen dengan indikator yang meluncur.
/// Pakai: &lt;components:SegmentedControl Items="Timer|Pomodoro|Stopwatch" SelectedIndex="{Binding ..., Mode=TwoWay}" /&gt;
/// Saat IsEnabled = false, kontrol memudar dan tidak bisa diklik (dikunci).
/// Durasi dan kurva dari MotionTokens (IndicatorMs). Kontrol ini tidak mengenal pengaturan Animation duration,
/// jadi durasinya tetap seperti di tabel dasar (tidak ikut pengali).
/// </summary>
public partial class SegmentedControl : UserControl
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items), typeof(string), typeof(SegmentedControl),
        new PropertyMetadata(string.Empty, (d, _) => ((SegmentedControl)d).Rebuild()));

    public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(
        nameof(SelectedIndex), typeof(int), typeof(SegmentedControl),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, _) => ((SegmentedControl)d).OnSelectedIndexChanged()));

    public static readonly DependencyProperty AllowKeyboardProperty = DependencyProperty.Register(
        nameof(AllowKeyboard), typeof(bool), typeof(SegmentedControl),
        new PropertyMetadata(false, (d, e) => ((SegmentedControl)d).OnAllowKeyboardChanged((bool)e.NewValue)));

    // Satu segmen: teks biasa (60%) dan teks aktif (hitam di atas indikator putih), saling crossfade.
    private sealed record Segment(TextBlock Normal, TextBlock Active);

    private readonly List<Segment> _segments = new();

    public SegmentedControl()
    {
        InitializeComponent();
        IsEnabledChanged += (_, _) => Opacity = IsEnabled ? 1.0 : 0.45;
    }

    /// <summary>Nama segmen dipisah tanda |, contoh "Timer|Pomodoro|Stopwatch".</summary>
    public string Items
    {
        get => (string)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    /// <summary>
    /// True: kontrol bisa difokus dengan Tab dan panah kiri/kanan mengganti segmen (untuk jendela Settings).
    /// Bawaan False: di island tidak ada fokus keyboard.
    /// </summary>
    public bool AllowKeyboard
    {
        get => (bool)GetValue(AllowKeyboardProperty);
        set => SetValue(AllowKeyboardProperty, value);
    }

    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    private void OnAllowKeyboardChanged(bool allow)
    {
        Focusable = allow;
        IsTabStop = allow;
        FocusVisualStyle = null; // cincin fokus digambar sendiri (FocusRing)

        if (!allow)
        {
            FocusRing.Visibility = Visibility.Collapsed;
        }
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);

        if (AllowKeyboard)
        {
            FocusRing.Visibility = Visibility.Visible;
        }
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        FocusRing.Visibility = Visibility.Collapsed;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (!AllowKeyboard || !IsEnabled || _segments.Count == 0)
        {
            return;
        }

        int step = e.Key is Key.Left or Key.Up ? -1 : e.Key is Key.Right or Key.Down ? 1 : 0;
        if (step == 0)
        {
            return;
        }

        int next = Math.Clamp(SelectedIndex + step, 0, _segments.Count - 1);
        if (next != SelectedIndex)
        {
            SelectedIndex = next;
        }

        e.Handled = true;
    }

    private double SegmentWidth => _segments.Count == 0 ? 0 : SegmentsGrid.ActualWidth / _segments.Count;

    private void Rebuild()
    {
        SegmentsGrid.Children.Clear();
        SegmentsGrid.ColumnDefinitions.Clear();
        _segments.Clear();

        string[] names = (Items ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        for (int i = 0; i < names.Length; i++)
        {
            int index = i;
            SegmentsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock normal = CreateLabel(names[i], (Brush)FindResource("TextPrimaryBrush"));
            TextBlock active = CreateLabel(names[i], (Brush)FindResource("IslandBrush"));
            bool selected = index == SelectedIndex;
            normal.Opacity = selected ? 0 : 0.6;
            active.Opacity = selected ? 1 : 0;

            var content = new Grid();
            content.Children.Add(normal);
            content.Children.Add(active);

            var button = new Button
            {
                Style = (Style)Resources["SegmentButtonStyle"],
                Content = content
            };
            AutomationProperties.SetName(button, names[i]); // pembaca layar membaca nama segmen
            button.Click += (_, _) => SelectedIndex = index;
            button.MouseEnter += (_, _) =>
            {
                if (index != SelectedIndex)
                {
                    normal.Opacity = 0.85;
                }
            };
            button.MouseLeave += (_, _) =>
            {
                if (index != SelectedIndex)
                {
                    normal.Opacity = 0.6;
                }
            };

            Grid.SetColumn(button, i);
            SegmentsGrid.Children.Add(button);
            _segments.Add(new Segment(normal, active));
        }

        UpdateIndicatorSize();
        MoveIndicator(animate: false);
    }

    private static TextBlock CreateLabel(string text, Brush brush)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = brush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private void SegmentsGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateIndicatorSize();
        MoveIndicator(animate: false);
    }

    private void UpdateIndicatorSize()
    {
        Indicator.Width = SegmentWidth;
        Indicator.Visibility = _segments.Count > 0 && SelectedIndex >= 0 && SelectedIndex < _segments.Count
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OnSelectedIndexChanged()
    {
        for (int i = 0; i < _segments.Count; i++)
        {
            bool selected = i == SelectedIndex;
            Fade(_segments[i].Normal, selected ? 0 : 0.6);
            Fade(_segments[i].Active, selected ? 1 : 0);
        }

        UpdateIndicatorSize();
        MoveIndicator(animate: true);
    }

    /// <summary>Menggeser indikator ke segmen terpilih. Berpindah satu segmen memakai kurva dengan pantulan kecil, lebih jauh tanpa pantulan.</summary>
    private void MoveIndicator(bool animate)
    {
        var shift = (TranslateTransform)Indicator.RenderTransform;
        double width = SegmentWidth;
        double targetX = Math.Max(SelectedIndex, 0) * width;

        if (!animate || MotionTokens.ReducedMotion || width <= 0)
        {
            shift.BeginAnimation(TranslateTransform.XProperty, null); // lepas animasi dulu, baru atur nilai
            shift.X = targetX;
            return;
        }

        double travelInSegments = Math.Abs(targetX - shift.X) / width;
        IEasingFunction ease = travelInSegments <= MotionTokens.SegmentOvershootMaxSegments
            ? MotionTokens.IndicatorEase
            : MotionTokens.ResizeEase;

        shift.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(targetX, TimeSpan.FromMilliseconds(MotionTokens.IndicatorMs)) { EasingFunction = ease });
    }

    private static void Fade(TextBlock label, double to)
    {
        double from = label.Opacity;
        label.BeginAnimation(UIElement.OpacityProperty, null); // lepas animasi lama
        label.Opacity = to; // nilai akhir diatur dulu, animasi hanya efek perjalanan

        if (MotionTokens.ReducedMotion || Math.Abs(from - to) < 0.01)
        {
            return;
        }

        label.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(from, to, MotionTokens.IconCrossfadeDuration) { FillBehavior = FillBehavior.Stop });
    }
}
