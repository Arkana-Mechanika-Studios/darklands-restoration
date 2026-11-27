using Darklands.Cards;
using Darklands.Content;
using Darklands.Core;
using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Darklands.Core.Rules;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Simulation;

namespace Darklands.Battles.Tests;

/// <summary>
/// The Comfort interface's mark on the pack lists (GitHub issue 4): an asterisk in the margin left of each line whose stack the member
/// has equipped, on the barter, the item store and the loot page; Classic draws the 1992 pages, without it.
/// </summary>
public sealed class ComfortEquippedMarkTests
{
    /// <summary>A host whose every key read is Esc: the page is drawn once and left.</summary>
    private sealed class EscHost : IHost, IVideo, IInput, IClock
    {
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
            key = Keys.Escape;
            return true;
        }

        public void Pump() { }
    }

    private sealed class NoScreens : IGameScreens
    {
        public void Barter(int shop, string name, long categories) { }
        public void Camp(int mode) { }
        public void Alchemy(int member, int place) { }
        public void ItemStore() { }
        public void CharacterSheet(int member) { }
        public void PartyInfo() { }
        public void Loot(IList<ItemStack> items, Money money) { }
        public int Battle(BattleRequest request) => 0;
    }

    private const int MarkX = 0x6B;
    private const int ListX = 0x72;

    // every member wears the second stack of their pack as body armour (the armour test has no item-kind condition): a pack line marked
    //   and the others not, whoever the page shows
    private static (byte[] Screen, bool[] Worn)? Draw(bool comfort, Action<BuySellPage, GameState> screen)
    {
        if (GameFolder.Files is not { } files)
        {
            return null;
        }
        var world = WorldData.Load(files);
        var ui = new GameUi(new EscHost(), new GameAssets(files)) { ComfortInterface = comfort };
        var state = NewGame.Quickstart(world, 1, () => 1);
        state.Data = world;
        foreach (var m in state.Party.Slots)
        {
            m.Weapon = m.MissileWeapon = m.LimbArmor = m.Shield = EquippedItem.None;
            m.VitalArmor = m.Pack.Count > 1 ? new EquippedItem(m.Pack[1].Type, m.Pack[1].Quality) : EquippedItem.None;
        }
        screen(new BuySellPage(ui, new PartyPanel(ui.Assets)), state);
        var pixels = new byte[320 * 200];
        for (var y = 0; y < 200; y++)
        {
            for (var x = 0; x < 320; x++)
            {
                pixels[y * 320 + x] = ui.Canvas[x, y];
            }
        }
        // the member the page shows on entry is the leader on all three pages
        var leader = state.Party.LeaderIndex;
        var pack = state.Party.Slot(leader).Pack;
        var worn = Enumerable.Range(0, 4).Select(i => i < pack.Count && Inventory.IsWorn(state, leader, pack[i])).ToArray();
        return (pixels, worn);
    }

    private static void TheMarkOnlyWithComfort(Action<BuySellPage, GameState> screen)
    {
        if (Draw(false, screen) is not var (classic, worn) || Draw(true, screen) is not var (comfort, _))
        {
            return;
        }
        Assert.Contains(true, worn);
        Assert.Contains(false, worn);
        for (var y = 0; y < 200; y++)
        {
            for (var x = 0; x < 320; x++)
            {
                var line = y - 0x93 >> 3;
                if (x is >= MarkX and < ListX && line is >= 0 and < 4 && worn[line])
                {
                    continue;
                }
                Assert.True(classic[y * 320 + x] == comfort[y * 320 + x], $"({x}, {y}) differs outside the marks");
            }
        }
        for (var line = 0; line < 4; line++)
        {
            var marked = false;
            for (var y = 0x93 + line * 8; y < 0x9B + line * 8; y++)
            {
                for (var x = MarkX; x < ListX; x++)
                {
                    marked |= classic[y * 320 + x] != comfort[y * 320 + x];
                }
            }
            Assert.True(marked == worn[line], $"pack line {line}: marked {marked}, worn {worn[line]}");
        }
    }

    [Fact]
    public void TheBarterMarksTheWornStacksWithComfortOnly() =>
        TheMarkOnlyWithComfort((page, state) => new BarterScreen(page, state, new NoScreens(), 1, null, -1).Run());

    [Fact]
    public void TheItemStoreMarksTheWornStacksWithComfortOnly() =>
        TheMarkOnlyWithComfort((page, state) => new ItemStoreScreen(page, state, new NoScreens()).Run());

    [Fact]
    public void TheLootPageMarksTheWornStacksWithComfortOnly() =>
        TheMarkOnlyWithComfort((page, state) =>
            new LootScreen(page, state, [new(0x26, state.Item(0x26).Type, 20, 4, (byte)state.Item(0x26).Weight)]).Run(Money.From(0, 0, 0)));
}
