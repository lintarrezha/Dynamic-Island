using System.Windows;
using System.Windows.Media;

namespace iLand.Helpers;

/// <summary>
/// Pembuat geometri bentuk Notch. Dipakai IslandWindow (bentuk dan klip island) dan IslandPreview (pratinjau Settings).
/// Semua geometri dibekukan (Freeze) supaya murah dipakai berulang.
/// Radius yang tidak masuk akal (negatif, atau lebih besar dari ruang yang ada, misalnya saat animasi memantul) dijepit.
/// </summary>
public static class IslandShapeGeometry
{
    /// <summary>
    /// Badan island untuk klip isi: sisi atas rata (radius 0), hanya dua sudut bawah yang membulat.
    /// Titik awal (0, 0) = sudut kiri atas badan.
    /// </summary>
    public static Geometry FlatTopBody(double width, double height, double bottomRadius)
    {
        width = Math.Max(width, 0);
        height = Math.Max(height, 0);
        double b = Math.Clamp(bottomRadius, 0, Math.Min(width, height) / 2);

        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(0, 0), true, true);
            ctx.LineTo(new Point(width, 0), true, false);
            ctx.LineTo(new Point(width, height - b), true, false);
            if (b > 0.01)
            {
                ctx.ArcTo(new Point(width - b, height), new Size(b, b), 0, false, SweepDirection.Clockwise, true, false);
            }

            ctx.LineTo(new Point(b, height), true, false);
            if (b > 0.01)
            {
                ctx.ArcTo(new Point(0, height - b), new Size(b, b), 0, false, SweepDirection.Clockwise, true, false);
            }
        }

        geometry.Freeze();
        return geometry;
    }

    /// <summary>
    /// Bentuk Notch lengkap: badan (sisi atas rata, sudut bawah membulat) ditambah dua "telinga" di kiri dan kanan atas.
    /// Telinga adalah lengkung ke luar (cekung) sebesar earRadius, tingginya earRadius dari tepi atas, yang membuat tepi atas
    /// island menyatu dengan tepi layar. Koordinat badan 0..width dan 0..height; telinga meluas ke x = -earRadius dan x = width + earRadius.
    /// </summary>
    public static Geometry NotchWithEars(double width, double height, double bottomRadius, double earRadius)
    {
        width = Math.Max(width, 0);
        height = Math.Max(height, 0);
        double b = Math.Clamp(bottomRadius, 0, Math.Min(width, height) / 2);
        double r = Math.Clamp(earRadius, 0, Math.Max(height - b, 0));

        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(-r, 0), true, true);
            ctx.LineTo(new Point(width + r, 0), true, false);

            // Telinga kanan: dari ujung atas ke sisi badan, lengkung cekung (berlawanan arah jarum jam).
            if (r > 0.01)
            {
                ctx.ArcTo(new Point(width, r), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, false);
            }

            ctx.LineTo(new Point(width, height - b), true, false);
            if (b > 0.01)
            {
                ctx.ArcTo(new Point(width - b, height), new Size(b, b), 0, false, SweepDirection.Clockwise, true, false);
            }

            ctx.LineTo(new Point(b, height), true, false);
            if (b > 0.01)
            {
                ctx.ArcTo(new Point(0, height - b), new Size(b, b), 0, false, SweepDirection.Clockwise, true, false);
            }

            // Telinga kiri: dari sisi badan ke ujung atas, lengkung cekung.
            ctx.LineTo(new Point(0, r), true, false);
            if (r > 0.01)
            {
                ctx.ArcTo(new Point(-r, 0), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, false);
            }
        }

        geometry.Freeze();
        return geometry;
    }
}
