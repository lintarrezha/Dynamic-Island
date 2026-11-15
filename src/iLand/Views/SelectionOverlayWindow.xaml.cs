using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using iLand.Models;
using iLand.Native;

namespace iLand.Views;

/// <summary>
/// Overlay layar penuh untuk memilih area atau jendela. Layar sudah dibekukan sebagai gambar,
/// jadi yang dipilih pengguna persis yang ada di gambar itu.
/// Hasilnya ada di SelectedRegion (dalam piksel pada gambar beku), atau null jika dibatalkan.
/// </summary>
public partial class SelectionOverlayWindow : Window
{
    private readonly BitmapSource _frozenImage;
    private readonly Rect _monitorBounds; // piksel layar
    private readonly CaptureSelectionMode _mode;
    private readonly Func<int, int, Rect?> _windowFinder;

    private Point _dragStart;
    private bool _isDragging;
    private Rect _hoverWindowRegion = Rect.Empty; // piksel pada gambar beku (mode jendela)

    /// <summary>Wilayah terpilih dalam piksel pada gambar beku. Null = dibatalkan.</summary>
    public Int32Rect? SelectedRegion { get; private set; }

    public SelectionOverlayWindow(
        BitmapSource frozenImage,
        Rect monitorBounds,
        CaptureSelectionMode mode,
        Func<int, int, Rect?> windowFinder)
    {
        InitializeComponent();

        _frozenImage = frozenImage;
        _monitorBounds = monitorBounds;
        _mode = mode;
        _windowFinder = windowFinder;

        FrozenImage.Source = frozenImage;
        HintText.Text = mode == CaptureSelectionMode.Area
            ? "Drag to select an area  \u00B7  Esc to cancel"
            : "Click a window to capture it  \u00B7  Esc to cancel";
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Pasang tepat menutupi monitor (dalam piksel fisik), di atas semua window lain.
        IntPtr handle = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(
            handle, NativeMethods.HWND_TOPMOST,
            (int)_monitorBounds.X, (int)_monitorBounds.Y,
            (int)_monitorBounds.Width, (int)_monitorBounds.Height,
            0);
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        Activate();
        Focus();
        UpdateVisual(Rect.Empty);
    }

    // Perbandingan piksel gambar dengan satuan WPF (DIP). Dihitung langsung dari ukuran
    // window sehingga benar di skala DPI berapa pun.
    private double ScaleX => _frozenImage.PixelWidth / ActualWidth;

    private double ScaleY => _frozenImage.PixelHeight / ActualHeight;

    // ---- Keyboard dan mouse ----

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Cancel();
        }
    }

    private void Root_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        Cancel();
    }

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_mode != CaptureSelectionMode.Area)
        {
            return;
        }

        _isDragging = true;
        _dragStart = e.GetPosition(SelectionCanvas);
        SelectionCanvas.CaptureMouse();
        UpdateVisual(Rect.Empty);
    }

    private void Root_MouseMove(object sender, MouseEventArgs e)
    {
        Point position = e.GetPosition(SelectionCanvas);

        if (_mode == CaptureSelectionMode.Area)
        {
            if (_isDragging)
            {
                UpdateVisual(GetDragRect(position));
            }

            return;
        }

        // Mode jendela: sorot jendela di bawah kursor.
        int screenX = (int)(_monitorBounds.X + position.X * ScaleX);
        int screenY = (int)(_monitorBounds.Y + position.Y * ScaleY);

        Rect? windowBounds = _windowFinder(screenX, screenY);
        if (windowBounds is Rect bounds)
        {
            // Ubah ke piksel pada gambar beku, dan potong agar tidak keluar dari monitor ini.
            var local = new Rect(bounds.X - _monitorBounds.X, bounds.Y - _monitorBounds.Y, bounds.Width, bounds.Height);
            local.Intersect(new Rect(0, 0, _frozenImage.PixelWidth, _frozenImage.PixelHeight));
            _hoverWindowRegion = local;

            UpdateVisual(local.IsEmpty
                ? Rect.Empty
                : new Rect(local.X / ScaleX, local.Y / ScaleY, local.Width / ScaleX, local.Height / ScaleY));
        }
        else
        {
            _hoverWindowRegion = Rect.Empty;
            UpdateVisual(Rect.Empty);
        }
    }

    private void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_mode == CaptureSelectionMode.Area)
        {
            if (!_isDragging)
            {
                return;
            }

            _isDragging = false;
            SelectionCanvas.ReleaseMouseCapture();

            Rect dipRect = GetDragRect(e.GetPosition(SelectionCanvas));
            if (dipRect.Width < 4 || dipRect.Height < 4)
            {
                UpdateVisual(Rect.Empty); // terlalu kecil, anggap salah klik
                return;
            }

            Finish(ToPixelRegion(new Rect(
                dipRect.X * ScaleX, dipRect.Y * ScaleY, dipRect.Width * ScaleX, dipRect.Height * ScaleY)));
            return;
        }

        if (!_hoverWindowRegion.IsEmpty)
        {
            Finish(ToPixelRegion(_hoverWindowRegion));
        }
    }

    // ---- Fungsi bantu ----

    private Rect GetDragRect(Point current)
    {
        double left = Math.Clamp(Math.Min(_dragStart.X, current.X), 0, ActualWidth);
        double top = Math.Clamp(Math.Min(_dragStart.Y, current.Y), 0, ActualHeight);
        double right = Math.Clamp(Math.Max(_dragStart.X, current.X), 0, ActualWidth);
        double bottom = Math.Clamp(Math.Max(_dragStart.Y, current.Y), 0, ActualHeight);

        return new Rect(left, top, right - left, bottom - top);
    }

    private Int32Rect ToPixelRegion(Rect pixelRect)
    {
        int x = Math.Clamp((int)Math.Round(pixelRect.X), 0, _frozenImage.PixelWidth - 1);
        int y = Math.Clamp((int)Math.Round(pixelRect.Y), 0, _frozenImage.PixelHeight - 1);
        int width = Math.Clamp((int)Math.Round(pixelRect.Width), 1, _frozenImage.PixelWidth - x);
        int height = Math.Clamp((int)Math.Round(pixelRect.Height), 1, _frozenImage.PixelHeight - y);

        return new Int32Rect(x, y, width, height);
    }

    /// <summary>Menggambar bagian gelap di luar pilihan, bingkai pilihan, dan label ukurannya.</summary>
    private void UpdateVisual(Rect selectionDip)
    {
        var fullArea = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));

        if (selectionDip.IsEmpty || selectionDip.Width <= 0 || selectionDip.Height <= 0)
        {
            DimPath.Data = fullArea;
            SelectionFrame.Visibility = Visibility.Collapsed;
            SizeBadge.Visibility = Visibility.Collapsed;
            return;
        }

        // Gelapkan semuanya kecuali bagian yang dipilih.
        DimPath.Data = new CombinedGeometry(
            GeometryCombineMode.Exclude, fullArea, new RectangleGeometry(selectionDip));

        Canvas_SetRect(SelectionFrame, selectionDip);
        SelectionFrame.Visibility = Visibility.Visible;

        int pixelWidth = (int)Math.Round(selectionDip.Width * ScaleX);
        int pixelHeight = (int)Math.Round(selectionDip.Height * ScaleY);
        SizeText.Text = $"{pixelWidth} \u00D7 {pixelHeight}";

        System.Windows.Controls.Canvas.SetLeft(SizeBadge, selectionDip.X);
        System.Windows.Controls.Canvas.SetTop(SizeBadge, Math.Max(0, selectionDip.Y - 28));
        SizeBadge.Visibility = Visibility.Visible;
    }

    private static void Canvas_SetRect(FrameworkElement element, Rect rect)
    {
        System.Windows.Controls.Canvas.SetLeft(element, rect.X);
        System.Windows.Controls.Canvas.SetTop(element, rect.Y);
        element.Width = rect.Width;
        element.Height = rect.Height;
    }

    private void Finish(Int32Rect region)
    {
        SelectedRegion = region;
        Close();
    }

    private void Cancel()
    {
        SelectedRegion = null;
        Close();
    }
}
