namespace iLand.Models;

/// <summary>
/// Bentuk island yang dipilih di Settings.
/// Dynamic: pill yang melayang dengan celah kecil dari tepi atas layar.
/// Notch: menempel rata di tepi atas layar, sudut atasnya melengkung ke luar sehingga menyatu dengan tepi layar.
/// </summary>
public enum IslandShape
{
    Dynamic,
    Notch
}
