using System.Reflection;
using System.Windows;
using iLand.Helpers;
using iLand.Interfaces;
using iLand.Services;
using iLand.ViewModels;
using iLand.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace iLand;

/// <summary>
/// Titik masuk aplikasi. Tugasnya:
/// 1. Mencegah aplikasi berjalan dua kali (single instance).
/// 2. Menyiapkan logging.
/// 3. Menyiapkan Dependency Injection (DI).
/// 4. Menampilkan window pertama.
/// 5. Menangkap error yang tidak tertangani, lalu mencatatnya ke log.
/// </summary>
public partial class App : Application
{
    // Nama mutex harus unik. Mutex dipakai Windows sebagai "penanda" bahwa iLand sedang berjalan.
    private const string MutexName = "iLand.SingleInstance.Mutex";

    private Mutex? _singleInstanceMutex;
    private bool _ownsMutex;
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1. Cek apakah iLand sudah berjalan.
        _singleInstanceMutex = new Mutex(initiallyOwned: true, MutexName, out bool isFirstInstance);
        _ownsMutex = isFirstInstance;

        if (!isFirstInstance)
        {
            // Sudah ada iLand yang berjalan, jadi instance kedua langsung ditutup.
            Shutdown();
            return;
        }

        // 2. Siapkan folder data dan logging.
        AppPaths.EnsureFoldersExist();
        ConfigureSerilog();

        // 3. Daftarkan global exception handler supaya error tercatat di log.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // 4. Bangun DI container.
        _serviceProvider = BuildServiceProvider();

        var logger = _serviceProvider.GetRequiredService<ILogger<App>>();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        logger.LogInformation("iLand starting. Version {Version}", version);

        // 5. Minta DI membuat window beserta semua dependency-nya, lalu tampilkan.
        var window = _serviceProvider.GetRequiredService<IslandWindow>();
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // DI container ikut membersihkan semua service yang IDisposable (ClockService, IslandViewModel).
        _serviceProvider?.Dispose();

        // Pastikan semua log tertulis ke file sebelum aplikasi benar-benar tutup.
        Log.CloseAndFlush();

        if (_ownsMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }
        _singleInstanceMutex?.Dispose();

        base.OnExit(e);
    }

    /// <summary>Mengatur Serilog agar menulis log ke file di %APPDATA%\iLand\logs.</summary>
    private static void ConfigureSerilog()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                AppPaths.LogFilePattern,
                rollingInterval: RollingInterval.Day,   // satu file per hari
                retainedFileCountLimit: 7)              // simpan 7 hari terakhir
            .CreateLogger();
    }

    /// <summary>
    /// Daftar semua service, ViewModel, dan Window di sini.
    /// Setiap fitur baru nanti didaftarkan di method ini.
    /// </summary>
    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        // Logging: pakai ILogger<T> bawaan Microsoft, hasilnya ditulis oleh Serilog.
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(Log.Logger, dispose: false);
        });

        // Services (satu instance dipakai bersama = Singleton).
        services.AddSingleton<IClockService, ClockService>();

        // ViewModels
        services.AddSingleton<IslandViewModel>();

        // Views
        services.AddSingleton<IslandWindow>();

        return services.BuildServiceProvider();
    }

    // ---- Penangkap error yang tidak tertangani ----

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled UI exception");
        MessageBox.Show(
            "Terjadi kesalahan tak terduga. Detail tersimpan di file log.\n\n" + e.Exception.Message,
            "iLand",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true; // aplikasi tidak langsung crash
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.ExceptionObject as Exception, "Unhandled domain exception");
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unobserved task exception");
        e.SetObserved();
    }
}
