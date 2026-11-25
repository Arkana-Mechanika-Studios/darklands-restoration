using Darklands.Platform.Recording;
using Darklands.Presentation.Recording;

namespace Darklands.Battles.Tests;

/// <summary>The replay of an input recording (R-1): another build's recording is refused cleanly, nothing of it played.</summary>
public sealed class GameReplayTests
{
    [Fact]
    public void AnOlderBuildsRecordingIsRefusedNotPlayed()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        using var writer = new TapeWriter();
        writer.Value(TapeKind.Pump, 0);
        var older = new InputRecording
        {
            Header = new RecordingHeader { Program = "1.0.0", Commit = "1234567", Entries = 1 },
            Tape = writer.Snapshot(),
            Saves = new Dictionary<string, byte[]>(),
        };
        var work = Path.Combine(Path.GetTempPath(), "dl-replay-refused-" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = GameReplay.Run(older, files, work);
            Assert.False(result.Same);
            Assert.Contains("made by Darklands Restoration 1.0.0 (commit 1234567)", result.Refused, StringComparison.Ordinal);
            Assert.Equal(0, result.Entries);
            Assert.Empty(result.Saves);
            // nothing was started: no saves folder made
            Assert.False(Directory.Exists(Path.Combine(work, "saves")));
        }
        finally
        {
            if (Directory.Exists(work))
            {
                Directory.Delete(work, recursive: true);
            }
        }
    }
}
