using System.IO.Compression;
using Darklands.Platform.Recording;
using Darklands.Platform.Reporting;

namespace Darklands.Platform.Tests;

/// <summary>
/// The input recording (R-1): the tape gives back every value in order and stops a replay that asks for another kind; a recording
/// file keeps its header, tape, start saves and end state; a problem report packs it.
/// </summary>
public sealed class InputRecordingTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "dl-recording-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void TheTapeGivesBackEveryValueInOrder()
    {
        using var writer = new TapeWriter();
        var written = new List<(TapeKind, long)>();
        void Write(TapeKind kind, long value)
        {
            writer.Value(kind, value);
            written.Add((kind, value));
        }
        // a clock that runs, the same mouse read many times over (repeats), a key, negative and large values
        for (var i = 0; i < 1000; i++)
        {
            Write(TapeKind.ClockMilliseconds, 1000 + i * 14);
            Write(TapeKind.Mouse, TapeValues.Mouse(new MouseState(160, 100, MouseButtons.None)));
            Write(TapeKind.Mouse, TapeValues.Mouse(new MouseState(160, 100, MouseButtons.None)));
        }
        Write(TapeKind.Key, 0x1E61 + 1L);
        Write(TapeKind.PlayMusicNow, -1);
        Write(TapeKind.Seed, uint.MaxValue);
        writer.Note("settings comfort=1");
        Write(TapeKind.Pump, 0);
        var tape = writer.Snapshot();
        Assert.True(tape.Length < 2000, $"{tape.Length} bytes");
        Assert.Equal(written.Count + 1, writer.Entries);

        var reader = new TapeReader(tape);
        var notes = new List<string>();
        foreach (var (kind, value) in written)
        {
            if (kind == TapeKind.Pump)
            {
                notes.Add(reader.TakeNote()!);
            }
            Assert.Equal(value, reader.Value(kind));
        }
        Assert.Equal(["settings comfort=1"], notes);
        Assert.True(reader.AtEnd);
        Assert.Throws<TapeEndedException>(() => reader.Value(TapeKind.Pump));
    }

    [Fact]
    public void AReplayThatAsksForAnotherKindStopsWhereItParts()
    {
        using var writer = new TapeWriter();
        writer.Value(TapeKind.Pump, 0);
        writer.Value(TapeKind.Mouse, 5);
        var reader = new TapeReader(writer.Snapshot());
        reader.Value(TapeKind.Pump);
        var parted = Assert.Throws<TapeMismatchException>(() => reader.Value(TapeKind.Key));
        Assert.Contains("entry 1: the game asked for Key, the recording has Mouse", parted.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTapeHostHandsTheValuesBackAndStopsAtThePumpItWasTakenIn()
    {
        using var writer = new TapeWriter();
        writer.Value(TapeKind.Pump, 0);
        writer.Value(TapeKind.Key, 'a' + 1L);
        writer.Value(TapeKind.Mouse, TapeValues.Mouse(new MouseState(12, 34, MouseButtons.Left)));
        writer.Value(TapeKind.ClockMilliseconds, 500);
        writer.Value(TapeKind.Pump, 0);
        writer.Note("settings comfort=1");
        var host = new TapeHost(new TapeReader(writer.Snapshot()));
        var notes = new List<string>();
        var ended = false;
        host.Note = notes.Add;
        host.Ended = () => ended = true;
        host.Pump();
        Assert.True(host.TryReadKey(out var key));
        Assert.Equal('a', key);
        Assert.Equal(new MouseState(12, 34, MouseButtons.Left), host.Mouse);
        Assert.Equal(500, host.Milliseconds);
        Assert.Throws<TapeEndedException>(host.Pump);
        Assert.True(ended);
        Assert.Equal(["settings comfort=1"], notes);
    }

    [Fact]
    public void TheRecordingHostWritesWhatTheGameIsGiven()
    {
        using var writer = new TapeWriter();
        var inner = new FixedHost();
        var host = new RecordingHost(inner, writer);
        host.Input.Pump();
        Assert.True(host.Input.TryReadKey(out var key));
        Assert.Equal(0x1C0D, key);
        Assert.Equal(inner.Mouse, host.Input.Mouse);
        Assert.Equal(42, host.Clock.Milliseconds);
        var replay = new TapeHost(new TapeReader(writer.Snapshot()));
        replay.Pump();
        Assert.True(replay.TryReadKey(out var again));
        Assert.Equal(0x1C0D, again);
        Assert.Equal(inner.Mouse, replay.Mouse);
        Assert.Equal(42, replay.Milliseconds);
    }

    [Fact]
    public void ARecordingFileKeepsItsPartsAndTheSavesTimes()
    {
        Directory.CreateDirectory(_folder);
        var saves = Path.Combine(_folder, "saves");
        Directory.CreateDirectory(saves);
        File.WriteAllBytes(Path.Combine(saves, "DKSAVE1.SAV"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(saves, "DKSAVE1.BSV"), [4]);
        File.WriteAllText(Path.Combine(saves, "notes.txt"), "not a save");
        var when = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Path.Combine(saves, "DKSAVE1.SAV"), when);
        var (files, bytes, cut) = InputRecording.Snapshot(saves);
        Assert.False(cut);
        Assert.Equal(["DKSAVE1.SAV", "DKSAVE1.BSV"], files.Select(f => f.Name).Order().Reverse());
        using var writer = new TapeWriter();
        writer.Value(TapeKind.Pump, 0);
        var recording = new InputRecording
        {
            Header = new RecordingHeader { Program = "1.2.0", Settings = { ["pack.language"] = "fr" }, Saves = files, Entries = 1, FinalState = "AB" },
            Tape = writer.Snapshot(),
            Saves = bytes,
            FinalState = [9, 9],
        };
        var path = Path.Combine(_folder, InputRecording.FileName);
        recording.Write(path);
        var read = InputRecording.Read(path);
        Assert.Equal("fr", read.Header.Settings["pack.language"]);
        Assert.Equal(recording.Tape, read.Tape);
        Assert.Equal([9, 9], read.FinalState);
        var restored = Path.Combine(_folder, "restored");
        read.RestoreSaves(restored);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(restored, "DKSAVE1.SAV")));
        Assert.Equal(when, File.GetLastWriteTimeUtc(Path.Combine(restored, "DKSAVE1.SAV")));
        Assert.False(File.Exists(Path.Combine(restored, "notes.txt")));
        // no folder of the player's in the header
        using var zip = ZipFile.OpenRead(path);
        using var header = new StreamReader(zip.GetEntry("recording.json")!.Open());
        Assert.DoesNotContain(_folder.Replace("\\", "\\\\", StringComparison.Ordinal), header.ReadToEnd(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ARecordingOfAnotherBuildIsRefused()
    {
        // the same version and commit (or a commit not known on either side): played
        Assert.Null(RecordingBuild.Refusal("1.2.0", "abc1234", "1.2.0", "abc1234"));
        Assert.Null(RecordingBuild.Refusal("1.2.0", "unknown", "1.2.0", "abc1234"));
        // an older build's: refused, with the build to use
        var older = RecordingBuild.Refusal("1.1.3", "6da0fec", "1.2.0", "abc1234");
        Assert.NotNull(older);
        Assert.Contains("made by Darklands Restoration 1.1.3 (commit 6da0fec)", older, StringComparison.Ordinal);
        Assert.Contains("play it with the build that made it", older, StringComparison.Ordinal);
        Assert.NotNull(RecordingBuild.Refusal("1.2.0", "0000000", "1.2.0", "abc1234"));
    }

    [Fact]
    public void ARecordingOfAnotherFormatIsRefusedWhenRead()
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, InputRecording.FileName);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using (var stream = new StreamWriter(zip.CreateEntry("recording.json").Open()))
            {
                stream.Write("{ \"format\": 0, \"program\": \"1.0.0\" }");
            }
            zip.CreateEntry("tape.bin");
        }
        var refused = Assert.Throws<InvalidDataException>(() => InputRecording.Read(path));
        Assert.Contains("recording format 0", refused.Message, StringComparison.Ordinal);
        // and a file that is no recording at all
        File.WriteAllText(path, "not a zip");
        Assert.Throws<InvalidDataException>(() => InputRecording.Read(path));
    }

    [Fact]
    public void AProblemReportPacksTheRecording()
    {
        var logs = Path.Combine(_folder, "logs");
        var reports = Path.Combine(_folder, "reports");
        Directory.CreateDirectory(logs);
        var file = Path.Combine(_folder, InputRecording.FileName);
        File.WriteAllBytes(file, [1, 2, 3, 4]);
        var result = ReportBuilder.Build(new ReportInput(reports, logs, "v", "c", false, "Ctrl-F12", null, null, [], null, null, [], null, null,
            new Scrubber(null, null), RecordingFile: file));
        Assert.Contains("recording/" + InputRecording.FileName, result.Entries);
    }

    private sealed class FixedHost : IHost, IVideo, IInput, IClock, IAudio
    {
        public IVideo Video => this;

        public IInput Input => this;

        public IAudio Audio => this;

        public IClock Clock => this;

        public void Present(Screen screen)
        {
        }

        public void Pump()
        {
        }

        public bool QuitRequested => false;

        public bool TryReadKey(out int key)
        {
            key = 0x1C0D;
            return true;
        }

        public MouseState Mouse => new(100, 50, MouseButtons.Right);

        public bool ShiftHeld => false;

        public long Milliseconds => 42;

        public void Sleep(int milliseconds)
        {
        }

        public void PlayMusic(int tune)
        {
        }

        public void StopMusic()
        {
        }

        public void PlaySound(int effect)
        {
        }
    }
}
