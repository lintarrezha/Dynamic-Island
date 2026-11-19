namespace iLand.Helpers;

/// <summary>Satu baris atribusi pihak ketiga di halaman About.</summary>
/// <param name="LicenseVerified">
/// True jika lisensinya sudah dicek ke halaman paket di nuget.org (atau sumber yang mengutipnya).
/// Jika false, About TIDAK menyebut lisensinya dan hanya mengarahkan ke halaman paket (supaya tidak menebak).
/// </param>
public sealed record ThirdPartyNotice(string Name, string Version, string License, bool LicenseVerified)
{
    public string Summary => LicenseVerified
        ? $"{Version}  -  {License}"
        : $"{Version}  -  see the package page on nuget.org for the license";
}

/// <summary>
/// Paket NuGet yang dipakai iLand (langsung dan turunan), dibaca dari obj/project.assets.json saat Tahap D ditulis.
/// PERBARUI daftar ini setiap kali versi paket di iLand.csproj berubah atau paket ditambah.
/// Baris dikelompokkan per proyek. Lisensi diverifikasi untuk paket utama tiap kelompok (lihat catatan di bawah).
/// </summary>
public static class ThirdPartyNotices
{
    public static IReadOnlyList<ThirdPartyNotice> All { get; } = new[]
    {
        new ThirdPartyNotice("CommunityToolkit.Mvvm", "8.4.2", "MIT", true),

        // Diverifikasi untuk H.NotifyIcon.Wpf. H.NotifyIcon dan H.GeneratedIcons.System.Drawing berasal dari proyek yang sama.
        new ThirdPartyNotice("H.NotifyIcon (Wpf, core, generated icons)", "2.4.1", "MIT", true),

        // Diverifikasi untuk Microsoft.Extensions.DependencyInjection (dan Abstractions). Logging, Options, Primitives dari proyek yang sama.
        new ThirdPartyNotice("Microsoft.Extensions (DependencyInjection, Logging, Options, Primitives)", "10.0.14", "MIT", true),

        // Diverifikasi untuk NAudio.Wasapi 3.3.0. NAudio.Core dari proyek yang sama.
        new ThirdPartyNotice("NAudio (Wasapi, Core)", "3.3.0", "MIT", true),

        // Diverifikasi untuk Serilog.Extensions.Logging 10.0.0 dan paket Serilog.
        new ThirdPartyNotice("Serilog and Serilog.Extensions.Logging", "4.2.0 and 10.0.0", "Apache-2.0", true),

        // Belum diverifikasi satu per satu: sengaja tidak menyebut lisensi.
        new ThirdPartyNotice("Serilog.Sinks.File", "7.0.0", string.Empty, false),
        // Diverifikasi: halaman paket System.Management di nuget.org menyebut lisensi MIT.
        new ThirdPartyNotice("System.Management", "10.0.11", "MIT", true),

        new ThirdPartyNotice("System.CodeDom", "10.0.x", string.Empty, false),
        new ThirdPartyNotice("System.Numerics.Tensors", "9.0.0", string.Empty, false)
    };
}
