using iLand.Models;

namespace iLand.Interfaces;

public interface ITimerService
{
    TimerMode Mode { get; }

    TimerStatus Status { get; }

    /// <summary>True jika sedang berjalan atau di-pause (belum direset).</summary>
    bool IsActive { get; }

    /// <summary>
    /// Waktu yang ditampilkan: sisa waktu (Timer dan Pomodoro) atau waktu berjalan (Stopwatch).
    /// Saat Idle: durasi yang akan dimulai.
    /// </summary>
    TimeSpan DisplayTime { get; }

    /// <summary>Durasi timer hitung mundur yang dipilih, dalam menit.</summary>
    int CountdownMinutes { get; }

    PomodoroPhase Phase { get; }

    /// <summary>Lama satu sesi fokus Pomodoro dalam menit (dipakai teks "Start 25 min" di tab Home).</summary>
    int PomodoroFocusMinutes { get; }

    /// <summary>Nomor sesi fokus dalam satu siklus (1 sampai PomodoroSessionsPerCycle).</summary>
    int PomodoroSessionNumber { get; }

    int PomodoroSessionsPerCycle { get; }

    /// <summary>
    /// Durasi penuh tahap yang sedang berjalan atau akan dimulai (dasar progress cincin).
    /// Stopwatch tidak punya durasi total, nilainya nol.
    /// </summary>
    TimeSpan TotalTime { get; }

    /// <summary>Jumlah sesi fokus Pomodoro yang selesai hari ini (tanggal lokal).</summary>
    int FocusSessionsToday { get; }

    /// <summary>Dipanggil saat status atau waktu yang ditampilkan berubah (tiap detik saat berjalan).</summary>
    event EventHandler? Changed;

    /// <summary>Dipanggil satu kali saat Timer habis atau satu tahap Pomodoro selesai (dipakai untuk denyut cincin).</summary>
    event EventHandler? PhaseCompleted;

    /// <summary>Ganti jenis penghitung. Hanya berlaku saat tidak berjalan.</summary>
    void SetMode(TimerMode mode);

    void SetCountdownMinutes(int minutes);

    void AdjustCountdownMinutes(int deltaMinutes);

    /// <summary>Mulai (jika belum jalan) atau lanjutkan (jika di-pause).</summary>
    void StartOrResume();

    void Pause();

    void Reset();
}
