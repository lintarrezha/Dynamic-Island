using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace iLand.Helpers;

/// <summary>
/// Membuat latar "ambience" gaya Glass dari sampul lagu: bagian tengah sampul diambil (jadi terlihat diperbesar), diperkecil
/// ke gambar sangat kecil, di-blur di memori, lalu ditampilkan memenuhi island. Hasilnya satu BitmapSource beku berukuran
/// sekitar 54 x 29 piksel (sekitar 6 KB) per lagu.
///
/// Mengapa tidak memakai BlurEffect WPF: efek itu dihitung ulang untuk seluruh area island pada setiap frame, membuat animasi
/// buka-tutup patah-patah dan memakai GPU terus-menerus. Gambar kecil yang dibuat sekali dan diperbesar halus oleh WPF
/// terlihat sama lembutnya dan hampir tanpa biaya setelah dibuat.
///
/// Dipanggil di UI thread, hanya saat gaya Glass aktif dan sampul berganti. Mengembalikan null bila sampul tidak bisa dibaca.
/// </summary>
public static class CoverBlur
{
    // Perbandingan ukuran bitmap hasil mengikuti perbandingan island Glass (540 x 290).
    private const int TargetWidth = 54;
    private const int TargetHeight = 29;

    // Bagian sampul yang dipakai (lebarnya). 0,72 = perbesaran sekitar 1,4 kali. Makin kecil makin besar zoom-nya.
    private const double ZoomFraction = 0.72;

    // Kekuatan blur: radius kotak dan jumlah putaran (tiga putaran kotak mendekati blur Gauss) pada bitmap 54 x 29.
    private const int BlurRadius = 3;
    private const int BlurPasses = 3;

    public static BitmapSource? Create(ImageSource? source)
    {
        if (source is not BitmapSource bitmap || bitmap.PixelWidth < 8 || bitmap.PixelHeight < 8)
        {
            return null;
        }

        try
        {
            double aspect = (double)TargetWidth / TargetHeight;

            // 1. Ambil bagian tengah sampul dengan perbandingan lebar-tinggi island (zoom).
            int cropWidth = Math.Max(8, (int)(bitmap.PixelWidth * ZoomFraction));
            int cropHeight = (int)(cropWidth / aspect);
            if (cropHeight > bitmap.PixelHeight)
            {
                cropHeight = bitmap.PixelHeight;
                cropWidth = Math.Max(8, (int)(cropHeight * aspect));
            }

            cropHeight = Math.Max(4, cropHeight);
            cropWidth = Math.Min(cropWidth, bitmap.PixelWidth);

            int x = (bitmap.PixelWidth - cropWidth) / 2;
            int y = (bitmap.PixelHeight - cropHeight) / 2;
            var cropped = new CroppedBitmap(bitmap, new Int32Rect(x, y, cropWidth, cropHeight));

            // 2. Perkecil ke gambar mini lalu ubah ke Bgra32 agar mudah diolah.
            var scaled = new TransformedBitmap(
                cropped,
                new ScaleTransform((double)TargetWidth / cropWidth, (double)TargetHeight / cropHeight));
            var converted = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);

            int width = converted.PixelWidth;
            int height = converted.PixelHeight;
            int stride = width * 4;
            byte[] pixels = new byte[stride * height];
            converted.CopyPixels(pixels, stride, 0);

            // 3. Blur di memori.
            Blur(pixels, width, height);

            var result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            result.Freeze();
            return result;
        }
        catch
        {
            return null; // sampul tidak bisa dibaca: latar tetap hitam
        }
    }

    private static void Blur(byte[] pixels, int width, int height)
    {
        byte[] temp = new byte[pixels.Length];

        for (int pass = 0; pass < BlurPasses; pass++)
        {
            BoxBlur(pixels, temp, width, height, horizontal: true);
            BoxBlur(temp, pixels, width, height, horizontal: false);
        }
    }

    /// <summary>Satu putaran blur kotak satu arah (tepi ditahan: piksel di luar gambar memakai piksel tepi).</summary>
    private static void BoxBlur(byte[] source, byte[] target, int width, int height, bool horizontal)
    {
        int lineCount = horizontal ? height : width;
        int lineLength = horizontal ? width : height;
        int stepAlong = horizontal ? 4 : width * 4;
        int stepAcross = horizontal ? width * 4 : 4;
        int windowSize = BlurRadius * 2 + 1;

        for (int line = 0; line < lineCount; line++)
        {
            int lineStart = line * stepAcross;

            for (int i = 0; i < lineLength; i++)
            {
                int blue = 0, green = 0, red = 0;

                for (int k = -BlurRadius; k <= BlurRadius; k++)
                {
                    int j = Math.Clamp(i + k, 0, lineLength - 1);
                    int index = lineStart + j * stepAlong;
                    blue += source[index];
                    green += source[index + 1];
                    red += source[index + 2];
                }

                int output = lineStart + i * stepAlong;
                target[output] = (byte)(blue / windowSize);
                target[output + 1] = (byte)(green / windowSize);
                target[output + 2] = (byte)(red / windowSize);
                target[output + 3] = 255;
            }
        }
    }
}
