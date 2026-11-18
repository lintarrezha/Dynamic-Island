using System.Text.Json;
using System.Text.Json.Serialization;

namespace iLand.Helpers;

/// <summary>
/// Pembaca enum yang TIDAK pernah gagal. Nama (huruf besar-kecil bebas) dan angka yang valid dibaca seperti biasa.
/// Nama yang tidak dikenal, angka di luar daftar, null, atau tipe lain menghasilkan nilai bawaan enum (anggota pertama).
///
/// Kenapa: JsonStringEnumConverter bawaan melempar JsonException untuk nama yang tidak dikenal, dan satu kesalahan saja
/// membuat SELURUH settings.json gagal dibaca (semua pengaturan kembali ke bawaan). Itu bisa terjadi jika file diedit tangan,
/// atau jika versi iLand yang lebih baru menulis nama bentuk baru lalu pengguna kembali ke versi lama.
/// Penulisan tetap berupa teks (misalnya "Notch") supaya file mudah dibaca.
/// </summary>
public sealed class LenientEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    // Tanpa ini, token null untuk tipe nilai dilewatkan ke pembaca bawaan yang melempar kesalahan.
    public override bool HandleNull => true;

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return Enum.TryParse(reader.GetString(), ignoreCase: true, out T byName) && Enum.IsDefined(byName)
                    ? byName
                    : default;

            case JsonTokenType.Number:
                if (reader.TryGetInt32(out int number))
                {
                    T byNumber = (T)Enum.ToObject(typeof(T), number);
                    return Enum.IsDefined(byNumber) ? byNumber : default;
                }

                return default;

            default:
                reader.Skip(); // objek atau larik: lewati isinya agar pembacaan field berikutnya tetap benar
                return default;
        }
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
