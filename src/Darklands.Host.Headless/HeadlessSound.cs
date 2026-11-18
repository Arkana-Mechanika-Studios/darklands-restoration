using Darklands.Audio;
using Darklands.Audio.Packs;
using Darklands.Content;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Presentation.Graphics;
using Darklands.Rendering;

/// <summary>The game's sound drivers rendered in step with the scripted clock, to a .wav file and a trace.</summary>
internal sealed class HeadlessSound : IDisposable
{
    public const int Rate = 44100;
    private readonly SynthAudio _audio;
    private readonly IPcmSource _source;
    private readonly IMidiSynth? _synth;
    private readonly List<short>? _samples;
    private readonly string? _wav;
    private readonly short[] _buffer = new short[2 * Rate];

    private HeadlessSound(SynthAudio audio, IPcmSource source, IMidiSynth? synth, string? wav, StreamWriter? trace)
    {
        _audio = audio;
        _source = source;
        _synth = synth;
        _wav = wav;
        _samples = wav is null ? null : new List<short>();
        Trace = trace;
    }

    public StreamWriter? Trace { get; }

    public static HeadlessSound? Attach(ScriptedHost host, GameFiles files, string choice, string? roms, string? wav, string? trace,
        IReadOnlyList<string> commandLine)
    {
        var selected = Darklands.Audio.Mt32.SoundSelection.Select(files, choice == "config" ? null : choice, roms, Rate,
            line => (Darklands.Audio.Mt32.SoundSelection.IsWarning(line) ? Console.Error : Console.Out).WriteLine("sound: " + line), commandLine);
        if (selected is null)
        {
            return null;
        }
        var setup = selected.Setup;
        var synth = selected.Midi;
        var writer = trace is null ? null : new StreamWriter(trace);
        writer?.WriteLine($"rate {Rate}");
        Action<AudioEvent>? traced = writer is null ? null : e => writer.WriteLine(e.Kind switch
        {
            AudioEventKind.Tick => $"tick {e.Tick} {e.Frame}",
            AudioEventKind.Request => $"request {e.Tick} {e.Frame} {e.Value:X2}",
            AudioEventKind.MidiByte => $"midi {e.Tick} {e.Frame} {e.Context} {e.Value:X2}",
            _ => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"sample {e.Tick} {e.Frame} {e.Context} {e.Value} {e.Delay:F6}"),
        });
        var audio = new SynthAudio(files, Rate, setup, synth, traced);
        // an audio pack: the drivers render under its mixer (the timer is still theirs), the game's calls reach them unchanged
        var mixer = ScriptedHost.ReplacementSound is { } pack ? new PackMixer(pack, Rate) { Source = audio, RenderedTicks = () => audio.RenderedTicks } : null;
        var sound = new HeadlessSound(audio, (IPcmSource?)mixer ?? audio, synth, wav, writer);
        host.Audio = mixer is null ? audio : new PackAudio(audio, audio, mixer);
        host.Timer = audio;
        host.Advanced = sound.RenderUntil;
        return sound;
    }

    /// <summary>Renders up to <paramref name="milliseconds"/> of the script's clock.</summary>
    private void RenderUntil(long milliseconds)
    {
        var frames = milliseconds * Rate / 1000 - _audio.RenderedFrames;
        while (frames > 0)
        {
            var n = (int)Math.Min(frames, _buffer.Length / 2);
            var span = _buffer.AsSpan(0, n * 2);
            _source.Render(span);
            _samples?.AddRange(span.ToArray());
            frames -= n;
        }
    }

    public void Dispose()
    {
        if (_wav is not null && _samples is not null)
        {
            WavFile.Write(_wav, _samples.ToArray(), Rate);
            Console.WriteLine($"sound: {_samples.Count / 2 / (double)Rate:0.0} s in {_wav}{(_audio.Fault is null ? "" : ", driver fault: " + _audio.Fault)}");
        }
        Trace?.Dispose();
        _synth?.Dispose();
    }
}
