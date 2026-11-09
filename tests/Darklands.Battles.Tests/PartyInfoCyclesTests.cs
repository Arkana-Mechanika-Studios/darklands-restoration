using Darklands.Cards;
using Darklands.Content;
using Darklands.Core;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Simulation;

namespace Darklands.Battles.Tests;

/// <summary>
/// The party information (F6) opened in a battle, while the battle's palette cycles run: the original lets them turn on over the
/// screen's picture (OriginalBehaviour.FixPartyInfoBattleCycles off); the player's game holds them while the screen is open and lets
/// them run again afterwards.
/// </summary>
public sealed class PartyInfoCyclesTests
{
    /// <summary>A host that waits a few frames, then presses Esc; it keeps every palette shown.</summary>
    private sealed class KeyHost : IHost, IVideo, IInput, IClock
    {
        private int _frames;
        private long _now;

        public IVideo Video => this;
        public IInput Input => this;
        public IAudio Audio { get; } = new SilentAudio();
        public IClock Clock => this;
        public bool QuitRequested => _frames > 2000;
        public MouseState Mouse { get; } = new(5, 5, MouseButtons.None);
        public bool ShiftHeld => false;
        public long Milliseconds => _now;
        public List<uint[]> Palettes { get; } = [];

        public void Present(Screen screen) => Palettes.Add((uint[])screen.Palette.Clone());

        public void Sleep(int milliseconds) => _now += Math.Max(1, milliseconds);

        public bool TryReadKey(out int key)
        {
            // the screen is up for a good half second of the timer (the cycles step every 4 to 6 frames), then Esc
            key = ++_frames == 60 ? Keys.Escape : 0;
            if (key == 0)
            {
                _now += 10;
            }
            return key != 0;
        }

        public void Pump()
        {
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InABattleThePartyInfoShowsItsOwnColoursUnlessExact(bool fixedGame)
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var world = WorldData.Load(files);
        // the colours the screen shows with no cycles at all (outside a battle)
        var plain = Shown(files, world, fixedGame, battleCycles: false, out _);
        var inBattle = Shown(files, world, fixedGame, battleCycles: true, out var runningAfter);
        Assert.True(runningAfter, "the battle's cycles run again when it is back");
        var cycled = Enumerable.Range(0x10, 0x1E - 0x10 + 1).Where(i => plain[i] != inBattle[i]).ToList();
        if (fixedGame)
        {
            Assert.Empty(cycled);
        }
        else
        {
            // the original: the battle's ranges turn over the screen's colours
            Assert.NotEmpty(cycled);
        }
    }

    /// <summary>The palette the party information showed last before it closed.</summary>
    private static uint[] Shown(GameFiles files, WorldData world, bool fixedGame, bool battleCycles, out bool runningAfter)
    {
        var host = new KeyHost();
        var ui = new GameUi(host, new GameAssets(files));
        var state = NewGame.Quickstart(world, 1, () => 1);
        state.Data = world;
        state.Behaviour = fixedGame ? OriginalBehaviour.DataSafe : OriginalBehaviour.Faithful;
        if (battleCycles)
        {
            // the battle's cycles as BattleScreen starts them, over colours unlike the screen's (the battlefield's palette)
            for (var i = 0x10; i <= 0x1E; i++)
            {
                ui.Canvas.SetColor(i, new Content.Formats.Graphics.Rgb24(0xFF, (byte)(i * 8), 0xFF));
            }
            ui.TakeCycle(1, 4, 0x10, 0x15);
            ui.TakeCycle(2, 6, 0x18, 0x1E);
            ui.TakeCycle(4, 5, 0x16, 0x17);
            ui.Cycles.Start(1);
            ui.Cycles.Start(-2);
            ui.Cycles.Start(4);
            ui.RunCycles(true);
            host.Sleep(500);
            ui.Present();
        }
        var screens = new GameScreens2(ui, new PartyPanel(ui.Assets), state, world, new NoScreens());
        var before = host.Palettes.Count;
        screens.PartyInfo();
        runningAfter = ui.Cycles.Running;
        // the last present is the screen below put back (GameScreens2.Keeping); the one before it is the party information's
        Assert.True(host.Palettes.Count - before >= 2);
        return host.Palettes[^2];
    }

    private sealed class NoScreens : IGameScreens
    {
        public void Barter(int shop, string name, long categories) { }
        public void Camp(int mode) { }
        public void Alchemy(int member, int place) { }
        public void ItemStore() { }
        public void CharacterSheet(int member) { }
        public void PartyInfo() { }
        public void Loot(IList<Darklands.Core.Characters.ItemStack> items, Darklands.Core.Economy.Money money) { }
        public int Battle(BattleRequest request) => 0;
    }
}
