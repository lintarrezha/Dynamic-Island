using System.Collections.Concurrent;
using System.Diagnostics;
using iLand.Models;
using iLand.Native;
using Microsoft.Extensions.Logging;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Storage.Streams;

namespace iLand.Services;

/// <summary>
/// Satu sesi perekaman layar dari mulai sampai file MP4 selesai ditulis.
///
/// Alurnya: penangkap layar (Windows.Graphics.Capture) mengirim frame ke antrean kecil. Penangkap suara
/// (WASAPI lewat NAudio) mengirim paket suara ke AudioTrack. MediaStreamSource meminta sampel gambar dan
/// sampel suara kapan pun encoder siap, lalu MediaTranscoder menulis file MP4 (H.264 untuk gambar,
/// AAC untuk suara).
///
/// Penyelarasan gambar dan suara: frame video dan paket suara sama-sama membawa waktu dari jam
/// QueryPerformanceCounter. Titik nol garis waktu adalah frame video pertama, dan semua suara
/// dihitung relatif terhadap titik itu.
///
/// Gambar dan suara hanya diproses di memori dan ditulis ke file yang dipilih pengguna.
/// Tidak ada yang dikirim ke mana pun.
/// </summary>
internal sealed class RecordingSession : IDisposable
{
    private const int FramesPerSecond = 30;

    // Jumlah buffer di kolam penangkap. Frame yang sedang dipakai encoder ikut menahan buffer,
    // jadi kolam harus lebih besar dari antrean ditambah frame yang ditahan.
    private const int FramePoolSize = 8;
    private const int MaxQueuedFrames = 3;
    private const int HeldFrameCount = 3;

    // Suara diproses per blok 20 ms (960 frame pada 48 kHz).
    private const int AudioBlockFrames = AudioTrack.SampleRate / 50;

    // Frame yang datang lebih rapat dari ini dibuang agar hasilnya 30 fps (layar 60 Hz atau 144 Hz tetap aman).
    private static readonly TimeSpan MinFrameInterval = TimeSpan.FromMilliseconds(30);

    // Jika layar diam selama ini tanpa frame baru, gambar terakhir dikirim ulang ("detak jantung").
    // Tanpa ini video bisa lebih pendek dari lama merekam, dan suara bisa menunggu terlalu lama.
    private static readonly TimeSpan VideoHeartbeat = TimeSpan.FromSeconds(1);

    // Blok suara baru dibuat setelah waktunya lewat sejauh ini, supaya paket dari thread penangkap sempat tiba.
    private static readonly TimeSpan AudioLatency = TimeSpan.FromMilliseconds(150);

    // Selisih minimum agar sampel penutup (lihat OnVideoSampleRequested) layak ditambahkan.
    private static readonly TimeSpan MinClosingGap = TimeSpan.FromMilliseconds(100);

    // Batas waktu menunggu encoder menutup file setelah perekaman dihentikan.
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(15);

    private readonly ILogger _logger;
    private readonly object _gate = new();

    private IDirect3DDevice? _device;
    private GraphicsCaptureItem? _item;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _captureSession;
    private MediaStreamSource? _mediaSource;
    private MediaTranscoder? _transcoder;
    private IRandomAccessStream? _outputStream;
    private BlockingCollection<Direct3D11CaptureFrame>? _frames;
    private Task? _transcodeTask;

    // ---- Suara ----
    private readonly List<AudioCaptureSource> _audioSources = new();
    private readonly List<string> _startupWarnings = new();
    private AudioMixer? _audioMixer;
    private long _nextAudioFrame;                // hanya dipakai thread encoder suara

    // ---- Garis waktu bersama ----
    private readonly ManualResetEventSlim _originReady = new(false);
    private readonly ManualResetEventSlim _stopSignal = new(false);
    private long _originQpcTicks;                // titik nol pada skala QueryPerformanceCounter (100 ns)
    private long _originStopwatch;               // titik nol pada skala Stopwatch
    private TimeSpan _stopElapsed;               // lama rekaman saat berhenti

    // Dijaga oleh _gate. Dibaca oleh thread penangkap dan thread yang menghentikan perekaman.
    private bool _isAccepting;
    private bool _stopRequested;

    // Hanya dipakai thread penangkap (FrameArrived).
    private TimeSpan _lastAcceptedTime;
    private bool _hasAcceptedFrame;

    // Hanya dipakai thread encoder gambar (SampleRequested untuk video).
    private readonly Queue<Direct3D11CaptureFrame> _heldFrames = new();
    private Direct3D11CaptureFrame? _lastFrame;
    private TimeSpan _firstFrameTime;
    private TimeSpan _lastSampleTime = TimeSpan.FromTicks(-1);
    private bool _hasFirstFrame;
    private bool _closingSampleSent;

    /// <summary>Error yang menghentikan encoder (null jika semuanya lancar).</summary>
    public Exception? Error { get; private set; }

    /// <summary>True jika mikrofon berhasil dibuka dan ikut direkam.</summary>
    public bool IsMicrophoneActive => _audioSources.Any(s => s.Kind == AudioSourceKind.Microphone);

    /// <summary>True jika ada suara (sistem dan atau mikrofon) yang ikut direkam.</summary>
    public bool HasAudio => _audioSources.Count > 0;

    /// <summary>
    /// Pesan tentang sumber suara yang diminta tetapi gagal dibuka saat mulai merekam.
    /// Dibaca oleh pemilik sesi setelah StartAsync selesai.
    /// </summary>
    public IReadOnlyList<string> StartupWarnings => _startupWarnings;

    /// <summary>
    /// Dipanggil jika perekaman berakhir bukan karena diminta pengguna
    /// (misalnya monitor dicabut atau encoder gagal). Dipanggil dari thread mana pun.
    /// </summary>
    public event EventHandler? EndedUnexpectedly;

    /// <summary>Masalah suara saat merekam (mikrofon hening total, perangkat dicabut). Dari thread mana pun.</summary>
    public event EventHandler<string>? AudioWarning;

    private RecordingSession(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>Menyiapkan semuanya dan mulai merekam monitor yang diberikan ke file yang diberikan.</summary>
    public static async Task<RecordingSession> StartAsync(
        IntPtr monitorHandle, StorageFile file, RecordingAudioOptions audio, ILogger logger)
    {
        var session = new RecordingSession(logger);
        try
        {
            await session.InitializeAsync(monitorHandle, file, audio);
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    private async Task InitializeAsync(IntPtr monitorHandle, StorageFile file, RecordingAudioOptions audio)
    {
        _device = GraphicsCaptureInterop.CreateDevice();
        _item = GraphicsCaptureInterop.CreateItemForMonitor(monitorHandle);
        _item.Closed += OnItemClosed;

        SizeInt32 size = _item.Size;

        // Suara dibuka lebih dulu. Paketnya menunggu di AudioTrack sampai gambar pertama menentukan titik nol.
        StartAudioSources(audio);

        _logger.LogInformation(
            "Recording started: {Width}x{Height} at {Fps} fps, system sound {System}, microphone {Mic}",
            size.Width, size.Height, FramesPerSecond,
            _audioSources.Any(s => s.Kind == AudioSourceKind.SystemSound), IsMicrophoneActive);

        // ---- Penangkap layar ----
        _frames = new BlockingCollection<Direct3D11CaptureFrame>();
        _isAccepting = true;

        _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _device, DirectXPixelFormat.B8G8R8A8UIntNormalized, FramePoolSize, size);
        _framePool.FrameArrived += OnFrameArrived;

        _captureSession = _framePool.CreateCaptureSession(_item);
        _captureSession.IsCursorCaptureEnabled = true;

        // ---- Encoder ----
        var videoProperties = VideoEncodingProperties.CreateUncompressed(
            MediaEncodingSubtypes.Bgra8, (uint)size.Width, (uint)size.Height);
        var videoDescriptor = new VideoStreamDescriptor(videoProperties);

        if (HasAudio)
        {
            // Suara masuk ke encoder sebagai PCM mentah, lalu dijadikan AAC oleh MediaTranscoder.
            var audioProperties = AudioEncodingProperties.CreatePcm(
                AudioTrack.SampleRate, AudioTrack.Channels, 16);
            var audioDescriptor = new AudioStreamDescriptor(audioProperties);
            _mediaSource = new MediaStreamSource(videoDescriptor, audioDescriptor);
        }
        else
        {
            _mediaSource = new MediaStreamSource(videoDescriptor);
        }

        _mediaSource.BufferTime = TimeSpan.Zero;
        _mediaSource.Starting += OnMediaSourceStarting;
        _mediaSource.SampleRequested += OnSampleRequested;

        _transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };
        _outputStream = await file.OpenAsync(FileAccessMode.ReadWrite);

        PrepareTranscodeResult prepared = await _transcoder.PrepareMediaStreamSourceTranscodeAsync(
            _mediaSource, _outputStream, CreateProfile(size, HasAudio));

        if (!prepared.CanTranscode)
        {
            throw new InvalidOperationException($"Windows cannot encode this recording: {prepared.FailureReason}");
        }

        // Mulai menangkap, lalu jalankan encoder di latar belakang (tidak ditunggu di sini).
        _captureSession.StartCapture();
        _transcodeTask = RunTranscodeAsync(prepared);
    }

    private static MediaEncodingProfile CreateProfile(SizeInt32 size, bool hasAudio)
    {
        // Mulai dari profil MP4 bawaan Windows (H.264), lalu sesuaikan ukuran dan bitrate.
        MediaEncodingProfile profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);

        profile.Video.Width = (uint)size.Width;
        profile.Video.Height = (uint)size.Height;
        profile.Video.FrameRate.Numerator = FramesPerSecond;
        profile.Video.FrameRate.Denominator = 1;
        profile.Video.PixelAspectRatio.Numerator = 1;
        profile.Video.PixelAspectRatio.Denominator = 1;

        // Kira-kira 0,13 bit per piksel per frame: layar 1080p sekitar 8 Mbps. Dibatasi 4 sampai 40 Mbps.
        double bitrate = (double)size.Width * size.Height * FramesPerSecond * 0.13;
        profile.Video.Bitrate = (uint)Math.Clamp(bitrate, 4_000_000, 40_000_000);

        // AAC 192 kbps stereo 48 kHz, atau tanpa jalur suara sama sekali.
        profile.Audio = hasAudio
            ? AudioEncodingProperties.CreateAac(AudioTrack.SampleRate, AudioTrack.Channels, 192_000)
            : null;

        return profile;
    }

    private async Task RunTranscodeAsync(PrepareTranscodeResult prepared)
    {
        try
        {
            await prepared.TranscodeAsync();
        }
        catch (Exception ex)
        {
            Error = ex;
            _logger.LogError(ex, "Encoder failed");
        }

        // Jika berakhir tanpa diminta (misalnya encoder gagal), beri tahu pemilik sesi.
        bool unexpected;
        lock (_gate)
        {
            unexpected = !_stopRequested;
        }

        if (unexpected)
        {
            EndedUnexpectedly?.Invoke(this, EventArgs.Empty);
        }
    }

    // ---- Suara: membuka perangkat ----

    private void StartAudioSources(RecordingAudioOptions options)
    {
        if (options.SystemAudio)
        {
            TryStartAudioSource(AudioSourceKind.SystemSound, "System sound");
        }

        if (options.Microphone)
        {
            TryStartAudioSource(AudioSourceKind.Microphone, "Microphone");
        }

        if (_audioSources.Count > 0)
        {
            var tracks = _audioSources.Select(source => source.Track).ToList();
            _audioMixer = new AudioMixer(tracks, AudioBlockFrames);
        }
    }

    /// <summary>
    /// Membuka satu sumber suara. Jika gagal (tidak ada perangkat, atau diblokir), rekaman tetap
    /// berjalan tanpa sumber itu dan pengguna diberi tahu, bukan merekam bisu diam-diam.
    /// </summary>
    private void TryStartAudioSource(AudioSourceKind kind, string displayName)
    {
        var source = new AudioCaptureSource(kind, _logger);
        source.Failed += OnAudioSourceFailed;
        source.SilenceDetected += OnMicrophoneSilent;

        try
        {
            source.Start();
            _audioSources.Add(source);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Source} could not be opened", displayName);
            source.Dispose();
            _startupWarnings.Add($"{displayName} is unavailable, so it was left out of the recording.");
        }
    }

    private void OnAudioSourceFailed(object? sender, Exception error)
    {
        string name = sender is AudioCaptureSource { Kind: AudioSourceKind.Microphone } ? "The microphone" : "System sound";
        AudioWarning?.Invoke(this, $"{name} stopped working. The rest of the recording continues without it.");
    }

    private void OnMicrophoneSilent(object? sender, EventArgs e)
    {
        AudioWarning?.Invoke(
            this,
            "The microphone is silent. Check that it is not muted and that Windows allows desktop apps to use it.");
    }

    // ---- Sisi penangkap layar (thread kolam) ----

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        Direct3D11CaptureFrame? frame = sender.TryGetNextFrame();
        if (frame is null)
        {
            return;
        }

        TimeSpan time = frame.SystemRelativeTime;
        bool tooSoon = _hasAcceptedFrame && time - _lastAcceptedTime < MinFrameInterval;

        bool accepted = false;
        lock (_gate)
        {
            // Antrean penuh berarti encoder belum sanggup mengejar: buang frame ini daripada menumpuk memori.
            if (_isAccepting && !tooSoon && _frames is not null && _frames.Count < MaxQueuedFrames)
            {
                _lastAcceptedTime = time;
                _hasAcceptedFrame = true;
                _frames.Add(frame);
                accepted = true;
            }
        }

        if (!accepted)
        {
            frame.Dispose(); // kembalikan buffer ke kolam
        }
    }

    private void OnItemClosed(GraphicsCaptureItem sender, object args)
    {
        // Monitor dicabut atau resolusi berubah drastis: Windows menutup objek penangkap.
        bool unexpected;
        lock (_gate)
        {
            unexpected = !_stopRequested;
        }

        if (unexpected)
        {
            _logger.LogWarning("The captured display was closed by Windows");
            EndedUnexpectedly?.Invoke(this, EventArgs.Empty);
        }
    }

    // ---- Sisi encoder (thread milik Media Foundation) ----

    private void OnMediaSourceStarting(MediaStreamSource sender, MediaStreamSourceStartingEventArgs args)
    {
        // Rekaman selalu dimulai dari detik 0. Waktu tiap frame dihitung relatif terhadap frame pertama.
        args.Request.SetActualStartPosition(TimeSpan.Zero);
    }

    private void OnSampleRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args)
    {
        try
        {
            if (args.Request.StreamDescriptor is AudioStreamDescriptor)
            {
                OnAudioSampleRequested(args);
            }
            else
            {
                OnVideoSampleRequested(args);
            }
        }
        catch (Exception ex)
        {
            Error = ex;
            _logger.LogError(ex, "Failed to provide a sample to the encoder");
            args.Request.Sample = null;
        }
    }

    // ---- Gambar ----

    private void OnVideoSampleRequested(MediaStreamSourceSampleRequestedEventArgs args)
    {
        BlockingCollection<Direct3D11CaptureFrame>? frames = _frames;
        if (frames is null)
        {
            args.Request.Sample = null;
            return;
        }

        while (true)
        {
            // Tunggu frame berikutnya, paling lama satu detik.
            if (frames.TryTake(out Direct3D11CaptureFrame? frame, (int)VideoHeartbeat.TotalMilliseconds))
            {
                if (!_hasFirstFrame)
                {
                    InitializeTimeline(frame);
                }

                TimeSpan timestamp = NextVideoTimestamp(frame.SystemRelativeTime - _firstFrameTime);
                args.Request.Sample = MediaStreamSample.CreateFromDirect3D11Surface(frame.Surface, timestamp);

                RetainFrame(frame);
                return;
            }

            // Perekaman sudah berhenti dan antrean kosong: akhiri.
            if (frames.IsCompleted)
            {
                break;
            }

            // Layar diam selama satu detik (Windows tidak mengirim frame baru): kirim ulang gambar terakhir
            // dengan waktu sekarang, agar garis waktu video tetap berjalan dan suara tidak tertahan.
            if (_lastFrame is not null)
            {
                TimeSpan timestamp = NextVideoTimestamp(Stopwatch.GetElapsedTime(_originStopwatch));
                args.Request.Sample = MediaStreamSample.CreateFromDirect3D11Surface(_lastFrame.Surface, timestamp);
                return;
            }

            // Belum ada satu frame pun: tunggu lagi.
        }

        // Satu sampel penutup (gambar terakhir diulang) pada waktu berhenti, supaya durasi video
        // sama dengan lama merekam walaupun layar diam di akhir.
        if (!_closingSampleSent && _lastFrame is not null)
        {
            _closingSampleSent = true;

            if (_stopElapsed - _lastSampleTime > MinClosingGap)
            {
                TimeSpan timestamp = NextVideoTimestamp(_stopElapsed);
                args.Request.Sample = MediaStreamSample.CreateFromDirect3D11Surface(_lastFrame.Surface, timestamp);
                return;
            }
        }

        args.Request.Sample = null; // null = akhir video
    }

    /// <summary>Waktu sampel harus selalu naik. Jika sama atau mundur, geser maju 1 ms.</summary>
    private TimeSpan NextVideoTimestamp(TimeSpan candidate)
    {
        if (candidate <= _lastSampleTime)
        {
            candidate = _lastSampleTime + TimeSpan.FromMilliseconds(1);
        }

        _lastSampleTime = candidate;
        return candidate;
    }

    /// <summary>
    /// Menentukan titik nol garis waktu dari frame video pertama, lalu memberi tahu thread suara.
    /// </summary>
    private void InitializeTimeline(Direct3D11CaptureFrame frame)
    {
        _hasFirstFrame = true;
        _firstFrameTime = frame.SystemRelativeTime;

        long frameTicks = _firstFrameTime.Ticks;
        long nowTicks = QpcNowTicks();
        long nowStopwatch = Stopwatch.GetTimestamp();

        // Jam video (SystemRelativeTime) dan jam suara (QueryPerformanceCounter) seharusnya satu jam yang sama.
        // Ini dicek dulu. Jika ternyata berbeda lebih dari dua detik, waktu "sekarang" dipakai sebagai titik nol bersama.
        bool sameClock = Math.Abs(frameTicks - nowTicks) < TimeSpan.TicksPerSecond * 2;
        if (sameClock)
        {
            _originQpcTicks = frameTicks;
            double lagSeconds = (nowTicks - frameTicks) / (double)TimeSpan.TicksPerSecond;
            _originStopwatch = nowStopwatch - (long)(lagSeconds * Stopwatch.Frequency);
        }
        else
        {
            _logger.LogWarning("Video and audio clocks differ, using the current time as the shared origin");
            _originQpcTicks = nowTicks;
            _originStopwatch = nowStopwatch;
        }

        _audioMixer?.SetOrigin(_originQpcTicks);
        _originReady.Set();
    }

    /// <summary>Waktu sekarang menurut QueryPerformanceCounter dalam satuan 100 ns (sama dengan paket suara).</summary>
    private static long QpcNowTicks()
    {
        return (long)(Stopwatch.GetTimestamp() * (TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency));
    }

    /// <summary>
    /// Encoder membaca gambar secara tidak langsung setelah sampel diserahkan, jadi frame TIDAK boleh
    /// langsung dikembalikan ke kolam (buffernya akan ditimpa frame baru). Beberapa frame terakhir
    /// ditahan, yang lebih lama dilepas.
    /// </summary>
    private void RetainFrame(Direct3D11CaptureFrame frame)
    {
        _heldFrames.Enqueue(frame);
        _lastFrame = frame;

        while (_heldFrames.Count > HeldFrameCount)
        {
            _heldFrames.Dequeue().Dispose();
        }
    }

    // ---- Suara ----

    private void OnAudioSampleRequested(MediaStreamSourceSampleRequestedEventArgs args)
    {
        AudioMixer? mixer = _audioMixer;
        if (mixer is null)
        {
            args.Request.Sample = null;
            return;
        }

        // Titik nol garis waktu baru ada setelah gambar pertama tiba.
        while (!_originReady.IsSet)
        {
            if (_stopSignal.IsSet)
            {
                args.Request.Sample = null;
                return;
            }

            _originReady.Wait(50);
        }

        long startFrame = _nextAudioFrame;

        // Blok suara dibuat sedikit terlambat dari waktu nyata, agar paket dari thread penangkap sudah masuk.
        // Setelah rekaman berhenti, tidak perlu menunggu lagi: sisa blok dibuat secepatnya.
        TimeSpan dueTime = FramesToTime(startFrame + AudioBlockFrames) + AudioLatency;
        while (!_stopSignal.IsSet)
        {
            TimeSpan remaining = dueTime - Stopwatch.GetElapsedTime(_originStopwatch);
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            _stopSignal.Wait(remaining < TimeSpan.FromMilliseconds(50) ? remaining : TimeSpan.FromMilliseconds(50));
        }

        // Berhenti membuat blok setelah melewati lama rekaman.
        if (_stopSignal.IsSet && FramesToTime(startFrame) >= _stopElapsed)
        {
            args.Request.Sample = null;
            return;
        }

        byte[] pcm = mixer.MixBlock(startFrame);

        MediaStreamSample sample = MediaStreamSample.CreateFromBuffer(CreateBuffer(pcm), FramesToTime(startFrame));
        sample.Duration = FramesToTime(AudioBlockFrames);
        args.Request.Sample = sample;

        _nextAudioFrame = startFrame + AudioBlockFrames;
    }

    private static TimeSpan FramesToTime(long frames)
    {
        return TimeSpan.FromTicks(frames * TimeSpan.TicksPerSecond / AudioTrack.SampleRate);
    }

    private static IBuffer CreateBuffer(byte[] data)
    {
        using var writer = new DataWriter();
        writer.WriteBytes(data);
        return writer.DetachBuffer();
    }

    private void StopAudioSources()
    {
        foreach (AudioCaptureSource source in _audioSources)
        {
            source.Stop();
        }
    }

    // ---- Berhenti ----

    /// <summary>
    /// Menghentikan penangkapan dan menunggu encoder selesai menulis file.
    /// Melempar exception jika encoder gagal atau tidak selesai dalam batas waktu.
    /// </summary>
    public async Task StopAsync()
    {
        bool isFirstStop;
        lock (_gate)
        {
            isFirstStop = !_stopRequested;
            _stopRequested = true;
            _isAccepting = false;
        }

        if (isFirstStop)
        {
            try
            {
                _captureSession?.Dispose(); // menghentikan pengambilan layar
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Capture session was already closed");
            }

            _captureSession = null;

            // Lama rekaman ditetapkan SEBELUM encoder dibangunkan, karena dipakai untuk menutup gambar dan suara.
            _stopElapsed = _originReady.IsSet ? Stopwatch.GetElapsedTime(_originStopwatch) : TimeSpan.Zero;

            // Perangkat suara ditutup dulu supaya paket terakhirnya sudah masuk sebelum blok terakhir dibuat.
            await Task.Run(StopAudioSources);

            _stopSignal.Set();       // membangunkan thread suara
            _frames?.CompleteAdding(); // membangunkan thread gambar: tidak ada frame baru lagi
        }

        if (_transcodeTask is not null)
        {
            Task finished = await Task.WhenAny(_transcodeTask, Task.Delay(StopTimeout));
            if (finished != _transcodeTask)
            {
                throw new TimeoutException("The video encoder did not finish in time.");
            }
        }

        if (Error is not null)
        {
            throw new InvalidOperationException("Recording failed while encoding.", Error);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _isAccepting = false;
            _stopRequested = true;
        }

        _stopSignal.Set(); // melepas thread encoder yang mungkin masih menunggu
        StopAudioSources();

        _captureSession?.Dispose();
        _captureSession = null;

        if (_framePool is not null)
        {
            _framePool.FrameArrived -= OnFrameArrived;
            _framePool.Dispose();
            _framePool = null;
        }

        if (_item is not null)
        {
            _item.Closed -= OnItemClosed;
            _item = null;
        }

        if (_mediaSource is not null)
        {
            _mediaSource.Starting -= OnMediaSourceStarting;
            _mediaSource.SampleRequested -= OnSampleRequested;
            _mediaSource = null;
        }

        _frames?.CompleteAdding();

        // Jika encoder belum selesai (misalnya saat aplikasi ditutup paksa), jangan melepas
        // frame dan file yang mungkin masih dibaca encoder. Windows akan membersihkannya saat proses berakhir.
        bool encoderFinished = _transcodeTask is null || _transcodeTask.IsCompleted;
        if (!encoderFinished)
        {
            _logger.LogWarning("Recording session disposed while the encoder was still running");
            return;
        }

        foreach (AudioCaptureSource source in _audioSources)
        {
            source.Failed -= OnAudioSourceFailed;
            source.SilenceDetected -= OnMicrophoneSilent;
            source.Dispose();
        }

        _audioSources.Clear();

        if (_frames is not null)
        {
            while (_frames.TryTake(out Direct3D11CaptureFrame? pending))
            {
                pending.Dispose();
            }

            _frames.Dispose();
            _frames = null;
        }

        while (_heldFrames.Count > 0)
        {
            _heldFrames.Dequeue().Dispose();
        }

        _lastFrame = null;

        _outputStream?.Dispose();
        _outputStream = null;

        _device?.Dispose();
        _device = null;

        _originReady.Dispose();
        _stopSignal.Dispose();
    }
}
