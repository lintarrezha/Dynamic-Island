using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using iLand.Native;

namespace iLand.Helpers;

/// <summary>
/// Mengambil gambar kecil untuk sebuah file: thumbnail untuk gambar, dan ikon bawaan Windows
/// (yang sama dengan di File Explorer) untuk file dan folder lain.
/// </summary>
public static class FileIconHelper
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".ico"
    };

    public static ImageSource? GetIcon(string path)
    {
        return TryLoadImageThumbnail(path) ?? TryGetShellIcon(path);
    }

    private static ImageSource? TryLoadImageThumbnail(string path)
    {
        if (!File.Exists(path) || !ImageExtensions.Contains(Path.GetExtension(path)))
        {
            return null;
        }

        try
        {
            // Dibaca lewat stream (bukan Uri) agar nama file dengan karakter khusus seperti "#" tetap aman.
            using FileStream stream = File.OpenRead(path);

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad; // baca semuanya sekarang, file tidak terkunci
            image.DecodePixelWidth = 128;                 // kecil saja, hemat memori
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            return null; // format rusak atau tidak didukung: pakai ikon biasa
        }
    }

    private static ImageSource? TryGetShellIcon(string path)
    {
        var info = new NativeMethods.ShFileInfo();

        IntPtr result = NativeMethods.SHGetFileInfo(
            path, 0, ref info,
            (uint)Marshal.SizeOf<NativeMethods.ShFileInfo>(),
            NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_LARGEICON);

        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(
                info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            // Handle ikon dari Windows wajib dilepas, kalau tidak aplikasi kehabisan handle GDI.
            NativeMethods.DestroyIcon(info.hIcon);
        }
    }
}
