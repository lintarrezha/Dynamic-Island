namespace iLand.Models;

/// <summary>Satu baris lirik tersinkron: baris ini menjadi baris aktif mulai dari waktu Time.</summary>
public sealed record LyricLine(TimeSpan Time, string Text);
