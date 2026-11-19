namespace iLand.Models;

public enum BatterySaverState
{
    /// <summary>Tidak bisa dipakai sekarang (perangkat tanpa baterai, atau sedang dicolok ke listrik).</summary>
    Unavailable,
    Off,
    On
}
