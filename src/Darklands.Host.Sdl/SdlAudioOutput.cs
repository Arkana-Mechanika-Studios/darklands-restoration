using System.Runtime.InteropServices;
using Darklands.Audio;

namespace Darklands.Host.Sdl;

/// <summary>
/// An SDL3 audio stream bound to the default output device, playing 16-bit stereo frames from a source. Three ways
/// (DARKLANDS_AUDIO_MODE): callback (the default): SDL asks on its audio thread for more and is given 44100 Hz signed 16-bit frames
/// in buffers of 1024 frames, which it converts for the device; queue: a thread of ours renders such buffers a little ahead and
/// puts them in the stream (SDL2's SDL_QueueAudio); native: as the callback, in the device's own rate and sample format (16/32-bit
/// integer or float), converted here instead of by SDL. The other two are for an SDL whose usual path stays silent.
/// </summary>
internal sealed class SdlAudioOutput : IPcmOutput
{
    private const uint AudioS16Sys = 0x8010; // SDL_AUDIO_S16LE
    private const uint AudioS32Sys = 0x8020; // SDL_AUDIO_S32LE
    private const uint AudioF32Sys = 0x8120; // SDL_AUDIO_F32LE

    /// <summary>Frames per buffer asked for (SDL2's spec.samples): what the callback renders at a time and the queue's unit.</summary>
    private const int AskedFrames = 1024;

    private readonly IntPtr _stream;
    private readonly SdlNative.AudioStreamCallback? _callback; // kept alive while SDL holds the pointer
    private readonly bool _queue;
    private readonly uint _format;
    private readonly IntPtr _staging;     // one buffer in the format written, handed to SDL from here
    private IPcmSource? _source;
    private short[] _buffer = Array.Empty<short>();
    private float[] _floats = Array.Empty<float>();
    private int[] _ints = Array.Empty<int>();
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private readonly object _positionLock = new();
    private long _pulled;          // frames handed to the device so far
    private long _pulledBefore;    // ... before the last buffer
    private long _lastCallback;    // stopwatch ticks of the last buffer
    private long _played;
    private int _peak;             // the loudest sample handed over since TakePeak
    private Thread? _feeder;
    private volatile bool _closing;
    private readonly FileStream? _record = Environment.GetEnvironmentVariable("DARKLANDS_AUDIO_RECORD") is { Length: > 0 } path
        ? new FileStream(path, FileMode.Create, FileAccess.Write)
        : null; // DARKLANDS_AUDIO_RECORD=<file>: what the device is given, raw 16-bit stereo at SampleRate

    private SdlAudioOutput(IntPtr stream, int sampleRate, int bufferFrames, SdlNative.AudioStreamCallback? callback, string driver,
        SdlAudioMode mode, uint format, string spec)
    {
        _stream = stream;
        SampleRate = sampleRate;
        BufferFrames = bufferFrames;
        _callback = callback;
        Driver = driver;
        Mode = mode;
        _queue = mode == SdlAudioMode.Queue;
        _format = format;
        Spec = spec;
        _staging = Marshal.AllocHGlobal(BufferBytes);
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

    /// <summary>What is written to SDL and what its device is (rate, channels, format, buffer).</summary>
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

    /// <summary>SDL's state of the device: "playing", or "paused" (not started yet).</summary>
    public string Status => SdlNative.SDL_AudioStreamDevicePaused(_stream) ? "paused" : "playing";

    public int SampleRate { get; }

    /// <summary>Frames per buffer handed to SDL.</summary>
    public int BufferFrames { get; }

    private int BufferBytes => BufferFrames * 2 * SampleBytes;

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
                var waiting = Math.Max(0, SdlNative.SDL_GetAudioStreamQueued(_stream)) / (2 * SampleBytes);
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
        if (!SdlNative.SDL_InitSubSystem(SdlNative.InitAudio))
        {
            error = SdlNative.GetErrorText();
            return null;
        }
        // the device's buffer as SDL2's was asked for: 1024 frames
        SdlNative.SDL_SetHint(SdlNative.HintAudioDeviceSampleFrames, AskedFrames.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var desired = new SdlNative.SdlAudioSpec { Format = AudioS16Sys, Channels = 2, Freq = 44100 };
        var known = SdlNative.SDL_GetAudioDeviceFormat(SdlNative.AudioDeviceDefaultPlayback, out var device, out var deviceFrames);
        if (native && known)
        {
            // the device's own rate and format (SDL then converts nothing); stereo still (as SDL2 was asked)
            desired = desired with
            {
                Freq = device.Freq > 0 ? device.Freq : 44100,
                Format = FormatName(device.Format) == "?" ? AudioS16Sys : device.Format,
            };
        }
        SdlAudioOutput? output = null;
        SdlNative.AudioStreamCallback? callback = chosen == SdlAudioMode.Queue ? null : (_, stream, additional, _) => output?.Pull(stream, additional);
        var stream = SdlNative.SDL_OpenAudioDeviceStream(SdlNative.AudioDeviceDefaultPlayback, ref desired,
            callback is null ? IntPtr.Zero : Marshal.GetFunctionPointerForDelegate(callback), IntPtr.Zero);
        if (stream == IntPtr.Zero)
        {
            error = SdlNative.GetErrorText();
            SdlNative.SDL_QuitSubSystem(SdlNative.InitAudio);
            return null;
        }
        if (SdlNative.SDL_GetAudioDeviceFormat(SdlNative.SDL_GetAudioStreamDevice(stream), out var opened, out var openedFrames))
        {
            (device, deviceFrames) = (opened, openedFrames);
        }
        var frames = native && deviceFrames > 0 ? deviceFrames : AskedFrames;
        var spec = $"{chosen.ToString().ToLowerInvariant()} mode; written {desired.Freq} Hz, {desired.Channels} channels, format 0x{desired.Format:X4} "
            + $"({FormatName(desired.Format)}), {frames} frames a buffer; the device: {device.Freq} Hz, {device.Channels} channels, "
            + $"format 0x{device.Format:X4} ({FormatName(device.Format)}), {deviceFrames} frames a buffer (SDL converts)";
        if (native && !known)
        {
            // native mode without the device's format: written as the callback's would be, and said, so that the log has the reason
            Console.Error.WriteLine("Sound: the audio device's own format is not known: " + spec + ".");
        }
        output = new SdlAudioOutput(stream, desired.Freq, frames, callback, SdlNative.Text(SdlNative.SDL_GetCurrentAudioDriver()) ?? "?",
            chosen, desired.Format, spec);
        return output;
    }

    private static string FormatName(uint format) => format switch
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
        SdlNative.SDL_ResumeAudioStreamDevice(_stream);
    }

    /// <summary>Queue mode: keeps about three buffers queued in SDL.</summary>
    private void Feed()
    {
        var chunk = new short[BufferFrames * 2];
        var bytes = BufferBytes;
        while (!_closing)
        {
            if (SdlNative.SDL_GetAudioStreamQueued(_stream) >= 3 * bytes)
            {
                Thread.Sleep(2);
                continue;
            }
            Render(chunk);
            Write(chunk, _staging);
            if (!SdlNative.SDL_PutAudioStreamData(_stream, _staging, bytes))
            {
                Console.Error.WriteLine("Sound: SDL_PutAudioStreamData failed: " + SdlNative.GetErrorText());
                return;
            }
        }
    }

    /// <summary>
    /// Callback and native modes: SDL wants <paramref name="additional"/> more bytes; it is given whole buffers of
    /// <see cref="BufferFrames"/>, as SDL2's callback filled them (what goes beyond waits in the stream for the next ask).
    /// </summary>
    private void Pull(IntPtr stream, int additional)
    {
        var bytes = BufferBytes;
        while (additional > 0 && !_closing)
        {
            Fill(_staging, bytes);
            SdlNative.SDL_PutAudioStreamData(stream, _staging, bytes);
            additional -= bytes;
        }
    }

    private void Fill(IntPtr target, int length)
    {
        var samples = length / SampleBytes;
        if (_buffer.Length != samples)
        {
            _buffer = new short[samples];
        }
        Render(_buffer);
        Write(_buffer, target);
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
        // the stream's device closes with it (its callback has returned for good then)
        SdlNative.SDL_DestroyAudioStream(_stream);
        SdlNative.SDL_QuitSubSystem(SdlNative.InitAudio);
        Marshal.FreeHGlobal(_staging);
        _record?.Dispose();
    }
}

/// <summary>How the SDL audio device is fed (DARKLANDS_AUDIO_MODE).</summary>
internal enum SdlAudioMode
{
    /// <summary>SDL pulls 44100 Hz 16-bit stereo through its callback and converts it (the default).</summary>
    Callback,

    /// <summary>We push 44100 Hz 16-bit stereo into the stream (SDL2's SDL_QueueAudio; SDL_PutAudioStreamData).</summary>
    Queue,

    /// <summary>SDL pulls through its callback in the device's own rate and format, converted here.</summary>
    Native,
}
