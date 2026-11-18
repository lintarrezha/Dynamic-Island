namespace iLand.Interfaces;

/// <summary>Pintasan keyboard global (berlaku di aplikasi mana pun).</summary>
public interface IHotkeyService
{
    /// <summary>Dipanggil saat pintasan screenshot area ditekan.</summary>
    event EventHandler? CaptureAreaRequested;

    /// <summary>Dipanggil saat pintasan mulai atau berhenti merekam layar ditekan.</summary>
    event EventHandler? ToggleRecordingRequested;

    /// <summary>Teks pintasan screenshot area untuk ditampilkan di UI.</summary>
    string CaptureAreaShortcutText { get; }

    /// <summary>Teks pintasan rekam layar untuk ditampilkan di UI.</summary>
    string ToggleRecordingShortcutText { get; }

    /// <summary>Daftarkan pintasan ke Windows. Panggil sekali saat aplikasi mulai.</summary>
    void Initialize();

    /// <summary>
    /// Diberi tahu oleh RecordingService saat perekaman mulai atau berhenti.
    /// Selama merekam, pintasan Ctrl + Alt + R tetap terdaftar (untuk berhenti) walau tab Capture dimatikan.
    /// </summary>
    void SetRecordingInProgress(bool inProgress);
}
