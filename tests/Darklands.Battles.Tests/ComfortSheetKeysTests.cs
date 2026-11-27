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
/// The loot page (GitHub issue 10) has neither in the original: Comfort adds both the keys and the click.
/// </summary>
public sealed class ComfortSheetKeysTests
{
    /// <summary>A host that gives one key a read (0: none), then Esc for good; the mouse plays one frame a pump, then stays still with no button held.</summary>
    private sealed class KeyHost(int[] keys, params MouseState[] frames) : IHost, IVideo, IInput, IClock
    {
        private readonly Queue<int> _keys = new(keys);
        private readonly Queue<MouseState> _frames = new(frames);
        private long _now;
        private int _reads;

        public IVideo Video => this;
        public IInput Input => this;
        public IAudio Audio { get; } = new SilentAudio();
        public IClock Clock => this;
        public bool QuitRequested => _reads > 5000;
        public MouseState Mouse { get; private set; } = new(160, 190, MouseButtons.None);
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

        public void Pump()
        {
            Mouse = _frames.TryDequeue(out var frame) ? frame : Mouse with { Buttons = MouseButtons.None };
        }
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

    private static readonly int[] SheetKeys = [0, 0, Keys.F2, 0, Keys.F1, 0, Keys.F5, 0];

    private static List<int>? Run(bool comfort, Action<BuySellPage, GameState, IGameScreens> screen) => Run(comfort, SheetKeys, [], screen);

    private static List<int>? Run(bool comfort, int[] keys, MouseState[] frames, Action<BuySellPage, GameState, IGameScreens> screen)
    {
        if (GameFolder.Files is not { } files)
        {
            return null;
        }
        var world = WorldData.Load(files);
        var ui = new GameUi(new KeyHost(keys, frames), new GameAssets(files)) { ComfortInterface = comfort };
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

    private static List<ItemStack> Pile(GameState state) => [new(0x26, state.Item(0x26).Type, 20, 4, (byte)state.Item(0x26).Weight)];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AtTheLootF1ToF5OpenTheSheetsWithComfortOnly(bool comfort)
    {
        if (Run(comfort, (page, state, screens) => new LootScreen(page, state, Pile(state)) { Screens = screens }.Run(Money.From(0, 0, 0))) is not { } opened)
        {
            return;
        }
        Assert.Equal(comfort ? [1, 0] : [], opened);
    }

    // the party column's rows are 40 pixels high: a click on the third member's row, then on the fifth slot's (empty)
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AtTheLootAClickOnThePartyOpensASheetWithComfortOnly(bool comfort)
    {
        MouseState[] frames = [new(20, 2 * 40 + 20, MouseButtons.Left), new(20, 2 * 40 + 20, MouseButtons.None), new(20, 2 * 40 + 20, MouseButtons.None),
            new(20, 4 * 40 + 20, MouseButtons.Left), new(20, 4 * 40 + 20, MouseButtons.None)];
        if (Run(comfort, [0, 0, 0, 0, 0, 0, 0, 0, 0, 0], frames,
                (page, state, screens) => new LootScreen(page, state, Pile(state)) { Screens = screens }.Run(Money.From(0, 0, 0))) is not { } opened)
        {
            return;
        }
        Assert.Equal(comfort ? [2] : [], opened);
    }
}
