using Darklands.Content;
using Darklands.Core;
using Darklands.Core.Rules;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Simulation;

namespace Darklands.Battles.Tests;

/// <summary>
/// The barter screen's list of codes for sale (DS:86A2) is never cleared: a listing writes only its own entries (R032+0x0ACD).
/// A click lights any of the four shop rows, also one past a shorter list's end, and P (R032+0x0F60) then buys the code an
/// earlier, longer listing left in that row (local/batch/c2stale/barter_probe.py runs the original's handler on that case).
/// </summary>
public sealed class BarterLeftoverRowTests
{
    /// <summary>One frame per Pump: the mouse as given, and a key read once in that frame (0: none).</summary>
    private sealed class StepHost(params (MouseState Mouse, int Key)[] steps) : IHost, IVideo, IInput, IClock
    {
        private readonly Queue<(MouseState Mouse, int Key)> _steps = new(steps);
        private int _key;
        private int _idle;
        private long _now;

        public IVideo Video => this;
        public IInput Input => this;
        public IAudio Audio { get; } = new SilentAudio();
        public IClock Clock => this;
        public bool QuitRequested { get; private set; }
        public MouseState Mouse { get; private set; } = new(5, 5, MouseButtons.None);
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
            if (_steps.TryDequeue(out var step))
            {
                Mouse = step.Mouse;
                _key = step.Key;
                return;
            }
            Mouse = Mouse with { Buttons = MouseButtons.None };
            _key = 0;
            QuitRequested = ++_idle > 1000;
        }
    }

    private static readonly MouseState Rest = new(5, 5, MouseButtons.None);

    /// <summary>The shop (shop, categories) of the quickstart's place whose stock has the wanted number of items.</summary>
    private static (int Shop, long Categories)? ShopWith(GameState state, Func<int, bool> wanted)
    {
        for (var shop = 0; shop <= 8; shop++)
        {
            foreach (var categories in Enumerable.Range(0, 31).Select(b => 1L << b).Append(-1L))
            {
                if (wanted(Trading.Stock(state, Trading.Open(state, shop, "Shop", categories)).Codes.Count))
                {
                    return (shop, categories);
                }
            }
        }
        return null;
    }

    [Fact]
    public void PurchaseOnARowPastTheListBuysWhatAnEarlierListingLeftThere()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var world = WorldData.Load(files);
        var state = NewGame.Quickstart(world, 1, () => 1);
        state.Data = world;
        state.Party.Purse = state.Party.Purse.Add(500, 0, 0);
        if (ShopWith(state, n => n >= 4) is not var (longShop, longCategories) || ShopWith(state, n => n is >= 1 and <= 3) is not var (shortShop, shortCategories))
        {
            Assert.Fail("no shops of the wanted sizes here");
            return;
        }
        var longList = Trading.Stock(state, Trading.Open(state, longShop, "Shop", longCategories)).Codes;
        var shortList = Trading.Stock(state, Trading.Open(state, shortShop, "Shop", shortCategories)).Codes;

        // row 3 (y 0x55 + 3 * 8 + 2), pressed for one frame, let go; then P; then Esc
        var row3 = new MouseState(0x80, 0x55 + 3 * 8 + 2, MouseButtons.Left);
        (MouseState, int)[] buyRow3 = [(Rest, 0), (Rest, 0), (row3, 0), (row3 with { Buttons = MouseButtons.None }, 0), (Rest, 0),
            (Rest, Keys.ForCharacter('P')), (Rest, 0), (Rest, Keys.Escape)];
        (MouseState, int)[] leave = [(Rest, 0), (Rest, 0), (Rest, Keys.Escape)];
        var host = new StepHost([.. leave, .. buyRow3]);
        var ui = new GameUi(host, new GameAssets(files));
        var panel = new PartyPanel(ui.Assets);

        // the longer listing first (left at once), then the shorter one, where row 3 is past the end
        new BarterScreen(new BuySellPage(ui, panel), state, new NoScreens(), longShop, "Shop", longCategories).Run();
        Assert.Equal(longList.Take(4), ui.BarterListing.Take(4));
        var leader = state.Party.LeaderIndex;
        var before = state.Party.Slot(leader).Pack.Where(s => s.Code == longList[3]).Sum(s => s.Count);
        var purse = state.Party.Purse;
        new BarterScreen(new BuySellPage(ui, panel), state, new NoScreens(), shortShop, "Shop", shortCategories).Run();

        Assert.False(host.QuitRequested);
        // the short listing wrote its own entries only
        Assert.Equal(shortList, ui.BarterListing.Take(shortList.Count));
        Assert.Equal(longList[3], ui.BarterListing[3]);
        // P bought the long listing's fourth code at this shop's price
        var after = state.Party.Slot(leader).Pack.Where(s => s.Code == longList[3]).Sum(s => s.Count);
        Assert.Equal(before + 1, after);
        Assert.True(state.Party.Purse < purse);
    }

    private sealed class NoScreens : Darklands.Cards.IGameScreens
    {
        public void Barter(int shop, string name, long categories) { }
        public void Camp(int mode) { }
        public void Alchemy(int member, int place) { }
        public void ItemStore() { }
        public void CharacterSheet(int member) { }
        public void PartyInfo() { }
        public void Loot(IList<Darklands.Core.Characters.ItemStack> items, Darklands.Core.Economy.Money money) { }
        public int Battle(Darklands.Cards.BattleRequest request) => 0;
    }
}
