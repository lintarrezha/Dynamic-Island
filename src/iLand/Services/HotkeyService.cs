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
    private readonly ISettingsService _settingsService;
    private readonly List<int> _registeredIds = new();
    private HwndSource? _source;

    // Pintasan yang terakhir DIMINTA terdaftar. Dipakai agar pendaftaran (dan pesan "Shortcut unavailable" bila gagal)
    // hanya terjadi saat keadaannya berubah, bukan di setiap perubahan pengaturan lain.
    private bool _areaHotkeyWanted;
    private bool _recordingHotkeyWanted;
    private bool _recordingInProgress;

    public event EventHandler? CaptureAreaRequested;

    public event EventHandler? ToggleRecordingRequested;

    public string CaptureAreaShortcutText => "Ctrl + Alt + S";

    public string ToggleRecordingShortcutText => "Ctrl + Alt + R";

    public HotkeyService(
        ILogger<HotkeyService> logger,
        INotificationService notificationService,
        ISettingsService settingsService)
    {
        _logger = logger;
        _notificationService = notificationService;
        _settingsService = settingsService;
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

        // Pintasan capture hanya terdaftar selama tab Capture menyala (lihat ApplyHotkeys).
        _settingsService.SettingsChanged += OnSettingsChanged;
        ApplyHotkeys();
    }

    public void SetRecordingInProgress(bool inProgress)
    {
        if (_recordingInProgress == inProgress)
        {
            return;
        }

        _recordingInProgress = inProgress;
        ApplyHotkeys();
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        ApplyHotkeys();
    }

    /// <summary>
    /// Mendaftarkan atau melepas pintasan sesuai tab Capture. Ctrl + Alt + S ikut tab Capture.
    /// Ctrl + Alt + R ikut tab Capture, kecuali sedang merekam (supaya perekaman tetap bisa dihentikan dari keyboard).
    /// </summary>
    private void ApplyHotkeys()
    {
        if (_source is null)
        {
            return;
        }

        bool captureEnabled = _settingsService.Current.IsTabEnabled(IslandTab.Capture);

        SetHotkey(CaptureAreaHotkeyId, VirtualKeyS, CaptureAreaShortcutText, captureEnabled, ref _areaHotkeyWanted);
        SetHotkey(ToggleRecordingHotkeyId, VirtualKeyR, ToggleRecordingShortcutText,
            captureEnabled || _recordingInProgress, ref _recordingHotkeyWanted);
    }

    private void SetHotkey(int id, uint virtualKey, string shortcutText, bool wanted, ref bool currentlyWanted)
    {
        if (wanted == currentlyWanted)
        {
            return;
        }

        currentlyWanted = wanted;

        if (wanted)
        {
            Register(id, virtualKey, shortcutText);
        }
        else
        {
            Unregister(id, shortcutText);
        }
    }

    private void Unregister(int id, string shortcutText)
    {
        // Hanya melepas pintasan yang memang berhasil terdaftar.
        if (_source is not null && _registeredIds.Remove(id))
        {
            NativeMethods.UnregisterHotKey(_source.Handle, id);
            _logger.LogInformation("Global shortcut released: {Shortcut}", shortcutText);
        }
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
        _settingsService.SettingsChanged -= OnSettingsChanged;

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
