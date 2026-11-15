using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace iLand.Helpers;

/// <summary>
/// Membuat gambar kecil persegi dari sebuah gambar besar (dipakai pratinjau screenshot di notifikasi).
///
/// Hasilnya adalah bitmap baru yang berdiri sendiri dan sudah dibekukan (Freeze). Penting: hasilnya TIDAK menyimpan
/// rujukan ke gambar asli, jadi screenshot besar (puluhan MB di layar 4K) bisa langsung dilepas dari memori.
/// Gambar hanya ada di memori. Tidak ditulis ke disk dan tidak dicatat di log.
/// </summary>
public static class ThumbnailHelper
{
    /// <summary>
    /// Mengecilkan gambar menjadi persegi size x size dengan cara "cover": sisi terpendek memenuhi kotak,
    /// bagian lebihnya dipotong dari tengah. Mengembalikan null jika gagal (notifikasi lalu memakai ikon biasa).
    /// Harus dipanggil dari thread UI.
    /// </summary>
    /// <param name="source">Gambar asli (misalnya hasil screenshot).</param>
    /// <param name="size">Sisi hasil dalam piksel.</param>
    public static BitmapSource? CreateSquare(BitmapSource source, int size)
    {
        try
        {
            if (size <= 0 || source.PixelWidth <= 0 || source.PixelHeight <= 0)
            {
                return null;
            }

            // Skala agar sisi terpendek tepat memenuhi kotak.
            double scale = Math.Max((double)size / source.PixelWidth, (double)size / source.PixelHeight);
            double drawWidth = source.PixelWidth * scale;
            double drawHeight = source.PixelHeight * scale;

            var visual = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);

            using (DrawingContext context = visual.RenderOpen())
            {
                context.PushClip(new RectangleGeometry(new Rect(0, 0, size, size)));
                context.DrawImage(source, new Rect((size - drawWidth) / 2, (size - drawHeight) / 2, drawWidth, drawHeight));
                context.Pop();
            }

            var result = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            result.Render(visual);
            result.Freeze();
            return result;
        }
        catch (Exception)
        {
            // Pratinjau hanya pemanis. Jika gagal, notifikasi tetap tampil dengan ikon biasa.
            return null;
        }
    }
}
