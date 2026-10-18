using Darklands.Audio.Driver;
using Darklands.Audio.Machine;
using Darklands.Audio.Synthesis;
using Darklands.Content;

namespace Darklands.Audio.Tests;

/// <summary>
/// The Roland driver (RSOUND.DLC, "RLND DrkLand") in the emulated PC with its MPU-401, and the
/// choice of the sound card. Needs the original game files (DARKLANDS_DIR, or the "game" key of local/paths.json);
/// without them these tests pass without checking anything.
/// </summary>
public sealed class RolandDriverTests
{
    private static GameFiles? Files()
    {
        var folder = Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") ?? "";
        return GameFiles.IsGameFolder(folder) && File.Exists(Path.Combine(folder, "RSOUND.DLC")) ? new GameFiles(folder) : null;
    }

    /// <summary>The Roland driver with a Sound Blaster Pro 2 at 220h, IRQ 5, DMA 1 (the capture's configuration).</summary>
    private static SoundSetup Roland(GameFiles files) =>
        SoundSetup.ForChoice(files, "mt32") with { Card = new SoundCardSettings(SoundCard.SoundBlasterPro2, 0x220, 5, 1) };

    private static (SynthAudio Audio, List<AudioEvent> Events) Start(GameFiles files)
    {
        var events = new List<AudioEvent>();
        var audio = new SynthAudio(files, 22050, Roland(files), new MidiStreamTests.Recorder(), events.Add);
        return (audio, events);
    }

    private static byte[] Midi(IEnumerable<AudioEvent> events) =>
        events.Where(e => e.Kind == AudioEventKind.MidiByte).Select(e => (byte)e.Value).ToArray();

    [Fact]
    public void TheDriverStartsTheModuleChannelByChannel()
    {
        if (Files() is not { } files)
        {
            return;
        }
        var (_, events) = Start(files);
        // the first bytes of the capture of the original: per channel 10, 9 ... all notes off, reset all controllers, volume 100, pan 64
        Assert.Equal(Convert.FromHexString("B97B00790007640A40B87B00790007640A40"), Midi(events)[..18]);
        Assert.All(events.Where(e => e.Kind == AudioEventKind.MidiByte), e => Assert.Equal('s', e.Context));
    }

    [Fact]
    public void TheOpeningTuneBeginsAsInTheOriginal()
    {
        if (Files() is not { } files)
        {
            return;
        }
        var (audio, events) = Start(files);
        var before = Midi(events).Length;
        audio.PlayMusic(0x2F);
        audio.Render(new short[2 * 2205]);
        var tune = Midi(events)[before..];
        // the capture's bytes 1168..1183: pitch bend centre and volume 100 on each part, sent by the driver's play call itself
        Assert.Equal(Convert.FromHexString("E10040B10764E20040B20764E30040B3"), tune[..16]);
        var request = Assert.Single(events, e => e.Kind == AudioEventKind.Request);
        Assert.Equal(0x2F, request.Value);
        Assert.Contains(events, e => e.Kind == AudioEventKind.SampleTransfer && e.Value == 2 && e.Context == 'r'); // the DSP's test transfer
    }

    [Fact]
    public void ARequestIsCarriedOutAtItsTick()
    {
        if (Files() is not { } files)
        {
            return;
        }
        var (audio, events) = Start(files);
        audio.PlayMusicAt(0x2F, 30);
        audio.Render(new short[2 * 22050]);
        var request = Assert.Single(events, e => e.Kind == AudioEventKind.Request);
        Assert.Equal(30, request.Tick);
        var tick30 = Assert.Single(events, e => e.Kind == AudioEventKind.Tick && e.Tick == 30);
        Assert.Equal(tick30.Frame, request.Frame);
        Assert.True(audio.RenderedTicks is >= 58 and <= 61, $"{audio.RenderedTicks} ticks in a second");
    }

    [Fact]
    public void ASampleStartsTheTimeTheDriversWorkTakesIntoItsTick()
    {
        if (Files() is not { } files)
        {
            return;
        }
        var (audio, events) = Start(files);
        audio.PlayMusic(0x2F);
        for (var i = 0; i < 22; i++)
        {
            audio.Render(new short[2 * 22050]);
        }
        // the first water drop (selector 2, 756 bytes): the capture started its DMA 44.3 ms after its tick began
        var first = events.First(e => e.Kind == AudioEventKind.SampleTransfer && e.Context == 'f');
        Assert.Equal(756, first.Value);
        Assert.InRange(first.Delay, 0.040, 0.048);
        Assert.Equal(1141, first.Tick);
    }

    [Fact]
    public void TheMpuTakesUartModeAndSendsItsBytes()
    {
        var mpu = new Mpu401();
        var sent = new List<byte>();
        mpu.MidiOut = sent.Add;
        mpu.Out(Mpu401.StatusPort, 0xFF);
        Assert.Equal(0x3F, mpu.In(Mpu401.StatusPort));     // ready, a byte to read
        Assert.Equal(0xFE, mpu.In(Mpu401.DataPort));       // the acknowledgement
        Assert.Equal(0xBF, mpu.In(Mpu401.StatusPort));
        mpu.Out(Mpu401.StatusPort, 0x3F);
        Assert.True(mpu.Uart);
        mpu.In(Mpu401.DataPort);
        mpu.Out(Mpu401.DataPort, 0x90);
        Assert.Equal([0x90], sent);
    }

    [Fact]
    public void TheConfigurationPicksTheDriverOfItsLetter()
    {
        if (Files() is not { } files)
        {
            return;
        }
        var config = SoundCardSettings.FromConfig(files);
        if (config?.Card != SoundCard.SoundBlasterPro2)
        {
            return; // the checks below are for the GOG installation (card 4, the dual-board Roland files)
        }
        var setup = SoundSetup.FromConfig(files);
        Assert.Equal("PSOUND.DLC", setup.TownsDriverName);
        Assert.Equal("PSOUND.DLB", setup.BattleDriverName);
        Assert.Equal(DriverKind.Roland, setup.Kind);
        Assert.True(setup.Card.PlaysSamples);
        Assert.Contains("MT-32", setup.Describe(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("adlib", DriverKind.Fm, "DrkCardAdLib", SoundCard.AdLib)]
    [InlineData("sb", DriverKind.Fm, "DrkCardAdLib", SoundCard.SoundBlaster)]
    [InlineData("sbpro2", DriverKind.Fm, "P Dark Cards", SoundCard.SoundBlasterPro2)]
    [InlineData("mt32", DriverKind.Roland, "RLND DrkLand", null)]
    [InlineData("cm32l", DriverKind.Roland, "RLND DrkLand", null)]
    public void ACardCanBeChosen(string choice, DriverKind kind, string signature, SoundCard? card)
    {
        if (Files() is not { } files)
        {
            return;
        }
        var setup = SoundSetup.ForChoice(files, choice);
        Assert.Equal(kind, setup.Kind);
        Assert.StartsWith(signature, setup.Signature, StringComparison.Ordinal);
        Assert.Equal(kind, SoundSetup.KindOf(setup.BattleDriver));
        if (card is { } c)
        {
            Assert.Equal(c, setup.Card.Card);
        }
        else
        {
            Assert.InRange((int)setup.Card.Card, 2, 6);  // Roland with a Sound Blaster for the samples
            Assert.Equal(choice == "cm32l" ? RolandModule.Cm32l : RolandModule.Mt32, setup.Module);
        }
    }

    [Fact]
    public void TheDriverLettersAreTheGames()
    {
        Assert.Equal('a', new SoundCardSettings(SoundCard.SoundBlaster).DriverLetter);
        Assert.Equal('p', new SoundCardSettings(SoundCard.ProAudioSpectrum16).DriverLetter);
        Assert.Equal('r', new SoundCardSettings(SoundCard.Roland).DriverLetter);
        Assert.Equal('i', new SoundCardSettings(SoundCard.PcSpeaker).DriverLetter);
        Assert.Equal('n', new SoundCardSettings((SoundCard)9).DriverLetter);
        Assert.False(new SoundCardSettings(SoundCard.Roland).PlaysSamples);
        Assert.True(new SoundCardSettings(SoundCard.AdLib).PlaysSamples);
    }

    [Fact]
    public void TheMt32EmulationPlaysTheDriversMusic()
    {
        if (Files() is not { } files)
        {
            return;
        }
        var roms = Mt32.SoundSelection.RomFolder(null);
        using var synth = Mt32.Mt32Synth.TryOpen(roms, RolandModule.Mt32, 22050, out var message);
        if (synth is null)
        {
            return; // no ROMs here
        }
        var audio = new SynthAudio(files, 22050, Roland(files), synth);
        audio.PlayMusic(0x2F);
        var buffer = new short[2 * 22050 * 3];
        audio.Render(buffer);
        Assert.Null(audio.Fault);
        Assert.Contains(buffer, s => Math.Abs((int)s) > 500);
        Assert.Contains("MT-32", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADriverCallReportsItsProcessorTime()
    {
        var machine = new PcMachine(new Opl3(), new SoundCardSettings(SoundCard.SoundBlaster));
        Assert.Equal(0, machine.SecondsIntoCall);
        Assert.Equal(3_000_000, PcMachine.InstructionsPerSecond);
    }
}
