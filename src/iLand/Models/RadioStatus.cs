namespace iLand.Models;

/// <summary>Keadaan satu radio (Wi-Fi atau Bluetooth).</summary>
/// <param name="IsAvailable">False jika perangkat tidak punya radio itu (misalnya PC desktop tanpa Wi-Fi).</param>
/// <param name="IsOn">True jika radio menyala.</param>
/// <param name="AccessDenied">True jika Windows tidak mengizinkan iLand mengubah radio.</param>
public sealed record RadioStatus(bool IsAvailable, bool IsOn, bool AccessDenied)
{
    public static RadioStatus Unavailable { get; } = new(false, false, false);
}
