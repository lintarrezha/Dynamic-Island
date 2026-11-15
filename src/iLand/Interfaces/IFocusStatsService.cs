namespace iLand.Interfaces;

/// <summary>
/// Menghitung berapa sesi fokus Pomodoro yang selesai hari ini.
/// Data hanya berisi tanggal lokal dan satu angka, disimpan di %APPDATA%\iLand\timer-stats.json.
/// Tidak dikirim ke mana pun.
/// </summary>
public interface IFocusStatsService
{
    /// <summary>Jumlah sesi fokus selesai pada tanggal lokal hari ini. Pergantian hari dihitung saat dibaca, tanpa timer.</summary>
    int SessionsToday { get; }

    /// <summary>Dipanggil satu kali setiap tahap Fokus Pomodoro selesai (bukan saat di-reset).</summary>
    void RecordFocusSession();
}
