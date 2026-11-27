using Darklands.Cards;
using Darklands.Content;
using Darklands.Core;
using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Simulation;

namespace Darklands.Battles.Tests;

/// <summary>
/// The Comfort interface's sheet keys on the buy and sell pages (GitHub issue 9): F1..F5 open a member's sheet at the barter and the
/// item store, as a click on the party column does; Classic leaves the keys alone, as the 1992 game does (only the click opens a sheet).
/// </summary>
public sealed class ComfortSheetKeysTests
{
    /// <summary>A host that gives one key a read (0: none), then Esc for good; the mouse stays still with no button held.</summary>
    private sealed class KeyHost(params int[] keys) : IHost, IVideo, IInput, IClock
    {
        private readonly Queue<int> _keys = new(keys);
        private long _now;
        private int _reads;

        public IVideo Video => this;
        public IInput Input => this;
        public IAudio Audio { get; } = new SilentAudio();
        public IClock Clock => this;
        public bool QuitRequested => _reads > 5000;
        public MouseState Mouse { get; } = new(160, 190, MouseButtons.None);
        public bool ShiftHeld => false;
        public long Milliseconds => _now;

        public void Present(Screen screen) { }

        public void Sleep(int milliseconds) => _now += Math.Max(1, milliseconds);

        public bool TryReadKey(out int key)
        {
            _reads++;
            _now += 10;
            key = _keys.TryDequeue(out var k) ? k : Keys.Escape;
            return key != 0;
        }

        public void Pump() { }
    }

    /// <summary>The members whose sheet the screens asked for.</summary>
    private sealed class Sheets : IGameScreens
    {
        public List<int> Opened { get; } = [];
        public void Barter(int shop, string name, long categories) { }
        public void Camp(int mode) { }
        public void Alchemy(int member, int place) { }
        public void ItemStore() { }
        public void CharacterSheet(int member) => Opened.Add(member);
        public void PartyInfo() { }
        public void Loot(IList<ItemStack> items, Money money) { }
        public int Battle(BattleRequest request) => 0;
    }

    private static List<int>? Run(bool comfort, Action<BuySellPage, GameState, IGameScreens> screen)
    {
        if (GameFolder.Files is not { } files)
        {
            return null;
        }
        var world = WorldData.Load(files);
        var ui = new GameUi(new KeyHost(0, 0, Keys.F2, 0, Keys.F1, 0, Keys.F5, 0), new GameAssets(files)) { ComfortInterface = comfort };
        var state = NewGame.Quickstart(world, 1, () => 1);
        state.Data = world;
        var sheets = new Sheets();
        screen(new BuySellPage(ui, new PartyPanel(ui.Assets)), state, sheets);
        return sheets.Opened;
    }

    // the Quickstart party has four members: F5 names an empty slot
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AtTheBarterF1ToF5OpenTheSheetsWithComfortOnly(bool comfort)
    {
        if (Run(comfort, (page, state, screens) => new BarterScreen(page, state, screens, 1, null, -1).Run()) is not { } opened)
        {
            return;
        }
        Assert.Equal(comfort ? [1, 0] : [], opened);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AtTheItemStoreF1ToF5OpenTheSheetsWithComfortOnly(bool comfort)
    {
        if (Run(comfort, (page, state, screens) => new ItemStoreScreen(page, state, screens).Run()) is not { } opened)
        {
            return;
        }
        Assert.Equal(comfort ? [1, 0] : [], opened);
    }
}
