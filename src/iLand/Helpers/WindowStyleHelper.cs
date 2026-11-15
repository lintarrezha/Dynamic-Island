using System.Windows;
using System.Windows.Interop;
using iLand.Native;

namespace iLand.Helpers;

public static class WindowStyleHelper
{
    /// <summary>
    /// Membuat window berperilaku seperti overlay:
    /// tidak muncul di Alt+Tab dan tidak mencuri fokus dari aplikasi lain.
    /// Harus dipanggil setelah window punya handle (di OnSourceInitialized).
    /// </summary>
    public static void ApplyOverlayStyle(Window window)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;

        int style = NativeMethods.GetWindowLong(handle, NativeMethods.GWL_EXSTYLE);
        style |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLong(handle, NativeMethods.GWL_EXSTYLE, style);
    }

    /// <summary>
    /// Mengatur apakah window ikut terekam saat layar di-capture (screenshot, rekaman layar, screen share).
    /// exclude = true  : window TIDAK ikut terekam. Dipakai agar iLand bisa "melihat" apa yang ada di
    ///                   belakangnya tanpa melihat dirinya sendiri.
    /// exclude = false : window terekam seperti biasa.
    /// Butuh Windows 10 versi 2004 (build 19041) atau lebih baru. Mengembalikan false jika Windows menolak perintahnya.
    /// </summary>
    public static bool SetCaptureExclusion(Window window, bool exclude)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        uint affinity = exclude ? NativeMethods.WDA_EXCLUDEFROMCAPTURE : NativeMethods.WDA_NONE;
        return NativeMethods.SetWindowDisplayAffinity(handle, affinity);
    }

    /// <summary>
    /// Mengizinkan atau mencegah window menjadi aktif saat diklik.
    /// activatable = true  : window bisa menerima ketikan keyboard (dipakai saat kotak teks diklik).
    /// activatable = false : kembali ke perilaku overlay yang tidak mengambil fokus.
    /// </summary>
    public static void SetActivatable(Window window, bool activatable)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        int style = NativeMethods.GetWindowLong(handle, NativeMethods.GWL_EXSTYLE);

        style = activatable
            ? style & ~NativeMethods.WS_EX_NOACTIVATE
            : style | NativeMethods.WS_EX_NOACTIVATE;

        NativeMethods.SetWindowLong(handle, NativeMethods.GWL_EXSTYLE, style);
    }

    /// <summary>
    /// Membuat title bar (garis judul) window menjadi gelap agar menyatu dengan isi window.
    /// Windows 10 versi 2004 ke atas: title bar mengikuti mode gelap.
    /// Windows 11: warna title bar disamakan dengan latar Settings (#151517) dan teksnya putih.
    /// Jika versi Windows tidak mendukung, Windows menolak perintahnya dan title bar tetap seperti biasa
    /// (tidak ada error). Harus dipanggil setelah window punya handle (di SourceInitialized).
    /// </summary>
    public static void ApplyDarkTitleBar(Window window)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;

        int enabled = 1;
        NativeMethods.DwmSetWindowAttribute(
            handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref enabled, sizeof(int));

        // Warna di Windows ditulis sebagai 0x00BBGGRR (urutan biru, hijau, merah).
        int captionColor = 0x00171515; // #151517
        NativeMethods.DwmSetWindowAttribute(
            handle, NativeMethods.DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));

        int textColor = 0x00FFFFFF; // putih
        NativeMethods.DwmSetWindowAttribute(
            handle, NativeMethods.DWMWA_TEXT_COLOR, ref textColor, sizeof(int));
    }
}
