using System.Runtime.InteropServices;
using System.Text;

namespace iLand.Native;

/// <summary>
/// Semua pemanggilan Windows API (P/Invoke) dikumpulkan di sini.
/// Semua fungsi di bawah adalah API resmi dan terdokumentasi dari Microsoft
/// (user32, kernel32, dwmapi, shell32).
/// </summary>
internal static class NativeMethods
{
    // ===================================================================
    // Style window
    // ===================================================================
    public const int GWL_EXSTYLE = -20;

    /// <summary>Window tidak ditembus mouse (overlay transparan).</summary>
    public const int WS_EX_TRANSPARENT = 0x00000020;

    /// <summary>Window tidak muncul di Alt+Tab.</summary>
    public const int WS_EX_TOOLWINDOW = 0x00000080;

    /// <summary>Window memang ingin tampil sebagai window aplikasi biasa.</summary>
    public const int WS_EX_APPWINDOW = 0x00040000;

    /// <summary>Window tidak mengambil fokus saat diklik.</summary>
    public const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    // ===================================================================
    // Posisi window (dalam piksel fisik)
    // ===================================================================
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    // ===================================================================
    // Baterai
    // ===================================================================

    /// <summary>Struktur hasil GetSystemPowerStatus (info baterai).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SystemPowerStatus
    {
        public byte ACLineStatus;        // 0 = baterai, 1 = dicolok, 255 = tidak diketahui
        public byte BatteryFlag;         // 128 = tidak ada baterai, 8 = sedang charging
        public byte BatteryLifePercent;  // 0-100, atau 255 = tidak diketahui
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    // ===================================================================
    // Persegi (dipakai window dan monitor)
    // ===================================================================
    [StructLayout(LayoutKind.Sequential)]
    public struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    // ===================================================================
    // Event window (SetWinEventHook): Windows memberi tahu kita saat ada perubahan,
    // jadi tidak perlu polling.
    // ===================================================================
    public delegate void WinEventDelegate(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0002; // abaikan event dari window iLand sendiri

    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
    public const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
    public const uint EVENT_OBJECT_DESTROY = 0x8001;
    public const uint EVENT_OBJECT_SHOW = 0x8002;
    public const uint EVENT_OBJECT_HIDE = 0x8003;
    public const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    public const uint EVENT_OBJECT_CLOAKED = 0x8017;
    public const uint EVENT_OBJECT_UNCLOAKED = 0x8018;

    /// <summary>Event ini berasal dari window itu sendiri (bukan tombol, menu, dsb).</summary>
    public const int OBJID_WINDOW = 0;

    [DllImport("user32.dll")]
    public static extern IntPtr SetWinEventHook(
        uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    // ===================================================================
    // Daftar window dan informasinya
    // ===================================================================
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    // ===================================================================
    // DWM (Desktop Window Manager)
    // ===================================================================

    /// <summary>Window "tersembunyi" oleh Windows (misalnya di desktop virtual lain).</summary>
    public const int DWMWA_CLOAKED = 14;

    /// <summary>Ukuran window yang benar-benar terlihat (tanpa bayangan/border tak terlihat).</summary>
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out NativeRect pvAttribute, int cbAttribute);

    /// <summary>Title bar mengikuti mode gelap (Windows 10 versi 2004 ke atas).</summary>
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    /// <summary>Warna title bar kustom (Windows 11).</summary>
    public const int DWMWA_CAPTION_COLOR = 35;

    /// <summary>Warna teks title bar kustom (Windows 11).</summary>
    public const int DWMWA_TEXT_COLOR = 36;

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);
    // ===================================================================
    // Deteksi aplikasi fullscreen
    // ===================================================================
    public const int QUNS_BUSY = 2;                    // aplikasi fullscreen berjalan
    public const int QUNS_RUNNING_D3D_FULL_SCREEN = 3; // game / video DirectX fullscreen
    public const int QUNS_PRESENTATION_MODE = 4;       // mode presentasi

    [DllImport("shell32.dll")]
    public static extern int SHQueryUserNotificationState(out int pquns);

    // ===================================================================
    // Monitor
    // ===================================================================
    public const uint MONITORINFOF_PRIMARY = 0x00000001;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MonitorInfoEx
    {
        public int cbSize;
        public NativeRect rcMonitor;
        public NativeRect rcWork;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref NativeRect lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfoEx lpmi);

        // ===================================================================
    // Menyembunyikan window dari tangkapan layar
    // ===================================================================

    /// <summary>Window terekam seperti biasa di screenshot dan screen capture.</summary>
    public const uint WDA_NONE = 0x00000000;

    /// <summary>Window tidak ikut terekam di screenshot atau screen capture (Windows 10 build 19041+).</summary>
    public const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    // ===================================================================
    // Menyalin sebagian kecil layar (GDI)
    // ===================================================================
    public const uint SRCCOPY = 0x00CC0020;
    public const uint DIB_RGB_COLORS = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct BitmapInfoHeader
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int cx, int cy);

    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BitBlt(
        IntPtr hdcDest, int xDest, int yDest, int width, int height,
        IntPtr hdcSrc, int xSrc, int ySrc, uint rop);

    [DllImport("gdi32.dll")]
    public static extern int GetDIBits(
        IntPtr hdc, IntPtr hbm, uint start, uint cLines,
        byte[] lpvBits, ref BitmapInfoHeader lpbmi, uint usage);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteObject(IntPtr ho);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteDC(IntPtr hdc);

        // ===================================================================
    // Ikon file (sama dengan yang tampil di File Explorer)
    // ===================================================================
    public const uint SHGFI_ICON = 0x00000100;
    public const uint SHGFI_LARGEICON = 0x00000000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct ShFileInfo
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SHGetFileInfo(
        string pszPath, uint dwFileAttributes, ref ShFileInfo psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr hIcon);

        // ===================================================================
    // Pintasan keyboard global, posisi kursor, dan tangkapan layar
    // ===================================================================
    public const int WM_HOTKEY = 0x0312;
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_NOREPEAT = 0x4000;

    /// <summary>Menyertakan window "layered" saat menyalin layar.</summary>
    public const uint CAPTUREBLT = 0x40000000;

    /// <summary>Induk khusus untuk window tak terlihat yang hanya menerima pesan.</summary>
    public static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

    /// <summary>Menempatkan window di atas semua window lain.</summary>
    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

    [StructLayout(LayoutKind.Sequential)]
    public struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out NativePoint lpPoint);

    // ===================================================================
    // Pemantau clipboard (berbasis event, tanpa polling)
    // ===================================================================
    public const int WM_CLIPBOARDUPDATE = 0x031D;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    // ===================================================================
    // Rekam layar: mencari handle monitor dari posisi kursor
    // ===================================================================
    public const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(NativePoint pt, uint flags);
}
