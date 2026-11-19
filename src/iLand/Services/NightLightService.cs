using iLand.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace iLand.Services;

/// <summary>
/// Night light lewat nilai biner registri milik Windows. Lihat catatan di INightLightService: formatnya tidak resmi.
///
/// Bentuk nilai (dari dokumentasi proyek komunitas win-nightlight-lib / sem-reg, diuji di Windows 11 24H2):
///   43 42 01 00 | 0A 02 01 00 | 2A 06 {stempel waktu detik, VLQ} | 2A 2B 0E {N} | 43 42 01 | data (N byte)
///   data = 00 [10 00 jika menyala] [D0 0A 02] C6 14 {FILETIME, VLQ} 00 00 00 00
/// VLQ = bilangan 7 bit per byte, byte terendah dulu, bit tertinggi menandai masih ada lanjutan.
/// Stempel waktu harus LEBIH BESAR dari yang tersimpan, kalau tidak Windows menganggap penulisan lama dan mengabaikannya.
///
/// Pengaman: penulisan hanya jika nilai lama cocok persis dengan bentuk di atas; setelah menulis, hasilnya dibaca ulang
/// dan harus sesuai target, kalau tidak dikembalikan false (pemanggil membuka halaman Settings Night light).
/// </summary>
public sealed class NightLightService : INightLightService
{
    private const string KeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\" +
        @"default$windows.data.bluelightreduction.bluelightreductionstate\windows.data.bluelightreduction.bluelightreductionstate";

    private const string ValueName = "Data";

    private static readonly byte[] Head = { 0x43, 0x42, 0x01, 0x00, 0x0A, 0x02, 0x01, 0x00, 0x2A, 0x06 };
    private static readonly byte[] Mid = { 0x2A, 0x2B, 0x0E };
    private static readonly byte[] Signature = { 0x43, 0x42, 0x01 };

    private readonly ILogger<NightLightService> _logger;

    public NightLightService(ILogger<NightLightService> logger)
    {
        _logger = logger;
    }

    public bool IsSupported => TryReadState(out _, out _);

    public bool IsOn => TryReadState(out bool isOn, out _) && isOn;

    public async Task<bool> SetAsync(bool on)
    {
        try
        {
            if (!TryReadState(out bool current, out ulong storedTimestamp))
            {
                _logger.LogInformation("Night light registry value is missing or has an unknown shape: not writing");
                return false;
            }

            if (current == on)
            {
                return true;
            }

            byte[] blob = Build(on, storedTimestamp);
            using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true))
            {
                if (key == null)
                {
                    return false;
                }

                key.SetValue(ValueName, blob, RegistryValueKind.Binary);
            }

            // Beri Windows waktu menerapkan lalu pastikan hasilnya benar-benar berubah.
            await Task.Delay(400);
            bool verified = TryReadState(out bool after, out _) && after == on;
            if (!verified)
            {
                _logger.LogWarning("Night light did not change after writing the registry value");
            }

            return verified;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not change Night light");
            return false;
        }
    }

    // Membaca dan memeriksa bentuk nilai. Mengembalikan false jika bentuknya tidak dikenali.
    private static bool TryReadState(out bool isOn, out ulong timestamp)
    {
        isOn = false;
        timestamp = 0;

        byte[]? blob;
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(KeyPath);
            blob = key?.GetValue(ValueName) as byte[];
        }
        catch (Exception)
        {
            return false;
        }

        if (blob == null || blob.Length < Head.Length + 8)
        {
            return false;
        }

        int i = 0;
        if (!StartsWith(blob, i, Head))
        {
            return false;
        }

        i += Head.Length;
        if (!TryReadVlq(blob, ref i, out timestamp))
        {
            return false;
        }

        if (!StartsWith(blob, i, Mid) || i + Mid.Length >= blob.Length)
        {
            return false;
        }

        i += Mid.Length;
        int n = blob[i++];

        // Sisa harus tepat: tanda tangan (3 byte) + N byte data.
        if (blob.Length - i != Signature.Length + n || !StartsWith(blob, i, Signature))
        {
            return false;
        }

        i += Signature.Length;
        if (n < 4 || blob[i] != 0x00)
        {
            return false;
        }

        // Empat byte nol di ujung.
        for (int k = blob.Length - 4; k < blob.Length; k++)
        {
            if (blob[k] != 0x00)
            {
                return false;
            }
        }

        isOn = n >= 3 && blob[i + 1] == 0x10 && blob[i + 2] == 0x00;
        return true;
    }

    private static byte[] Build(bool on, ulong storedTimestamp)
    {
        ulong now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        ulong timestamp = Math.Max(now, storedTimestamp + 2);

        var data = new List<byte> { 0x00 };
        if (on)
        {
            data.Add(0x10);
            data.Add(0x00);
        }

        data.AddRange(new byte[] { 0xD0, 0x0A, 0x02, 0xC6, 0x14 });
        WriteVlq(data, (ulong)DateTime.UtcNow.ToFileTimeUtc());
        data.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00 });

        var blob = new List<byte>(Head);
        WriteVlq(blob, timestamp);
        blob.AddRange(Mid);
        blob.Add((byte)data.Count);
        blob.AddRange(Signature);
        blob.AddRange(data);
        return blob.ToArray();
    }

    private static bool StartsWith(byte[] data, int index, byte[] prefix)
    {
        if (index + prefix.Length > data.Length)
        {
            return false;
        }

        for (int k = 0; k < prefix.Length; k++)
        {
            if (data[index + k] != prefix[k])
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryReadVlq(byte[] data, ref int index, out ulong value)
    {
        value = 0;
        for (int shift = 0; shift < 64 && index < data.Length; shift += 7)
        {
            byte b = data[index++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return true;
            }
        }

        return false;
    }

    private static void WriteVlq(List<byte> target, ulong value)
    {
        while (value >= 0x80)
        {
            target.Add((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }

        target.Add((byte)value);
    }
}
