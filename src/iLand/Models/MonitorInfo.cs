using System.Windows;

namespace iLand.Models;

/// <summary>
/// Satu monitor. Bounds dalam PIKSEL layar (bukan DIP WPF).
/// DeviceName contoh: \\.\DISPLAY1. DeviceName kosong dipakai untuk pilihan "Primary monitor".
/// </summary>
public sealed record MonitorInfo(string DeviceName, string Label, Rect Bounds, bool IsPrimary);
