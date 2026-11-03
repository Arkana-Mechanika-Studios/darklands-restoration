using Darklands.Cards;
using Darklands.Content;
using Darklands.Core;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Simulation;

namespace Darklands.Battles.Tests;

/// <summary>
/// Inside the character sheet the hot-key table (root 0000:0CAA, run by the sheet's menu-bar-loop R031+0x06CA) takes F6 and Alt-O as
/// everywhere else: the game's GameUi.GameKey (HotKeys.Handle) gets them from the sheet's poll before the sheet does.
/// </summary>
public sealed class SheetHotKeyTests
{
    private sealed class KeyHost(params int[] keys) : IHost, IVideo, IInput, IClock
    {
        private readonly Queue<int> _keys = new(keys);
        private int _key;
        private int _idle;
        private long _now;

        public IVideo Video => this;
        public IInput Input => this;
        public IAudio Audio { get; } = new SilentAudio();
        public IClock Clock => this;
        public bool QuitRequested { get; private set; }
        public MouseState Mouse { get; } = new(5, 5, MouseButtons.None);
        public bool ShiftHeld => false;
        public long Milliseconds => _now;

        public void Present(Screen screen) { }

        public void Sleep(int milliseconds) => _now += Math.Max(1, milliseconds);

        public bool TryReadKey(out int key)
        {
            key = _key;
            _key = 0;
            return key != 0;
        }

        public void Pump()
        {
            if (_keys.TryDequeue(out var key))
            {
                _key = key;
                return;
            }
            _key = 0;
            QuitRequested = ++_idle > 1000;
        }
    }

    [Theory]
    [InlineData(Keys.F6, 1, 0)]
    [InlineData(Keys.AltO, 0, 1)]
    public void F6AndAltOAreTakenInTheSheet(int key, int partyInfo, int order)
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var host = new KeyHost(0, 0, key, 0, 0, Keys.Escape);
        var world = WorldData.Load(files);
        var ui = new GameUi(host, new GameAssets(files));
        var state = NewGame.Quickstart(world, 1, () => 1);
        state.Data = world;
        int shownInfo = 0, shownOrder = 0;
        var hotKeys = new HotKeys(ui, state, new GameOptions())
        {
            PartyInfo = () => shownInfo++,
            ChoosePartyOrder = () => shownOrder++,
        };
        ui.GameKey = hotKeys.Handle;
        var panel = new PartyPanel(ui.Assets);
        var screens = new GameScreens2(ui, panel, state, world, new NoScreens()) { HotKeys = hotKeys, MenuBarKey = hotKeys.Handle };
        screens.CharacterSheet(0);
        Assert.False(host.QuitRequested);
        Assert.Equal(partyInfo, shownInfo);
        Assert.Equal(order, shownOrder);
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
