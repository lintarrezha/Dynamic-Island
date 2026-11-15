using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace iLand.Helpers;

/// <summary>
/// Mengambil satu warna aksen dari sampul album. Dipakai untuk isi progress dan visualizer (latar tetap hitam).
///
/// Cara kerja: sampul diperkecil menjadi 24 x 24 piksel (576 piksel, murah), tiap piksel yang cukup berwarna
/// dimasukkan ke salah satu dari 12 kelompok rona dengan bobot saturasi x kecerahan, lalu rata-rata warna
/// kelompok terberat dinormalkan agar terbaca di atas latar hitam. Piksel kelabu dan sangat gelap diabaikan.
/// Sampul yang nyaris monokrom (kurang dari 8% piksel berwarna) menghasilkan null: pemakai memakai aksen bawaan.
/// Dipanggil sekali per lagu, tanpa timer, tanpa menyimpan gambar.
/// </summary>
public static class AccentColorExtractor
{
    private const int Size = 24;
    private const int HueBuckets = 12;
    private const double MinSaturation = 0.25;
    private const double MinValue = 0.20;
    private const double MinColoredShare = 0.08;

    public static Color? Extract(BitmapSource? source)
    {
        if (source is null || source.PixelWidth < 1 || source.PixelHeight < 1)
        {
            return null;
        }

        try
        {
            var scaled = new TransformedBitmap(
                source, new ScaleTransform((double)Size / source.PixelWidth, (double)Size / source.PixelHeight));
            var converted = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);

            int width = converted.PixelWidth;
            int height = converted.PixelHeight;
            int stride = width * 4;
            var pixels = new byte[stride * height];
            converted.CopyPixels(pixels, stride, 0);

            var weight = new double[HueBuckets];
            var sumR = new double[HueBuckets];
            var sumG = new double[HueBuckets];
            var sumB = new double[HueBuckets];
            int colored = 0;

            for (int i = 0; i < pixels.Length; i += 4)
            {
                if (pixels[i + 3] < 128)
                {
                    continue; // hampir transparan
                }

                double b = pixels[i];
                double g = pixels[i + 1];
                double r = pixels[i + 2];

                RgbToHsv(r, g, b, out double hue, out double saturation, out double value);
                if (saturation < MinSaturation || value < MinValue)
                {
                    continue; // kelabu atau terlalu gelap: bukan warna yang mewakili sampul
                }

                int bucket = Math.Min(HueBuckets - 1, (int)(hue / 360.0 * HueBuckets));
                double w = saturation * value;
                weight[bucket] += w;
                sumR[bucket] += r * w;
                sumG[bucket] += g * w;
                sumB[bucket] += b * w;
                colored++;
            }

            if (colored < width * height * MinColoredShare)
            {
                return null; // sampul monokrom
            }

            int best = 0;
            for (int k = 1; k < HueBuckets; k++)
            {
                if (weight[k] > weight[best])
                {
                    best = k;
                }
            }

            if (weight[best] <= 0)
            {
                return null;
            }

            double avgR = sumR[best] / weight[best];
            double avgG = sumG[best] / weight[best];
            double avgB = sumB[best] / weight[best];

            // Normalisasi: rona dipertahankan, saturasi dan kecerahan dijaga supaya jelas di atas hitam.
            RgbToHsv(avgR, avgG, avgB, out double h, out double s, out double v);
            s = Math.Clamp(s, 0.50, 0.95);
            v = Math.Clamp(v, 0.80, 1.00);
            return HsvToRgb(h, s, v);
        }
        catch
        {
            // Format gambar tidak didukung atau bitmap tidak bisa dibaca: pakai aksen bawaan.
            return null;
        }
    }

    private static void RgbToHsv(double r, double g, double b, out double hue, out double saturation, out double value)
    {
        r /= 255.0;
        g /= 255.0;
        b /= 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        value = max;
        saturation = max <= 0 ? 0 : delta / max;

        if (delta <= 0)
        {
            hue = 0;
        }
        else if (max == r)
        {
            hue = 60.0 * (((g - b) / delta) % 6.0);
        }
        else if (max == g)
        {
            hue = 60.0 * (((b - r) / delta) + 2.0);
        }
        else
        {
            hue = 60.0 * (((r - g) / delta) + 4.0);
        }

        if (hue < 0)
        {
            hue += 360.0;
        }
    }

    private static Color HsvToRgb(double hue, double saturation, double value)
    {
        double c = value * saturation;
        double x = c * (1 - Math.Abs((hue / 60.0) % 2 - 1));
        double m = value - c;

        double r, g, b;
        if (hue < 60) { r = c; g = x; b = 0; }
        else if (hue < 120) { r = x; g = c; b = 0; }
        else if (hue < 180) { r = 0; g = c; b = x; }
        else if (hue < 240) { r = 0; g = x; b = c; }
        else if (hue < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }
}
