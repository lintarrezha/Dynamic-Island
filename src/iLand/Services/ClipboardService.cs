using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using iLand.Interfaces;
using iLand.Models;
using iLand.Native;
using Microsoft.Extensions.Logging;

namespace iLand.Services;

/// <summary>
/// Mencatat riwayat teks yang disalin, untuk ditampilkan di tab Shelf (bagian Clipboard).
///
/// Cara kerja: Windows mengirim pesan WM_CLIPBOARDUPDATE ke sebuah window tak terlihat milik kita setiap kali
/// isi clipboard berubah. Jadi tidak ada polling atau timer yang berjalan terus.
///
/// Privasi (penting):
/// 1. Bawaannya MATI. Pemantau baru dipasang jika pengguna menyalakan "Clipboard history" di Settings,
///    dan dilepas lagi (riwayat dihapus) saat dimatikan.
/// 2. Hanya TEKS yang dicatat, maksimal 20 item dan 4.000 karakter per item, hanya di memori.
///    Tidak ada yang ditulis ke disk atau dikirim ke mana pun.
/// 3. Salinan yang ditandai "pribadi" oleh aplikasi (misalnya pengelola kata sandi) diabaikan.
/// 4. Isi clipboard TIDAK PERNAH ditulis ke log.
/// </summary>
public sealed class ClipboardService : IClipboardService, IDisposable
{
    private const int MaxItems = 20;
    private const int MaxTextLength = 4000;

    // Clipboard sering masih dipegang aplikasi asal beberapa milidetik setelah berubah.
    private const int FirstReadDelayMs = 30;
    private const int RetryDelayMs = 60;
    private const int ReadAttempts = 5;

    // Format clipboard yang dipakai aplikasi untuk menandai "jangan masuk riwayat" (aturan resmi Windows).
    private const string ExcludeFromMonitorFormat = "ExcludeClipboardContentFromMonitorProcessing";
    private const string CanIncludeInHistoryFormat = "CanIncludeInClipboardHistory";

    // Setelah kita menyalin sendiri (klik item di Shelf), perubahan clipboard itu diabaikan selama ini.
    private static readonly TimeSpan SelfCopyIgnoreWindow = TimeSpan.FromMilliseconds(1000);

    private readonly ILogger<ClipboardService> _logger;
    private readonly ISettingsService _settingsService;
    private readonly List<ClipboardEntry> _items = new();

    private HwndSource? _source;
    private bool _isListening;
    private bool _isReading;
    private bool _readAgain;
    private string? _selfCopiedText;
    private DateTime _selfCopiedAt;

    public event EventHandler? Changed;

    public IReadOnlyList<ClipboardEntry> Items => _items;

    public ClipboardService(ILogger<ClipboardService> logger, ISettingsService settingsService)
    {
        _logger = logger;
        _settingsService = settingsService;
    }

    public void Initialize()
    {
        _settingsService.SettingsChanged += OnSettingsChanged;
        ApplyEnabledState();
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        ApplyEnabledState();
    }

    /// <summary>Memasang atau melepas pemantau sesuai pengaturan. Aman dipanggil berulang kali.</summary>
    private void ApplyEnabledState()
    {
        // Riwayat clipboard hidup di tab Shelf, jadi ikut mati saat tab Shelf dimatikan (isinya dikosongkan).
        bool enabled = _settingsService.Current.ClipboardHistoryEnabled
                       && _settingsService.Current.IsTabEnabled(IslandTab.Shelf);

        if (enabled && !_isListening)
        {
            StartListening();
        }
        else if (!enabled && _isListening)
        {
            StopListening();
            Clear();
        }
    }

    private void StartListening()
    {
        // "Message-only window": window tak terlihat yang hanya menerima pesan (pola yang sama dengan HotkeyService).
        var parameters = new HwndSourceParameters("iLand.ClipboardWindow")
        {
            ParentWindow = NativeMethods.HWND_MESSAGE,
            WindowStyle = 0
        };

        var source = new HwndSource(parameters);
        source.AddHook(WndProc);

        if (!NativeMethods.AddClipboardFormatListener(source.Handle))
        {
            _logger.LogWarning("Could not start the clipboard listener");
            source.Dispose();
            return;
        }

        _source = source;
        _isListening = true;
        _logger.LogInformation("Clipboard history started");
    }

    private void StopListening()
    {
        if (_source is not null)
        {
            NativeMethods.RemoveClipboardFormatListener(_source.Handle);
            _source.Dispose();
            _source = null;
        }

        _isListening = false;
        _logger.LogInformation("Clipboard history stopped");
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_CLIPBOARDUPDATE)
        {
            // Jangan membaca clipboard di dalam callback ini: ditunda lewat async supaya callback cepat selesai.
            _ = ReadClipboardAsync();
        }

        return IntPtr.Zero;
    }

    // ---- Membaca clipboard ----

    private async Task ReadClipboardAsync()
    {
        // Jika pembacaan sedang berjalan, cukup minta satu kali pembacaan ulang setelah selesai.
        if (_isReading)
        {
            _readAgain = true;
            return;
        }

        _isReading = true;

        try
        {
            do
            {
                _readAgain = false;
                await TryReadOnceAsync();
            }
            while (_readAgain && _isListening);
        }
        finally
        {
            _isReading = false;
        }
    }

    private async Task TryReadOnceAsync()
    {
        for (int attempt = 0; attempt < ReadAttempts; attempt++)
        {
            await Task.Delay(attempt == 0 ? FirstReadDelayMs : RetryDelayMs);

            if (!_isListening)
            {
                return;
            }

            try
            {
                // Bukan teks (gambar, file, dan lain-lain): diabaikan.
                if (!Clipboard.ContainsText())
                {
                    return;
                }

                if (IsMarkedAsPrivate())
                {
                    _logger.LogDebug("Skipped a clipboard item marked as private");
                    return;
                }

                AddText(Clipboard.GetText());
                return;
            }
            catch (ExternalException)
            {
                // Clipboard sedang dipakai aplikasi lain: coba lagi sebentar lagi.
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not read the clipboard");
                return;
            }
        }
    }

    /// <summary>
    /// True jika aplikasi asal menandai salinan ini sebagai pribadi (tidak boleh masuk riwayat).
    /// Aturan Windows: format "ExcludeClipboardContentFromMonitorProcessing" ada = pribadi.
    /// Format "CanIncludeInClipboardHistory" berisi angka 4 byte: 1 = boleh, 0 = jangan.
    /// </summary>
    private static bool IsMarkedAsPrivate()
    {
        System.Windows.IDataObject? data = Clipboard.GetDataObject();
        if (data is null)
        {
            return false;
        }

        if (data.GetDataPresent(ExcludeFromMonitorFormat))
        {
            return true;
        }

        if (data.GetDataPresent(CanIncludeInHistoryFormat))
        {
            // Jika nilainya tidak terbaca, anggap pribadi (lebih aman).
            return ReadDword(data.GetData(CanIncludeInHistoryFormat)) != 1;
        }

        return false;
    }

    private static int? ReadDword(object? raw)
    {
        if (raw is int number)
        {
            return number;
        }

        byte[]? bytes = raw switch
        {
            MemoryStream stream => stream.ToArray(),
            byte[] array => array,
            _ => null
        };

        return bytes is { Length: >= 4 } ? BitConverter.ToInt32(bytes, 0) : null;
    }

    private void AddText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (text.Length > MaxTextLength)
        {
            _logger.LogDebug("Skipped clipboard text longer than {Max} characters", MaxTextLength);
            return;
        }

        // Teks yang baru saja kita salin sendiri (klik item di Shelf) tidak dicatat lagi.
        if (_selfCopiedText is not null
            && text == _selfCopiedText
            && DateTime.UtcNow - _selfCopiedAt < SelfCopyIgnoreWindow)
        {
            return;
        }

        // Teks yang sama dipindah ke paling atas, bukan dicatat dua kali.
        _items.RemoveAll(item => item.Text == text);
        _items.Insert(0, new ClipboardEntry(Guid.NewGuid(), text, DateTime.Now));

        if (_items.Count > MaxItems)
        {
            _items.RemoveRange(MaxItems, _items.Count - MaxItems);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    // ---- Perintah dari Shelf ----

    public bool CopyBack(Guid id)
    {
        ClipboardEntry? entry = _items.FirstOrDefault(item => item.Id == id);
        if (entry is null)
        {
            return false;
        }

        // Tandai dulu, supaya perubahan clipboard yang kita sebabkan sendiri tidak dicatat sebagai item baru.
        _selfCopiedText = entry.Text;
        _selfCopiedAt = DateTime.UtcNow;

        try
        {
            Clipboard.SetText(entry.Text);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not copy the item back to the clipboard");
            _selfCopiedText = null;
            return false;
        }
    }

    public void Remove(Guid id)
    {
        if (_items.RemoveAll(item => item.Id == id) > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Clear()
    {
        if (_items.Count == 0)
        {
            return;
        }

        _items.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _settingsService.SettingsChanged -= OnSettingsChanged;

        if (_isListening)
        {
            StopListening();
        }

        _items.Clear();
    }
}
