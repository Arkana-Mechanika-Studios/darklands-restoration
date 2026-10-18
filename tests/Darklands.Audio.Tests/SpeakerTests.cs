using Darklands.Audio.Machine;
using Darklands.Content;

namespace Darklands.Audio.Tests;

/// <summary>The PC speaker (timer counter 2 and port 61h) and the ISOUND drivers that play on it; the DOS command line's -A.</summary>
public sealed class SpeakerTests
{
    private static GameFiles? Files()
    {
        var folder = Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") ?? "";
        return GameFiles.IsGameFolder(folder) && File.Exists(Path.Combine(folder, "ISOUND.DLC")) ? new GameFiles(folder) : null;
    }

    private static int SignChanges(PcSpeaker speaker, int samples, double rate)
    {
        var changes = 0;
        var last = 0.0;
        for (var i = 0; i < samples; i++)
        {
            var v = speaker.Next(rate);
            if (Math.Abs(v) > 0.2 && Math.Sign(v) != Math.Sign(last) && last != 0)
            {
                changes++;
            }
            if (Math.Abs(v) > 0.2)
            {
                last = v;
            }
        }
        return changes;
    }

    [Fact]
    public void Mode3PlaysASquareWaveOfTheClockOverTheCount()
    {
        var speaker = new PcSpeaker();
        speaker.Control(0xB6);
        speaker.Out(0x42, 0xA9);   // 1193: about 1000 Hz
        speaker.Out(0x42, 0x04);
        speaker.Out(0x61, 3);
        var changes = SignChanges(speaker, 48000, 48000);
        Assert.InRange(changes, 1960, 2040);   // two a period
    }

    [Fact]
    public void TheSpeakerIsSilentWithoutBit1OrWithTheGateHeldLow()
    {
        var speaker = new PcSpeaker();
        speaker.Control(0xB6);
        speaker.Out(0x42, 0xA9);
        speaker.Out(0x42, 0x04);
        speaker.Out(0x61, 1);
        Assert.Equal(0, SignChanges(speaker, 4800, 48000));
        speaker.Out(0x61, 2);
        Assert.Equal(0, SignChanges(speaker, 4800, 48000));
    }

    [Fact]
    public void ACountWrittenWhileCountingWaitsForTheEndOfTheHalfCycle()
    {
        var speaker = new PcSpeaker();
        speaker.Control(0xB6);
        speaker.Out(0x42, 0x00);   // 65536: about 18 Hz
        speaker.Out(0x42, 0x00);
        speaker.Out(0x61, 3);
        speaker.Out(0x42, 0xA9);   // the new count only after the first half (27.5 ms)
        speaker.Out(0x42, 0x04);
        Assert.Equal(0, SignChanges(speaker, 1200, 48000));   // the first 25 ms stay high
        Assert.InRange(SignChanges(speaker, 48000, 48000), 1900, 2040);
    }

    [Fact]
    public void TheSpeakerDriverPlaysAPieceOnTheSpeaker()
    {
        if (Files() is not { } files)
        {
            return;
        }
        var setup = SoundSetup.FromCommandLine(files, ["-ai"]);
        Assert.Equal(DriverKind.Speaker, setup.Kind);
        Assert.Equal("ISOUND.DLC", setup.TownsDriverName);
        var audio = new SynthAudio(files, 44100, setup);
        // the driver's start-up sequence (priority 100) is over first; a piece asked for during it is refused, as on the PC
        audio.Render(new short[44100 * 2 * 5]);
        audio.PlayMusic(11);
        var buffer = new short[44100 * 2 * 3];
        audio.Render(buffer);
        Assert.Null(audio.Fault);
        var loud = buffer.Count(s => Math.Abs((int)s) > 1000);
        Assert.True(loud > 4410, $"{loud} loud samples in three seconds");
    }

    [Fact]
    public void MinusANamesTheDriverAndKeepsTheCardOfTheConfiguration()
    {
        if (Files() is not { } files)
        {
            return;
        }
        var config = SoundCardSettings.FromConfig(files);
        var setup = SoundSetup.FromCommandLine(files, ["/AI", "-Q"]);
        Assert.Equal("ISOUND.DLC", setup.TownsDriverName);
        Assert.Equal("ISOUND.DLB", setup.BattleDriverName);
        if (config is not null)
        {
            Assert.Equal(config, setup.Card);
        }
        // a letter without its file leaves the configuration's driver (the game stops at start-up with "Sound driver not found")
        Assert.Equal(SoundSetup.FromConfig(files).TownsDriverName, SoundSetup.FromCommandLine(files, ["-AZ"]).TownsDriverName);
    }
}
