using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using iLand.Interfaces;
using iLand.Models;
using iLand.Native;
using Microsoft.Win32;

namespace iLand.Services;

/// <summary>Mendaftar monitor lewat Windows API dan memberi tahu saat susunan monitor berubah.</summary>
public sealed class MonitorService : IMonitorService, IDisposable
{
    private readonly Dispatcher _uiDispatcher;

    public event EventHandler? MonitorsChanged;

    public MonitorService()
    {
        _uiDispatcher = Dispatcher.CurrentDispatcher; // dibuat di UI thread
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var raw = new List<(string Name, Rect Bounds, bool IsPrimary)>();

        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr hMonitor, IntPtr hdcMonitor, ref NativeMethods.NativeRect lprcMonitor, IntPtr dwData) =>
            {
                var info = new NativeMethods.MonitorInfoEx
                {
                    cbSize = Marshal.SizeOf<NativeMethods.MonitorInfoEx>()
                };

                if (NativeMethods.GetMonitorInfo(hMonitor, ref info))
                {
                    var bounds = new Rect(
                        info.rcMonitor.Left,
                        info.rcMonitor.Top,
                        info.rcMonitor.Right - info.rcMonitor.Left,
                        info.rcMonitor.Bottom - info.rcMonitor.Top);

                    bool isPrimary = (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0;
                    raw.Add((info.szDevice, bounds, isPrimary));
                }

                return true; // lanjut ke monitor berikutnya
            }, IntPtr.Zero);

        // Monitor utama di urutan pertama, sisanya dari kiri ke kanan.
        var sorted = raw.OrderByDescending(m => m.IsPrimary).ThenBy(m => m.Bounds.Left).ToList();

        var result = new List<MonitorInfo>();
        for (int i = 0; i < sorted.Count; i++)
        {
            var m = sorted[i];
            string label = $"Monitor {i + 1} ({m.Bounds.Width:0} x {m.Bounds.Height:0}"
                           + (m.IsPrimary ? ", primary)" : ")");
            result.Add(new MonitorInfo(m.Name, label, m.Bounds, m.IsPrimary));
        }

        return result;
    }

    public MonitorInfo GetMonitorOrPrimary(string? deviceName)
    {
        var monitors = GetMonitors();

        if (!string.IsNullOrEmpty(deviceName))
        {
            var match = monitors.FirstOrDefault(m =>
                string.Equals(m.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        // Cadangan: monitor utama, lalu monitor pertama, lalu ukuran layar dari WPF.
        return monitors.FirstOrDefault(m => m.IsPrimary)
               ?? monitors.FirstOrDefault()
               ?? new MonitorInfo(string.Empty, "Primary monitor",
                   new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight), true);
    }

    // Event ini datang dari thread lain, jadi pindahkan ke UI thread.
    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        _uiDispatcher.BeginInvoke(new Action(() => MonitorsChanged?.Invoke(this, EventArgs.Empty)));
    }

    public void Dispose()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
    }
}
