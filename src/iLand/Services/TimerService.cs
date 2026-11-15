using iLand.Interfaces;
using iLand.Models;

namespace iLand.Services;

/// <summary>
/// Timer hitung mundur, Pomodoro, dan stopwatch.
///
/// Cara kerja waktu: kita TIDAK menghitung "tiap detik kurangi satu". Kita menyimpan waktu selesai
/// (_endTime) atau waktu mulai (_runStart), lalu menghitung selisihnya dengan jam saat ini.
/// Hasilnya selalu akurat walaupun aplikasi sempat lambat, dan timer tetap berjalan saat island
/// collapsed karena service ini hidup selama aplikasi berjalan.
///
/// Untuk memperbarui tampilan, service ini memakai detak jam yang sudah ada (IClockService),
/// jadi tidak membuat timer baru.
/// </summary>
public sealed class TimerService : ITimerService, IDisposable
{
    // Pengaturan Pomodoro (belum bisa diubah di Settings, nanti bisa ditambahkan).
    private const int FocusMinutes = 25;
    private const int ShortBreakMinutes = 5;
    private const int LongBreakMinutes = 15;
    private const int SessionsBeforeLongBreak = 4;

    private const int MinCountdownMinutes = 1;
    private const int MaxCountdownMinutes = 180;

    private readonly IClockService _clockService;
    private readonly INotificationService _notificationService;
    private readonly IFocusStatsService _statsService;

    private int _countdownMinutes = 5;
    private int _completedFocusSessions;

    // Untuk Timer dan Pomodoro (hitung mundur).
    private TimeSpan _remainingWhenNotRunning;
    private DateTimeOffset _endTime;

    // Untuk Stopwatch (hitung maju).
    private TimeSpan _elapsedWhenNotRunning;
    private DateTimeOffset _runStart;

    public TimerMode Mode { get; private set; } = TimerMode.Countdown;

    public TimerStatus Status { get; private set; } = TimerStatus.Idle;

    public PomodoroPhase Phase { get; private set; } = PomodoroPhase.Focus;

    public int CountdownMinutes => _countdownMinutes;

    public int PomodoroSessionsPerCycle => SessionsBeforeLongBreak;

    public int PomodoroFocusMinutes => FocusMinutes;

    public bool IsActive => Status is TimerStatus.Running or TimerStatus.Paused;

    public event EventHandler? Changed;

    public event EventHandler? PhaseCompleted;

    public TimeSpan TotalTime => Mode == TimerMode.Stopwatch ? TimeSpan.Zero : GetPlannedDuration();

    public int FocusSessionsToday => _statsService.SessionsToday;

    public TimerService(IClockService clockService, INotificationService notificationService, IFocusStatsService statsService)
    {
        _clockService = clockService;
        _notificationService = notificationService;
        _statsService = statsService;

        _clockService.Ticked += OnClockTicked;
    }

    public int PomodoroSessionNumber
    {
        get
        {
            // Saat fokus: sesi yang sedang berjalan. Saat istirahat: sesi yang baru selesai.
            int number = Phase == PomodoroPhase.Focus
                ? _completedFocusSessions % SessionsBeforeLongBreak + 1
                : (_completedFocusSessions - 1) % SessionsBeforeLongBreak + 1;
            return Math.Max(number, 1);
        }
    }

    public TimeSpan DisplayTime
    {
        get
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;

            if (Mode == TimerMode.Stopwatch)
            {
                TimeSpan elapsed = Status switch
                {
                    TimerStatus.Running => now - _runStart,
                    TimerStatus.Paused => _elapsedWhenNotRunning,
                    _ => TimeSpan.Zero
                };
                return TimeSpan.FromSeconds(Math.Floor(Math.Max(elapsed.TotalSeconds, 0)));
            }

            TimeSpan remaining = Status switch
            {
                TimerStatus.Running => _endTime - now,
                TimerStatus.Paused => _remainingWhenNotRunning,
                TimerStatus.Completed => TimeSpan.Zero,
                _ => GetPlannedDuration()
            };

            // Dibulatkan ke atas agar angka 00:00 baru muncul saat waktu benar-benar habis.
            return TimeSpan.FromSeconds(Math.Ceiling(Math.Max(remaining.TotalSeconds, 0)));
        }
    }

    // ---- Pengaturan ----

    public void SetMode(TimerMode mode)
    {
        if (IsActive || mode == Mode)
        {
            return; // tidak boleh ganti jenis saat berjalan (pengguna harus Reset dulu)
        }

        Mode = mode;
        ResetState();
        RaiseChanged();
    }

    public void SetCountdownMinutes(int minutes)
    {
        if (IsActive)
        {
            return;
        }

        _countdownMinutes = Math.Clamp(minutes, MinCountdownMinutes, MaxCountdownMinutes);
        Status = TimerStatus.Idle; // jika sebelumnya Completed, kembali siap dimulai
        RaiseChanged();
    }

    public void AdjustCountdownMinutes(int deltaMinutes)
    {
        SetCountdownMinutes(_countdownMinutes + deltaMinutes);
    }

    // ---- Kontrol ----

    public void StartOrResume()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        switch (Status)
        {
            case TimerStatus.Running:
                return;

            case TimerStatus.Paused:
                // Lanjutkan dari sisa waktu / waktu berjalan yang tersimpan.
                _endTime = now + _remainingWhenNotRunning;
                _runStart = now - _elapsedWhenNotRunning;
                break;

            default: // Idle atau Completed: mulai baru
                _remainingWhenNotRunning = GetPlannedDuration();
                _endTime = now + _remainingWhenNotRunning;
                _elapsedWhenNotRunning = TimeSpan.Zero;
                _runStart = now;
                break;
        }

        Status = TimerStatus.Running;
        RaiseChanged();
    }

    public void Pause()
    {
        if (Status != TimerStatus.Running)
        {
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        _remainingWhenNotRunning = _endTime - now;
        _elapsedWhenNotRunning = now - _runStart;

        Status = TimerStatus.Paused;
        RaiseChanged();
    }

    public void Reset()
    {
        ResetState();
        RaiseChanged();
    }

    private void ResetState()
    {
        Status = TimerStatus.Idle;
        Phase = PomodoroPhase.Focus;
        _completedFocusSessions = 0;
        _remainingWhenNotRunning = TimeSpan.Zero;
        _elapsedWhenNotRunning = TimeSpan.Zero;
    }

    // ---- Detak jam: perbarui tampilan dan cek apakah waktu habis ----

    private void OnClockTicked(object? sender, EventArgs e)
    {
        if (Status != TimerStatus.Running)
        {
            return;
        }

        if (Mode != TimerMode.Stopwatch && DateTimeOffset.UtcNow >= _endTime)
        {
            HandleCompletion();
            return;
        }

        RaiseChanged(); // perbarui angka di layar
    }

    private void HandleCompletion()
    {
        if (Mode == TimerMode.Countdown)
        {
            Status = TimerStatus.Completed;
            _remainingWhenNotRunning = TimeSpan.Zero;

            string unit = _countdownMinutes == 1 ? "minute" : "minutes";
            _notificationService.Show("Timer finished", $"Your {_countdownMinutes} {unit} timer is done.", NotificationKind.Success);
        }
        else
        {
            CompletePomodoroPhase();
        }

        RaiseChanged();
        PhaseCompleted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Selesai satu tahap Pomodoro: umumkan, lalu langsung mulai tahap berikutnya.</summary>
    private void CompletePomodoroPhase()
    {
        if (Phase == PomodoroPhase.Focus)
        {
            _completedFocusSessions++;
            _statsService.RecordFocusSession(); // hitungan harian, hanya untuk tahap Fokus yang benar-benar selesai
            bool isLongBreak = _completedFocusSessions % SessionsBeforeLongBreak == 0;
            Phase = isLongBreak ? PomodoroPhase.LongBreak : PomodoroPhase.ShortBreak;

            _notificationService.Show(
                "Focus session complete",
                isLongBreak ? "Great work! Take a long break." : "Nice! Time for a short break.",
                NotificationKind.Success);
        }
        else
        {
            Phase = PomodoroPhase.Focus;

            _notificationService.Show("Break is over", "Ready to focus again?", NotificationKind.Info);
        }

        // Tahap berikutnya berjalan otomatis.
        _remainingWhenNotRunning = GetPlannedDuration();
        _endTime = DateTimeOffset.UtcNow + _remainingWhenNotRunning;
    }

    // ---- Fungsi bantu ----

    private TimeSpan GetPlannedDuration()
    {
        return Mode switch
        {
            TimerMode.Countdown => TimeSpan.FromMinutes(_countdownMinutes),
            TimerMode.Pomodoro => TimeSpan.FromMinutes(Phase switch
            {
                PomodoroPhase.Focus => FocusMinutes,
                PomodoroPhase.ShortBreak => ShortBreakMinutes,
                _ => LongBreakMinutes
            }),
            _ => TimeSpan.Zero
        };
    }

    private void RaiseChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _clockService.Ticked -= OnClockTicked;
    }
}
