using System.Runtime.InteropServices;
using Darklands.Audio;

namespace Darklands.Host.Sdl;

/// <summary>An SDL2 audio device pulling 16-bit stereo frames from a source on SDL's audio thread.</summary>
internal sealed class SdlAudioOutput : IPcmOutput
{
    private const ushort AudioS16Sys = 0x8010; // AUDIO_S16LSB
    private readonly uint _device;
    private readonly SdlNative.AudioCallback _callback; // kept alive while SDL holds the pointer
    private IPcmSource? _source;
    private short[] _buffer = Array.Empty<short>();
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private readonly object _positionLock = new();
    private long _pulled;          // frames handed to the device so far
    private long _pulledBefore;    // ... before the last callback
    private long _lastCallback;    // stopwatch ticks of the last callback
    private long _played;
    private readonly FileStream? _record = Environment.GetEnvironmentVariable("DARKLANDS_AUDIO_RECORD") is { Length: > 0 } path
        ? new FileStream(path, FileMode.Create, FileAccess.Write)
        : null; // DARKLANDS_AUDIO_RECORD=<file>: what the device is given, raw 16-bit stereo at SampleRate

    private SdlAudioOutput(uint device, int sampleRate, int bufferFrames, SdlNative.AudioCallback callback)
    {
        _device = device;
        SampleRate = sampleRate;
        BufferFrames = bufferFrames;
        _callback = callback;
    }

    public int SampleRate { get; }

    /// <summary>Frames per callback (the device's buffer).</summary>
    public int BufferFrames { get; }

    /// <summary>
    /// When SDL asks for a buffer, the one before it is starting to play: what is heard is what was
    /// pulled before the last callback, less one buffer, plus the time since then.
    /// </summary>
    public long FramesPlayed
    {
        get
        {
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

    public int LatencyFrames => 2 * BufferFrames;

    /// <summary>Opens the default device, or returns null (and the reason) when there is none.</summary>
    public static SdlAudioOutput? TryOpen(out string? error)
    {
        error = null;
        if (SdlNative.SDL_InitSubSystem(SdlNative.InitAudio) != 0)
        {
            error = SdlNative.GetErrorText();
            return null;
        }
        SdlAudioOutput? output = null;
        SdlNative.AudioCallback callback = (_, stream, length) => output?.Fill(stream, length);
        var desired = new SdlNative.SdlAudioSpec
        {
            Freq = 44100,
            Format = AudioS16Sys,
            Channels = 2,
            Samples = 1024,
            Callback = Marshal.GetFunctionPointerForDelegate(callback),
        };
        var device = SdlNative.SDL_OpenAudioDevice(IntPtr.Zero, 0, ref desired, out var obtained, 0);
        if (device == 0)
        {
            error = SdlNative.GetErrorText();
            SdlNative.SDL_QuitSubSystem(SdlNative.InitAudio);
            return null;
        }
        output = new SdlAudioOutput(device, obtained.Freq, obtained.Samples, callback);
        return output;
    }

    public void Start(IPcmSource source)
    {
        _source = source;
        SdlNative.SDL_PauseAudioDevice(_device, 0);
    }

    private void Fill(IntPtr stream, int length)
    {
        var samples = length / 2;
        if (_buffer.Length != samples)
        {
            _buffer = new short[samples];
        }
        if (_source is null)
        {
            Array.Clear(_buffer);
        }
        else
        {
            _source.Render(_buffer);
        }
        Marshal.Copy(_buffer, 0, stream, samples);
        _record?.Write(MemoryMarshal.AsBytes(_buffer.AsSpan()));
        lock (_positionLock)
        {
            _pulledBefore = _pulled;
            _pulled += samples / 2;
            _lastCallback = _clock.ElapsedTicks;
        }
    }

    public void Dispose()
    {
        SdlNative.SDL_CloseAudioDevice(_device);
        SdlNative.SDL_QuitSubSystem(SdlNative.InitAudio);
        _record?.Dispose();
    }
}
