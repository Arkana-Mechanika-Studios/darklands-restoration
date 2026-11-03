using Darklands.Audio.Packs;
using Darklands.Platform;

namespace Darklands.Audio.Tests;

/// <summary>
/// Music per card (docs/PACKS.md, docs/MODS.md): a card with music in the pack plays it in place of the game's piece, the next
/// card with the same file keeps it, a card without one hands the game's piece back, a battle ends it, music off means none,
/// pause holds it, a loaded game's card starts it again. The notice is read-only: nothing reaches the drivers.
/// </summary>
public sealed class CardMusicTests : IDisposable
{
    private const int Rate = 22050;
    private const short Game = 1000;
    private static readonly short Quarter = (short)(0.25f * 32767);
    private readonly string _folder = Directory.CreateTempSubdirectory("dkcard").FullName;
    private readonly string _program;

    public CardMusicTests()
    {
        _program = Directory.CreateDirectory(Path.Combine(_folder, "program")).FullName;
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private class ConstantSource(short level) : IPcmSource
    {
        public void Render(Span<short> interleavedStereo) => interleavedStereo.Fill(level);
    }

    private string Wav(string name, int frames, float level)
    {
        var path = Path.Combine(_folder, "pack", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var samples = new short[frames * 2];
        Array.Fill(samples, (short)Math.Round(level * 32768));
        WavFile.Write(path, samples, Rate);
        return path;
    }

    /// <summary>A file that counts up, a step a frame (so that a restart is seen).</summary>
    private string Ramp(string name, int frames)
    {
        var path = Path.Combine(_folder, "pack", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var samples = new short[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            samples[2 * i] = samples[2 * i + 1] = (short)(1 + i);
        }
        WavFile.Write(path, samples, Rate);
        return path;
    }

    private (PackMixer Mixer, AudioPack Pack, long[] Tick) Mixer(string packJson, bool includeMidi = false)
    {
        File.WriteAllText(Path.Combine(_folder, "pack", "pack.json"), packJson);
        var pack = AudioPack.Load(Path.Combine(_folder, "pack"), programFolder: _program);
        pack.PreloadAsync(Rate, includeMidi).GetAwaiter().GetResult();
        var ticks = new long[1];
        return (new PackMixer(pack, Rate) { Source = new ConstantSource(Game), RenderedTicks = () => ticks[0] }, pack, ticks);
    }

    private static short[] Block(PackMixer mixer, int frames = 2048)
    {
        var buffer = new short[2 * frames];
        mixer.Render(buffer);
        return buffer;
    }

    // ---------------------------------------------------------------- pack.json

    [Fact]
    public void ThePackNamesCardMusicByHexNumberInEitherForm()
    {
        Wav("a.wav", 100, 0.1f);
        Wav("sub/b.wav", 100, 0.1f);
        var (_, pack, _) = Mixer("""
            // comments are fine
            { "card:1D": "a.wav", "card:0c": { "file": "sub/b.wav", "loop": false, "volume": 0.5 }, "soundfont": "x.sf2", "assets": { }, }
            """);
        Assert.Equal([0x0C, 0x1D], pack.CardMusics.Select(m => m.Card));
        Assert.Equal(new CardMusic(0x1D, Path.Combine(_folder, "pack", "a.wav"), true, 1f), pack.FindCard(0x1D));
        Assert.Equal(new CardMusic(0x0C, Path.Combine(_folder, "pack", "sub", "b.wav"), false, 0.5f), pack.FindCard(0x0C));
        Assert.Null(pack.FindCard(0x1E));
        Assert.DoesNotContain(pack.Problems, p => p.Contains("card:", StringComparison.Ordinal));
    }

    [Fact]
    public void ABadCardEntryIsSaidAndIgnored()
    {
        Wav("a.wav", 100, 0.1f);
        File.WriteAllText(Path.Combine(_folder, "pack", "notes.txt"), "x");
        var (_, pack, _) = Mixer("""
            { "card:zz": "a.wav", "card:1E": "missing.wav", "card:1F": "notes.txt", "card:20": 5, "card:21": { "file": "a.wav", "loops": true }, "card:-1": "a.wav" }
            """);
        Assert.Equal([0x21], pack.CardMusics.Select(m => m.Card));
        Assert.Contains(pack.Problems, p => p.Contains("card:zz", StringComparison.Ordinal));
        Assert.Contains(pack.Problems, p => p.Contains("missing.wav", StringComparison.Ordinal));
        Assert.Contains(pack.Problems, p => p.Contains("not a .ogg, .wav or .mid", StringComparison.Ordinal));
        Assert.Contains(pack.Problems, p => p.Contains("card:20", StringComparison.Ordinal));
        Assert.Contains(pack.Problems, p => p.Contains("\"loops\"", StringComparison.Ordinal));
        Assert.Contains(pack.Problems, p => p.Contains("card:-1", StringComparison.Ordinal));
    }

    [Fact]
    public void ThePacksMappingWinsOverAModsMusicForTheSameCard()
    {
        Wav("a.wav", 100, 0.1f);
        var mod1 = Wav("mod1.wav", 100, 0.1f);
        var mod2 = Wav("mod2.wav", 100, 0.1f);
        var (_, pack, _) = Mixer("""{ "card:1D": "a.wav" }""");
        Assert.Empty(pack.AddModMusic([(0x1D, mod1), (0x1E, mod1), (0x1E, mod2)]));
        Assert.EndsWith("a.wav", pack.FindCard(0x1D)!.Path, StringComparison.Ordinal);
        Assert.Equal(mod2, pack.FindCard(0x1E)!.Path);     // of two mods, the later wins
        Assert.Equal([0x1D, 0x1E], pack.CardMusics.Select(m => m.Card));
    }

    [Fact]
    public void ModMusicNeedsNoAudioPackAndAMidiOneWithoutASoundFontIsSaidOnce()
    {
        var pack = AudioPack.ForMods(programFolder: _program);
        Assert.Empty(pack.Problems);
        var midi = Path.Combine(_folder, "x.mid");
        File.WriteAllBytes(midi, TestSoundFont.Midi());
        var said = pack.AddModMusic([(400, midi), (401, midi)]);
        Assert.Single(said);
        Assert.Contains("SOUNDFONT", said[0], StringComparison.Ordinal);
        Assert.Equal(said, pack.Problems);
        Assert.NotNull(pack.FindCard(400));
        // a file that is not there gives a line and the game's own music
        var gone = pack.AddModMusic([(402, Path.Combine(_folder, "gone.ogg"))]);
        Assert.Contains("gone.ogg", Assert.Single(gone), StringComparison.Ordinal);
        Assert.Null(pack.FindCard(402));
    }

    // ---------------------------------------------------------------- the rules

    [Fact]
    public void ItStartsWhenTheCardShowsAndTheGamesPieceIsMuted()
    {
        Wav("a.wav", 4000, 0.25f);
        var (mixer, _, tick) = Mixer("""{ "card:1D": "a.wav" }""");
        mixer.Request(5, AudioKind.Card, 0x1D);
        Assert.All(Block(mixer), s => Assert.Equal(Game, s));       // not due before its tick
        tick[0] = 5;
        Block(mixer);
        Assert.True(mixer.SourceMuted);
        Assert.All(Block(mixer), s => Assert.Equal(Quarter, s));
        Assert.Equal(1, mixer.Replaced);
    }

    [Fact]
    public void ACardWithoutMusicLeavesTheGameAlone()
    {
        Wav("a.wav", 4000, 0.25f);
        var (mixer, _, _) = Mixer("""{ "card:1D": "a.wav" }""");
        mixer.Request(0, AudioKind.Card, 0x1E);
        mixer.Request(0, AudioKind.Card, -1);
        Block(mixer);
        Assert.False(mixer.SourceMuted);
        Assert.All(Block(mixer), s => Assert.Equal(Game, s));
        Assert.Equal(0, mixer.Replaced);
    }

    [Fact]
    public void TheNextCardWithTheSameFileKeepsItPlayingAnotherFileReplacesIt()
    {
        Ramp("ramp.wav", 20000);
        Wav("b.wav", 4000, 0.5f);
        var (mixer, _, _) = Mixer("""{ "card:1D": "ramp.wav", "card:1E": "ramp.wav", "card:1F": "b.wav", "card:20": { "file": "ramp.wav", "volume": 1 } }""");
        mixer.Request(0, AudioKind.Card, 0x1D);
        Block(mixer, 1000);
        var first = Block(mixer, 1000);
        Assert.InRange(first[0], 900, 1100);                        // the ramp is at its frame 1000
        mixer.Request(0, AudioKind.Card, 0x1E);                     // the same file: no restart
        var second = Block(mixer, 1000);
        Assert.True(second[0] > first[^1], $"{second[0]} should go on from {first[^1]}");
        Assert.Equal(1, mixer.Replaced);
        mixer.Request(0, AudioKind.Card, 0x1F);                     // another file: replaces it
        Block(mixer, 2000);
        Assert.All(Block(mixer, 1000), s => Assert.Equal((short)(0.5f * 32767), s));
        mixer.Request(0, AudioKind.Card, 0x20);                     // back to the ramp file: a new start
        Block(mixer, 2000);
        Assert.InRange(Block(mixer, 100)[0], 1, 4000);              // the ramp is near its start, not at frame 3000
        Assert.Equal(3, mixer.Replaced);
    }

    [Fact]
    public void ACardWithoutAFileBringsTheGamesCurrentPieceBack()
    {
        Wav("a.wav", 4000, 0.25f);
        var (mixer, _, _) = Mixer("""{ "card:1D": "a.wav" }""");
        mixer.Request(0, AudioKind.Music, 0x0B);                    // the game's piece, not replaced: the drivers' own
        Block(mixer);
        mixer.Request(0, AudioKind.Card, 0x1D);
        Block(mixer);
        Assert.True(mixer.SourceMuted);
        mixer.Request(0, AudioKind.Music, 0x0C);                    // the game moves on to another piece meanwhile: only remembered
        Assert.All(Block(mixer), s => Assert.Equal(Quarter, s));
        Assert.True(mixer.SourceMuted);
        mixer.Request(0, AudioKind.Card, 0x1E);                     // a card without music
        Block(mixer, Rate / 5);
        Assert.False(mixer.SourceMuted);
        Assert.All(Block(mixer), s => Assert.Equal(Game, s));
    }

    [Fact]
    public void TheHandedBackPieceIsItsReplacementWhenThePackHasOne()
    {
        Wav("a.wav", 4000, 0.25f);
        Wav("inn-1.wav", 4000, 0.1f);
        var (mixer, _, _) = Mixer("""{ "card:1D": "a.wav" }""");
        mixer.Request(0, AudioKind.Music, 0x09);                    // inn-1 is a cue of the pack
        Block(mixer);
        Assert.All(Block(mixer), s => Assert.Equal((short)(0.1f * 32767), s));
        mixer.Request(0, AudioKind.Card, 0x1D);
        Block(mixer);
        Assert.All(Block(mixer), s => Assert.Equal(Quarter, s));    // the card's music, not the cue
        mixer.Request(0, AudioKind.Card, 0x1E);
        Block(mixer, Rate / 5);
        Assert.All(Block(mixer), s => Assert.Equal((short)(0.1f * 32767), s));      // the cue is back
    }

    [Fact]
    public void ABattleEndsItAndTheNextCardStartsItAgain()
    {
        Wav("a.wav", 4000, 0.25f);
        var (mixer, _, _) = Mixer("""{ "card:1D": "a.wav" }""");
        mixer.Request(0, AudioKind.Card, 0x1D);
        Block(mixer);
        Assert.All(Block(mixer), s => Assert.Equal(Quarter, s));
        mixer.Request(0, AudioKind.Effect, 0x0B);                   // the battle driver: a battle begins
        Block(mixer, Rate / 5);
        Assert.False(mixer.SourceMuted);
        Assert.All(Block(mixer), s => Assert.Equal(Game, s));
        mixer.Request(0, AudioKind.Card, 0x1D);
        Block(mixer);
        Assert.True(mixer.SourceMuted);
        Assert.All(Block(mixer), s => Assert.Equal(Quarter, s));
    }

    [Fact]
    public void WithMusicOffNoneStartsAndNoneKeepsPlaying()
    {
        Wav("a.wav", 4000, 0.25f);
        var (mixer, _, _) = Mixer("""{ "card:1D": "a.wav" }""");
        mixer.Request(0, AudioKind.Card, -1);                       // the notice of a game with music off
        Block(mixer);
        Assert.False(mixer.SourceMuted);
        mixer.Request(0, AudioKind.Card, 0x1D);
        Block(mixer);
        Assert.True(mixer.SourceMuted);
        mixer.Request(0, AudioKind.Music, GameSounds.Stop);         // the music option turned off plays the stop
        mixer.Request(0, AudioKind.Card, -1);
        Block(mixer, Rate * 2);
        Assert.All(Block(mixer), s => Assert.Equal(0, s));          // silence: the stop of the music option, no card music
    }

    [Fact]
    public void PauseHoldsItAndResumeGoesOn()
    {
        Wav("a.wav", 4000, 0.25f);
        var (mixer, _, _) = Mixer("""{ "card:1D": "a.wav" }""");
        mixer.Request(0, AudioKind.Card, 0x1D);
        Block(mixer);
        mixer.Request(0, AudioKind.Music, GameSounds.Pause);
        Assert.All(Block(mixer), s => Assert.Equal(0, s));
        mixer.Request(0, AudioKind.Music, GameSounds.Resume);
        Assert.All(Block(mixer), s => Assert.Equal(Quarter, s));
    }

    [Fact]
    public void ALoadedGamesCardStartsItAgainAndNothingIsKept()
    {
        Wav("a.wav", 4000, 0.25f);
        var (mixer, pack, _) = Mixer("""{ "card:1D": "a.wav" }""");
        mixer.Request(0, AudioKind.Card, 0x1D);
        Block(mixer);
        // a loaded game: the game resets the sound, then shows its card again
        mixer.Request(0, AudioKind.Music, GameSounds.Reset);
        Block(mixer, Rate / 5);
        Assert.All(Block(mixer), s => Assert.Equal(0, s));
        mixer.Request(0, AudioKind.Card, 0x1D);
        Block(mixer);
        Assert.All(Block(mixer), s => Assert.Equal(Quarter, s));
        Assert.Equal(2, mixer.Replaced);
        Assert.Single(pack.CardMusics);     // the pack is only read; no state of the game holds the music
    }

    [Fact]
    public async Task AFileThatCannotBeReadLeavesTheGamesPiece()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "pack"));
        File.WriteAllText(Path.Combine(_folder, "pack", "bad.ogg"), "not a sound");
        var (mixer, pack, _) = Mixer("""{ "card:1D": "bad.ogg" }""");
        await pack.PreloadAsync(Rate);
        mixer.Request(0, AudioKind.Card, 0x1D);
        Block(mixer);
        Block(mixer);
        Assert.False(mixer.SourceMuted);
        Assert.All(Block(mixer), s => Assert.Equal(Game, s));
        Assert.Contains(pack.Problems, p => p.Contains("bad.ogg", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AMidiCardMusicIsRenderedThroughTheSoundFontAndSkippedWithoutOne()
    {
        var pack = Path.Combine(_folder, "pack");
        Directory.CreateDirectory(pack);
        File.WriteAllBytes(Path.Combine(pack, "tune.mid"), TestSoundFont.Midi(69, 4));
        File.WriteAllText(Path.Combine(pack, "pack.json"), """{ "card:1D": "tune.mid" }""");

        var without = AudioPack.Load(pack, programFolder: _program);
        Assert.Single(without.Problems);     // the file is a card's music (pack.json), not an unknown cue
        Assert.Contains("SoundFont", without.Problems[0], StringComparison.Ordinal);
        var silent = new PackMixer(without, Rate) { Source = new ConstantSource(Game) };
        silent.Request(0, AudioKind.Card, 0x1D);
        await without.PreloadAsync(Rate, includeMidi: true);
        Block(silent);
        Block(silent);
        Assert.False(silent.SourceMuted);
        Assert.All(Block(silent), s => Assert.Equal(Game, s));

        Directory.CreateDirectory(Path.Combine(_program, "SOUNDFONT"));
        File.WriteAllBytes(Path.Combine(_program, "SOUNDFONT", "sine.sf2"), TestSoundFont.Sf2());
        var with = AudioPack.Load(pack, programFolder: _program);
        Assert.Empty(with.Problems);
        var heard = new PackMixer(with, Rate) { Source = new ConstantSource(Game) };
        heard.Request(0, AudioKind.Card, 0x1D);
        Assert.NotNull(await with.ClipAsync(with.FindCard(0x1D)!.Path, Rate));
        Block(heard);
        var block = Block(heard);
        Assert.True(heard.SourceMuted);
        Assert.Contains(block, s => Math.Abs(s) > 500);
    }

    // ---------------------------------------------------------------- the notice

    private sealed class RecordingAudio : IAudio
    {
        public List<string> Calls { get; } = [];

        public List<int> Cards { get; } = [];

        public void PlayMusic(int tune) => Calls.Add($"music {tune}");

        public void StopMusic() => Calls.Add("stop");

        public void PlaySound(int effect) => Calls.Add($"sound {effect}");

        public int PlayMusicNow(int tune)
        {
            Calls.Add($"now {tune}");
            return 7;
        }

        public void CardShown(int card) => Cards.Add(card);
    }

    [Fact]
    public void TheNoticeIsReadOnlyNothingElseReachesTheDriversAndTheDefaultDoesNothing()
    {
        Wav("a.wav", 4000, 0.25f);
        var (mixer, _, _) = Mixer("""{ "card:1D": "a.wav" }""");
        var inner = new RecordingAudio();
        var audio = new PackAudio(inner, null, mixer);
        audio.CardShown(0x1D);
        audio.CardShown(-1);
        Assert.Equal([0x1D, -1], inner.Cards);
        Assert.Empty(inner.Calls);              // no driver call came of it
        Block(mixer);
        Assert.True(mixer.SourceMuted || mixer.Replaced == 1);
        // an audio without the method (every one but the pack's) ignores the notice
        IAudio plain = new SilentAudio();
        plain.CardShown(5);
        Assert.Equal(7, new PackAudio(inner, null, mixer).PlayMusicNow(3));
        Assert.Equal(["now 3"], inner.Calls);
    }
}
