using System.Globalization;
using System.Windows;
using System.Windows.Media;
using iLand.Helpers;
using iLand.Models;

namespace iLand.Views.SettingsControls;

/// <summary>
/// Pratinjau island untuk halaman Island di Settings: menggambar pill idle (jam dan cincin baterai contoh)
/// dengan bentuk (Dynamic atau Notch) dan skala yang dipilih, di atas "layar" berlatar terang supaya bentuk hitamnya jelas.
///
/// Memakai IslandProfile dan IslandShapeGeometry, yaitu sumber angka dan geometri yang sama dengan island sungguhan,
/// jadi pratinjau selalu sama dengan hasilnya. Digambar langsung lewat OnRender (tanpa XAML).
/// </summary>
public sealed class IslandPreview : FrameworkElement
{
    public static readonly DependencyProperty ShapeProperty = DependencyProperty.Register(
        nameof(Shape), typeof(IslandShape), typeof(IslandPreview),
        new FrameworkPropertyMetadata(IslandShape.Dynamic, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ScaleProperty = DependencyProperty.Register(
        nameof(Scale), typeof(double), typeof(IslandPreview),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Bentuk yang digambar.</summary>
    public IslandShape Shape
    {
        get => (IslandShape)GetValue(ShapeProperty);
        set => SetValue(ShapeProperty, value);
    }

    /// <summary>Skala 0,8 sampai 1,15.</summary>
    public double Scale
    {
        get => (double)GetValue(ScaleProperty);
        set => SetValue(ScaleProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        double width = ActualWidth;
        double height = ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        double scale = Math.Clamp(double.IsNaN(Scale) ? 1.0 : Scale, AppSettings.MinIslandScale, AppSettings.MaxIslandScale);

        // "Layar": latar terang bergradasi (seperti wallpaper terang) supaya island hitam terlihat.
        var screenBrush = new LinearGradientBrush(
            Color.FromRgb(0xB9, 0xC0, 0xD6), Color.FromRgb(0xE9, 0xEC, 0xF4), 90);
        drawingContext.DrawRoundedRectangle(screenBrush, null, new Rect(0, 0, width, height), 10, 10);

        // Angka bentuk dari IslandProfile (pada skala 100%), lalu dikalikan skala.
        IslandShapeMetrics metrics = IslandProfile.Get(Shape, IslandVisualMode.Normal, IslandProfile.PillHeight, 28);
        double bodyWidth = IslandProfile.PillWidth * scale;
        double bodyHeight = IslandProfile.PillHeight * scale;
        double left = (width - bodyWidth) / 2;
        double top = metrics.TopGap * scale;

        Brush islandBrush = TryFindResource("IslandBrush") as Brush ?? Brushes.Black;

        if (Shape == IslandShape.Notch)
        {
            drawingContext.PushTransform(new TranslateTransform(left, top));
            drawingContext.DrawGeometry(islandBrush, null,
                IslandShapeGeometry.NotchWithEars(bodyWidth, bodyHeight, metrics.BottomRadius * scale, metrics.EarRadius * scale));
            drawingContext.Pop();
        }
        else
        {
            double radius = metrics.BottomRadius * scale;
            drawingContext.DrawRoundedRectangle(islandBrush, null, new Rect(left, top, bodyWidth, bodyHeight), radius, radius);
        }

        DrawSampleContent(drawingContext, left, top, bodyWidth, bodyHeight, scale);
    }

    /// <summary>Isi contoh di dalam pill: jam di kiri, cincin baterai di kanan (sama seperti pill idle).</summary>
    private void DrawSampleContent(DrawingContext dc, double left, double top, double bodyWidth, double bodyHeight, double scale)
    {
        double margin = 16 * scale;

        // Jam contoh.
        var clock = new FormattedText(
            "21:35",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI Variable Display, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            13 * scale,
            Brushes.White,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(clock, new Point(left + margin, top + (bodyHeight - clock.Height) / 2));

        // Cincin baterai contoh (lintasan redup + busur hijau 75%).
        double ringRadius = 7.5 * scale;
        double thickness = 2.5 * scale;
        var center = new Point(left + bodyWidth - margin - ringRadius, top + bodyHeight / 2);

        dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)), thickness),
            center, ringRadius, ringRadius);

        var green = new Pen(new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58)), thickness) // hijau semantik #30D158
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };

        var arc = new StreamGeometry();
        using (StreamGeometryContext ctx = arc.Open())
        {
            // Mulai dari atas, searah jarum jam, 270 derajat berakhir di sisi kiri.
            ctx.BeginFigure(new Point(center.X, center.Y - ringRadius), false, false);
            ctx.ArcTo(new Point(center.X - ringRadius, center.Y), new Size(ringRadius, ringRadius), 0,
                true, SweepDirection.Clockwise, true, false);
        }

        arc.Freeze();
        dc.DrawGeometry(null, green, arc);
    }
}
