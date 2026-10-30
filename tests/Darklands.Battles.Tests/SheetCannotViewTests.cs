using Darklands.Content;
using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Simulation;

namespace Darklands.Battles.Tests;

/// <summary>
/// In a battle a member who left the battlefield through an exit (fighter-floor-level 0x9D) is refused by the character sheet's own member
/// switch (R031+0x0124: F1..F5, a portrait click, Enter on a member box) and skipped by the loot screen's D (R040+0x0DB4..0x0DC8),
/// both after thunk 09C0:1D51 (R022+0x04E4, "You can't view ... at this time.").
/// </summary>
public sealed class SheetCannotViewTests
{
    /// <summary>A host that gives one key per frame (0: none) and no mouse.</summary>
    private sealed class KeyHost(params int[] keys) : IHost, IVideo, IInput, IClock
    {
        private readonly Queue<int> _keys = new(keys);
        private int _waiting;
        private long _now;

        public IVideo Video => this;
        public IInput Input => this;
        public IAudio Audio { get; } = new SilentAudio();
        public IClock Clock => this;
        public bool QuitRequested { get; private set; }
        public MouseState Mouse => new(160, 190, MouseButtons.None);
        public bool ShiftHeld => false;
        public long Milliseconds => _now;

        public void Present(Screen screen) { }

        public void Sleep(int milliseconds) => _now += Math.Max(1, milliseconds);

        public bool TryReadKey(out int key)
        {
            key = _waiting;
            _waiting = 0;
            return key != 0;
        }

        public void Pump()
        {
            if (_keys.TryDequeue(out var k))
            {
                _waiting = k == 0 ? _waiting : k;
            }
            else
            {
                QuitRequested = true;
            }
        }
    }

    private static int[] Spaced(params int[] keys) => [.. keys.SelectMany(k => new[] { 0, 0, 0, k })];

    [Fact]
    public void TheLootScreensDSkipsAMemberWhoLeftTheField()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var world = WorldData.Load(files);
        var host = new KeyHost(Spaced(0x2044, 0x266C));
        var ui = new GameUi(host, new GameAssets(files));
        var state = NewGame.Quickstart(world, 1, () => 1);
        state.Data = world;
        state.Party.LeaderIndex = 0;
        Assert.True(state.Party.Members.Count >= 3);
        var asked = new List<int>();
        var page = new BuySellPage(ui, new PartyPanel(ui.Assets));
        new LootScreen(page, state, new List<ItemStack>()) { CannotView = m => { asked.Add(m); return m == 1; } }.Run(Money.From(0, 0, 0));
        Assert.False(host.QuitRequested);
        // D from member 0: member 1 is refused (the message), member 2 is shown
        Assert.Equal([1, 2], asked);
    }

    [Fact]
    public void TheSheetRefusesAMemberWhoLeftTheField()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var world = WorldData.Load(files);
        // F2 (member 1, refused), F3 (member 2, shown), F3 again (the member shown: leaves)
        var host = new KeyHost(Spaced(Keys.F2, Keys.F3, Keys.F3));
        var ui = new GameUi(host, new GameAssets(files));
        var state = NewGame.Quickstart(world, 1, () => 1);
        state.Data = world;
        var formulas = FormulaBook.Load(files, world);
        var panel = new PartyPanel(ui.Assets);
        var asked = new List<int>();
        var sheet = new CharacterSheetScreen(ui, panel, formulas, world.Items.Saints.Select(s => s.FullName).ToList(), new AlchemyScreen(ui, panel, formulas))
        {
            CannotView = m => { asked.Add(m); return m == 1; },
        };
        sheet.Show(state, 0);
        Assert.False(host.QuitRequested);
        Assert.Equal([1, 2], asked);
    }

    /// <summary>
    /// Alt-Q read by the sheet itself (no hot keys installed): case 0x1000 R031+0x1C3C runs the clean-up R031+0x08F6 and the quit question root
    /// 0000:13A0, whose "no" leaves the sheet up with the save and the load offered again (it quit at once before).
    /// </summary>
    [Fact]
    public void AltQReadByTheSheetAsksAndTheSheetGoesOnAfterNo()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var world = WorldData.Load(files);
        // Alt-Q, "n", then Esc (the sheet leaves)
        var host = new KeyHost(Spaced(Keys.AltQ, 0x316E, Keys.Escape));
        var ui = new GameUi(host, new GameAssets(files));
        var state = NewGame.Quickstart(world, 1, () => 1);
        state.Data = world;
        var formulas = FormulaBook.Load(files, world);
        var panel = new PartyPanel(ui.Assets);
        var offered = new List<int>();
        var sheet = new CharacterSheetScreen(ui, panel, formulas, world.Items.Saints.Select(s => s.FullName).ToList(), new AlchemyScreen(ui, panel, formulas))
        {
            SaveAndLoad = offered.Add,
        };
        sheet.Show(state, 0);
        Assert.False(host.QuitRequested);
        Assert.Equal([2, 1, 1], offered);
    }
}
