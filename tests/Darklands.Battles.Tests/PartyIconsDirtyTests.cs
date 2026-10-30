using Darklands.Cards;
using Darklands.Content;
using Darklands.Core;
using Darklands.Core.Rules;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Simulation;

namespace Darklands.Battles.Tests;

/// <summary>
/// The party information (F6) sets party-icons-stale (DS:9C46, R034+0x119C); the loops of the alchemy, camp and prayer screens test it after
/// the menu bar and draw the screen again. Here the party information is stood in for by a call that paints the whole screen: what the
/// pass after it presents must be what was shown before it.
/// </summary>
public sealed class PartyIconsDirtyTests
{
    /// <summary>A host that plays a key in some frames, keeps the pixels of every presented frame, and idles with no input after the last.</summary>
    private sealed class KeyHost(params int[] keys) : IHost, IVideo, IInput, IClock
    {
        private readonly Queue<int> _keys = new(keys);
        private int _key;
        private int _idle;
        private long _now;

        public List<byte[]> Presented { get; } = [];
        public byte[]? AtF6 { get; private set; }
        public byte[]? AtEscape { get; private set; }
        public IVideo Video => this;
        public IInput Input => this;
        public IAudio Audio { get; } = new SilentAudio();
        public IClock Clock => this;
        public bool QuitRequested { get; private set; }
        public MouseState Mouse { get; } = new(5, 5, MouseButtons.None);
        public bool ShiftHeld => false;
        public long Milliseconds => _now;

        public void Present(Screen screen) => Presented.Add((byte[])screen.Pixels.Clone());

        public void Sleep(int milliseconds) => _now += Math.Max(1, milliseconds);

        public bool TryReadKey(out int key)
        {
            key = _key;
            _key = 0;
            // a key is read right after the frame it is read in was presented
            if (key == Keys.F6)
            {
                AtF6 = Presented[^1];
            }
            else if (key == Keys.Escape)
            {
                AtEscape = Presented[^1];
            }
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

    // paintFrom: the first column the stand-in paints (the prayer screen draws from column 60 on: the party panel at its left stays, R034+0x0878)
    /// <summary>Runs <paramref name="screen"/> with keys, F6 among them or not; the frame F6 is read in and the one Esc is read in are returned.</summary>
    private static (byte[] BeforeF6, byte[] AtEnd, int Painted)? Run(bool f6, Action<GameUi, GameState, PartyPanel, WorldData> screen, int paintFrom, params int[] after)
    {
        int[] keys = f6 ? [0, 0, Keys.F6, 0, .. after] : [0, 0, 0, 0, .. after];
        var host = new KeyHost(keys);
        if (GameFolder.Files is not { } files)
        {
            return null;
        }
        var world = WorldData.Load(files);
        var ui = new GameUi(host, new GameAssets(files));
        var state = NewGame.Quickstart(world, 1, () => 1);
        state.Data = world;
        var painted = 0;
        var hotKeys = new HotKeys(ui, state, new GameOptions())
        {
            PartyInfo = () =>
            {
                painted++;
                ui.Canvas.Fill(paintFrom, 0, 320 - paintFrom, 200, 77);
            },
        };
        ui.GameKey = hotKeys.Handle;
        screen(ui, state, new PartyPanel(ui.Assets), world);
        Assert.False(host.QuitRequested);
        return (f6 ? host.AtF6! : host.Presented[3], host.AtEscape!, painted);
    }

    private static void Compare(Action<GameUi, GameState, PartyPanel, WorldData> screen, int paintFrom, params int[] after)
    {
        if (Run(true, screen, paintFrom, after) is not var (before, atEnd, painted))
        {
            return;
        }
        Assert.Equal(1, painted);
        Assert.Equal(before, atEnd);
        // the control: the same keys with no F6 end on the same picture, so the comparison is not of two blank screens
        var control = Run(false, screen, paintFrom, after)!.Value;
        Assert.Equal(control.BeforeF6, before);
        Assert.Contains(before, p => p != 77);
    }

    [Fact]
    public void TheAlchemyScreenDrawsItselfAgain() =>
        Compare((ui, state, panel, world) => new AlchemyScreen(ui, panel, FormulaBook.Load(ui.Assets.Files, world)).Use(state, 0, 0), 0, 0, 0, Keys.Escape);

    [Fact]
    public void TheFormulaPageDrawsItselfAgain() =>
        Compare((ui, state, panel, world) => new AlchemyScreen(ui, panel, FormulaBook.Load(ui.Assets.Files, world)).ShowFormula(state, 0, 0), 0, 0, 0, Keys.Escape);

    [Fact]
    public void TheCampDrawsItselfAgain() =>
        Compare((ui, state, panel, world) =>
        {
            var formulas = FormulaBook.Load(ui.Assets.Files, world);
            var alchemy = new AlchemyScreen(ui, panel, formulas);
            var sheet = new CharacterSheetScreen(ui, panel, formulas, world.Items.Saints.Select(s => s.FullName).ToList(), alchemy);
            new CampScreen(ui, panel, formulas, alchemy, sheet).CampInTheWild(state, 0);
        }, 0, 0, 0, Keys.Escape);

    [Fact]
    public void ThePrayerScreenDrawsItselfAgain() =>
        Compare((ui, state, panel, world) => new SaintPrayerScreen(ui, panel).Pray(state, 0, 1, PrayerMode.ForParty, SaintEffects.Instance), 60, 0, 0, Keys.Escape);

    /// <summary>
    /// The barter loop's test (R032+0x06BF): the picture, the panel and R032+0x0B30 again, which differs from the first drawing in a few texts
    /// ("has..." for "currently has...", the barters line at x 0x4E): nothing of the stand-in is left, the page is what it was but for those.
    /// </summary>
    [Fact]
    public void TheBarterPageDrawsItselfAgain()
    {
        if (Run(true, (ui, state, panel, world) => new BarterScreen(new BuySellPage(ui, panel), state, new NoScreens(), 1, null!, -1).Run(), 0, 0, 0, Keys.Escape)
            is not var (before, atEnd, painted))
        {
            return;
        }
        Assert.Equal(1, painted);
        AssertDrawnAgain(before, atEnd, 3000);
    }

    private static void AssertDrawnAgain(byte[] before, byte[] atEnd, int atMost)
    {
        Assert.DoesNotContain(atEnd.Where((p, i) => p == 77 && before[i] != 77), _ => true);
        var differing = Enumerable.Range(0, before.Length).Count(i => before[i] != atEnd[i]);
        Assert.InRange(differing, 1, atMost);
    }

    /// <summary>
    /// The sheet's loop (R031+0x06D2): the sheet drawn again with its box closed, but not the cursor frame (no R031+0x1D44 there): the pass after
    /// F6 shows the sheet as before except where that frame was; nothing of the stand-in is left. The sheet keeps no GameUi.Redraw of the screen below.
    /// </summary>
    [Fact]
    public void TheSheetDrawsItselfAgainWithoutItsCursorFrame()
    {
        CharacterSheetScreen? sheet = null;
        void Show(GameUi ui, GameState state, PartyPanel panel, WorldData world)
        {
            var formulas = FormulaBook.Load(ui.Assets.Files, world);
            sheet = new CharacterSheetScreen(ui, panel, formulas, world.Items.Saints.Select(s => s.FullName).ToList(), new AlchemyScreen(ui, panel, formulas));
            var belowRedrawn = 0;
            ui.Redraw = () => belowRedrawn++;
            sheet.Show(state, 0);
            Assert.Equal(0, belowRedrawn);
        }
        if (Run(true, Show, 0, 0, 0, Keys.Escape) is not var (before, atEnd, painted))
        {
            return;
        }
        Assert.Equal(1, painted);
        AssertDrawnAgain(before, atEnd, 2000);
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
        public int Battle(Darklands.Cards.BattleRequest request) => 0;
    }
}
