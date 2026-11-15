using System.Text.RegularExpressions;

namespace iLand.Helpers;

/// <summary>
/// Membersihkan judul dan artis dari pemutar media sebelum dicari di LRCLIB, supaya "Song (feat. X) - Remastered 2011"
/// dan "Song (Official Video)" cocok dengan "Song". Hanya membuang bagian yang jelas bukan bagian judul.
/// </summary>
public static class TrackNameCleaner
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.Compiled;

    // "(feat. X)", "[ft. X]", "(featuring X)"
    private static readonly Regex FeatInBrackets = new(
        @"\s*[\(\[]\s*(?:feat\.?|ft\.?|featuring)\s[^\)\]]*[\)\]]", Options);

    // "Song feat. X" atau "Song - feat. X" sampai akhir teks
    private static readonly Regex FeatTrailing = new(
        @"\s+(?:-\s*)?(?:feat\.?|ft\.?|featuring)\s+.+$", Options);

    // "(Official Video)", "[Lyrics]", "(Audio)", "[HD]", "[4K]", dan sejenisnya
    private static readonly Regex BracketNoise = new(
        @"\s*[\(\[]\s*(?:official\s+(?:music\s+)?(?:video|audio|lyric\s+video|visualizer)|lyric\s+video|lyrics?|audio|visualizer|hd|hq|4k|mv|m/v)\s*[\)\]]", Options);

    // "(Remastered 2011)", "[2009 Remaster]", "(Remaster)"
    private static readonly Regex BracketRemaster = new(
        @"\s*[\(\[][^\)\]]*remaster(?:ed)?[^\)\]]*[\)\]]", Options);

    // "Song - Remastered 2011", "Song - 2011 Remaster", "Song - Official Video", "Song - Lyrics"
    private static readonly Regex DashSuffix = new(
        @"\s+-\s+(?:\d{4}\s+)?(?:remaster(?:ed)?(?:\s+version)?(?:\s+\d{4})?|official\s+.+|lyrics?|lyric\s+video)\s*$", Options);

    // Artis YouTube Music: "Nama Artis - Topic"
    private static readonly Regex TopicSuffix = new(@"\s*-\s*Topic\s*$", Options);

    private static readonly Regex ManySpaces = new(@"\s{2,}", RegexOptions.Compiled);

    /// <summary>Mengembalikan judul dan artis yang sudah dibersihkan. Hasilnya tidak pernah null.</summary>
    public static (string Title, string Artist) Clean(string? title, string? artist)
    {
        string cleanTitle = (title ?? string.Empty).Trim();
        string cleanArtist = TopicSuffix.Replace((artist ?? string.Empty).Trim(), string.Empty).Trim();

        cleanTitle = FeatInBrackets.Replace(cleanTitle, string.Empty);
        cleanTitle = BracketNoise.Replace(cleanTitle, string.Empty);
        cleanTitle = BracketRemaster.Replace(cleanTitle, string.Empty);
        cleanTitle = DashSuffix.Replace(cleanTitle, string.Empty);
        cleanTitle = FeatTrailing.Replace(cleanTitle, string.Empty);
        cleanTitle = ManySpaces.Replace(cleanTitle, " ").Trim();

        // Video musik sering berjudul "Artis - Lagu". Pecah hanya jika sisi kiri cocok dengan artis dari pemutar
        // (atau artis kosong), supaya judul seperti "Hello - Goodbye" dari artis lain tidak salah dipotong.
        int dash = cleanTitle.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0)
        {
            string left = cleanTitle[..dash].Trim();
            string right = cleanTitle[(dash + 3)..].Trim();

            bool leftIsArtist = cleanArtist.Length == 0
                                || left.Contains(cleanArtist, StringComparison.OrdinalIgnoreCase)
                                || cleanArtist.Contains(left, StringComparison.OrdinalIgnoreCase);

            if (left.Length > 0 && right.Length > 0 && leftIsArtist)
            {
                cleanArtist = left;
                cleanTitle = right;
            }
        }

        return (cleanTitle, cleanArtist);
    }

    /// <summary>"A, B &amp; C" menjadi "A". Dipakai untuk pencarian cadangan yang lebih longgar. Jika tidak ada pemisah, artis dikembalikan apa adanya.</summary>
    public static string FirstArtist(string artist)
    {
        int separator = artist.IndexOfAny(new[] { ',', ';', '&' });
        if (separator <= 0)
        {
            return artist;
        }

        string first = artist[..separator].Trim();
        return first.Length > 0 ? first : artist;
    }
}
