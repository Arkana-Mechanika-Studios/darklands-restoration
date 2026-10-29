using Darklands.Platform;

namespace Darklands.Audio.Packs;

/// <summary>
/// The game's audio with a pack's replacements: every call goes on to the game's own audio (the drivers) unchanged, in the
/// same order, and every value it returns is the drivers' own; the call is also noted for the <see cref="PackMixer"/>, which
/// plays the replacement and mutes the drivers' sound while it does. The drivers keep running: their rendering is the game
/// timer and their AX answers (number 5, the piece last played) are the game's, so a pack changes what is heard, never
/// what the game does or when.
/// </summary>
/// <remarks>New code (replacement sound, not part of the game). tests: PackAudioTests.</remarks>
public sealed class PackAudio(IAudio inner, IGameTimer? timer, PackMixer mixer) : IAudio
{
    /// <summary>The game's own audio, which every call reaches.</summary>
    public IAudio Inner => inner;

    public PackMixer Mixer => mixer;

    public void PlayMusic(int tune)
    {
        var tick = When();
        inner.PlayMusic(tune);
        mixer.Request(tick, AudioKind.Music, tune);
    }

    public void PlayMusicAt(int tune, long tick)
    {
        inner.PlayMusicAt(tune, tick);
        mixer.Request(tick, AudioKind.Music, tune);
    }

    public void StopMusic()
    {
        var tick = When();
        inner.StopMusic();
        mixer.Request(tick, AudioKind.Music, GameSounds.Stop);
    }

    public void PlaySound(int effect)
    {
        var tick = When();
        inner.PlaySound(effect);
        mixer.Request(tick, AudioKind.Effect, effect);
    }

    public int PlayMusicNow(int tune)
    {
        var ax = inner.PlayMusicNow(tune);
        mixer.RequestNow(tune);
        return ax;
    }

    public int RequestCurrentPiece()
    {
        var ax = inner.RequestCurrentPiece();
        mixer.RequestNow(GameSounds.CurrentPiece);
        return ax;
    }

    /// <summary>The tick the drivers carry a request out at (their RequestTick: the ticks heard plus the output's latency).</summary>
    private long When() => timer?.RequestTick ?? long.MinValue;
}

/// <summary>
/// An audio output that plays the <see cref="PackMixer"/> instead of the source it is started with: the source (the drivers)
/// becomes the mixer's <see cref="PackMixer.Source"/>. Its rate, position and latency are the device's, so the drivers' timer
/// reads the same as without a pack.
/// </summary>
/// <remarks>New code (replacement sound, not part of the game).</remarks>
public sealed class PackOutput(IPcmOutput device, PackMixer mixer) : IPcmOutput
{
    public int SampleRate => device.SampleRate;

    public long FramesPlayed => device.FramesPlayed;

    public int LatencyFrames => device.LatencyFrames;

    public void Start(IPcmSource source)
    {
        mixer.Source = source;
        device.Start(mixer);
    }

    public void Dispose() => device.Dispose();
}
