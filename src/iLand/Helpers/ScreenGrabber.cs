using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using iLand.Native;

namespace iLand.Helpers;

/// <summary>Menyalin sebagian layar menjadi gambar (lewat GDI, API resmi Windows).</summary>
public static class ScreenGrabber
{
    /// <summary>Ambil area layar berukuran piksel fisik. Mengembalikan null jika gagal.</summary>
    public static BitmapSource? Grab(Rect pixelBounds)
    {
        int x = (int)Math.Round(pixelBounds.X);
        int y = (int)Math.Round(pixelBounds.Y);
        int width = (int)Math.Round(pixelBounds.Width);
        int height = (int)Math.Round(pixelBounds.Height);

        if (width <= 0 || height <= 0)
        {
            return null;
        }

        byte[] pixels = new byte[width * height * 4];

        IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            return null;
        }

        IntPtr memoryDc = IntPtr.Zero;
        IntPtr bitmap = IntPtr.Zero;
        IntPtr previousBitmap = IntPtr.Zero;

        try
        {
            memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
            bitmap = NativeMethods.CreateCompatibleBitmap(screenDc, width, height);
            if (memoryDc == IntPtr.Zero || bitmap == IntPtr.Zero)
            {
                return null;
            }

            previousBitmap = NativeMethods.SelectObject(memoryDc, bitmap);

            // CAPTUREBLT ikut menyertakan window "layered" (misalnya overlay aplikasi lain).
            bool copied = NativeMethods.BitBlt(
                memoryDc, 0, 0, width, height, screenDc, x, y,
                NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT);
            if (!copied)
            {
                return null;
            }

            var header = new NativeMethods.BitmapInfoHeader
            {
                biSize = (uint)Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(),
                biWidth = width,
                biHeight = -height, // negatif = baris dari atas ke bawah
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0
            };

            int lines = NativeMethods.GetDIBits(
                memoryDc, bitmap, 0, (uint)height, pixels, ref header, NativeMethods.DIB_RGB_COLORS);
            if (lines != height)
            {
                return null;
            }
        }
        finally
        {
            // Semua objek GDI harus dilepas, kalau tidak aplikasi akan kehabisan handle.
            if (previousBitmap != IntPtr.Zero)
            {
                NativeMethods.SelectObject(memoryDc, previousBitmap);
            }
            if (bitmap != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(bitmap);
            }
            if (memoryDc != IntPtr.Zero)
            {
                NativeMethods.DeleteDC(memoryDc);
            }
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }

        // Bgr32: byte alpha dari GDI selalu 0, jadi diabaikan (kalau tidak, gambar jadi transparan).
        BitmapSource image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, width * 4);
        image.Freeze();
        return image;
    }
}
