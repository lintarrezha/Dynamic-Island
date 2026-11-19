using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using iLand.Native;

namespace iLand.Views;

/// <summary>
/// Overlay layar penuh untuk memilih warna. Layar sudah dibekukan sebagai gambar, jadi warna yang dipilih persis yang
/// terlihat. Kaca pembesar mengikuti kursor. Hasilnya ada di PickedColor, atau null jika dibatalkan (Esc atau klik kanan).
/// Gambar hanya hidup di memori selama jendela ini terbuka.
/// </summary>
public partial class ColorPickerOverlayWindow : Window
{
    private const double LoupeSize = 128;
    private const double LoupeHeight = 148; // lingkaran + label warna
    private const double CursorOffset = 24;
    private const double SourcePixels = 16; // lebar area layar yang diperbesar

    private readonly BitmapSource _frozenImage;
    private readonly Rect _monitorBounds; // piksel layar
    private readonly ImageBrush _loupeBrush;

    /// <summary>Warna piksel yang diklik. Null = dibatalkan.</summary>
    public Color? PickedColor { get; private set; }

    public ColorPickerOverlayWindow(BitmapSource frozenImage, Rect monitorBounds)
    {
        InitializeComponent();

        _frozenImage = frozenImage;
        _monitorBounds = monitorBounds;
        FrozenImage.Source = frozenImage;

        // Satuan Viewbox mutlak: piksel gambar (gambar beku selalu 96 dpi, jadi 1 piksel = 1 satuan).
        _loupeBrush = new ImageBrush(frozenImage)
        {
            ViewboxUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.Fill
        };
        LoupeImage.Fill = _loupeBrush;
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
    }

    // Piksel gambar per satuan WPF, dihitung dari ukuran window agar benar di skala DPI berapa pun.
    private double ScaleX => _frozenImage.PixelWidth / ActualWidth;

    private double ScaleY => _frozenImage.PixelHeight / ActualHeight;

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            PickedColor = null;
            Close();
        }
    }

    private void Canvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        PickedColor = null;
        Close();
    }

    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Point position = e.GetPosition(PickCanvas);
        PickedColor = ReadPixel((int)(position.X * ScaleX), (int)(position.Y * ScaleY));
        Close();
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        Point position = e.GetPosition(PickCanvas);
        int pixelX = (int)(position.X * ScaleX);
        int pixelY = (int)(position.Y * ScaleY);

        // Area layar yang diperbesar: SourcePixels x SourcePixels di sekitar kursor (boleh melewati tepi gambar).
        double half = SourcePixels / 2;
        _loupeBrush.Viewbox = new Rect(pixelX + 0.5 - half, pixelY + 0.5 - half, SourcePixels, SourcePixels);

        Color? color = ReadPixel(pixelX, pixelY);
        if (color is Color c)
        {
            HexText.Text = ToHex(c);
            Swatch.Background = new SolidColorBrush(c);
        }

        // Letakkan kaca pembesar di kanan-bawah kursor, dan balik ke sisi lain jika akan keluar dari layar.
        double left = position.X + CursorOffset;
        double top = position.Y + CursorOffset;
        if (left + LoupeSize > ActualWidth)
        {
            left = position.X - CursorOffset - LoupeSize;
        }

        if (top + LoupeHeight > ActualHeight)
        {
            top = position.Y - CursorOffset - LoupeHeight;
        }

        Canvas.SetLeft(Loupe, left);
        Canvas.SetTop(Loupe, top);
        Loupe.Visibility = Visibility.Visible;
    }

    /// <summary>Membaca satu piksel dari gambar beku (format Bgr32). Null jika di luar gambar.</summary>
    private Color? ReadPixel(int x, int y)
    {
        if (x < 0 || y < 0 || x >= _frozenImage.PixelWidth || y >= _frozenImage.PixelHeight)
        {
            return null;
        }

        byte[] pixel = new byte[4];
        _frozenImage.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromRgb(pixel[2], pixel[1], pixel[0]);
    }

    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
