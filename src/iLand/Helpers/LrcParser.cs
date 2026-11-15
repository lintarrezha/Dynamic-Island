using System.Text.RegularExpressions;
using iLand.Models;

namespace iLand.Helpers;

/// <summary>
/// Membaca lirik berformat LRC, misalnya "[01:23.45] teks baris". Mendukung beberapa penanda waktu dalam satu
/// baris ("[00:10.00][01:20.00] teks"), pecahan detik 1 sampai 3 digit, dan membuang penanda kata "&lt;00:12.34&gt;".
/// Baris metadata seperti [ar:...] atau [ti:...] dan [offset:...] diabaikan (offset tidak diterapkan).
/// </summary>
public static class LrcParser
{
    // [mm:ss], [mm:ss.xx], [mm:ss.xxx] (titik dua juga diterima sebagai pemisah pecahan).
    private static readonly Regex TimeTag = new(
        @"\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]",
        RegexOptions.Compiled);

    // Penanda waktu per kata (LRC "enhanced"): dibuang dari teks.
    private static readonly Regex WordTag = new(
        @"<\d{1,3}:\d{1,2}(?:[.:]\d{1,3})?>",
        RegexOptions.Compiled);

    /// <summary>Mengubah teks LRC menjadi daftar baris terurut menurut waktu. Teks kosong atau tidak valid menghasilkan daftar kosong.</summary>
    public static IReadOnlyList<LyricLine> Parse(string? lrc)
    {
        if (string.IsNullOrWhiteSpace(lrc))
        {
            return Array.Empty<LyricLine>();
        }

        var lines = new List<LyricLine>();

        foreach (string rawLine in lrc.Split('\n'))
        {
            string line = rawLine.Trim(); // Trim juga membuang '\r'
            if (line.Length == 0 || line[0] != '[')
            {
                continue;
            }

            // Kumpulkan penanda waktu yang berderet di awal baris.
            var times = new List<TimeSpan>();
            int textStart = 0;
            foreach (Match match in TimeTag.Matches(line))
            {
                if (match.Index != textStart)
                {
                    break;
                }

                times.Add(ToTime(match));
                textStart = match.Index + match.Length;
            }

            if (times.Count == 0)
            {
                continue; // baris metadata atau bukan baris lirik
            }

            string text = WordTag.Replace(line[textStart..], string.Empty).Trim();
            foreach (TimeSpan time in times)
            {
                lines.Add(new LyricLine(time, text));
            }
        }

        // OrderBy bersifat stabil: baris dengan waktu sama tetap berurutan seperti di file.
        return lines.OrderBy(l => l.Time).ToList();
    }

    /// <summary>
    /// Mencari indeks baris yang sedang aktif pada posisi tertentu (binary search): baris terakhir yang waktunya
    /// sudah lewat. Mengembalikan -1 jika posisi masih sebelum baris pertama. Daftar harus terurut (hasil Parse).
    /// </summary>
    public static int FindLineIndex(IReadOnlyList<LyricLine> lines, TimeSpan position)
    {
        int low = 0;
        int high = lines.Count - 1;
        int result = -1;

        while (low <= high)
        {
            int mid = low + (high - low) / 2;
            if (lines[mid].Time <= position)
            {
                result = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return result;
    }

    private static TimeSpan ToTime(Match match)
    {
        int minutes = int.Parse(match.Groups[1].Value);
        int seconds = int.Parse(match.Groups[2].Value);

        double fraction = 0;
        Group fractionGroup = match.Groups[3];
        if (fractionGroup.Success)
        {
            // "93" = 0,93 detik, "5" = 0,5 detik, "930" = 0,930 detik.
            fraction = int.Parse(fractionGroup.Value) / Math.Pow(10, fractionGroup.Value.Length);
        }

        return TimeSpan.FromSeconds(minutes * 60 + seconds + fraction);
    }
}
