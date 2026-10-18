using Darklands.Audio.Driver;
using Darklands.Audio.Machine;
using Darklands.Audio.Synthesis;
using Darklands.Content;

namespace Darklands.Audio.Tests;

/// <summary>
/// The player's own drivers in the emulated PC. Needs the original game files (DARKLANDS_DIR,
/// or the "game" key of local/paths.json); without them these tests pass without checking anything.
/// </summary>
public sealed class DriverTests
{
    private static GameFiles? Files()
    {
        var folder = Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") ?? "";
        return GameFiles.IsGameFolder(folder) && File.Exists(Path.Combine(folder, "ASOUND.DC")) ? new GameFiles(folder) : null;
    }

    [Fact]
    public void FmDriversAreRecognisedByTheirSignature()
    {
        if (Files() is not { } files)
        {
            return;
        }
        Assert.StartsWith("DrkCardAdLib", DriverImage.Signature(files.Read("ASOUND.DC")), StringComparison.Ordinal);
        Assert.StartsWith("DKBttleAdLib", DriverImage.Signature(files.Read("ASOUND.DB")), StringComparison.Ordinal);
        Assert.StartsWith("DrkCardAdLib", DriverImage.Signature(SoundSetup.ForFmCard(files, new SoundCardSettings(SoundCard.AdLib)).TownsDriver), StringComparison.Ordinal);
        Assert.StartsWith("P DarkBattle", DriverImage.Signature(SoundSetup.ForFmCard(files, new SoundCardSettings(SoundCard.SoundBlasterPro2)).BattleDriver), StringComparison.Ordinal);
    }

    [Fact]
    public void TheDriverRemembersThePiecePlaying()
    {
        if (Files() is not { } files)
        {
            return;
        }
        var machine = new PcMachine(new Opl3(), new SoundCardSettings(SoundCard.AdLib));
        var driver = new SoundDriver(machine, files.Read("ASOUND.DC"));
        driver.Start(new SoundCardSettings(SoundCard.AdLib), null);
        driver.Play(11);
        Assert.Equal(11, driver.Play(5));
    }

    /// <summary>
    /// Number 5 answers the number last stored (the pieces and the stop alike) and is not stored itself;
    /// the stop (what music_play(5) plays with music off) leaves AX 0 on the FM and Roland drivers; the speaker driver's
    /// handler leaves a value of its own. (NSOUND.DLC's play slot is a bare far return: AX is the caller's.)
    /// </summary>
    [Theory]
    [InlineData("ASOUND.DC", SoundCard.AdLib)]
    [InlineData("ASOUND.DLC", SoundCard.AdLib)]
    [InlineData("PSOUND.DC", SoundCard.SoundBlasterPro2)]
    [InlineData("PSOUND.DLC", SoundCard.SoundBlasterPro2)]
    [InlineData("RSOUND.DLC", SoundCard.SoundBlaster)]
    [InlineData("ISOUND.DLC", SoundCard.PcSpeaker)]
    public void NumberFiveAnswersTheNumberLastPlayed(string file, SoundCard card)
    {
        if (Files() is not { } files || !files.Exists(file))
        {
            return;
        }
        var settings = new SoundCardSettings(card);
        var machine = new PcMachine(new Opl3(), settings);
        var driver = new SoundDriver(machine, files.Read(file));
        driver.Start(settings, null);
        driver.Play(9);
        Assert.Equal(9, driver.Play(5));
        Assert.Equal(9, driver.Play(5));
        var stop = driver.Play(1);
        if (card != SoundCard.PcSpeaker)
        {
            Assert.Equal(0, stop);
        }
        Assert.Equal(1, driver.Play(5));
        driver.Play(0x15);
        Assert.Equal(0x15, driver.Play(5));
    }

    /// <summary>The audio answers number 5 at once, after the requests made before it.</summary>
    [Fact]
    public void TheAudioAnswersThePiecePlaying()
    {
        if (Files() is not { } files)
        {
            return;
        }
        var requests = new List<int>();
        var audio = new SynthAudio(files, 22050, SoundSetup.ForFmCard(files, new SoundCardSettings(SoundCard.AdLib)), null,
            e => { if (e.Kind == AudioEventKind.Request) { requests.Add(e.Value); } });
        audio.PlayMusic(0x0B);
        Assert.Equal(0x0B, ((Darklands.Platform.IAudio)audio).RequestCurrentPiece());
        Assert.Equal(0x0B, GameSounds.RequestCurrentPiece(audio, musicOn: true));
        Assert.Equal(0, GameSounds.RequestCurrentPiece(audio, musicOn: false));
        Assert.Equal([0x0B, 5, 5, 1], requests);
        Assert.Equal(-1, ((Darklands.Platform.IAudio)new Darklands.Platform.SilentAudio()).RequestCurrentPiece());
    }

    [Fact]
    public void APiecePlaysNotes()
    {
        if (Files() is not { } files)
        {
            return;
        }
        var machine = new PcMachine(new Opl3(), new SoundCardSettings(SoundCard.AdLib));
        var keyOns = 0;
        var index = 0;
        machine.PortWritten = (port, value) =>
        {
            if (port == 0x388)
            {
                index = value;
            }
            else if (port == 0x389 && index is >= 0xB0 and <= 0xB8 && (value & 0x20) != 0)
            {
                keyOns++;
            }
        };
        var driver = new SoundDriver(machine, files.Read("ASOUND.DC"));
        driver.Start(new SoundCardSettings(SoundCard.AdLib), null);
        driver.Play(11);
        var timer = new GameTimer();
        for (var i = 0; i < 700; i++)
        {
            timer.Interrupt(driver);
        }
        Assert.True(keyOns > 20, $"{keyOns} notes");
    }

    private static short[] Render(SynthAudio audio, double seconds)
    {
        var buffer = new short[(int)(seconds * audio.SampleRate) * 2];
        audio.Render(buffer);
        Assert.Null(audio.Fault);
        return buffer;
    }

    [Theory]
    [InlineData(SoundCard.AdLib)]
    [InlineData(SoundCard.SoundBlaster)]
    [InlineData(SoundCard.SoundBlasterPro2)]
    public void MusicIsHeard(SoundCard card)
    {
        if (Files() is not { } files)
        {
            return;
        }
        var audio = new SynthAudio(files, 22050, new SoundCardSettings(card));
        audio.PlayMusic(11);
        Assert.Contains(Render(audio, 3), s => Math.Abs((int)s) > 500);
    }

    [Fact]
    public void TheOpeningPlaysItsSampledSounds()
    {
        if (Files() is not { } files)
        {
            return;
        }
        var audio = new SynthAudio(files, 22050);
        audio.PlayMusic(0x2F);
        Render(audio, 20);
        audio.PlayMusic(0x30);
        Assert.Contains(Render(audio, 6), s => Math.Abs((int)s) > 500);
    }

    [Fact]
    public void BattleSoundsSwapToTheBattleDriver()
    {
        if (Files() is not { } files)
        {
            return;
        }
        var audio = new SynthAudio(files, 22050);
        audio.PlaySound(15);
        Assert.Contains(Render(audio, 2), s => Math.Abs((int)s) > 500);
        audio.PlayMusic(11);
        Render(audio, 1);
    }

    [Fact]
    public void WavFilesHoldTheFrames()
    {
        var path = Path.GetTempFileName();
        try
        {
            WavFile.Write(path, new short[200], 22050);
            Assert.Equal(44 + 400, new FileInfo(path).Length);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
