using Darklands.Audio.Packs;

namespace Darklands.Audio.Tests;

/// <summary>
/// .mid cues of an audio pack: a General MIDI file rendered through a SoundFont (here a generated sine-wave one) into a clip,
/// the SoundFont lookup order, and the cue skipped, with one message, when no SoundFont is found.
/// </summary>
public sealed class MidiPackTests : IDisposable
{
    private const int Rate = 22050;
    private readonly string _folder = Directory.CreateTempSubdirectory("dkmidi").FullName;
    private readonly string _program;

    public MidiPackTests()
    {
        _program = Directory.CreateDirectory(Path.Combine(_folder, "program")).FullName;
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string Pack(string name = "pack")
    {
        var pack = Path.Combine(_folder, name);
        Directory.CreateDirectory(pack);
        return pack;
    }

    private static string WriteFont(string path, byte[]? content = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content ?? TestSoundFont.Sf2());
        return path;
    }

    private static string WriteMidi(string path, int key = 69, int beats = 1)
    {
        File.WriteAllBytes(path, TestSoundFont.Midi(key, beats));
        return path;
    }

    private static int ZeroCrossings(float[] stereo, int fromFrame, int toFrame)
    {
        var count = 0;
        for (var f = fromFrame + 1; f < toFrame; f++)
        {
            if ((stereo[2 * (f - 1)] < 0) != (stereo[2 * f] < 0))
            {
                count++;
            }
        }
        return count;
    }

    private class ConstantSource(short level) : IPcmSource
    {
        public void Render(Span<short> interleavedStereo) => interleavedStereo.Fill(level);
    }

    // ---------------------------------------------------------------- rendering

    [Fact]
    public void AMidiFileIsRenderedThroughTheSoundFontIntoAClip()
    {
        var font = SoundFontBank.Load(WriteFont(Path.Combine(_folder, "sine.sf2")));
        var clip = font.RenderMidi(WriteMidi(Path.Combine(_folder, "a.mid")), 44100);
        Assert.Equal(44100, clip.SampleRate);
        // one beat at 120 a minute is half a second; the release rings on a little, and the silence after it is cut
        Assert.InRange(clip.Frames, 44100 / 2, 44100 * 2);
        Assert.Equal(0, clip.Samples.Length % 2);
        Assert.InRange(clip.Samples.Max(x => Math.Abs(x)), 0.01f, 1f);
        // the note is A4 (about 441 Hz): about 440 rising and falling crossings in the half second it is held
        Assert.InRange(ZeroCrossings(clip.Samples, 4410, 4410 + 22050), 400, 480);
        // the two sides carry sound
        Assert.Contains(Enumerable.Range(0, clip.Frames), f => clip.Samples[2 * f + 1] != 0);
    }

    [Fact]
    public void ADifferentNoteIsADifferentPitchAndALongerFileALongerClip()
    {
        var font = SoundFontBank.Load(WriteFont(Path.Combine(_folder, "sine.sf2")));
        var octave = font.RenderMidi(WriteMidi(Path.Combine(_folder, "high.mid"), 81), Rate);
        Assert.InRange(ZeroCrossings(octave.Samples, 2205, 2205 + Rate / 4), 400, 480);     // an octave up: 880 Hz
        var short1 = font.RenderMidi(WriteMidi(Path.Combine(_folder, "one.mid"), 69, 1), Rate);
        var long4 = font.RenderMidi(WriteMidi(Path.Combine(_folder, "four.mid"), 69, 4), Rate);
        Assert.True(long4.Frames > short1.Frames + Rate);
    }

    [Fact]
    public void ABrokenMidiOrSoundFontIsAnInvalidDataException()
    {
        var path = Path.Combine(_folder, "junk.sf2");
        File.WriteAllText(path, "not a soundfont");
        Assert.Throws<InvalidDataException>(() => SoundFontBank.Load(path));
        var font = SoundFontBank.Load(WriteFont(Path.Combine(_folder, "sine.sf2")));
        var junk = Path.Combine(_folder, "junk.mid");
        File.WriteAllText(junk, "not a midi file");
        Assert.Throws<InvalidDataException>(() => font.RenderMidi(junk, Rate));
    }

    [Fact]
    public async Task AMidiCueOfAPackPlaysThroughTheMixerLikeAnyClip()
    {
        var pack = Pack();
        WriteMidi(Path.Combine(pack, "inn-1.mid"));
        WriteFont(Path.Combine(_program, "SOUNDFONT", "sine.sf2"));
        var loaded = AudioPack.Load(pack, programFolder: _program);
        Assert.Empty(loaded.Problems);
        Assert.Equal(1, loaded.Count);
        var mixer = new PackMixer(loaded, Rate) { Source = new ConstantSource(1000) };
        mixer.Request(0, AudioKind.Music, 0x09);
        var replacement = loaded.Find(AudioKind.Music, 0x09)!;
        Assert.NotNull(await loaded.ClipAsync(replacement, Rate));
        var block = new short[4096];
        mixer.Render(block);
        mixer.Render(block);
        mixer.Render(block);
        Assert.True(mixer.SourceMuted);
        Assert.Contains(block, s => Math.Abs(s) > 500 && s != 1000);
        Assert.Equal(1, mixer.Replaced);
    }

    [Fact]
    public async Task AMidiFileIsRenderedWhenItsCueIsFirstNeededNotWhenThePackLoads()
    {
        var pack = Pack();
        WriteMidi(Path.Combine(pack, "inn-1.mid"));
        File.WriteAllText(WriteFont(Path.Combine(_program, "SOUNDFONT", "a.sf2")), "broken");     // read only when a cue needs it
        var loaded = AudioPack.Load(pack, programFolder: _program);
        await loaded.PreloadAsync(Rate);
        Assert.Empty(loaded.Problems);
        Assert.Null(await loaded.ClipAsync(loaded.Find(AudioKind.Music, 0x09)!, Rate));
        Assert.Single(loaded.Problems);
        Assert.Contains("SoundFont", loaded.Problems[0], StringComparison.Ordinal);
    }

    [Fact]
    public void ARecordingWinsOverAMidiFileOfTheSameCueAndOtherPacksAreUnchanged()
    {
        var pack = Pack();
        WriteMidi(Path.Combine(pack, "inn-1.mid"));
        WavFile.Write(Path.Combine(pack, "inn-1.wav"), new short[200], Rate);
        WriteMidi(Path.Combine(pack, "inn-2.midi"));
        var loaded = AudioPack.Load(pack, programFolder: _program);
        Assert.EndsWith("inn-1.wav", loaded.Find(AudioKind.Music, 0x09)!.Path, StringComparison.Ordinal);
        Assert.EndsWith("inn-2.midi", loaded.Find(AudioKind.Music, 0x0A)!.Path, StringComparison.Ordinal);
        Assert.Contains(loaded.Problems, p => p.Contains("inn-1.mid: inn-1 has another file", StringComparison.Ordinal));     // the .mid is the one ignored
    }

    // ---------------------------------------------------------------- the lookup order

    [Fact]
    public void TheSoundFontIsThePacksThenTheChosenOneThenTheDefaultFolders()
    {
        var pack = Pack();
        WriteMidi(Path.Combine(pack, "inn-1.mid"));
        var fromDefault = WriteFont(Path.Combine(_program, "SOUNDFONT", "b.sf2"));
        WriteFont(Path.Combine(_program, "SOUNDFONT", "c.sf2"));
        var chosen = WriteFont(Path.Combine(_folder, "chosen", "mine.sf2"));
        var own = WriteFont(Path.Combine(pack, "fonts", "pack.sf2"));

        Assert.Equal(fromDefault, AudioPack.Load(pack, programFolder: _program).SoundFontPath);                 // 3: the default
        WriteFont(Path.Combine(_program, "SOUNDFONT", "a.sf2"));
        Assert.Equal(Path.Combine(_program, "SOUNDFONT", "a.sf2"), AudioPack.Load(pack, programFolder: _program).SoundFontPath);    // the first by name
        Assert.Equal(chosen, AudioPack.Load(pack, chosen, _program).SoundFontPath);                              // 2: --soundfont or the settings
        File.WriteAllText(Path.Combine(pack, "pack.json"), """{ "soundfont": "fonts/pack.sf2", }""");
        var loaded = AudioPack.Load(pack, chosen, _program);
        Assert.Equal(own, loaded.SoundFontPath);                                                                  // 1: the pack's own
        Assert.Empty(loaded.Problems);
    }

    [Fact]
    public void ANamedSoundFontThatIsMissingIsSaidAndTheNextOneIsTried()
    {
        var pack = Pack();
        WriteMidi(Path.Combine(pack, "inn-1.mid"));
        File.WriteAllText(Path.Combine(pack, "pack.json"), """{ "soundfont": "nope.sf2" }""");
        var chosen = WriteFont(Path.Combine(_folder, "mine.sf2"));
        var loaded = AudioPack.Load(pack, chosen, _program);
        Assert.Equal(chosen, loaded.SoundFontPath);
        Assert.Single(loaded.Problems);
        Assert.Contains("nope.sf2", loaded.Problems[0], StringComparison.Ordinal);
        var missing = AudioPack.Load(pack, Path.Combine(_folder, "gone.sf2"), _program);
        Assert.Null(missing.SoundFontPath);
        Assert.Contains(missing.Problems, p => p.Contains("gone.sf2", StringComparison.Ordinal));
    }

    [Fact]
    public void TheLocatorTakesTheFirstSf2ByNameAndIgnoresOtherFiles()
    {
        var folder = Path.Combine(_program, SoundFontLocator.FolderName);
        Assert.Null(SoundFontLocator.DefaultIn(_program));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "0-readme.txt"), "x");
        Assert.Null(SoundFontLocator.DefaultIn(_program));
        WriteFont(Path.Combine(folder, "Zeta.SF2"));
        WriteFont(Path.Combine(folder, "alpha.sf2"));
        Assert.Equal(Path.Combine(folder, "alpha.sf2"), SoundFontLocator.DefaultIn(_program));
    }

    // ---------------------------------------------------------------- no SoundFont

    [Fact]
    public async Task WithoutASoundFontTheMidiCueIsSkippedWithOneMessageAndTheGameSoundPlays()
    {
        var pack = Pack();
        WriteMidi(Path.Combine(pack, "inn-1.mid"));
        WriteMidi(Path.Combine(pack, "inn-2.mid"));
        WavFile.Write(Path.Combine(pack, "marketplace.wav"), new short[4000], Rate);
        var loaded = AudioPack.Load(pack, programFolder: _program);
        Assert.Null(loaded.SoundFontPath);
        var message = Assert.Single(loaded.Problems);
        Assert.Contains("SOUNDFONT", message, StringComparison.Ordinal);
        Assert.Contains("--soundfont", message, StringComparison.Ordinal);
        Assert.Contains("launcher", message, StringComparison.Ordinal);
        await loaded.PreloadAsync(Rate, includeMidi: true);
        Assert.Null(await loaded.ClipAsync(loaded.Find(AudioKind.Music, 0x09)!, Rate));
        Assert.Single(loaded.Problems);     // said once, not once per cue

        var mixer = new PackMixer(loaded, Rate) { Source = new ConstantSource(1000) };
        mixer.Request(0, AudioKind.Music, 0x09);
        var block = new short[4096];
        mixer.Render(block);
        mixer.Render(block);
        Assert.False(mixer.SourceMuted);
        Assert.All(block, s => Assert.Equal(1000, s));
        mixer.Request(0, AudioKind.Music, 0x0B);        // a recording of the same pack still plays
        mixer.Render(block);
        mixer.Render(block);
        Assert.True(mixer.SourceMuted);
    }

    [Fact]
    public void APackWithoutMidiFilesNeverMentionsSoundFonts()
    {
        var pack = Pack();
        WavFile.Write(Path.Combine(pack, "inn-1.wav"), new short[200], Rate);
        Assert.Empty(AudioPack.Load(pack, programFolder: _program).Problems);
    }
}
