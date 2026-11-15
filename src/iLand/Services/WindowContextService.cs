using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using iLand.Interfaces;
using iLand.Native;
using Microsoft.Extensions.Logging;

namespace iLand.Services;

/// <summary>
/// Mendeteksi apakah island menutupi sesuatu, dan apakah ada aplikasi fullscreen.
///
/// Cara kerja deteksi "isi di bawah island":
/// 1. Cek murah: adakah window aplikasi yang beririsan dengan area island? Jika tidak, selesai (kosong).
/// 2. Jika ada, ambil gambar kecil dari area tepat di belakang island (hanya beberapa ribu piksel,
///    dan island sendiri tidak ikut terekam). Gambar ini hanya dianalisis di memori:
///    tidak disimpan ke disk, tidak dikirim ke mana pun, dan langsung dibuang.
/// 3. Analisis: adakah tepi vertikal yang tajam (tepi tab, huruf, ikon, tombol)? Area polos
///    atau gradasi halus (bagian kosong di title bar) tidak punya tepi tajam, jadi dianggap kosong.
///
/// Pemicu pengecekan:
/// - Event Windows (SetWinEventHook): window berpindah, dibuka, ditutup, di-minimize.
/// - Timer 0,8 detik, HANYA selama ada window di bawah island, karena isi di dalam window
///   (misalnya tab baru di Chrome) tidak menghasilkan event Windows.
/// </summary>
public sealed class WindowContextService : IWindowContextService, IDisposable
{
    // ---- Pengaturan deteksi isi ----

    /// <summary>Selisih kecerahan (0-255) antara dua piksel bersebelahan yang dianggap "tepi tajam".</summary>
    private const int EdgeThreshold = 12;

    /// <summary>Jumlah tepi tajam minimum agar area dianggap berisi sesuatu.</summary>
    private const int MinEdgeCount = 20;

    // Window milik sistem Windows yang bukan "aplikasi" dan harus diabaikan.
    private static readonly HashSet<string> IgnoredClassNames = new(StringComparer.Ordinal)
    {
        "Progman",                              // desktop
        "WorkerW",                              // desktop (wallpaper)
        "Shell_TrayWnd",                        // taskbar
        "Shell_SecondaryTrayWnd",               // taskbar di monitor kedua
        "Windows.UI.Core.CoreWindow",           // panel Start, Search, dan sejenisnya
        "XamlExplorerHostIslandWindow",
        "TopLevelWindowForOverflowXamlIsland"
    };

    private readonly ILogger<WindowContextService> _logger;
    private readonly DispatcherTimer _debounceTimer;
    private readonly DispatcherTimer _contentWatchTimer;
    private readonly List<IntPtr> _hooks = new();
    private readonly uint _ownProcessId = (uint)Environment.ProcessId;

    // PENTING: delegate harus disimpan di field. Jika tidak, garbage collector bisa
    // menghapusnya dan aplikasi crash saat Windows memanggil callback.
    private readonly NativeMethods.WinEventDelegate _winEventCallback;

    private Rect _pillArea = Rect.Empty;
    private byte[] _pixelBuffer = Array.Empty<byte>();
    private bool _isContentDetectionAvailable;

    public bool IsContentBelowIsland { get; private set; }

    public bool IsFullscreenAppActive { get; private set; }

    public bool IsContentDetectionAvailable
    {
        get => _isContentDetectionAvailable;
        set
        {
            if (_isContentDetectionAvailable == value)
            {
                return;
            }

            _isContentDetectionAvailable = value;

            if (!value)
            {
                _logger.LogWarning("Content detection unavailable: smart collapse is disabled");
            }

            Evaluate();
        }
    }

    public event EventHandler? Changed;

    public WindowContextService(ILogger<WindowContextService> logger)
    {
        _logger = logger;

        _winEventCallback = OnWinEvent;

        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _debounceTimer.Tick += OnDebounceTick;

        _contentWatchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _contentWatchTimer.Tick += OnContentWatchTick;

        InstallHook(NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND);
        InstallHook(NativeMethods.EVENT_SYSTEM_MINIMIZESTART, NativeMethods.EVENT_SYSTEM_MINIMIZEEND);
        InstallHook(NativeMethods.EVENT_OBJECT_DESTROY, NativeMethods.EVENT_OBJECT_HIDE);
        InstallHook(NativeMethods.EVENT_OBJECT_LOCATIONCHANGE, NativeMethods.EVENT_OBJECT_LOCATIONCHANGE);
        InstallHook(NativeMethods.EVENT_OBJECT_CLOAKED, NativeMethods.EVENT_OBJECT_UNCLOAKED);

        _logger.LogInformation("Window context service started ({Count} hooks)", _hooks.Count);
    }

    public void SetIslandZone(Rect pillAreaInPixels)
    {
        _pillArea = pillAreaInPixels;
        Evaluate(); // area baru: cek ulang sekarang juga
    }

    private void InstallHook(uint eventMin, uint eventMax)
    {
        IntPtr hook = NativeMethods.SetWinEventHook(
            eventMin, eventMax, IntPtr.Zero, _winEventCallback, 0, 0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

        if (hook == IntPtr.Zero)
        {
            _logger.LogWarning("Failed to install WinEvent hook {Min:X}-{Max:X}", eventMin, eventMax);
            return;
        }

        _hooks.Add(hook);
    }

    // Callback dari Windows (berjalan di UI thread). Jangan sampai melempar exception ke Windows.
    private void OnWinEvent(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        try
        {
            // Hanya peduli pada event milik window itu sendiri.
            if (idObject != NativeMethods.OBJID_WINDOW || idChild != 0 || hwnd == IntPtr.Zero)
            {
                return;
            }

            // Mulai ulang hitung mundur 200 ms.
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in WinEvent callback");
        }
    }

    private void OnDebounceTick(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        Evaluate();
    }

    private void OnContentWatchTick(object? sender, EventArgs e)
    {
        Evaluate();
    }

    private void Evaluate()
    {
        try
        {
            bool windowBelow = CheckWindowBelowIsland();

            // Timer pemantau hanya hidup selama ada window di bawah island.
            // Jika tidak ada window sama sekali, tidak ada pekerjaan berulang.
            if (windowBelow)
            {
                if (!_contentWatchTimer.IsEnabled)
                {
                    _contentWatchTimer.Start();
                }
            }
            else
            {
                _contentWatchTimer.Stop();
            }

            bool contentBelow = windowBelow && _isContentDetectionAvailable && CheckContentBelowIsland();
            bool fullscreen = CheckFullscreen();

            if (contentBelow == IsContentBelowIsland && fullscreen == IsFullscreenAppActive)
            {
                return; // tidak ada perubahan
            }

            IsContentBelowIsland = contentBelow;
            IsFullscreenAppActive = fullscreen;
            _logger.LogDebug("Context changed: contentBelow={Content}, fullscreen={Fullscreen}", contentBelow, fullscreen);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to evaluate window context");
        }
    }

    // ---- Langkah 1: adakah window aplikasi di bawah island? ----

    private bool CheckWindowBelowIsland()
    {
        if (_pillArea.IsEmpty)
        {
            return false;
        }

        bool found = false;

        // EnumWindows memanggil lambda ini untuk setiap window, dari yang paling atas.
        // Mengembalikan true = lanjut, false = berhenti.
        NativeMethods.EnumWindows((hwnd, lParam) =>
        {
            if (!IsCandidateWindow(hwnd) || !TryGetVisibleBounds(hwnd, out Rect bounds))
            {
                return true;
            }

            if (bounds.IntersectsWith(_pillArea))
            {
                found = true;
                return false; // sudah ketemu, berhenti
            }

            return true;
        }, IntPtr.Zero);

        return found;
    }

    /// <summary>Apakah window ini aplikasi sungguhan yang terlihat oleh pengguna?</summary>
    private bool IsCandidateWindow(IntPtr hwnd)
    {
        if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd))
        {
            return false; // tersembunyi atau di-minimize
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint processId);
        if (processId == _ownProcessId)
        {
            return false; // window iLand sendiri
        }

        // "Cloaked" = window ada tetapi disembunyikan Windows (misalnya di desktop virtual lain).
        if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0
            && cloaked != 0)
        {
            return false;
        }

        // Abaikan overlay (tool window, tidak bisa difokus, tembus mouse),
        // kecuali window itu menyatakan dirinya aplikasi biasa.
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

    /// <summary>Ambil ukuran window yang benar-benar terlihat (dalam piksel layar).</summary>
    private static bool TryGetVisibleBounds(IntPtr hwnd, out Rect bounds)
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

    // ---- Langkah 2 dan 3: adakah isi (tab, teks, ikon) tepat di belakang island? ----

    private bool CheckContentBelowIsland()
    {
        int x = (int)Math.Round(_pillArea.X);
        int y = (int)Math.Round(_pillArea.Y);
        int width = (int)Math.Round(_pillArea.Width);
        int height = (int)Math.Round(_pillArea.Height);

        if (width <= 0 || height <= 0)
        {
            return false;
        }

        if (!CaptureScreenRegion(x, y, width, height))
        {
            return false; // gagal mengambil gambar: anggap kosong, island tetap normal
        }

        return ContainsSharpVerticalEdges(_pixelBuffer, width, height);
    }

    /// <summary>
    /// Menyalin sebagian kecil layar ke _pixelBuffer (format BGRA, 4 byte per piksel).
    /// Window island sudah dikecualikan dari tangkapan layar, jadi tidak ikut terambil.
    /// </summary>
    private bool CaptureScreenRegion(int x, int y, int width, int height)
    {
        int requiredSize = width * height * 4;
        if (_pixelBuffer.Length < requiredSize)
        {
            _pixelBuffer = new byte[requiredSize];
        }

        IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            return false;
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
                return false;
            }

            previousBitmap = NativeMethods.SelectObject(memoryDc, bitmap);

            if (!NativeMethods.BitBlt(memoryDc, 0, 0, width, height, screenDc, x, y, NativeMethods.SRCCOPY))
            {
                return false;
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
                memoryDc, bitmap, 0, (uint)height, _pixelBuffer, ref header, NativeMethods.DIB_RGB_COLORS);

            return lines == height;
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
    }

    /// <summary>
    /// Menghitung "tepi vertikal tajam": tempat kecerahan berubah drastis antara dua piksel
    /// yang bersebelahan secara horizontal. Tepi tab, huruf, ikon, dan tombol menghasilkan banyak tepi.
    /// Area polos, gradasi halus, dan garis datar (misalnya pemisah toolbar) tidak menghasilkannya.
    /// </summary>
    private static bool ContainsSharpVerticalEdges(byte[] pixels, int width, int height)
    {
        int edgeCount = 0;

        for (int y = 0; y < height; y++)
        {
            int rowStart = y * width * 4;
            int previousLuminance = GetLuminance(pixels, rowStart);

            for (int x = 1; x < width; x++)
            {
                int luminance = GetLuminance(pixels, rowStart + x * 4);

                if (Math.Abs(luminance - previousLuminance) >= EdgeThreshold)
                {
                    edgeCount++;
                    if (edgeCount >= MinEdgeCount)
                    {
                        return true; // sudah cukup bukti, tidak perlu lanjut
                    }
                }

                previousLuminance = luminance;
            }
        }

        return false;
    }

    /// <summary>Kecerahan satu piksel (0-255). Urutan byte piksel adalah Blue, Green, Red, Alpha.</summary>
    private static int GetLuminance(byte[] pixels, int index)
    {
        int blue = pixels[index];
        int green = pixels[index + 1];
        int red = pixels[index + 2];
        return (red * 299 + green * 587 + blue * 114) / 1000;
    }

    // ---- Deteksi fullscreen ----

    private static bool CheckFullscreen()
    {
        int hr = NativeMethods.SHQueryUserNotificationState(out int state);
        if (hr != 0)
        {
            return false;
        }

        return state == NativeMethods.QUNS_BUSY
               || state == NativeMethods.QUNS_RUNNING_D3D_FULL_SCREEN
               || state == NativeMethods.QUNS_PRESENTATION_MODE;
    }

    // Lepas semua hook dan timer agar tidak ada yang tertinggal di Windows.
    public void Dispose()
    {
        _debounceTimer.Stop();
        _debounceTimer.Tick -= OnDebounceTick;
        _contentWatchTimer.Stop();
        _contentWatchTimer.Tick -= OnContentWatchTick;

        foreach (IntPtr hook in _hooks)
        {
            NativeMethods.UnhookWinEvent(hook);
        }
        _hooks.Clear();
    }
}
