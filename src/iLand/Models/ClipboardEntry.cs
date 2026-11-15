namespace iLand.Models;

/// <summary>
/// Satu teks yang pernah disalin. Hanya hidup di memori: tidak pernah ditulis ke disk atau dikirim ke mana pun.
/// </summary>
/// <param name="Id">Pengenal unik, dipakai untuk menghapus atau menyalin ulang item ini.</param>
/// <param name="Text">Isi teks yang disalin.</param>
/// <param name="CopiedAt">Waktu teks ini disalin (waktu lokal).</param>
public sealed record ClipboardEntry(Guid Id, string Text, DateTime CopiedAt);
