using Darklands.Content;
using Darklands.Core;
using Darklands.Core.Characters;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Simulation;

namespace Darklands.Battles.Tests;

/// <summary>
/// Dragging a pack line on the character sheet (the left button pressed on the line, let go elsewhere): onto another line the item
/// moves there, onto the equipment box it is readied, onto a member in the party panel it is given (with Shift the stack), onto the
/// figure below the scroll or a skill box nothing happens; a line of the formulae list let go on the equipment box readies the pack
/// stack of that line's number. Checked against the original by the lockstep session sheet-drag (tools/oracle/drag_mouse.py).
/// </summary>
// orig: R031+0x1E42 (the list line's click: the line lit, `while (mouse-button-state & 1);`, then the place of the pointer decides)
public sealed class SheetDragTests
{
    private sealed class DragHost(params (int Key, MouseState Mouse, bool Shift)[] frames) : IHost, IVideo, IInput, IClock
    {
        private readonly Queue<(int Key, MouseState Mouse, bool Shift)> _frames = new(frames);
        private int _key;
        private int _idle;
        private long _now;

        public IVideo Video => this;
        public IInput Input => this;
        public IAudio Audio { get; } = new SilentAudio();
        public IClock Clock => this;
        public bool QuitRequested { get; private set; }
        public MouseState Mouse { get; private set; } = new(5, 5, MouseButtons.None);
        public bool ShiftHeld { get; private set; }
        public long Milliseconds => _now;
        public int Left => _frames.Count;

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
            if (!_frames.TryDequeue(out var frame))
            {
                QuitRequested = ++_idle > 1000;
                (_key, Mouse) = (0, Mouse with { Buttons = MouseButtons.None });
                return;
            }
            (_key, Mouse, ShiftHeld) = frame;
        }
    }

    private const int PackX = 100;

    /// <summary>The y of pack line n (the scroll at y 30, its lines from y 39, 8 pixels each).</summary>
    private static int Line(int n) => 39 + n * 8 + 3;

    private static IEnumerable<(int, MouseState, bool)> Idle(int n, int x = 5, int y = 5) =>
        Enumerable.Repeat((0, new MouseState(x, y, MouseButtons.None), false), n);

    private static IEnumerable<(int, MouseState, bool)> Key(int key, int idle = 1) =>
        new[] { (key, new MouseState(5, 5, MouseButtons.None), false) }.Concat(Idle(idle));

    /// <summary>The left button pressed at (x, y), the pointer moved with it held to (tx, ty), let go there.</summary>
    private static IEnumerable<(int, MouseState, bool)> Drag(int x, int y, int tx, int ty, bool shift = false) =>
        Enumerable.Repeat((0, new MouseState(x, y, MouseButtons.Left), shift), 3)
            .Concat(Enumerable.Repeat((0, new MouseState(tx, ty, MouseButtons.Left), shift), 3))
            .Concat(Enumerable.Repeat((0, new MouseState(tx, ty, MouseButtons.None), shift), 3))
            .Concat(Idle(60, tx, ty));

    /// <summary>Member 0's sheet with <paramref name="open"/> keys (the pack: Down three times, Enter), then <paramref name="drag"/>, Esc.</summary>
    private static GameState? Play(Action<GameState> setUp, IEnumerable<(int, MouseState, bool)> drag, params int[] open)
    {
        var frames = new List<(int, MouseState, bool)>(Idle(2));
        foreach (var k in open.Length > 0 ? open : [Keys.Down, Keys.Down, Keys.Down, Keys.Enter])
        {
            frames.AddRange(Key(k, k == Keys.Enter ? 60 : 1));
        }
        frames.AddRange(drag);
        frames.AddRange(Key(Keys.Escape));
        frames.AddRange(Key(Keys.Escape));
        var host = new DragHost([.. frames]);
        if (GameFolder.Files is not { } files)
        {
            return null;
        }
        var world = WorldData.Load(files);
        var ui = new GameUi(host, new GameAssets(files));
        var state = NewGame.Quickstart(world, 1, () => 1);
        state.Data = world;
        setUp(state);
        var panel = new PartyPanel(ui.Assets);
        var formulas = FormulaBook.Load(ui.Assets.Files, world);
        new CharacterSheetScreen(ui, panel, formulas, world.Items.Saints.Select(s => s.FullName).ToList(), new AlchemyScreen(ui, panel, formulas))
            .Show(state, 0);
        Assert.False(host.QuitRequested);
        return state;
    }

    private static List<(int Code, int Count)> PackOf(GameState s, int member)
    {
        var pack = s.Party.Slot(member).Pack;
        return [.. Enumerable.Range(0, Math.Max(0, (int)pack.Stacks)).Select(i => (pack[i].Code, pack[i].Count))];
    }

    /// <summary>Three stacks in member 0's pack: a hand weapon not readied, two items that are no hand weapons (the second two of them).</summary>
    private static void ThreeStacks(GameState s)
    {
        var m = s.Party.Slot(0);
        var weapon = Enumerable.Range(1, 0x40).First(c => (s.Item(c).Flags & Darklands.Core.Items.ItemFlags.HandWeapon) != 0);
        var others = Enumerable.Range(1, 0x40).Where(c => c != weapon && (s.Item(c).Flags & Darklands.Core.Items.ItemFlags.HandWeapon) == 0).Take(2).ToArray();
        m.Pack.Stacks = 3;
        m.Pack[0] = new ItemStack(weapon, s.Item(weapon).Type, 25, 1, s.Item(weapon).Weight);
        m.Pack[1] = new ItemStack(others[0], s.Item(others[0]).Type, 20, 1, s.Item(others[0]).Weight);
        m.Pack[2] = new ItemStack(others[1], s.Item(others[1]).Type, 20, 2, s.Item(others[1]).Weight);
    }

    [Fact]
    public void ALineDroppedOnAnotherLineMovesThere()
    {
        List<(int, int)> before = [];
        var state = Play(s => { ThreeStacks(s); before = PackOf(s, 0); }, Drag(PackX, Line(0), PackX, Line(2)));
        if (state is null)
        {
            return;
        }
        // R031+0x5922 item-relocate (PackRules.Move): line 0 goes to line 2, lines 1 and 2 move up one
        Assert.Equal([before[1], before[2], before[0]], PackOf(state, 0));
    }

    [Fact]
    public void ALineDroppedOnTheFigureOrASkillBoxDoesNothing()
    {
        foreach (var (x, y) in new[] { (120, 176), (280, 40) })
        {
            List<(int, int)> before = [];
            EquippedItem weapon = default;
            var state = Play(s => { ThreeStacks(s); before = PackOf(s, 0); weapon = s.Party.Slot(0).Weapon; }, Drag(PackX, Line(0), x, y));
            if (state is null)
            {
                return;
            }
            Assert.Equal(before, PackOf(state, 0));
            Assert.Equal(weapon, state.Party.Slot(0).Weapon);
        }
    }

    [Fact]
    public void ALineDroppedOnTheEquipmentBoxIsReadied()
    {
        var weapon = -1;
        var state = Play(s =>
        {
            ThreeStacks(s);
            weapon = s.Party.Slot(0).Pack[0].Code;
        }, Drag(PackX, Line(0), 220, 110));
        if (state is null)
        {
            return;
        }
        // R031+0x5A02 (PackRules.Equip): the hand weapon of line 0 readied
        Assert.Equal(state.Item(weapon).Type, state.Party.Slot(0).Weapon.Type);
        Assert.Equal(25, state.Party.Slot(0).Weapon.Quality);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public void ALineDroppedOnAMemberIsGivenWithShiftTheStack(bool shift, int given)
    {
        var code = -1;
        var had = 0;
        var state = Play(s =>
        {
            ThreeStacks(s);
            code = s.Party.Slot(0).Pack[2].Code;
            had = PackOf(s, 1).Where(p => p.Code == code).Sum(p => p.Count);
        }, Drag(PackX, Line(2), 20, 60, shift));
        if (state is null)
        {
            return;
        }
        // R031+0x4850 hand-over-one / R031+0x48CC hand-over-stack to the member of the row let go on (y 60: member 1)
        Assert.Equal(had + given, PackOf(state, 1).Where(p => p.Code == code).Sum(p => p.Count));
        Assert.Equal(2 - given, PackOf(state, 0).Where(p => p.Code == code).Sum(p => p.Count));
    }

    [Fact]
    public void AFormulaLineDroppedOnTheEquipmentBoxReadiesThePackStackOfItsNumber()
    {
        var weapon = -1;
        var state = Play(s =>
        {
            ThreeStacks(s);
            weapon = s.Party.Slot(0).Pack[0].Code;
            s.Party.Slot(0).Weapon = EquippedItem.None;
            if (s.Party.Slot(0).Formulas.Count == 0)
            {
                s.Party.Slot(0).Formulas.Add(1);
            }
        }, Drag(PackX, 30 + 16 + 9 + 3, 220, 110), Keys.Down, Keys.Down, Keys.Down, Keys.Down, Keys.Enter);
        if (state is null)
        {
            return;
        }
        // the formulae box (box 10: Down four times; its scroll at y 46): line 0 dropped on the equipment box readies pack stack 0
        Assert.Equal(state.Item(weapon).Type, state.Party.Slot(0).Weapon.Type);
    }
}
