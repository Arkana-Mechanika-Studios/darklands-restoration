using Darklands.Audio.Packs;
using Darklands.Content;
using Darklands.Platform;

namespace Darklands.Audio.Tests;

/// <summary>
/// Audio packs: the decorator forwards every call to the game's audio unchanged and returns its answers; the drivers under
/// a pack render the same timer and the same events as without one; the mixer mutes them only while a replacement plays.
/// </summary>
public sealed class PackAudioTests : IDisposable
{
    private const int Rate = 22050;
    private readonly string _folder = Directory.CreateTempSubdirectory("dkpack").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    // ---------------------------------------------------------------- the decorator

    /// <summary>The game's audio as a test sees it: every call in order, with answers that differ per call.</summary>
    private sealed class RecordingAudio : IAudio
    {
        public List<string> Calls { get; } = [];

        public void PlayMusic(int tune) => Calls.Add($"music {tune}");

        public void PlayMusicAt(int tune, long tick) => Calls.Add($"at {tune} {tick}");

        public void StopMusic() => Calls.Add("stop");

        public void PlaySound(int effect) => Calls.Add($"sound {effect}");

        public int PlayMusicNow(int tune)
        {
            Calls.Add($"now {tune}");
            return 0x100 + tune * 3 + Calls.Count;
        }

        public int RequestCurrentPiece()
        {
            Calls.Add("current");
            return 0x200 + Calls.Count;
        }
    }

    private sealed class FixedTimer(long tick) : IGameTimer
    {
        public long Ticks => tick;

        public long RequestTick => tick + 2;

        public void Follow(TimerTrack? track, long afterTick)
        {
        }
    }

    /// <summary>A session's calls: pieces (replaced and not), controls, effects, number 5 and timed cues.</summary>
    private static List<int> Script(IAudio audio)
    {
        var answers = new List<int>();
        audio.PlayMusic(GameSounds.Ready);
        audio.PlayMusic(0x14);
        answers.Add(audio.RequestCurrentPiece());
        audio.PlayMusicAt(0x2F, 40);
        audio.PlayMusic(0x0B);
        answers.Add(audio.PlayMusicNow(GameSounds.CurrentPiece));
        answers.Add(audio.PlayMusicNow(GameSounds.Stop));
        audio.StopMusic();
        audio.PlaySound(0x11);
        audio.PlaySound(0x0A);
        audio.PlayMusic(GameSounds.Pause);
        audio.PlayMusic(GameSounds.Resume);
        audio.PlayMusic(0x13);
        answers.Add(audio.PlayMusicNow(0x14));
        answers.Add(GameSounds.RequestCurrentPiece(audio, musicOn: true));
        answers.Add(GameSounds.RequestCurrentPiece(audio, musicOn: false));
        return answers;
    }

    [Fact]
    public void EveryCallReachesTheGameAudioUnchangedAndItsAnswersComeBack()
    {
        var plain = new RecordingAudio();
        var plainAnswers = Script(plain);
        foreach (var pack in new[] { AudioPack.Empty, ExamplePack() })
        {
            var inner = new RecordingAudio();
            var packed = new PackAudio(inner, new FixedTimer(10), new PackMixer(pack, Rate));
            Assert.Equal(plainAnswers, Script(packed));
            Assert.Equal(plain.Calls, inner.Calls);
        }
    }

    /// <summary>
    /// The drivers under a pack: the same requests, ticks, MIDI and sample transfers at the same output frames, the same AX
    /// answers and the same timer as without a pack (needs the game files; passes without checking otherwise).
    /// </summary>
    [Theory]
    [InlineData(SoundCard.SoundBlaster)]
    [InlineData(SoundCard.AdLib)]
    public void TheDriversRunTheSameUnderAPack(SoundCard card)
    {
        if (GameFolder() is not { } files)
        {
            return;
        }
        var (plainEvents, plainLog, _) = RunDrivers(files, card, null);
        var (packedEvents, packedLog, replaced) = RunDrivers(files, card, ExamplePack());
        Assert.True(plainEvents.Count(e => e.Kind == AudioEventKind.Tick) > 400);
        Assert.Equal(4, replaced);     // 14h twice, 13h and the effect 11h
        Assert.Equal(plainEvents, packedEvents);
        Assert.Equal(plainLog, packedLog);
    }

    /// <summary>Without a replacement playing the mixer passes the drivers' samples through as they are.</summary>
    [Fact]
    public void NothingReplacedLeavesTheSoundAsItIs()
    {
        if (GameFolder() is not { } files)
        {
            return;
        }
        var setup = SoundSetup.ForFmCard(files, new SoundCardSettings(SoundCard.SoundBlaster));
        var plain = new SynthAudio(files, Rate, setup);
        var under = new SynthAudio(files, Rate, setup);
        var mixer = new PackMixer(ExamplePack(), Rate) { Source = under, RenderedTicks = () => under.RenderedTicks };
        var packed = new PackAudio(under, under, mixer);
        plain.PlayMusic(0x0B);
        packed.PlayMusic(0x0B);
        var a = new short[2 * 4096];
        var b = new short[2 * 4096];
        for (var block = 0; block < 40; block++)
        {
            plain.Render(a);
            mixer.Render(b);
            Assert.Equal(a, b);
        }
    }

    private static (List<AudioEvent> Events, List<string> Log, int Replaced) RunDrivers(GameFiles files, SoundCard card, AudioPack? pack)
    {
        var events = new List<AudioEvent>();
        var log = new List<string>();
        var synth = new SynthAudio(files, Rate, SoundSetup.ForFmCard(files, new SoundCardSettings(card)), null, events.Add);
        IAudio audio = synth;
        IPcmSource source = synth;
        PackMixer? mixer = null;
        if (pack is not null)
        {
            mixer = new PackMixer(pack, Rate) { Source = synth, RenderedTicks = () => synth.RenderedTicks };
            pack.PreloadAsync(Rate).GetAwaiter().GetResult();
            audio = new PackAudio(synth, synth, mixer);
            source = mixer;
        }
        var buffer = new short[2 * 1024];
        void Render(int blocks)
        {
            for (var i = 0; i < blocks; i++)
            {
                source.Render(buffer);
            }
            log.Add($"ticks {synth.RenderedTicks} heard {synth.Ticks} request {synth.RequestTick} frames {synth.RenderedFrames}");
        }
        Render(5);
        audio.PlayMusic(0x14);     // replaced by the example pack
        Render(40);
        log.Add($"ax {audio.RequestCurrentPiece()}");
        audio.PlayMusicAt(0x13, synth.RenderedTicks + 30);     // replaced (manifest)
        Render(60);
        audio.PlayMusic(0x0B);     // not replaced
        Render(40);
        log.Add($"ax {audio.PlayMusicNow(GameSounds.CurrentPiece)}");
        audio.StopMusic();
        Render(10);
        audio.PlaySound(0x11);     // replaced
        audio.PlaySound(0x0A);     // not replaced
        Render(30);
        audio.PlayMusic(0x14);
        Render(20);
        log.Add($"ax {GameSounds.RequestCurrentPiece(audio, musicOn: false)}");
        audio.PlayMusic(GameSounds.Pause);
        Render(5);
        audio.PlayMusic(GameSounds.Resume);
        Render(5);
        log.Add($"fault {synth.Fault}");
        return (events, log, mixer?.Replaced ?? 0);
    }

    // ---------------------------------------------------------------- the mixer

    /// <summary>A source of a constant level (the drivers' sound as the mixer sees it).</summary>
    private sealed class ConstantSource(short level) : IPcmSource
    {
        public void Render(Span<short> interleavedStereo) => interleavedStereo.Fill(level);
    }

    private PackMixer ToneMixer(out long[] tick)
    {
        WriteWav(Path.Combine(_folder, "inn-1.wav"), Rate / 2, 0.25f);
        WriteWav(Path.Combine(_folder, "battle-death.wav"), Rate / 10, 0.5f);
        var pack = AudioPack.Load(_folder);
        Assert.Empty(pack.Problems);
        pack.PreloadAsync(Rate).GetAwaiter().GetResult();
        var ticks = new long[1];
        tick = ticks;
        return new PackMixer(pack, Rate) { Source = new ConstantSource(1000), RenderedTicks = () => ticks[0] };
    }

    private static short[] Block(PackMixer mixer, int frames = 2048)
    {
        var buffer = new short[2 * frames];
        mixer.Render(buffer);
        return buffer;
    }

    [Fact]
    public void AReplacedPieceMutesTheDriversUntilAnotherPiece()
    {
        var mixer = ToneMixer(out var tick);
        Assert.All(Block(mixer), s => Assert.Equal(1000, s));
        mixer.Request(5, AudioKind.Music, 0x09);
        Assert.All(Block(mixer), s => Assert.Equal(1000, s));      // not due before its tick
        tick[0] = 5;
        Block(mixer);
        Assert.True(mixer.SourceMuted);
        var playing = Block(mixer);
        Assert.All(playing, s => Assert.Equal((short)(0.25f * 32767), s));     // the replacement alone (looping)
        mixer.Request(5, AudioKind.Music, GameSounds.Stop);
        Block(mixer, Rate + Rate / 10);     // the replacement fades out (1 s) ...
        Assert.True(mixer.SourceMuted);     // ... and so does the drivers' piece, unheard
        Assert.All(Block(mixer), s => Assert.Equal(0, s));
        mixer.Request(5, AudioKind.Music, 0x0B);
        Block(mixer);
        Assert.False(mixer.SourceMuted);
        Assert.All(Block(mixer), s => Assert.Equal(1000, s));
        Assert.Equal(1, mixer.Replaced);
    }

    [Fact]
    public void AReplacedEffectMutesTheDriversWhileItPlays()
    {
        var mixer = ToneMixer(out _);
        mixer.Request(0, AudioKind.Effect, 0x0B);       // not replaced
        Assert.All(Block(mixer), s => Assert.Equal(1000, s));
        mixer.Request(0, AudioKind.Effect, 0x0A);
        var first = Block(mixer, Rate / 20);
        Assert.True(mixer.SourceMuted);
        Assert.Equal((short)(0.5f * 32767), first[^1]);
        Block(mixer, Rate / 10);        // the effect (0.1 s) is over
        Block(mixer);
        Assert.False(mixer.SourceMuted);
        Assert.All(Block(mixer), s => Assert.Equal(1000, s));
    }

    [Fact]
    public void PauseHoldsTheReplacementAndNumberFiveChangesNothing()
    {
        var mixer = ToneMixer(out _);
        mixer.Request(0, AudioKind.Music, 0x0A);       // not replaced
        mixer.Request(0, AudioKind.Music, 0x09);
        Block(mixer);
        mixer.RequestNow(GameSounds.CurrentPiece);
        Block(mixer);
        Assert.True(mixer.SourceMuted);
        mixer.Request(0, AudioKind.Music, GameSounds.Pause);
        Assert.All(Block(mixer), s => Assert.Equal(0, s));
        mixer.Request(0, AudioKind.Music, GameSounds.Resume);
        Assert.All(Block(mixer), s => Assert.Equal((short)(0.25f * 32767), s));
    }

    [Fact]
    public async Task AFileThatCannotBeReadLeavesTheGameSound()
    {
        File.WriteAllText(Path.Combine(_folder, "inn-1.ogg"), "not a sound");
        var pack = AudioPack.Load(_folder);
        var mixer = new PackMixer(pack, Rate) { Source = new ConstantSource(1000) };
        mixer.Request(0, AudioKind.Music, 0x09);
        await pack.PreloadAsync(Rate);
        Block(mixer);
        Block(mixer);
        Assert.False(mixer.SourceMuted);
        Assert.All(Block(mixer), s => Assert.Equal(1000, s));
        Assert.Contains(pack.Problems, p => p.Contains("inn-1.ogg", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- packs, files, catalogue

    [Fact]
    public void ThePackFindsFilesByNameAndTheManifestWins()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "music"));
        WriteWav(Path.Combine(_folder, "music", "inn-1.wav"), 100, 0.1f);
        WriteWav(Path.Combine(_folder, "MUSIC-19.wav"), 100, 0.1f);
        WriteWav(Path.Combine(_folder, "thud.wav"), 100, 0.1f);
        WriteWav(Path.Combine(_folder, "no-such-cue.wav"), 100, 0.1f);
        File.WriteAllText(Path.Combine(_folder, AudioPack.ManifestName), """
            // comments and trailing commas are fine
            { "volume": 0.5, "cues": { "battle-death": { "file": "thud.wav", "volume": 2 }, "inn-1": { "loop": false }, "music-05": "thud.wav", } }
            """);
        var pack = AudioPack.Load(_folder);
        Assert.Equal(3, pack.Count);
        Assert.Equal(new AudioReplacement(AudioCatalogue.Find("inn-1")!, Path.Combine(_folder, "music", "inn-1.wav"), false, 0.5f),
            pack.Find(AudioKind.Music, 0x09));
        Assert.Equal(1f, pack.Find(AudioKind.Effect, 0x0A)!.Volume);
        Assert.True(pack.Find(AudioKind.Music, 0x19)!.Loop);
        Assert.Null(pack.Find(AudioKind.Music, 0x0A));
        Assert.Null(pack.Find(AudioKind.Music, 5));
        Assert.Contains(pack.Problems, p => p.Contains("no-such-cue.wav", StringComparison.Ordinal));
        Assert.Contains(pack.Problems, p => p.Contains("\"music-05\"", StringComparison.Ordinal));
        Assert.Single(AudioPack.Load(Path.Combine(_folder, "missing")).Problems);
    }

    [Fact]
    public async Task TheExamplePackLoadsAndDecodes()
    {
        var pack = ExamplePack();
        Assert.Empty(pack.Problems);
        Assert.Equal(["travel-1", "travel-2", "battle-swing"], pack.Replacements.Select(r => r.Cue.Name));
        foreach (var replacement in pack.Replacements)
        {
            var clip = await pack.ClipAsync(replacement, 44100);
            Assert.NotNull(clip);
            Assert.True(clip.Frames > 44100 / 10);
            Assert.Contains(clip.Samples, s => Math.Abs(s) > 0.05f);
        }
        Assert.Empty(pack.Problems);
        Assert.Equal(0.8f * 0.6f, pack.Find(AudioKind.Music, 0x13)!.Volume, 5);
    }

    [Fact]
    public void WaveFilesOfTheUsualFormatsAreRead()
    {
        byte[] Wave(short format, short channels, short bits, byte[] data) =>
        [
            .. "RIFF"u8, .. BitConverter.GetBytes(36 + data.Length), .. "WAVE"u8, .. "fmt "u8, .. BitConverter.GetBytes(16),
            .. BitConverter.GetBytes(format), .. BitConverter.GetBytes(channels), .. BitConverter.GetBytes(8000),
            .. BitConverter.GetBytes(8000 * channels * bits / 8), .. BitConverter.GetBytes((short)(channels * bits / 8)),
            .. BitConverter.GetBytes(bits), .. "LIST"u8, .. BitConverter.GetBytes(3), 1, 2, 3, 0, .. "data"u8, .. BitConverter.GetBytes(data.Length), .. data,
        ];
        Assert.Equal([0.5f, -0.5f], AudioClip.ReadWav(Wave(1, 1, 16, [0x00, 0x40, 0x00, 0xC0])).Samples);
        Assert.Equal([0f, 0.5f], AudioClip.ReadWav(Wave(1, 2, 8, [0x80, 0xC0])).Samples);
        Assert.Equal([-0.5f], AudioClip.ReadWav(Wave(1, 1, 24, [0x00, 0x00, 0xC0])).Samples);
        Assert.Equal([0.25f], AudioClip.ReadWav(Wave(3, 1, 32, BitConverter.GetBytes(0.25f))).Samples);
        Assert.Throws<InvalidDataException>(() => AudioClip.ReadWav(Wave(2, 1, 4, [0])));
        Assert.Throws<InvalidDataException>(() => AudioClip.ReadWav("RIFX"u8.ToArray()));
        Assert.Equal([0f, 0f, 0.5f, 0.5f, 1f, 1f, 1f, 1f], AudioClip.Resample([0f, 0f, 1f, 1f], 1, 2));
    }

    [Fact]
    public void TheCatalogueNamesEveryNumberOnce()
    {
        Assert.Equal(AudioCatalogue.All.Count, AudioCatalogue.All.Select(c => c.Name).Distinct().Count());
        Assert.Equal(AudioCatalogue.All.Count, AudioCatalogue.All.Select(c => (c.Kind, c.Number)).Distinct().Count());
        Assert.All(AudioCatalogue.All, c => Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", c.Name));
        Assert.DoesNotContain(AudioCatalogue.All, c => c.Kind == AudioKind.Music && AudioCatalogue.IsControl(c.Number));
        Assert.Equal(0x14, AudioCatalogue.Find("Travel-2")!.Number);
        Assert.Equal(AudioCatalogue.Find("travel-1"), AudioCatalogue.Find("music-13"));
        Assert.Equal("music-19", AudioCatalogue.Find("MUSIC-19")!.Name);
        Assert.Equal("battle-death", AudioCatalogue.Find("effect-0a")!.Name);
        Assert.Null(AudioCatalogue.Find("music-06"));
        Assert.Null(AudioCatalogue.Find("music-1"));
    }

    /// <summary>The guide's catalogue (docs/PACKS.md) lists every cue as the code names it.</summary>
    [Fact]
    public void TheGuideListsTheCatalogue()
    {
        var guide = File.ReadAllLines(Path.Combine(RepositoryFolder(), "docs", "PACKS.md"));
        var rows = guide.Where(l => l.StartsWith("| `", StringComparison.Ordinal) && (l.Contains("| music |", StringComparison.Ordinal)
            || l.Contains("| effect |", StringComparison.Ordinal))).ToList();
        Assert.Equal(AudioCatalogue.All.Select(c =>
                $"| `{c.Name}` | {(c.Kind == AudioKind.Music ? "music" : "effect")} | {c.Number:X2}h | {(c.Loops ? "yes" : "no")} | {c.Where} |"),
            rows);
    }

    // ---------------------------------------------------------------- helpers

    private static void WriteWav(string path, int frames, float level)
    {
        var samples = new short[frames * 2];
        Array.Fill(samples, (short)Math.Round(level * 32768));
        WavFile.Write(path, samples, Rate);
    }

    /// <summary>The repository's example pack (docs/audio-pack-example).</summary>
    private static AudioPack ExamplePack() => AudioPack.Load(Path.Combine(RepositoryFolder(), "docs", "audio-pack-example"));

    private static string RepositoryFolder()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Darklands.slnx")))
        {
            folder = folder.Parent;
        }
        Assert.NotNull(folder);
        return folder.FullName;
    }

    private static GameFiles? GameFolder()
    {
        var folder = LocalPaths.Get("game", "DARKLANDS_DIR") ?? "";
        return GameFiles.IsGameFolder(folder) && File.Exists(Path.Combine(folder, "ASOUND.DC")) ? new GameFiles(folder) : null;
    }
}
