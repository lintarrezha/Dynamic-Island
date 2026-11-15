using System.Buffers.Binary;

namespace iLand.Services;

/// <summary>
/// Mencampur suara dari beberapa AudioTrack (speaker dan mikrofon) menjadi satu blok PCM 16 bit stereo.
/// Penjumlahan dilakukan dalam int lalu dipotong ke rentang 16 bit supaya tidak meluap (clipping).
/// </summary>
internal sealed class AudioMixer
{
    private readonly IReadOnlyList<AudioTrack> _tracks;
    private readonly int[] _accumulator;

    /// <param name="tracks">Track yang akan dicampur.</param>
    /// <param name="framesPerBlock">Jumlah frame per blok yang akan diminta.</param>
    public AudioMixer(IReadOnlyList<AudioTrack> tracks, int framesPerBlock)
    {
        _tracks = tracks;
        _accumulator = new int[framesPerBlock * AudioTrack.Channels];
        FramesPerBlock = framesPerBlock;
    }

    public int FramesPerBlock { get; }

    /// <summary>Menetapkan titik nol garis waktu pada semua track.</summary>
    public void SetOrigin(long originTicks)
    {
        foreach (AudioTrack track in _tracks)
        {
            track.SetOrigin(originTicks);
        }
    }

    /// <summary>
    /// Membuat satu blok suara untuk frame [startFrame, startFrame + FramesPerBlock).
    /// Hanya boleh dipanggil dari satu thread (thread encoder suara).
    /// </summary>
    public byte[] MixBlock(long startFrame)
    {
        Array.Clear(_accumulator);

        foreach (AudioTrack track in _tracks)
        {
            track.MixInto(startFrame, FramesPerBlock, _accumulator);
        }

        var block = new byte[_accumulator.Length * sizeof(short)];
        for (int i = 0; i < _accumulator.Length; i++)
        {
            short sample = (short)Math.Clamp(_accumulator[i], short.MinValue, short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(block.AsSpan(i * sizeof(short)), sample);
        }

        return block;
    }
}
