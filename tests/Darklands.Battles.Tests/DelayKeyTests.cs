using Darklands.Content;
using Darklands.Platform;
using Darklands.Presentation;

namespace Darklands.Battles.Tests;

/// <summary>A delay that cannot be skipped reads no input: a key pressed meanwhile waits for the screen (the map's pause after a pass, R030+0x3096).</summary>
public sealed class DelayKeyTests
{
    /// <summary>A host whose keys stay queued until read, as the hosts' keyboard buffers do.</summary>
    private sealed class QueueHost : IHost, IVideo, IInput, IClock
    {
        public readonly Queue<int> Keys = new();
        private long _now;

        public IVideo Video => this;
        public IInput Input => this;
        public IAudio Audio { get; } = new SilentAudio();
        public IClock Clock => this;
        public bool QuitRequested => false;
        public MouseState Mouse => new(160, 100, MouseButtons.None);
        public bool ShiftHeld => false;
        public long Milliseconds => _now;

        public void Present(Screen screen) { }

        public void Sleep(int milliseconds) => _now += Math.Max(1, milliseconds);

        public bool TryReadKey(out int key) => Keys.TryDequeue(out key);

        public void Pump() { }
    }

    [Fact]
    public void AKeyPressedDuringAnUnskippableDelayIsReadAfterIt()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var host = new QueueHost();
        var ui = new GameUi(host, new GameAssets(files));
        host.Keys.Enqueue(0x1E61);
        ui.Wait(30, skippable: false);
        Assert.Equal(0x1E61, ui.Poll()?.Key);
    }

    [Fact]
    public void ASkippableDelayStillEndsOnAKey()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var host = new QueueHost();
        var ui = new GameUi(host, new GameAssets(files));
        host.Keys.Enqueue(0x1E61);
        var start = host.Milliseconds;
        ui.Wait(1000);
        Assert.True(host.Milliseconds - start < 1000);
        Assert.Null(ui.Poll());
    }
}
