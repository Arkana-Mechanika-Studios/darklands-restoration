using Darklands.Cards;
using Darklands.Content;
using Darklands.Core;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Simulation;

namespace Darklands.Battles.Tests;

/// <summary>
/// The mouse wheel's ↑ / ↓ (host comfort, input.mouseWheel): the game's UI tells the host before each pump whether a selection list is
/// up (GameUi.SelectionList, IInput.SelectionListActive), and the host then gives a notch as the key itself. Where a list is up the
/// notches play exactly as the keys; where none is (the map, a battle, a card's text, a message box, the sheet with no list open) the
/// host is told so and the notches go nowhere.
/// </summary>
public sealed class MouseWheelListTests
{
    /// <summary>One frame of input: a key, the mouse, and wheel notches (positive away from the player: ↑).</summary>
    private readonly record struct Frame(int Key, MouseState Mouse, int Wheel = 0);

    /// <summary>
    /// A host that plays scripted frames (a step returning null waits a frame), turning wheel notches into ↑ / ↓ only while the UI says
    /// a list is up, as the SDL host does; it keeps what the UI told it at every pump.
    /// </summary>
    private sealed class WheelHost(params Func<Frame?>[] steps) : IHost, IVideo, IInput, IClock
    {
        private readonly Queue<Func<Frame?>> _steps = new(steps);
        private readonly Queue<int> _keys = new();
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
        public bool SelectionListActive { get; set; }
        public List<bool> Told { get; } = [];
        public int Given { get; private set; }
        public int Dropped { get; private set; }
        public int Left => _steps.Count;
        public int IdleLimit { get; init; } = 5000;

        public void Present(Screen screen) { }

        public void Sleep(int milliseconds) => _now += Math.Max(1, milliseconds);

        public bool TryReadKey(out int key) => _keys.TryDequeue(out key);

        public void Pump()
        {
            Told.Add(SelectionListActive);
            if (_steps.TryPeek(out var next) && next() is { } frame)
            {
                _steps.Dequeue();
                Mouse = frame.Mouse;
                if (frame.Wheel != 0)
                {
                    if (SelectionListActive)
                    {
                        for (var i = 0; i < Math.Abs(frame.Wheel); i++)
                        {
                            _keys.Enqueue(frame.Wheel > 0 ? Keys.Up : Keys.Down);
                            Given++;
                        }
                    }
                    else
                    {
                        Dropped += Math.Abs(frame.Wheel);
                    }
                }
                if (frame.Key != 0)
                {
                    _keys.Enqueue(frame.Key);
                }
                return;
            }
            QuitRequested = ++_idle > IdleLimit;
        }
    }

    private static readonly MouseState Rest = new(5, 5, MouseButtons.None);

    private static Func<Frame?> At(Frame frame) => () => frame;

    private static Func<Frame?> Idle => At(new Frame(0, Rest));

    private static Func<Frame?> Key(int key) => At(new Frame(key, Rest));

    private static Func<Frame?> Wheel(int notches) => At(new Frame(0, Rest, notches));

    private static (GameUi Ui, GameState State, PartyPanel Panel, WorldData World)? Start(WheelHost host)
    {
        if (GameFolder.Files is not { } files)
        {
            return null;
        }
        var world = WorldData.Load(files);
        var ui = new GameUi(host, new GameAssets(files));
        var state = NewGame.Quickstart(world, 1, () => 1);
        state.Data = world;
        return (ui, state, new PartyPanel(ui.Assets), world);
    }

    // ---- the character sheet ---------------------------------------------------------------

    /// <summary>The sheet played with <paramref name="steps"/> (an idle frame after each, 60 after Enter: the scroll unrolls), then Esc.</summary>
    private static (int Cursor, bool Open, int Selected, int Top, WheelHost Host)? Sheet(params Func<Frame?>[] steps)
    {
        var frames = new List<Func<Frame?>> { Idle, Idle };
        foreach (var step in steps)
        {
            frames.Add(step);
            frames.AddRange(Enumerable.Repeat(Idle, step().GetValueOrDefault().Key == Keys.Enter ? 60 : 1));
        }
        frames.Add(Key(Keys.Escape));
        frames.Add(Idle);
        var host = new WheelHost([.. frames]);
        if (Start(host) is not var (ui, state, panel, world))
        {
            return null;
        }
        var formulas = FormulaBook.Load(ui.Assets.Files, world);
        var sheet = new CharacterSheetScreen(ui, panel, formulas, world.Items.Saints.Select(s => s.FullName).ToList(), new AlchemyScreen(ui, panel, formulas));
        sheet.Show(state, 0);
        Assert.False(host.QuitRequested);
        Assert.Null(ui.SelectionList);
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var cursor = (int)typeof(CharacterSheetScreen).GetField("_cursor", flags)!.GetValue(sheet)!;
        var selected = (int[,])typeof(CharacterSheetScreen).GetField("_selected", flags)!.GetValue(sheet)!;
        var top = (int[,])typeof(CharacterSheetScreen).GetField("_top", flags)!.GetValue(sheet)!;
        return (cursor, (bool)typeof(CharacterSheetScreen).GetField("_open", flags)!.GetValue(sheet)!, selected[cursor, 0], top[cursor, 0], host);
    }

    [Fact]
    public void InTheOpenPackTheWheelPlaysAsTheCursorKeys()
    {
        // the pack (box 9): three Downs from the name box, Enter
        Func<Frame?>[] open = [Key(Keys.Down), Key(Keys.Down), Key(Keys.Down), Key(Keys.Enter)];
        if (Sheet([.. open, Wheel(-1), Wheel(-1), Wheel(1)]) is not var (cursor, isOpen, selected, top, host))
        {
            return;
        }
        Assert.Equal((9, true), (cursor, isOpen));
        Assert.Equal(3, host.Given);
        Assert.Equal(0, host.Dropped);
        var keys = Sheet([.. open, Key(Keys.Down), Key(Keys.Down), Key(Keys.Up)])!.Value;
        Assert.Equal((keys.Cursor, keys.Open, keys.Selected, keys.Top), (cursor, isOpen, selected, top));
        Assert.Equal(1, selected);
    }

    [Fact]
    public void InTheSaintsListTheWheelPlaysAsTheCursorKeys()
    {
        // the saints list (box 11): five Downs, Enter
        Func<Frame?>[] open = [Key(Keys.Down), Key(Keys.Down), Key(Keys.Down), Key(Keys.Down), Key(Keys.Down), Key(Keys.Enter)];
        if (Sheet([.. open, Wheel(-2)]) is not var (cursor, isOpen, selected, top, host))
        {
            return;
        }
        Assert.Equal((11, true), (cursor, isOpen));
        Assert.Equal(2, host.Given);
        var keys = Sheet([.. open, Key(Keys.Down), Key(Keys.Down)])!.Value;
        Assert.Equal((keys.Cursor, keys.Open, keys.Selected, keys.Top), (cursor, isOpen, selected, top));
    }

    [Fact]
    public void WithNoListOpenTheWheelDoesNotMoveTheSheetsFrame()
    {
        // ↓ there moves the frame between the boxes (name box -> 3): not a selection list, the wheel goes nowhere
        if (Sheet(Wheel(-1), Wheel(-1)) is not var (cursor, isOpen, _, _, host))
        {
            return;
        }
        Assert.Equal((0, false), (cursor, isOpen));
        Assert.Equal(0, host.Given);
        Assert.Equal(2, host.Dropped);
        Assert.Equal(3, Sheet(Key(Keys.Down))!.Value.Cursor);
    }

    // ---- the card menus ---------------------------------------------------------------------

    /// <summary>A card with a menu; the pointer onto its first line, then <paramref name="move"/>, then Enter: the option chosen.</summary>
    private static (int Chosen, WheelHost Host)? CardMenu(Func<Frame?> move)
    {
        var off = Rest;
        MouseState Line(int n) => CardScreen.OptionPoint(n) is var (x, y) ? new MouseState(x, y, MouseButtons.None) : off;
        GameUi? ui = null;
        Frame? InMenu(Frame frame) => ui?.Redraw?.Method.Name.Contains("Refresh") == true ? frame : null;
        var host = new WheelHost(
            () => new Frame(0, off), () => new Frame(Keys.Enter, off),
            () => InMenu(new Frame(0, Line(0))), () => new Frame(0, Line(0)),
            () => move() is { } m ? m with { Mouse = Line(0) } : null, () => new Frame(0, Line(0)),
            () => new Frame(Keys.Enter, Line(0)), () => new Frame(0, Line(0)));
        if (GameFolder.Files is not { } files)
        {
            return null;
        }
        var world = WorldData.Load(files);
        ui = new GameUi(host, new GameAssets(files));
        var state = NewGame.Quickstart(world, 1, () => 1);
        state.Data = world;
        Darklands.Cards.CardContext? context = null;
        var screen = new CardScreen(ui, () => context!, new PartyPanel(ui.Assets));
        context = new Darklands.Cards.CardContext(state, world, screen);
        screen.Show("$Other00", Darklands.Cards.PictureNames.Of(34));
        var options = Enumerable.Repeat(Darklands.Cards.OptionState.Enabled, 10).ToList();
        var chosen = screen.Choose(0, options);
        Assert.False(host.QuitRequested);
        Assert.Null(ui.SelectionList);
        return (chosen, host);
    }

    // orig: R043+0x1D46 (Down, 0x5000) with the pointer inside a line (DS:ED6C == 1): the next line; the wheel gives that key
    [Fact]
    public void InACardMenuTheWheelPlaysAsTheCursorKeys()
    {
        if (CardMenu(Wheel(-1)) is not var (chosen, host))
        {
            return;
        }
        Assert.Equal(1, host.Given);
        Assert.Equal(CardMenu(Key(Keys.Down))!.Value.Chosen, chosen);
        Assert.Equal(1, chosen);
        // the card's text before the menu (press-to-continue) has no list: the host was told none there
        Assert.False(host.Told[1]);
        Assert.True(host.Told[^1]);
    }

    // ---- the lists of the other screens -----------------------------------------------------

    [Fact]
    public void TheLootListsAreSelectionLists()
    {
        var host = new WheelHost(Idle, Idle, Wheel(-1), Idle, Key(Keys.Escape), Idle, Idle) { IdleLimit = 50 };
        if (Start(host) is not var (ui, state, _, _))
        {
            return;
        }
        var page = new BuySellPage(ui, new PartyPanel(ui.Assets));
        try
        {
            new LootScreen(page, state, new List<Darklands.Core.Characters.ItemStack>()).Run(Darklands.Core.Economy.Money.From(0, 0, 0));
        }
        catch (QuitGameException)
        {
        }
        Assert.Equal(1, host.Given);
        Assert.Null(ui.SelectionList);
    }

    [Fact]
    public void TheLoadListIsASelectionList()
    {
        if (GameFolder.Files is not { } files || CopySaves(files, "DKSAVE1.SAV", "DKSAVE2.SAV") is not { } folder)
        {
            return;
        }
        // the wheel's ↓ to the second row, then L loads it (as Down then L)
        var host = new WheelHost(Idle, Idle, Wheel(-1), Idle, Key(Keys.ForCharacter('l')), Idle, Idle);
        var ui = new GameUi(host, new GameAssets(files));
        var screen = new LoadGameScreen(ui, folder, Content.Formats.Saves.LoadScreenTexts.Decode(files.Read("DARKLAND.EXE")));
        string? loaded = null;
        screen.Run(fromStartScreen: true, name =>
        {
            loaded = name;
            return folder.Load(name, Darklands.Core.World.NoGameData.Instance);
        });
        Assert.Equal(1, host.Given);
        Assert.Equal(folder.Names()[1], loaded);
        Assert.Null(ui.SelectionList);
    }

    [Fact]
    public void TheCampsMemberChoiceIsASelectionList()
    {
        var host = new WheelHost(Idle, Idle, Wheel(-1), Idle) { IdleLimit = 30 };
        if (Start(host) is not var (ui, state, panel, world))
        {
            return;
        }
        var formulas = FormulaBook.Load(ui.Assets.Files, world);
        var alchemy = new AlchemyScreen(ui, panel, formulas);
        var sheet = new CharacterSheetScreen(ui, panel, formulas, world.Items.Saints.Select(s => s.FullName).ToList(), alchemy);
        try
        {
            new CampScreen(ui, panel, formulas, alchemy, sheet).CampInTheWild(state, 0);
        }
        catch (QuitGameException)
        {
        }
        Assert.Equal(1, host.Given);
        Assert.Null(ui.SelectionList);
    }

    // ---- where the wheel does nothing ---------------------------------------------------------

    [Fact]
    public void TheMapHasNoSelectionList()
    {
        var host = new WheelHost(Idle, Idle, Wheel(-1), Idle, Wheel(1), Idle) { IdleLimit = 60 };
        if (Start(host) is not var (ui, state, panel, _) || GameFolder.Files is not { } files)
        {
            return;
        }
        var map = new Darklands.Simulation.Overland.WorldMap(Content.Formats.Maps.WorldMapData.Decode(files.Read("DARKLAND.MAP")), state.Locations);
        try
        {
            new OverlandScreen(ui, panel, map).Travel(state);
        }
        catch (QuitGameException)
        {
        }
        Assert.DoesNotContain(true, host.Told);
        Assert.Equal(0, host.Given);
        Assert.Equal(2, host.Dropped);
    }

    [Fact]
    public void ABattleHasNoSelectionListEvenOverOne()
    {
        var host = new WheelHost(Idle, Idle, Wheel(-1), Idle, Wheel(1), Idle) { IdleLimit = 200 };
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var ui = new GameUi(host, new GameAssets(files));
        var state = NewGame.Quickstart(WorldData.Load(files), 7);
        var screens = new BattleScreens(ui, () => state);
        using (ui.UseSelectionList(static () => true))
        {
            try
            {
                screens.Battle(new BattleRequest(0x14, 863, [(34, 4, 8), (18, 5, 2), (34, 1, 2)]));
            }
            catch (QuitGameException)
            {
            }
            Assert.NotNull(ui.SelectionList);
        }
        Assert.DoesNotContain(true, host.Told);
        Assert.Equal(0, host.Given);
    }

    [Fact]
    public void AMessageBoxOverAListHasNone()
    {
        var host = new WheelHost(Idle, Idle, Idle, Idle, Wheel(-1), Idle, Key(Keys.Escape), Idle);
        if (Start(host) is not var (ui, _, _, _))
        {
            return;
        }
        using (ui.UseSelectionList(static () => true))
        {
            ui.Present();
            new MessageBox(ui).Pause();
        }
        // the presents before the box (the list's) say a list is up; the box's wait for any key says none, and its notch goes nowhere
        Assert.True(host.Told[0]);
        Assert.Contains(true, host.Told);
        Assert.False(host.Told[4]);
        Assert.Equal(0, host.Given);
        Assert.Equal(1, host.Dropped);
    }

    private static Darklands.Persistence.SaveFolder? CopySaves(GameFiles files, params string[] names)
    {
        var saves = Path.Combine(files.Root, "SAVES");
        if (!names.All(n => File.Exists(Path.Combine(saves, n))))
        {
            return null;
        }
        var copy = TempCopies.Create("darklands-wheel-");
        foreach (var n in names)
        {
            File.Copy(Path.Combine(saves, n), Path.Combine(copy, n));
        }
        return new Darklands.Persistence.SaveFolder(copy);
    }
}
