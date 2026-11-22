using Darklands.Content;
using Darklands.Platform;
using Darklands.Presentation;

namespace Darklands.Battles.Tests;

/// <summary>
/// The translation test's notice (DarklandsGame.OpenFirst, MessageBox.Notice(string)): a long text in one box, wrapped inside the card
/// window in its font with every word kept, and closed by a key.
/// </summary>
public sealed class StartAtNoticeTests
{
    /// <summary>A host that waits a few frames, then presses Enter; it keeps the frame shown while the notice is up.</summary>
    private sealed class EnterHost : IHost, IVideo, IInput, IClock
    {
        private int _frames;
        private long _now;

        public IVideo Video => this;
        public IInput Input => this;
        public IAudio Audio { get; } = new SilentAudio();
        public IClock Clock => this;
        public bool QuitRequested => _frames > 200;
        public MouseState Mouse { get; } = new(5, 5, MouseButtons.None);
        public bool ShiftHeld => false;
        public long Milliseconds => _now;
        public byte[]? Shown { get; private set; }

        public void Present(Screen screen)
        {
            if (_frames == 3)
            {
                Shown = (byte[])screen.Pixels.Clone();
            }
        }

        public void Sleep(int milliseconds) => _now += Math.Max(1, milliseconds);

        public bool TryReadKey(out int key)
        {
            key = ++_frames == 6 ? Keys.Enter : 0;
            _now += 10;
            return key != 0;
        }

        public void Pump()
        {
        }
    }

    private const string Text = "This text comes later than the Workshop can play to: after a battle, further into the scene than 6 choices, or after an "
        + "event of the story (a quest, an item, an earlier choice). The card ($Armor00) opens at its start; the Workshop's preview shows the text.";

    [Fact]
    public void ALongNoticeIsWrappedInsideTheCardWindowAndClosedByAKey()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var host = new EnterHost();
        var ui = new GameUi(host, new GameAssets(files));
        var before = (byte[])ui.Canvas.Screen.Pixels.Clone();
        var box = new MessageBox(ui);
        var lines = box.Wrap(Text, 236);
        Assert.True(lines.Count is > 2 and <= MessageBox.MaxNoticeLines, string.Join(" | ", lines));
        Assert.All(lines, l => Assert.True(ui.CardWindowText.Measure(l) <= 236, l));
        Assert.Equal(Text, string.Join(" ", lines));
        box.Notice(Text);
        // shown inside the card window (x 60..319), then put back
        Assert.NotNull(host.Shown);
        // (the pointer at (5, 5) left out)
        var drawn = Enumerable.Range(0, before.Length).Where(i => host.Shown![i] != before[i] && !(i % Screen.Width < 32 && i / Screen.Width < 32)).ToList();
        Assert.NotEmpty(drawn);
        Assert.All(drawn, i => Assert.InRange(i % Screen.Width, 60, Screen.Width - 1));
        Assert.Equal(before, ui.Canvas.Screen.Pixels);
    }
}
