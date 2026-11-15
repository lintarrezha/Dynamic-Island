using System.Buffers.Binary;

namespace iLand.Services;

/// <summary>
/// Menampung suara dari satu sumber (speaker atau mikrofon) dan menempatkannya pada garis waktu video.
///
/// Format tetap: PCM 16 bit, stereo, 48 kHz (4 byte per frame).
///
/// Masalah yang diselesaikan di sini:
/// 1. Paket suara datang dari thread lain dengan waktu QueryPerformanceCounter sendiri (satuan 100 ns).
///    Waktu itu diubah menjadi nomor frame pada garis waktu video.
/// 2. Suara sistem TIDAK mengirim data saat tidak ada yang diputar. Celah itu otomatis menjadi hening,
///    karena posisi paket berikutnya melompat dan bagian yang tidak terisi dibaca sebagai nol.
/// 3. Selisih waktu kecil antar paket (jitter) diabaikan, paket disambung rapat tanpa lubang atau tumpang tindih.
/// </summary>
internal sealed class AudioTrack
{
    public const int SampleRate = 48000;
    public const int Channels = 2;
    public const int BytesPerFrame = 4; // 16 bit x 2 saluran

    // Selisih waktu yang masih dianggap "menyambung" (20 ms).
    private const long ContinuityToleranceFrames = SampleRate / 50;

    // Batas memori: jika pembaca macet, buang suara tertua setelah 30 detik menumpuk.
    private const int MaxQueuedBytes = SampleRate * BytesPerFrame * 30;

    private const long TicksPerSecond = 10_000_000; // satuan waktu 100 ns

    private sealed record Chunk(long StartFrame, byte[] Data)
    {
        public int Frames => Data.Length / BytesPerFrame;

        public long EndFrame => StartFrame + Frames;
    }

    private readonly object _gate = new();
    private readonly Queue<Chunk> _chunks = new();
    private int _queuedBytes;

    private bool _hasFirstPacket;
    private long _firstPacketTicks;   // waktu paket pertama (100 ns)
    private long _nextFrame;          // posisi frame berikutnya, dihitung dari paket pertama
    private bool _originSet;
    private long _originTicks;        // titik nol garis waktu video (100 ns)

    /// <summary>
    /// Dipanggil dari thread penangkap suara untuk setiap paket.
    /// </summary>
    /// <param name="qpcTicks">Waktu paket menurut QueryPerformanceCounter, satuan 100 ns.</param>
    /// <param name="pcm">Data PCM 16 bit stereo 48 kHz. Disalin, jadi aman dipakai ulang oleh pemanggil.</param>
    public void Write(long qpcTicks, ReadOnlySpan<byte> pcm)
    {
        int frames = pcm.Length / BytesPerFrame;
        if (frames == 0)
        {
            return;
        }

        byte[] copy = pcm[..(frames * BytesPerFrame)].ToArray();

        lock (_gate)
        {
            if (!_hasFirstPacket)
            {
                _hasFirstPacket = true;
                _firstPacketTicks = qpcTicks;
                _nextFrame = 0;
            }

            long expectedFrame = (qpcTicks - _firstPacketTicks) * SampleRate / TicksPerSecond;

            // Ada celah besar (misalnya tidak ada suara diputar): lompat ke posisi sebenarnya,
            // bagian yang dilewati menjadi hening. Jika tidak, sambung rapat setelah paket sebelumnya.
            long startFrame = expectedFrame > _nextFrame + ContinuityToleranceFrames
                ? expectedFrame
                : _nextFrame;

            _chunks.Enqueue(new Chunk(startFrame, copy));
            _queuedBytes += copy.Length;
            _nextFrame = startFrame + frames;

            while (_queuedBytes > MaxQueuedBytes && _chunks.Count > 1)
            {
                _queuedBytes -= _chunks.Dequeue().Data.Length;
            }
        }
    }

    /// <summary>
    /// Menentukan titik nol garis waktu video (dalam satuan waktu QueryPerformanceCounter, 100 ns).
    /// Suara yang terekam sebelum titik ini otomatis terbuang saat dibaca.
    /// </summary>
    public void SetOrigin(long originTicks)
    {
        lock (_gate)
        {
            _originTicks = originTicks;
            _originSet = true;
        }
    }

    /// <summary>
    /// Menambahkan suara track ini ke akumulator untuk rentang frame [timelineStartFrame, +frameCount)
    /// pada garis waktu video. Bagian yang tidak punya suara dibiarkan (hening).
    /// </summary>
    /// <param name="accumulator">Array int dengan panjang frameCount x 2 (kiri, kanan bergantian).</param>
    public void MixInto(long timelineStartFrame, int frameCount, int[] accumulator)
    {
        lock (_gate)
        {
            // Belum ada titik nol atau belum ada suara sama sekali (misalnya suara sistem
            // belum pernah diputar): tidak ada yang perlu dicampur.
            if (!_originSet || !_hasFirstPacket)
            {
                return;
            }

            // Posisi frame pertama track ini terhadap garis waktu video. Bisa negatif jika
            // suara mulai terekam sebelum gambar pertama.
            long offsetFrames = (_firstPacketTicks - _originTicks) * SampleRate / TicksPerSecond;

            long relativeStart = timelineStartFrame - offsetFrames;
            long relativeEnd = relativeStart + frameCount;

            while (_chunks.Count > 0)
            {
                Chunk chunk = _chunks.Peek();

                if (chunk.EndFrame <= relativeStart)
                {
                    // Sudah lewat dari rentang yang dibaca: buang.
                    _queuedBytes -= chunk.Data.Length;
                    _chunks.Dequeue();
                    continue;
                }

                if (chunk.StartFrame >= relativeEnd)
                {
                    break; // belum waktunya
                }

                long from = Math.Max(chunk.StartFrame, relativeStart);
                long to = Math.Min(chunk.EndFrame, relativeEnd);

                for (long frame = from; frame < to; frame++)
                {
                    int sourceIndex = (int)(frame - chunk.StartFrame) * BytesPerFrame;
                    int targetIndex = (int)(frame - relativeStart) * Channels;

                    accumulator[targetIndex] += BinaryPrimitives.ReadInt16LittleEndian(chunk.Data.AsSpan(sourceIndex));
                    accumulator[targetIndex + 1] += BinaryPrimitives.ReadInt16LittleEndian(chunk.Data.AsSpan(sourceIndex + 2));
                }

                if (chunk.EndFrame <= relativeEnd)
                {
                    _queuedBytes -= chunk.Data.Length;
                    _chunks.Dequeue(); // habis dibaca
                }
                else
                {
                    break; // sisanya dibaca pada blok berikutnya
                }
            }
        }
    }
}
