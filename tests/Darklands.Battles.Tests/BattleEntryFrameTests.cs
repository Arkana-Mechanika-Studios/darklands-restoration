using Darklands.Cards;
using Darklands.Content;
using Darklands.Core.Battles;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Presentation.Battles;

namespace Darklands.Battles.Tests;

/// <summary>
/// The change from a card to a battle: the battle's first frame is not the card's picture in the battle's palette (the flash a player saw),
/// it is the frame that was on show; the next frame is the battlefield.
/// </summary>
public sealed class BattleEntryFrameTests
{
    private sealed class Stop : Exception;

    private sealed class RecordingHost : IHost, IVideo, IInput, IClock, IAudio
    {
        private long _now;

        public List<(byte[] Pixels, uint[] Palette)> Frames { get; } = [];

        public int StopAfter { get; set; } = int.MaxValue;

        public IVideo Video => this;
        public IInput Input => this;
        public IAudio Audio => this;
        public IClock Clock => this;
        public bool QuitRequested => false;
        public MouseState Mouse => new(160, 100, MouseButtons.None);
        public bool ShiftHeld => false;
        public long Milliseconds => _now;

        public void Present(Screen screen)
        {
            Frames.Add(((byte[])screen.Pixels.Clone(), (uint[])screen.Palette.Clone()));
            if (Frames.Count >= StopAfter)
            {
                throw new Stop();
            }
        }

        public void Pump()
        {
        }

        public bool TryReadKey(out int key)
        {
            key = 0;
            return false;
        }

        public void Sleep(int milliseconds) => _now += milliseconds;

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

    [Fact]
    public void TheBattlesFirstFrameIsTheScreenOnShowThenTheBattlefield()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var host = new RecordingHost();
        var ui = new GameUi(host, new GameAssets(files)) { PointerVisible = false };
        var state = Simulation.NewGame.Quickstart(WorldData.Load(files), 7);
        var screen = new BattleScreen(ui, new BattleAssets(files), state, new BattleScreens(ui, () => state));
        // the screen before the battle: a picture of one colour (a card's), shown once
        const byte Card = 0xFD;
        Array.Fill(ui.Canvas.Screen.Pixels, Card);
        ui.Present();
        var before = host.Frames[^1];
        host.StopAfter = host.Frames.Count + 2;
        Assert.Throws<Stop>(() => screen.Run(new BattleRequest(0x14, 863, [(34, 4, 8), (18, 5, 2), (34, 1, 2)])));
        var (firstPixels, firstPalette) = host.Frames[^2];
        Assert.Equal(before.Pixels, firstPixels);
        Assert.Equal(before.Palette, firstPalette);
        var (secondPixels, secondPalette) = host.Frames[^1];
        Assert.DoesNotContain(Card, secondPixels);
        Assert.NotEqual(before.Palette, secondPalette);
    }
}
