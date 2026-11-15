using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;

namespace iLand.Helpers;

/// <summary>
/// Mencari ikon aplikasi pemutar media dari identitas yang diberikan Windows (SourceAppUserModelId),
/// misalnya "Spotify.exe" atau "SpotifyAB.SpotifyMusic_xxxx!Spotify".
///
/// Caranya: ambil nama proses dari identitas itu, cari proses yang berjalan, lalu ambil ikon dari file exe-nya
/// lewat FileIconHelper. Ikon diambil dari aplikasi itu sendiri, tidak ada logo yang dibundel.
///
/// Tidak selalu berhasil (misalnya Firefox memakai identitas berupa kode acak, atau proses berjalan sebagai
/// administrator). Jika gagal hasilnya null, dan pemanggil menampilkan ikon nada sebagai pengganti.
/// Hasil (termasuk gagal) disimpan per identitas, jadi pencarian proses tidak diulang setiap lagu berganti.
/// Aman dipanggil dari thread latar: gambar hasilnya sudah dibekukan (Freeze).
/// </summary>
public static class AppIconResolver
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    public static ImageSource? GetIcon(string appUserModelId)
    {
        if (string.IsNullOrWhiteSpace(appUserModelId))
        {
            return null;
        }

        lock (Gate)
        {
            if (Cache.TryGetValue(appUserModelId, out ImageSource? cached))
            {
                return cached;
            }
        }

        ImageSource? icon = null;
        try
        {
            string? processName = GetProcessName(appUserModelId);
            string? exePath = processName is null ? null : FindExecutablePath(processName);
            icon = exePath is null ? null : FileIconHelper.GetIcon(exePath);
        }
        catch (Exception)
        {
            icon = null; // apa pun yang gagal di sini tidak boleh mengganggu tampilan musik
        }

        lock (Gate)
        {
            Cache[appUserModelId] = icon;
        }

        return icon;
    }

    /// <summary>"Paket!App" menjadi "App", lalu akhiran ".exe" dibuang.</summary>
    private static string? GetProcessName(string appUserModelId)
    {
        string name = appUserModelId.Trim();

        int separatorIndex = name.LastIndexOf('!');
        if (separatorIndex >= 0 && separatorIndex < name.Length - 1)
        {
            name = name[(separatorIndex + 1)..];
        }

        // Jika berupa path lengkap, ambil nama file saja.
        if (name.Contains('\\') || name.Contains('/'))
        {
            name = Path.GetFileName(name);
        }

        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        return name.Length == 0 ? null : name;
    }

    private static string? FindExecutablePath(string processName)
    {
        Process[] processes = Process.GetProcessesByName(processName);
        string? path = null;

        foreach (Process process in processes)
        {
            try
            {
                if (path is null)
                {
                    path = process.MainModule?.FileName;
                }
            }
            catch (Win32Exception)
            {
                // Akses ditolak (proses administrator) atau proses sudah selesai: coba proses berikutnya.
            }
            catch (InvalidOperationException)
            {
            }
            finally
            {
                process.Dispose(); // objek Process wajib dilepas, termasuk yang tidak dipakai
            }
        }

        return path;
    }
}
