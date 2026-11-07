using Darklands.Content;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Presentation.Graphics;
using Darklands.Simulation;
using Darklands.Simulation.Overland;

namespace Darklands.Battles.Tests;

/// <summary>
/// The pointer shape's hot spot (pointer-shape-set root 047C:124F): world-map-start R030+0x0242 sets (5, 0) for the gauntlet, so on the
/// map the driver draws it 5 pixels left of the mouse (vis10 session creation-map: the original's pointer 5 pixels left of the C#'s before
/// this); the character sheet, the party information, the alchemy screen's exit and a battle set (0, 0) again; the card engine and the
/// camp set nothing, so they keep the map's.
/// </summary>
public sealed class PointerHotSpotTests
{
    /// <summary>A host with the mouse at a fixed place that keeps the last frame shown and quits after some frames.</summary>
    private sealed class FrameHost(int x, int y, int frames) : IHost, IVideo, IInput, IClock
    {
        private int _left = frames;
        private long _now;

        public IVideo Video => this;
        public IInput Input => this;
        public IAudio Audio { get; } = new SilentAudio();
        public IClock Clock => this;
        public bool QuitRequested { get; private set; }
        public MouseState Mouse => new(x, y, MouseButtons.None);
        public bool PointerInWindow { get; set; } = true;
        public bool ShiftHeld => false;
        public long Milliseconds => _now;
        public byte[]? Last { get; private set; }

        public void Present(Screen screen) => Last = (byte[])screen.Pixels.Clone();

        public void Sleep(int milliseconds) => _now += Math.Max(1, milliseconds);

        public bool TryReadKey(out int key)
        {
            key = 0;
            return false;
        }

        public void Pump()
        {
            if (--_left < 0)
            {
                QuitRequested = true;
            }
        }
    }

    private static bool PointerAt(GameUi ui, byte[] frame, int x, int y)
    {
        var shown = new Screen();
        frame.CopyTo(shown.Pixels, 0);
        SpriteDrawing.Draw(new Canvas(shown), ui.Assets.CommonSprites.Pointer, x, y);
        return shown.Pixels.AsSpan().SequenceEqual(frame);
    }

    [Fact]
    public void TheMapDrawsTheGauntletFivePixelsLeftOfTheMouseAndKeepsItSo()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var world = WorldData.Load(files);
        var host = new FrameHost(200, 60, 30);
        var ui = new GameUi(host, new GameAssets(files));
        Assert.Equal((0, 0), ui.PointerHotSpot);
        var state = NewGame.Quickstart(world, 1, () => 1);
        state.Data = world;
        var map = new WorldMap(Content.Formats.Maps.WorldMapData.Decode(files.Read("DARKLAND.MAP")), state.Locations);
        try
        {
            new OverlandScreen(ui, new PartyPanel(ui.Assets), map).Travel(state);
        }
        catch (QuitGameException)
        {
        }
        Assert.NotNull(host.Last);
        Assert.True(PointerAt(ui, host.Last, 195, 60), "the gauntlet is not drawn at (195, 60) for the mouse at (200, 60)");
        Assert.False(PointerAt(ui, host.Last, 200, 60), "the gauntlet is drawn at the mouse");
        // the map does not put it back when it is left: the next screens (a card, the camp) keep (5, 0)
        Assert.Equal((5, 0), ui.PointerHotSpot);
    }

    [Fact]
    public void ThePartyInformationPutsTheHotSpotBack()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var world = WorldData.Load(files);
        var host = new FrameHost(150, 100, 5);
        var ui = new GameUi(host, new GameAssets(files)) { PointerHotSpot = (5, 0) };
        var state = NewGame.Quickstart(world, 1, () => 1);
        state.Data = world;
        try
        {
            new PartyInfoScreen(ui, new PartyPanel(ui.Assets)).Show(state);
        }
        catch (QuitGameException)
        {
        }
        Assert.Equal((0, 0), ui.PointerHotSpot);
        Assert.NotNull(host.Last);
        Assert.True(PointerAt(ui, host.Last, 150, 100), "the gauntlet is not at the mouse on the party information");
    }

    /// <summary>
    /// Host enhancement (IInput.PointerInWindow, the SDL host's window "mouse left"): while the host's pointer is outside the window the
    /// gauntlet is left out of the picture shown, the screen the game draws on is not touched, and it comes back at the mouse after.
    /// </summary>
    [Fact]
    public void TheGauntletIsLeftOutWhileTheHostsPointerIsOutsideTheWindow()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var host = new FrameHost(150, 100, 10);
        var ui = new GameUi(host, new GameAssets(files));
        var blank = new byte[Screen.Width * Screen.Height];
        ui.Present();
        Assert.True(PointerAt(ui, host.Last!, 150, 100), "the gauntlet is not at the mouse");
        host.PointerInWindow = false;
        ui.Present();
        Assert.Equal(blank, host.Last);
        Assert.Equal(blank, ui.Canvas.Screen.Pixels);
        host.PointerInWindow = true;
        ui.Present();
        Assert.True(PointerAt(ui, host.Last!, 150, 100), "the gauntlet is not back at the mouse");
    }
}
