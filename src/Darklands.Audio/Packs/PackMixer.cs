namespace Darklands.Audio.Packs;

/// <summary>
/// Mixes a pack's replacements over the game's own sound. The game's sound (<see cref="Source"/>, the drivers) is always
/// rendered, whatever the pack replaces, because its rendering drives the game timer; while a replacement owns the sound it
/// is muted (faded over a few milliseconds), and the replacement is added instead.
/// </summary>
/// <remarks>
/// <para>The requests come from <see cref="PackAudio"/> with the tick the drivers carry them out at; a request takes effect in
/// the first block rendered after that tick. The rules follow the drivers: a music number starts or stops the music, a battle
/// effect sounds over it, and a request for the other driver ends the sounds of the first (the drivers are swapped).</para>
/// <list type="bullet">
/// <item>A replaced piece owns the music until the next piece or the battle driver: the game's piece is muted meanwhile, also
/// while it fades out after a stop (1), is silenced (6) or reset (0).</item>
/// <item>A replaced effect mutes the game's sound while the replacement plays (an unreplaced effect that sounds at the same
/// time is muted with it).</item>
/// <item>Pause (2) holds the replacements, resume (3) goes on; 5 and 7 change nothing.</item>
/// <item>Music per card (<see cref="AudioKind.Card"/> requests, noted when the game shows a card): a card with music in the pack
/// starts it, in place of the game's piece (which plays on, muted, in the drivers); the next card with the same file keeps it
/// playing; a card without one hands back the game's current piece (its replacement if the pack has one, else the drivers' own);
/// a battle, or the game's own stop, silence or reset, ends it. The game's pieces asked for meanwhile are only remembered.</item>
/// </list>
/// New code (replacement sound, not part of the game).
/// </remarks>
public sealed class PackMixer : IPcmSource
{
    private const double MuteSeconds = 0.01;
    private const double SwitchSeconds = 0.05;
    private const double StopSeconds = 1.0;

    private readonly AudioPack _pack;
    private readonly List<(long Tick, AudioKind Kind, int Number)> _requests = new();
    private readonly Lock _requestLock = new();
    private readonly Lock _renderLock = new();
    private readonly List<Voice> _effects = new();
    private readonly List<Voice> _fading = new();
    private float[] _mix = [];
    private Voice? _music;
    private Voice? _card;
    private string? _cardPath;
    private int _lastPiece;
    private bool _musicOwned;
    private bool _battle;
    private bool _paused;
    private float _sourceGain = 1f;

    /// <param name="pack">The replacements.</param>
    /// <param name="sampleRate">The output's rate (the files are converted to it).</param>
    public PackMixer(AudioPack pack, int sampleRate)
    {
        _pack = pack;
        SampleRate = sampleRate;
    }

    /// <summary>The game timer's ticks rendered so far by <see cref="Source"/> (when the requests are due); null: every request is
    /// due at once.</summary>
    public Func<long>? RenderedTicks { get; set; }

    public int SampleRate { get; }

    /// <summary>The game's own sound (the drivers); null renders silence under the replacements.</summary>
    public IPcmSource? Source { get; set; }

    /// <summary>True while a replacement mutes the game's sound (tests, diagnostics).</summary>
    public bool SourceMuted { get; private set; }

    /// <summary>How many requests a replacement answered (diagnostics: the pack was used).</summary>
    public int Replaced { get; private set; }

    /// <summary>Notes a request the drivers carry out at <paramref name="tick"/> (music: the towns-and-map driver, effect: the battle driver).</summary>
    public void Request(long tick, AudioKind kind, int number)
    {
        lock (_requestLock)
        {
            // in tick order, as the drivers carry them out; one tick keeps the order of the calls
            var at = _requests.Count;
            while (at > 0 && _requests[at - 1].Tick > tick)
            {
                at--;
            }
            _requests.Insert(at, (tick, kind, number));
        }
    }

    /// <summary>A music number carried out at once (after every waiting request, as the drivers do: IAudio.PlayMusicNow).</summary>
    public void RequestNow(int number)
    {
        lock (_requestLock)
        {
            for (var i = 0; i < _requests.Count; i++)
            {
                _requests[i] = _requests[i] with { Tick = long.MinValue };
            }
            _requests.Add((long.MinValue, AudioKind.Music, number));
        }
    }

    public void Render(Span<short> interleavedStereo)
    {
        lock (_renderLock)
        {
            if (Source is { } source)
            {
                source.Render(interleavedStereo);
            }
            else
            {
                interleavedStereo.Clear();
            }
            CarryDue(RenderedTicks?.Invoke() ?? long.MaxValue);
            Mix(interleavedStereo);
        }
    }

    private void CarryDue(long tick)
    {
        while (true)
        {
            (long Tick, AudioKind Kind, int Number) request;
            lock (_requestLock)
            {
                if (_requests.Count == 0 || _requests[0].Tick > tick)
                {
                    return;
                }
                request = _requests[0];
                _requests.RemoveAt(0);
            }
            Carry(request.Kind, request.Number);
        }
    }

    private void Carry(AudioKind kind, int number)
    {
        if (kind == AudioKind.Card)
        {
            CarryCard(number);
            return;
        }
        if (kind == AudioKind.Effect)
        {
            if (!_battle)
            {
                // the battle driver replaces the towns-and-map driver: its music ends
                _battle = true;
                StopMusic(SwitchSeconds);
                StopCard(SwitchSeconds);
                _lastPiece = 0;
                _musicOwned = false;
            }
            if (_pack.Find(AudioKind.Effect, number) is { } effect)
            {
                _effects.Add(Start(effect));
                Replaced++;
            }
            return;
        }
        if (_battle)
        {
            _battle = false;
            foreach (var voice in _effects)
            {
                FadeOut(voice, SwitchSeconds);
            }
            _effects.Clear();
        }
        switch (number)
        {
            case GameSounds.Stop:
                StopMusic(StopSeconds);
                StopCard(StopSeconds);
                _lastPiece = 0;
                break;
            case GameSounds.Reset or GameSounds.Silence:
                StopMusic(SwitchSeconds);
                StopCard(SwitchSeconds);
                _lastPiece = 0;
                break;
            case GameSounds.Pause:
                _paused = true;
                break;
            case GameSounds.Resume:
                _paused = false;
                break;
            case GameSounds.CurrentPiece or GameSounds.Ready or 4:
                break;
            default:
                _lastPiece = number;
                _paused = false;
                if (_card is not null)
                {
                    // a card's music owns the music: the game's piece plays on, muted, and comes back with the next card that has no music
                    break;
                }
                StopMusic(SwitchSeconds);
                if (_pack.Find(AudioKind.Music, number) is { } piece)
                {
                    _music = Start(piece);
                    _musicOwned = true;
                    Replaced++;
                }
                else
                {
                    _musicOwned = false;
                }
                break;
        }
    }

    private Voice Start(AudioReplacement replacement) => new(_pack.ClipAsync(replacement, SampleRate), replacement.Loop, replacement.Volume);

    /// <summary>A card is shown (<paramref name="card"/> -1: music is off, no card music).</summary>
    private void CarryCard(int card)
    {
        if (card < 0 || _pack.FindCard(card) is not { } music)
        {
            if (_card is not null)
            {
                // a card without music: the game's own current piece again (its replacement from the start, if the pack has one)
                StopCard(SwitchSeconds);
                _musicOwned = false;
                if (!_battle && _lastPiece != 0 && _pack.Find(AudioKind.Music, _lastPiece) is { } piece)
                {
                    _music = Start(piece);
                    _musicOwned = true;
                }
            }
            return;
        }
        if (_card is not null && _cardPath == music.Path)
        {
            return;     // the same file: it keeps playing, no restart
        }
        StopMusic(SwitchSeconds);
        StopCard(SwitchSeconds);
        _card = new Voice(_pack.ClipAsync(music.Path, SampleRate), music.Loop, music.Volume);
        _cardPath = music.Path;
        _musicOwned = true;
        Replaced++;
    }

    private void StopCard(double seconds)
    {
        if (_card is { } card)
        {
            FadeOut(card, seconds);
            _card = null;
            _cardPath = null;
        }
    }

    private void StopMusic(double seconds)
    {
        if (_music is { } music)
        {
            FadeOut(music, seconds);
            _music = null;
        }
    }

    private void FadeOut(Voice voice, double seconds)
    {
        voice.FadeStep = (float)(1.0 / Math.Max(1, seconds * SampleRate));
        _fading.Add(voice);
    }

    private void Mix(Span<short> output)
    {
        var frames = output.Length / 2;
        _effects.RemoveAll(v => v.Finished);
        _fading.RemoveAll(v => v.Finished || v.Gain <= 0);
        // the game's sound is muted while a replacement owns it (a file that could not be read owns nothing)
        var muted = (_musicOwned && !((_card ?? _music)?.Failed ?? false)) || _effects.Exists(v => !v.Failed);
        SourceMuted = muted;
        var target = muted ? 0f : 1f;
        var gainStep = (float)(1.0 / (MuteSeconds * SampleRate));
        if (_mix.Length < output.Length)
        {
            _mix = new float[output.Length];
        }
        var mix = _mix.AsSpan(0, output.Length);
        mix.Clear();
        if (!_paused)
        {
            _music?.AddTo(mix);
            _card?.AddTo(mix);
            foreach (var voice in _effects)
            {
                voice.AddTo(mix);
            }
            foreach (var voice in _fading)
            {
                voice.AddTo(mix);
            }
        }
        if (_sourceGain == 1f && target == 1f && !mix.ContainsAnyExcept(0f))
        {
            return;     // nothing replaced: the game's sound as it is
        }
        for (var f = 0; f < frames; f++)
        {
            _sourceGain = _sourceGain < target ? Math.Min(target, _sourceGain + gainStep) : Math.Max(target, _sourceGain - gainStep);
            for (var c = 0; c < 2; c++)
            {
                var i = 2 * f + c;
                output[i] = (short)Math.Clamp(output[i] * _sourceGain + mix[i] * 32767f, short.MinValue, short.MaxValue);
            }
        }
    }

    /// <summary>A replacement playing: its clip (decoded on a worker thread), position, loop, volume and fade.</summary>
    private sealed class Voice(Task<AudioClip?> clip, bool loop, float volume)
    {
        private int _position;

        public float Gain { get; private set; } = 1f;

        /// <summary>Per frame, while fading out (0: not fading).</summary>
        public float FadeStep { get; set; }

        /// <summary>The file could not be read: the game's sound plays.</summary>
        public bool Failed => clip.IsCompleted && clip.Result is null;

        public bool Finished => Failed || (clip.IsCompleted && !loop && _position >= clip.Result!.Samples.Length);

        public void AddTo(Span<float> mix)
        {
            // a clip still being decoded starts when it is ready (the game's sound stays muted meanwhile)
            if (!clip.IsCompletedSuccessfully || clip.Result is not { } decoded || decoded.Samples.Length == 0)
            {
                return;
            }
            var samples = decoded.Samples;
            for (var i = 0; i < mix.Length; i++)
            {
                if (_position >= samples.Length)
                {
                    if (!loop)
                    {
                        return;
                    }
                    _position = 0;
                }
                if (FadeStep > 0 && (i & 1) == 0)
                {
                    Gain = Math.Max(0, Gain - FadeStep);
                }
                mix[i] += samples[_position++] * volume * Gain;
            }
        }
    }
}
