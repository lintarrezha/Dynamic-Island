using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using iLand.Native;

namespace iLand.Helpers;

/// <summary>Mencari jendela aplikasi yang terlihat pada sebuah titik layar (untuk screenshot jendela).</summary>
public static class WindowFinder
{
    // Window milik sistem Windows yang bukan "aplikasi" dan harus diabaikan.
    private static readonly HashSet<string> IgnoredClassNames = new(StringComparer.Ordinal)
    {
        "Progman",                              // desktop
        "WorkerW",                              // desktop (wallpaper)
        "Windows.UI.Core.CoreWindow",           // panel Start, Search, dan sejenisnya
        "XamlExplorerHostIslandWindow",
        "TopLevelWindowForOverflowXamlIsland"
    };

    /// <summary>
    /// Mengembalikan ukuran (piksel layar) jendela teratas di titik (x, y),
    /// atau null jika tidak ada. Window iLand sendiri diabaikan.
    /// </summary>
    public static Rect? GetWindowBoundsAt(int x, int y)
    {
        uint ownProcessId = (uint)Environment.ProcessId;
        Rect? result = null;

        // EnumWindows memanggil lambda ini untuk setiap window dari yang paling atas.
        NativeMethods.EnumWindows((hwnd, lParam) =>
        {
            if (!IsCandidate(hwnd, ownProcessId) || !TryGetBounds(hwnd, out Rect bounds))
            {
                return true; // lanjut
            }

            if (bounds.Contains(x, y))
            {
                result = bounds;
                return false; // ketemu, berhenti
            }

            return true;
        }, IntPtr.Zero);

        return result;
    }

    private static bool IsCandidate(IntPtr hwnd, uint ownProcessId)
    {
        if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd))
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint processId);
        if (processId == ownProcessId)
        {
            return false;
        }

        // Window yang disembunyikan Windows (misalnya di desktop virtual lain).
        if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0
            && cloaked != 0)
        {
            return false;
        }

        // Abaikan overlay (tool window, tidak bisa difokus, tembus mouse), kecuali window aplikasi biasa.
        int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        bool isAppWindow = (exStyle & NativeMethods.WS_EX_APPWINDOW) != 0;
        int overlayFlags = NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TRANSPARENT;
        if (!isAppWindow && (exStyle & overlayFlags) != 0)
        {
            return false;
        }

        var className = new StringBuilder(256);
        NativeMethods.GetClassName(hwnd, className, className.Capacity);
        return !IgnoredClassNames.Contains(className.ToString());
    }

    private static bool TryGetBounds(IntPtr hwnd, out Rect bounds)
    {
        bounds = Rect.Empty;

        int result = NativeMethods.DwmGetWindowAttribute(
            hwnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out NativeMethods.NativeRect rect,
            Marshal.SizeOf<NativeMethods.NativeRect>());

        if (result != 0 && !NativeMethods.GetWindowRect(hwnd, out rect))
        {
            return false;
        }

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        bounds = new Rect(rect.Left, rect.Top, width, height);
        return true;
    }
}
