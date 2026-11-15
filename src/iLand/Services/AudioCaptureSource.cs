using Microsoft.Extensions.Logging;
using NAudio.Wave;

namespace iLand.Services;

internal enum AudioSourceKind
{
    /// <summary>Suara yang keluar dari speaker atau headphone (loopback).</summary>
    SystemSound,

    /// <summary>Mikrofon default Windows.</summary>
    Microphone
}

/// <summary>
/// Menangkap suara dari satu sumber memakai WasapiRecorder (NAudio 3, API WASAPI resmi Windows)
/// dan menyimpannya ke sebuah AudioTrack dalam format PCM 16 bit stereo 48 kHz.
///
/// Privasi: suara hanya disimpan di memori sementara lalu ditulis ke file video yang dipilih pengguna.
/// Mikrofon hanya dibuka jika pengguna menyalakannya, dan ditutup lagi saat rekaman selesai.
/// </summary>
internal sealed class AudioCaptureSource : IDisposable
{
    // Windows mengubah format perangkat ke format ini di mode shared, jadi kodenya tidak perlu
    // mengurus perangkat yang berbeda-beda (44,1 kHz, 24 bit, 5.1 saluran, dan seterusnya).
    private static readonly WaveFormat CaptureFormat = new(AudioTrack.SampleRate, 16, AudioTrack.Channels);

    // Mikrofon yang terblokir (privasi Windows) atau dibisukan mengirim nol murni. Setelah selama ini
    // tidak ada satu pun sampel bukan-nol, pengguna diberi tahu.
    private const int SilenceWarningFrames = AudioTrack.SampleRate * 3;

    private readonly ILogger _logger;
    private WasapiRecorder? _recorder;
    private bool _isStopping;
    private long _silentFrames;
    private bool _silenceReported;

    public AudioSourceKind Kind { get; }

    public AudioTrack Track { get; } = new();

    /// <summary>Mikrofon mengirim hening total selama beberapa detik. Dipanggil dari thread penangkap suara.</summary>
    public event EventHandler? SilenceDetected;

    /// <summary>Perangkat suara berhenti di tengah rekaman (misalnya headset dicabut).</summary>
    public event EventHandler<Exception>? Failed;

    public AudioCaptureSource(AudioSourceKind kind, ILogger logger)
    {
        Kind = kind;
        _logger = logger;
    }

    /// <summary>Membuka perangkat dan mulai menangkap. Melempar exception jika perangkat tidak tersedia.</summary>
    public void Start()
    {
        WasapiRecorderBuilder builder = new WasapiRecorderBuilder().WithFormat(CaptureFormat);

        if (Kind == AudioSourceKind.SystemSound)
        {
            // Loopback: merekam apa yang diputar di perangkat keluaran default.
            builder = builder.WithLoopbackCapture();
        }

        WasapiRecorder recorder = builder.Build();

        WaveFormat actual = recorder.WaveFormat;
        if (actual.SampleRate != CaptureFormat.SampleRate
            || actual.BitsPerSample != CaptureFormat.BitsPerSample
            || actual.Channels != CaptureFormat.Channels)
        {
            recorder.Dispose();
            throw new NotSupportedException(
                $"The audio device could not provide 48 kHz 16 bit stereo (got {actual.SampleRate} Hz, "
                + $"{actual.BitsPerSample} bit, {actual.Channels} channels).");
        }

        // Event membawa span langsung ke buffer WASAPI yang hanya valid selama callback berjalan.
        // Track menyalinnya, jadi aman. qpcPosition adalah waktu paket dalam satuan 100 ns.
        recorder.DataAvailable += (buffer, flags, devicePosition, qpcPosition) =>
            OnData(buffer, (long)qpcPosition);

        recorder.RecordingStopped += (sender, args) => OnRecordingStopped(args.Exception);

        _recorder = recorder;
        recorder.StartRecording();

        _logger.LogInformation("Audio capture started: {Kind}", Kind);
    }

    private void OnData(ReadOnlySpan<byte> buffer, long qpcTicks)
    {
        Track.Write(qpcTicks, buffer);

        if (Kind != AudioSourceKind.Microphone || _silenceReported)
        {
            return;
        }

        // IndexOfAnyExcept mengembalikan -1 jika seluruh buffer berisi nol.
        if (buffer.IndexOfAnyExcept((byte)0) < 0)
        {
            _silentFrames += buffer.Length / AudioTrack.BytesPerFrame;
            if (_silentFrames >= SilenceWarningFrames)
            {
                _silenceReported = true;
                SilenceDetected?.Invoke(this, EventArgs.Empty);
            }
        }
        else
        {
            _silentFrames = 0;
        }
    }

    private void OnRecordingStopped(Exception? error)
    {
        if (error is not null && !_isStopping)
        {
            _logger.LogWarning(error, "Audio capture stopped unexpectedly: {Kind}", Kind);
            Failed?.Invoke(this, error);
        }
    }

    /// <summary>Berhenti menangkap dan melepas perangkat. Aman dipanggil berulang kali.</summary>
    public void Stop()
    {
        WasapiRecorder? recorder = _recorder;
        if (recorder is null)
        {
            return;
        }

        _isStopping = true;
        _recorder = null;

        try
        {
            recorder.StopRecording();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Audio capture was already stopped: {Kind}", Kind);
        }

        recorder.Dispose();
        _logger.LogInformation("Audio capture stopped: {Kind}", Kind);
    }

    public void Dispose()
    {
        Stop();
    }
}
