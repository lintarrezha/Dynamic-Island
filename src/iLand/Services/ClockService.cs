using System.Windows.Threading;
using iLand.Interfaces;
using Microsoft.Extensions.Logging;

namespace iLand.Services;

/// <summary>
/// Menyediakan "detak" setiap detik. Memakai DispatcherTimer
/// agar event berjalan di UI thread dan aman dipakai untuk update tampilan.
/// </summary>
public sealed class ClockService : IClockService, IDisposable
{
    private readonly DispatcherTimer _timer;

    public event EventHandler? Ticked;

    public DateTime Now => DateTime.Now;

    public ClockService(ILogger<ClockService> logger)
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTimerTick;
        _timer.Start();

        logger.LogDebug("ClockService started");
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        Ticked?.Invoke(this, EventArgs.Empty);
    }

    // Timer dan event harus dibersihkan supaya tidak terjadi memory leak.
    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
    }
}
