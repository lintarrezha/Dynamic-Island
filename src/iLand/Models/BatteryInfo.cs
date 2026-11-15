namespace iLand.Models;

/// <summary>
/// Potret kondisi baterai pada satu waktu. "record" membuat dua objek dengan isi sama
/// dianggap sama, jadi mudah mengecek apakah ada yang berubah.
/// </summary>
public sealed record BatteryInfo(bool HasBattery, int Percent, bool IsCharging, bool IsPluggedIn)
{
    /// <summary>Baterai rendah: 20% ke bawah dan tidak sedang dicolok.</summary>
    public bool IsLow => HasBattery && !IsPluggedIn && Percent <= 20;

    /// <summary>Baterai penuh: 100% dan charger terpasang.</summary>
    public bool IsFull => HasBattery && IsPluggedIn && Percent >= 100;

    /// <summary>Perangkat tanpa baterai (misalnya PC desktop).</summary>
    public static BatteryInfo None { get; } = new(false, 0, false, false);
}
