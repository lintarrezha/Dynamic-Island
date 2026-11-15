namespace iLand.Models;

/// <summary>Jenis penghitung waktu.</summary>
public enum TimerMode
{
    /// <summary>Timer hitung mundur (1, 5, 10 menit atau durasi sendiri).</summary>
    Countdown,

    /// <summary>Pomodoro: fokus 25 menit, istirahat 5 menit, istirahat panjang setiap 4 sesi.</summary>
    Pomodoro,

    /// <summary>Stopwatch: menghitung maju dari nol.</summary>
    Stopwatch
}

/// <summary>Status penghitung waktu saat ini.</summary>
public enum TimerStatus
{
    /// <summary>Belum dimulai.</summary>
    Idle,

    Running,

    Paused,

    /// <summary>Timer hitung mundur sudah habis.</summary>
    Completed
}

/// <summary>Tahap Pomodoro saat ini.</summary>
public enum PomodoroPhase
{
    Focus,
    ShortBreak,
    LongBreak
}
