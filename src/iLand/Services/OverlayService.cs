using System.Windows.Threading;
using iLand.Helpers;
using iLand.Interfaces;
using iLand.Models;

namespace iLand.Services;

/// <summary>
/// Antrean overlay island. Lihat IOverlayService untuk aturan prioritas.
/// Biaya: satu DispatcherTimer sekali-jalan yang hanya berjalan selama ada overlay yang tampil atau jeda antar overlay.
/// Tidak ada polling dan tidak ada event Windows. Semua keadaan hanya disentuh di UI thread.
/// </summary>
public sealed class OverlayService : IOverlayService, IDisposable
{
    private const int MaxPending = 4;
    private const int MinDurationMs = 300;

    private enum TimerMode
    {
        None,
        Showing,
        Gap
    }

    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _timer;
    private readonly List<OverlayRequest> _pending = new();

    private OverlayRequest? _current;
    private TimerMode _mode;
    private bool _held;
    private bool _suppressed;
    private bool _disposed;
    private volatile bool _meetingMode;

    public OverlayService()
    {
        _ui = Dispatcher.CurrentDispatcher; // service ini dibuat di UI thread
        _timer = new DispatcherTimer(DispatcherPriority.Normal, _ui);
        _timer.Tick += OnTimerTick;
    }

    public OverlayRequest? Current => _current;

    public event EventHandler? Changed;

    public bool IsMeetingMode
    {
        get => _meetingMode;
        set => _meetingMode = value;
    }

    public bool IsSuppressed
    {
        get => _suppressed;
        set
        {
            if (_suppressed == value)
            {
                return;
            }

            _suppressed = value;
            Serilog.Log.Information("Overlay suppressed={Suppressed}", value);
            if (value)
            {
                Clear();
            }
        }
    }

    public void Show(OverlayRequest request)
    {
        if (!_ui.CheckAccess())
        {
            _ui.BeginInvoke(new Action(() => Show(request)));
            return;
        }

        if (_disposed || _suppressed)
        {
            return;
        }

        // Mode rapat: peringatan, charging, dan perangkat ditahan kecuali yang kritis.
        if (_meetingMode && !request.IsCritical && request.Priority >= OverlayPriority.Device)
        {
            return;
        }

        if (_current is null)
        {
            // Jeda antar overlay sedang berjalan: yang boleh antre ikut antre agar urutan prioritasnya benar.
            if (_mode == TimerMode.Gap && request.IsQueueable)
            {
                Enqueue(request, atFront: false);
                return;
            }

            Present(request);
            return;
        }

        // Overlay yang sama: perbarui di tempat. Bila pointer sedang menahannya, timer tetap berhenti.
        if (request.Key == _current.Key)
        {
            _current = request;
            if (!_held)
            {
                StartTimer(request.DurationMs, TimerMode.Showing);
            }

            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (request.Priority > _current.Priority)
        {
            // Yang digantikan menunggu giliran bila boleh antre (misalnya "perangkat tersambung" yang disela charging).
            if (_current.IsQueueable)
            {
                Enqueue(_current, atFront: true);
            }

            Present(request);
            return;
        }

        if (request.Priority == _current.Priority && !request.IsQueueable)
        {
            Present(request); // misalnya volume lalu kecerahan: yang terbaru langsung menggantikan
            return;
        }

        if (request.IsQueueable)
        {
            Enqueue(request, atFront: false);
        }

        // Selain itu (prioritas lebih rendah dan tidak boleh antre): dibuang.
    }

    public void Dismiss()
    {
        if (!_ui.CheckAccess())
        {
            _ui.BeginInvoke(new Action(Dismiss));
            return;
        }

        if (_current is not null)
        {
            EndCurrent();
        }
    }

    public void Hold()
    {
        if (_current is null || _mode != TimerMode.Showing)
        {
            return;
        }

        _timer.Stop();
        _held = true;
    }

    public void Release(int remainingMs)
    {
        if (_current is null || !_held)
        {
            return;
        }

        _held = false;
        StartTimer(remainingMs, TimerMode.Showing);
    }

    public void Clear()
    {
        if (!_ui.CheckAccess())
        {
            _ui.BeginInvoke(new Action(Clear));
            return;
        }

        bool hadCurrent = _current is not null;
        _pending.Clear();
        StopTimer();
        _current = null;
        _held = false;

        if (hadCurrent)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        _pending.Clear();
        _current = null;
    }

    // ---- Dalam ----

    private void Present(OverlayRequest request)
    {
        _current = request;
        _held = false;
        StartTimer(request.DurationMs, TimerMode.Showing);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void EndCurrent()
    {
        _current = null;
        _held = false;

        if (_pending.Count > 0)
        {
            StartTimer(MotionTokens.OverlayGapMs, TimerMode.Gap);
        }
        else
        {
            StopTimer();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Enqueue(OverlayRequest request, bool atFront)
    {
        int existing = _pending.FindIndex(p => p.Key == request.Key);
        if (existing >= 0)
        {
            _pending[existing] = request; // overlay yang sama menunggu: perbarui isinya
        }
        else if (atFront)
        {
            _pending.Insert(0, request);
        }
        else
        {
            _pending.Add(request);
        }

        // Urut menurut prioritas (tinggi lebih dulu). OrderByDescending stabil, jadi yang lebih lama tetap lebih dulu.
        List<OverlayRequest> sorted = _pending.OrderByDescending(p => (int)p.Priority).ToList();
        _pending.Clear();
        _pending.AddRange(sorted);

        while (_pending.Count > MaxPending)
        {
            _pending.RemoveAt(_pending.Count - 1);
        }
    }

    private void StartTimer(int milliseconds, TimerMode mode)
    {
        _timer.Stop();
        _mode = mode;
        _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(milliseconds, MinDurationMs));
        _timer.Start();
    }

    private void StopTimer()
    {
        _timer.Stop();
        _mode = TimerMode.None;
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        _timer.Stop();

        if (_mode == TimerMode.Showing)
        {
            EndCurrent();
        }
        else if (_mode == TimerMode.Gap)
        {
            _mode = TimerMode.None;
            if (_pending.Count > 0)
            {
                OverlayRequest next = _pending[0];
                _pending.RemoveAt(0);
                Present(next);
            }
        }
    }
}
