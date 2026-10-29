using System.Runtime.InteropServices;
using Darklands.Audio;

namespace Darklands.Host.Sdl;

/// <summary>
/// An SDL2 audio device playing 16-bit stereo frames from a source. Three ways (DARKLANDS_AUDIO_MODE): callback (the default):
/// SDL pulls 44100 Hz signed 16-bit frames on its audio thread and converts them for the device; queue: a thread of ours renders
/// them a little ahead and queues them (SDL_QueueAudio); native: SDL pulls in the device's own rate and sample format (16/32-bit
/// integer or float), converted here instead of by SDL. The other two are for an SDL whose usual path stays silent.
/// </summary>
internal sealed class SdlAudioOutput : IPcmOutput
{
    private const ushort AudioS16Sys = 0x8010; // AUDIO_S16LSB
    private const ushort AudioS32Sys = 0x8020; // AUDIO_S32LSB
    private const ushort AudioF32Sys = 0x8120; // AUDIO_F32LSB
    private const int AllowFrequencyChange = 0x1;
    private const int AllowFormatChange = 0x2;
    private const int AllowSamplesChange = 0x8;
    private readonly uint _device;
    private readonly SdlNative.AudioCallback? _callback; // kept alive while SDL holds the pointer
    private readonly bool _queue;
    private readonly ushort _format;
    private IPcmSource? _source;
    private short[] _buffer = Array.Empty<short>();
    private float[] _floats = Array.Empty<float>();
    private int[] _ints = Array.Empty<int>();
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private readonly object _positionLock = new();
    private long _pulled;          // frames handed to the device so far
    private long _pulledBefore;    // ... before the last callback
    private long _lastCallback;    // stopwatch ticks of the last callback
    private long _played;
    private int _peak;             // the loudest sample handed over since TakePeak
    private Thread? _feeder;
    private volatile bool _closing;
    private readonly FileStream? _record = Environment.GetEnvironmentVariable("DARKLANDS_AUDIO_RECORD") is { Length: > 0 } path
        ? new FileStream(path, FileMode.Create, FileAccess.Write)
        : null; // DARKLANDS_AUDIO_RECORD=<file>: what the device is given, raw 16-bit stereo at SampleRate

    private SdlAudioOutput(uint device, int sampleRate, int bufferFrames, SdlNative.AudioCallback? callback, string driver,
        SdlAudioMode mode, ushort format, string spec)
    {
        _device = device;
        SampleRate = sampleRate;
        BufferFrames = bufferFrames;
        _callback = callback;
        Driver = driver;
        Mode = mode;
        _queue = mode == SdlAudioMode.Queue;
        _format = format;
        Spec = spec;
    }

    /// <summary>The mode DARKLANDS_AUDIO_MODE asks for: callback (the default), queue or native.</summary>
    public static SdlAudioMode ModeAsked =>
        Environment.GetEnvironmentVariable("DARKLANDS_AUDIO_MODE")?.Trim().ToLowerInvariant() switch
        {
            "queue" => SdlAudioMode.Queue,
            "native" => SdlAudioMode.Native,
            _ => SdlAudioMode.Callback,
        };

    public SdlAudioMode Mode { get; }

    /// <summary>SDL's audio driver in use ("pipewire", "pulseaudio", "wasapi" ...).</summary>
    public string Driver { get; }

    /// <summary>What was asked of SDL and what it gave (rate, channels, format, buffer).</summary>
    public string Spec { get; }

    /// <summary>The device, as printed.</summary>
    public string Description => $"SDL audio driver {Driver}, {Spec}";

    /// <summary>Frames handed to SDL so far (0: SDL never asked for any).</summary>
    public long FramesPulled
    {
        get
        {
            lock (_positionLock)
            {
                return _pulled;
            }
        }
    }

    /// <summary>The loudest sample (0..32768) handed to the device since the last call; 0: only silence was played.</summary>
    public int TakePeak() => Interlocked.Exchange(ref _peak, 0);

    /// <summary>SDL's state of the device: "stopped", "playing" or "paused".</summary>
    public string Status => SdlNative.SDL_GetAudioDeviceStatus(_device) switch { 1 => "playing", 2 => "paused", _ => "stopped" };

    public int SampleRate { get; }

    /// <summary>Frames per callback (the device's buffer).</summary>
    public int BufferFrames { get; }

    /// <summary>
    /// When SDL asks for a buffer, the one before it is starting to play: what is heard is what was
    /// pulled before the last callback, less one buffer, plus the time since then. In queue mode: what was queued less what waits.
    /// </summary>
    public long FramesPlayed
    {
        get
        {
            if (_queue)
            {
                var waiting = SdlNative.SDL_GetQueuedAudioSize(_device) / (2 * SampleBytes);
                lock (_positionLock)
                {
                    _played = Math.Max(_played, _pulled - waiting);
                    return _played;
                }
            }
            lock (_positionLock)
            {
                if (_pulled == 0)
                {
                    return 0;
                }
                var since = (double)(_clock.ElapsedTicks - _lastCallback) / System.Diagnostics.Stopwatch.Frequency * SampleRate;
                var estimate = _pulledBefore - BufferFrames + (long)since;
                estimate = Math.Clamp(estimate, _played, Math.Max(_played, _pulledBefore));
                _played = estimate;
                return estimate;
            }
        }
    }

    public int LatencyFrames => (_queue ? 4 : 2) * BufferFrames;

    /// <summary>
    /// Opens the default device (44100 Hz, signed 16-bit stereo, SDL converting for the device; native mode: the device's own rate
    /// and format), or returns null (and the reason) when there is none. <paramref name="mode"/> null: as DARKLANDS_AUDIO_MODE asks.
    /// </summary>
    public static SdlAudioOutput? TryOpen(out string? error, SdlAudioMode? mode = null)
    {
        error = null;
        var chosen = mode ?? ModeAsked;
        var native = chosen == SdlAudioMode.Native;
        if (SdlNative.SDL_InitSubSystem(SdlNative.InitAudio) != 0)
        {
            error = SdlNative.GetErrorText();
            return null;
        }
        SdlAudioOutput? output = null;
        SdlNative.AudioCallback? callback = chosen == SdlAudioMode.Queue ? null : (_, stream, length) => output?.Fill(stream, length);
        var desired = new SdlNative.SdlAudioSpec
        {
            Freq = 44100,
            Format = AudioS16Sys,
            Channels = 2,
            Samples = 1024,
            Callback = callback is null ? IntPtr.Zero : Marshal.GetFunctionPointerForDelegate(callback),
        };
        // callback and queue: no changes allowed, SDL converts our frames for the device; native: the device's own rate and format
        var device = SdlNative.SDL_OpenAudioDevice(IntPtr.Zero, 0, ref desired, out var obtained,
            native ? AllowFrequencyChange | AllowFormatChange | AllowSamplesChange : 0);
        if (device == 0)
        {
            error = SdlNative.GetErrorText();
            SdlNative.SDL_QuitSubSystem(SdlNative.InitAudio);
            return null;
        }
        var format = native ? obtained.Format : AudioS16Sys;
        var spec = $"{chosen.ToString().ToLowerInvariant()} mode; asked 44100 Hz, 2 channels, format 0x{AudioS16Sys:X4} (signed 16-bit), {desired.Samples} frames; "
            + $"SDL gave {obtained.Freq} Hz, {obtained.Channels} channels, format 0x{obtained.Format:X4} ({FormatName(obtained.Format)}), "
            + $"{obtained.Samples} frames, {obtained.Size} bytes per buffer";
        if (obtained.Channels != 2 || (native ? FormatName(format) == "?" : obtained.Format != AudioS16Sys || obtained.Freq != desired.Freq))
        {
            // the device would read the frames wrongly (noise or silence): said, so that the log has the reason
            Console.Error.WriteLine("Sound: the audio device's format differs from the one written: " + spec + ".");
        }
        var rate = native && obtained.Freq > 0 ? obtained.Freq : desired.Freq;
        var frames = obtained.Samples > 0 ? obtained.Samples : desired.Samples;
        output = new SdlAudioOutput(device, rate, frames, callback, SdlNative.Text(SdlNative.SDL_GetCurrentAudioDriver()) ?? "?", chosen, format, spec);
        return output;
    }

    private static string FormatName(ushort format) => format switch
    {
        AudioS16Sys => "signed 16-bit",
        AudioS32Sys => "signed 32-bit",
        AudioF32Sys => "32-bit float",
        _ => "?",
    };

    /// <summary>Bytes of one sample in the format written.</summary>
    private int SampleBytes => _format is AudioS32Sys or AudioF32Sys ? 4 : 2;

    public void Start(IPcmSource source)
    {
        _source = source;
        if (_queue)
        {
            _feeder = new Thread(Feed) { IsBackground = true, Name = "SDL audio queue" };
            _feeder.Start();
        }
        SdlNative.SDL_PauseAudioDevice(_device, 0);
    }

    /// <summary>Queue mode: keeps about three buffers queued in SDL.</summary>
    private void Feed()
    {
        var chunk = new short[BufferFrames * 2];
        var bytes = chunk.Length * SampleBytes;
        var staging = Marshal.AllocHGlobal(bytes);
        try
        {
            while (!_closing)
            {
                if (SdlNative.SDL_GetQueuedAudioSize(_device) >= 3 * (uint)bytes)
                {
                    Thread.Sleep(2);
                    continue;
                }
                Render(chunk);
                Write(chunk, staging);
                if (SdlNative.SDL_QueueAudio(_device, staging, (uint)bytes) != 0)
                {
                    Console.Error.WriteLine("Sound: SDL_QueueAudio failed: " + SdlNative.GetErrorText());
                    return;
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(staging);
        }
    }

    private void Fill(IntPtr stream, int length)
    {
        var samples = length / SampleBytes;
        if (_buffer.Length != samples)
        {
            _buffer = new short[samples];
        }
        Render(_buffer);
        Write(_buffer, stream);
    }

    /// <summary>Copies 16-bit samples to the device's memory in the format written.</summary>
    private void Write(short[] samples, IntPtr target)
    {
        switch (_format)
        {
            case AudioF32Sys:
                if (_floats.Length != samples.Length)
                {
                    _floats = new float[samples.Length];
                }
                for (var i = 0; i < samples.Length; i++)
                {
                    _floats[i] = samples[i] / 32768f;
                }
                Marshal.Copy(_floats, 0, target, samples.Length);
                break;
            case AudioS32Sys:
                if (_ints.Length != samples.Length)
                {
                    _ints = new int[samples.Length];
                }
                for (var i = 0; i < samples.Length; i++)
                {
                    _ints[i] = samples[i] << 16;
                }
                Marshal.Copy(_ints, 0, target, samples.Length);
                break;
            default:
                Marshal.Copy(samples, 0, target, samples.Length);
                break;
        }
    }

    /// <summary>The next frames from the source, with the loudest sample and the position noted.</summary>
    private void Render(short[] buffer)
    {
        if (_source is null)
        {
            Array.Clear(buffer);
        }
        else
        {
            _source.Render(buffer);
        }
        var peak = 0;
        foreach (var sample in buffer)
        {
            peak = Math.Max(peak, Math.Abs((int)sample));
        }
        if (peak > Volatile.Read(ref _peak))
        {
            Volatile.Write(ref _peak, peak);
        }
        _record?.Write(MemoryMarshal.AsBytes(buffer.AsSpan()));
        lock (_positionLock)
        {
            _pulledBefore = _pulled;
            _pulled += buffer.Length / 2;
            _lastCallback = _clock.ElapsedTicks;
        }
    }

    public void Dispose()
    {
        _closing = true;
        _feeder?.Join(500);
        SdlNative.SDL_CloseAudioDevice(_device);
        SdlNative.SDL_QuitSubSystem(SdlNative.InitAudio);
        _record?.Dispose();
    }
}

/// <summary>How the SDL audio device is fed (DARKLANDS_AUDIO_MODE).</summary>
internal enum SdlAudioMode
{
    /// <summary>SDL pulls 44100 Hz 16-bit stereo through its callback and converts it (the default).</summary>
    Callback,

    /// <summary>We push 44100 Hz 16-bit stereo with SDL_QueueAudio.</summary>
    Queue,

    /// <summary>SDL pulls through its callback in the device's own rate and format, converted here.</summary>
    Native,
}
