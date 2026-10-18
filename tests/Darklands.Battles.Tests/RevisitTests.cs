using Darklands.Cards;
using Darklands.Content;
using Darklands.Core.Battles;
using Darklands.Core.Characters;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Presentation.Battles;

namespace Darklands.Battles.Tests;

/// <summary>
/// Going back onto the last battlefield (battle modes 1..3): mode 1 keeps the party where the last battle left it and shows the
/// level shown then; modes 2 and 3 put it at the level's start.
/// </summary>
public sealed class RevisitTests
{
    private static readonly BattleRequest Corridor = new(40, 1234, [(34, 4, 3), (-1, 0, 0), (-1, 0, 0)]);

    [Fact]
    public void ModeOneKeepsThePlacesAndModeTwoGoesBackToTheStart()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var ui = new GameUi(new NullHost(), new GameAssets(files));
        var state = Simulation.NewGame.Quickstart(WorldData.Load(files), 7);
        var screens = new BattleScreens(ui, () => state);
        var assets = new BattleAssets(files);
        var first = new BattleScreen(ui, assets, state, screens);
        first.Start(Corridor);
        var field = screens.LastField!;
        var start = Enumerable.Range(0, Combatant.PartySlots).Select(i => first.Tactics!.Battle[i].Position).ToArray();
        // the last battle ended with everybody moved 24 pixels east, facing 3
        field.Store.FloorShown = field.StartLevel;
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            var c = first.Tactics!.Battle[i];
            field.Store.PartyPlaces[i] = c.Status == CharacterStatus.Empty ? null : (c.Position with { X = c.Position.X + 24 }, 3, 3);
        }

        var second = new BattleScreen(ui, assets, state, screens);
        second.Start(Corridor with { Mode = 1 });
        Assert.Same(field, screens.LastField);
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            var c = second.Tactics!.Battle[i];
            if (c.Status == CharacterStatus.Empty)
            {
                continue;
            }
            Assert.Equal(start[i] with { X = start[i].X + 24 }, c.Position);
            Assert.Equal(3, c.Facing);
        }

        var third = new BattleScreen(ui, assets, state, screens);
        third.Start(Corridor with { Mode = 2 });
        Assert.Equal(start, Enumerable.Range(0, Combatant.PartySlots).Select(i => third.Tactics!.Battle[i].Position).ToArray());
    }

    private sealed class NullHost : IHost, IVideo, IInput, IClock, IAudio
    {
        private long _now;

        public IVideo Video => this;
        public IInput Input => this;
        public IAudio Audio => this;
        public IClock Clock => this;
        public bool QuitRequested => false;
        public MouseState Mouse => new(160, 100, MouseButtons.None);
        public bool ShiftHeld => false;
        public long Milliseconds => _now;

        public void Present(Screen screen) { }

        public void Pump() { }

        public bool TryReadKey(out int key)
        {
            key = 0;
            return false;
        }

        public void Sleep(int milliseconds) => _now += milliseconds;

        public void PlayMusic(int tune) { }

        public void StopMusic() { }

        public void PlaySound(int effect) { }
    }
}
