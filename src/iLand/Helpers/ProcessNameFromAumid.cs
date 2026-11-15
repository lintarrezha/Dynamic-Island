using System.IO;

namespace iLand.Helpers;

/// <summary>
/// Menebak nama proses dari SourceAppUserModelId milik SMTC, contoh "Spotify.exe",
/// "chrome.exe", "Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic".
/// Hanya tebakan: pencocokan dibuat longgar di AudioLevelService.
/// </summary>
internal static class ProcessNameFromAumid
{
    public static string? Guess(string? aumid)
    {
        if (string.IsNullOrWhiteSpace(aumid))
        {
            return null;
        }

        var id = aumid.Trim();

        // Aplikasi Store: "PackageFamily!AppId" -> pakai bagian sebelum '_' dari package.
        var bang = id.IndexOf('!');
        if (bang > 0)
        {
            id = id[..bang];
            var underscore = id.IndexOf('_');
            if (underscore > 0)
            {
                id = id[..underscore];
            }

            // "SpotifyAB.SpotifyMusic" -> "SpotifyMusic"
            var dot = id.LastIndexOf('.');
            if (dot >= 0 && dot < id.Length - 1)
            {
                id = id[(dot + 1)..];
            }

            return id;
        }

        // Path atau nama exe biasa.
        id = Path.GetFileName(id);
        if (id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            id = id[..^4];
        }

        return id.Length == 0 ? null : id;
    }
}
