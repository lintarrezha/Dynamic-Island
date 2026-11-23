#if DEBUG
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using iLand.Interfaces;
using iLand.Models;

namespace iLand.Helpers;

/// <summary>
/// HANYA build Debug. Menyalakan lalu mematikan setiap fitur Prompt 7 sebanyak 10 kali lewat pengaturan (seperti pengguna
/// menekan toggle), lalu mencatat jumlah thread, handle, dan memori sebelum dan sesudah tiap fitur. Fitur yang melepas event,
/// timer, dan objek COM dengan benar tidak akan membuat angkanya naik terus.
/// Jalankan (PowerShell):  $env:ILAND_LIFECYCLE_TEST = "1"; dotnet run --project src/iLand
/// Hasilnya ada di log dengan awalan "Lifecycle test". Tidak ada data pribadi yang dicatat. Nilai pengaturan asli dikembalikan di akhir.
/// Satu siklus pemanasan dijalankan lebih dulu per fitur agar biaya sekali pakai (JIT, thread pool) tidak terhitung sebagai kebocoran.
/// </summary>
public static class LifecycleDebugTest
{
    // Jumlah siklus bisa diubah lewat ILAND_LIFECYCLE_CYCLES (1 sampai 200). ILAND_LIFECYCLE_ONLY menjalankan hanya fitur
    // yang namanya mengandung kata itu (tanpa membedakan huruf besar kecil), misalnya "Volume".
    private static readonly int Cycles = ReadCycles();
    private const int StepDelayMs = 500;

    private sealed record Feature(string Name, Func<AppSettings, bool> Get, Action<AppSettings, bool> Set);

    private readonly record struct Snapshot(int Threads, int Handles, long ManagedKb, long PrivateMb);

    private static int ReadCycles()
    {
        string? text = Environment.GetEnvironmentVariable("ILAND_LIFECYCLE_CYCLES");
        return int.TryParse(text, out int value) ? Math.Clamp(value, 1, 200) : 10;
    }

    public static void RunIfRequested(IServiceProvider services)
    {
        if (Environment.GetEnvironmentVariable("ILAND_LIFECYCLE_TEST") != "1")
        {
            return;
        }

        // Dipanggil di UI thread, jadi await di bawah juga melanjutkan di UI thread.
        _ = RunAsync(services);
    }

    private static Snapshot Take()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        using Process process = Process.GetCurrentProcess();
        return new Snapshot(
            process.Threads.Count,
            process.HandleCount,
            GC.GetTotalMemory(false) / 1024,
            process.PrivateMemorySize64 / (1024 * 1024));
    }

    private static async Task Cycle(ISettingsService settingsService, Feature feature)
    {
        AppSettings settings = settingsService.Current;

        feature.Set(settings, true);
        settingsService.NotifyChanged();
        await Task.Delay(StepDelayMs);

        feature.Set(settings, false);
        settingsService.NotifyChanged();
        await Task.Delay(StepDelayMs);
    }

    private static async Task RunAsync(IServiceProvider services)
    {
        var settingsService = services.GetRequiredService<ISettingsService>();
        AppSettings settings = settingsService.Current;

        var features = new[]
        {
            new Feature("Volume and brightness overlay", s => s.ShowVolumeBrightnessOverlay, (s, v) => s.ShowVolumeBrightnessOverlay = v),
            new Feature("Charging animation", s => s.ChargingAnimationEnabled, (s, v) => s.ChargingAnimationEnabled = v),
            new Feature("Bluetooth device connected", s => s.BluetoothConnectedOverlay, (s, v) => s.BluetoothConnectedOverlay = v),
            new Feature("Caps Lock indicator", s => s.CapsLockOverlay, (s, v) => s.CapsLockOverlay = v),
            new Feature("Meeting mode", s => s.MeetingModeEnabled, (s, v) => s.MeetingModeEnabled = v)
        };

        await Task.Delay(6000);
        Snapshot overallStart = Take();
        Log.Information("Lifecycle test: begin threads={T} handles={H} managedKB={M} privateMB={P}",
            overallStart.Threads, overallStart.Handles, overallStart.ManagedKb, overallStart.PrivateMb);

        string? only = Environment.GetEnvironmentVariable("ILAND_LIFECYCLE_ONLY");

        foreach (Feature feature in features)
        {
            if (!string.IsNullOrWhiteSpace(only)
                && !feature.Name.Contains(only, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            bool original = feature.Get(settings);

            await Cycle(settingsService, feature); // pemanasan
            Snapshot before = Take();

            for (int i = 0; i < Cycles; i++)
            {
                await Cycle(settingsService, feature);
            }

            feature.Set(settings, original);
            settingsService.NotifyChanged();
            await Task.Delay(1500);

            Snapshot after = Take();
            Log.Information(
                "Lifecycle test: {Feature} x{Cycles}: threads {T0}->{T1}, handles {H0}->{H1}, managedKB {M0}->{M1}, privateMB {P0}->{P1}",
                feature.Name, Cycles,
                before.Threads, after.Threads, before.Handles, after.Handles,
                before.ManagedKb, after.ManagedKb, before.PrivateMb, after.PrivateMb);
        }

        Snapshot overallEnd = Take();
        Log.Information("Lifecycle test: end threads={T} handles={H} managedKB={M} privateMB={P}",
            overallEnd.Threads, overallEnd.Handles, overallEnd.ManagedKb, overallEnd.PrivateMb);
    }
}
#endif
