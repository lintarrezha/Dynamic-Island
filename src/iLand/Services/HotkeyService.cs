using System.Runtime.InteropServices;
using System.Windows.Interop;
using iLand.Interfaces;
using iLand.Models;
using iLand.Native;
using Microsoft.Extensions.Logging;

namespace iLand.Services;

/// <summary>
/// Mendaftarkan pintasan keyboard global lewat RegisterHotKey (API resmi Windows).
/// Windows mengirim pesan WM_HOTKEY ke sebuah window tak terlihat milik kita, tanpa memantau
/// ketikan di keyboard (tidak ada "keylogger", hanya kombinasi tombol yang didaftarkan yang kita terima).
///
/// Pintasan bawaan:
/// Ctrl + Alt + S : screenshot area. Sengaja BUKAN Ctrl + Shift + S karena kombinasi itu dipakai
///                  hampir semua aplikasi untuk "Save As", dan pintasan global akan merebutnya.
/// Ctrl + Alt + R : mulai atau berhenti merekam layar.
/// </summary>
public sealed class HotkeyService : IHotkeyService, IDisposable
{
    private const int CaptureAreaHotkeyId = 1;
    private const int ToggleRecordingHotkeyId = 2;
    private const uint VirtualKeyS = 0x53;
    private const uint VirtualKeyR = 0x52;

    private readonly ILogger<HotkeyService> _logger;
    private readonly INotificationService _notificationService;
    private readonly List<int> _registeredIds = new();
    private HwndSource? _source;

    public event EventHandler? CaptureAreaRequested;

    public event EventHandler? ToggleRecordingRequested;

    public string CaptureAreaShortcutText => "Ctrl + Alt + S";

    public string ToggleRecordingShortcutText => "Ctrl + Alt + R";

    public HotkeyService(ILogger<HotkeyService> logger, INotificationService notificationService)
    {
        _logger = logger;
        _notificationService = notificationService;
    }

    public void Initialize()
    {
        if (_source is not null)
        {
            return;
        }

        // "Message-only window": window tak terlihat yang hanya menerima pesan.
        var parameters = new HwndSourceParameters("iLand.HotkeyWindow")
        {
            ParentWindow = NativeMethods.HWND_MESSAGE,
            WindowStyle = 0
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);

        Register(CaptureAreaHotkeyId, VirtualKeyS, CaptureAreaShortcutText);
        Register(ToggleRecordingHotkeyId, VirtualKeyR, ToggleRecordingShortcutText);
    }

    /// <summary>Mendaftarkan satu pintasan Ctrl + Alt + tombol. Jika gagal, pengguna diberi tahu.</summary>
    private void Register(int id, uint virtualKey, string shortcutText)
    {
        bool registered = NativeMethods.RegisterHotKey(
            _source!.Handle,
            id,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT,
            virtualKey);

        if (registered)
        {
            _registeredIds.Add(id);
            _logger.LogInformation("Global shortcut registered: {Shortcut}", shortcutText);
            return;
        }

        int errorCode = Marshal.GetLastWin32Error();
        _logger.LogWarning("Could not register {Shortcut} (error {Code})", shortcutText, errorCode);

        _notificationService.Show(
            "Shortcut unavailable",
            $"{shortcutText} is used by another app.",
            NotificationKind.Warning);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != NativeMethods.WM_HOTKEY)
        {
            return IntPtr.Zero;
        }

        switch (wParam.ToInt32())
        {
            case CaptureAreaHotkeyId:
                handled = true;
                CaptureAreaRequested?.Invoke(this, EventArgs.Empty);
                break;

            case ToggleRecordingHotkeyId:
                handled = true;
                ToggleRecordingRequested?.Invoke(this, EventArgs.Empty);
                break;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_source is null)
        {
            return;
        }

        foreach (int id in _registeredIds)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, id);
        }

        _registeredIds.Clear();

        _source.RemoveHook(WndProc);
        _source.Dispose();
        _source = null;
    }
}
