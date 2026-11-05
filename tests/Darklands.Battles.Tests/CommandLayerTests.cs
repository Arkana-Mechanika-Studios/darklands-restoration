using Darklands.Content;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Presentation.Commands;
using Darklands.Simulation;

namespace Darklands.Battles.Tests;

/// <summary>
/// The command layer (docs/COMMANDS.md): a command is the player's key or click of one pass, carried out by the same code; the card
/// screens read the player's actions as intents. A command chooses what the player's own input chooses.
/// </summary>
public sealed class CommandLayerTests
{
    /// <summary>A host that plays scripted steps once each is ready (a step returning null waits a frame), moves its pointer when told, then idles.</summary>
    private sealed class ScriptHost(params Func<(int Key, MouseState Mouse)?>[] steps) : IHost, IVideo, IInput, IClock
    {
        private readonly Queue<Func<(int Key, MouseState Mouse)?>> _steps = new(steps);
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

        /// <summary>Runs at every pump, before the step (a script issuing commands when the screen is ready).</summary>
        public Action? OnPump { get; set; }

        public void Present(Screen screen) { }

        public void Sleep(int milliseconds) => _now += Math.Max(1, milliseconds);

        public bool TryReadKey(out int key)
        {
            key = _key;
            _key = 0;
            return key != 0;
        }

        public void MovePointer(int x, int y) => Mouse = Mouse with { X = x, Y = y };

        public void Pump()
        {
            _key = 0;
            OnPump?.Invoke();
            if (_steps.TryPeek(out var next) && next() is { } frame)
            {
                _steps.Dequeue();
                (_key, Mouse) = frame;
                return;
            }
            Mouse = Mouse with { Buttons = MouseButtons.None };
            QuitRequested = ++_idle > 5000;
        }
    }

    [Fact]
    public void TheCardScreensReadTheseIntentsFromThePlayersActions()
    {
        static PlayerAction Key(int key) => new(key, 100, 100, MouseButtons.None);
        Assert.Equal(new PressOnPortraits(45), CardIntent.Of(new PlayerAction(Keys.None, 59, 45, MouseButtons.Left), 60, false));
        Assert.Equal(new PressAt(60, 45), CardIntent.Of(new PlayerAction(Keys.None, 60, 45, MouseButtons.Left), 60, false));
        Assert.Equal(new Confirm(), CardIntent.Of(Key(Keys.Enter), 60, false));
        Assert.Equal(new Confirm(), CardIntent.Of(Key(Keys.Space), 60, false));
        Assert.Equal(new MoveHighlight(-1), CardIntent.Of(Key(Keys.Up), 60, false));
        Assert.Equal(new MoveHighlight(1), CardIntent.Of(Key(Keys.Down), 60, false));
        // the digits are an interface convenience: dropped unless it is on, as the original drops them
        Assert.Null(CardIntent.Of(Key(Keys.ForCharacter('3')), 60, false));
        Assert.Equal(new ChooseByNumber(2), CardIntent.Of(Key(Keys.ForCharacter('3')), 60, true));
        Assert.Null(CardIntent.Of(Key(Keys.ForCharacter('0')), 60, true));
        Assert.Null(CardIntent.Of(Key(Keys.Escape), 60, true));
        Assert.Null(CardIntent.Of(new PlayerAction(Keys.None, 100, 100, MouseButtons.Right), 60, true));
    }

    [Fact]
    public void ACommandIsOnePassOfInputAndNothingWithoutOne()
    {
        var host = new ScriptHost();
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var ui = new GameUi(host, new GameAssets(files));
        Assert.Equal(0, ui.Commands.Count);
        Assert.Null(ui.Poll());
        ui.Commands.Enqueue([new PressKey(Keys.ForCharacter('x')), new ClickAt(100, 50), new PointAt(7, 8), new ChooseLine(0)]);
        Assert.Equal(Keys.ForCharacter('x'), ui.Poll()?.Key);
        var click = ui.Poll();
        Assert.True(click is { IsLeftClick: true, X: 100, Y: 50 });
        Assert.Equal((100, 50), (host.Mouse.X, host.Mouse.Y));
        Assert.Null(ui.Poll());
        Assert.Equal((7, 8), (host.Mouse.X, host.Mouse.Y));
        // no list to choose from: the line is dropped
        Assert.Null(ui.Poll());
        Assert.Equal(0, ui.Commands.Count);
        // a point off the screen is taken to its edge, as the host keeps a real pointer: no click where no mouse can reach
        ui.Commands.Enqueue([new ClickAt(-5, 500), new PointAt(400, -3)]);
        Assert.True(ui.Poll() is { IsLeftClick: true, X: 0, Y: 199 });
        Assert.Equal((0, 199), (host.Mouse.X, host.Mouse.Y));
        Assert.Null(ui.Poll());
        Assert.Equal((319, 0), (host.Mouse.X, host.Mouse.Y));
        ui.CommandTargets = n => n == 2 ? (150, 90) : null;
        ui.Commands.Enqueue(new ChooseLine(2));
        Assert.Null(ui.Poll());
        Assert.Equal((150, 90), (host.Mouse.X, host.Mouse.Y));
        Assert.True(ui.Poll() is { IsLeftClick: true, X: 150, Y: 90 });
        Assert.Equal(0, ui.Commands.Count);
    }

    /// <summary>Enter at the card's text and a click on the second line, by the player and by commands: the same option, the same menu state.</summary>
    [Fact]
    public void CommandsChooseTheCardOptionThePlayersInputChooses()
    {
        var off = new MouseState(5, 5, MouseButtons.None);
        GameUi? ui = null;
        bool InMenu() => ui?.CommandTargets is not null;
        (int, MouseState) Line(int n, MouseButtons buttons) =>
            CardScreen.OptionPoint(n) is var (x, y) ? (0, new MouseState(x, y, buttons)) : (0, off);
        var player = new ScriptHost(
            () => (0, off), () => (Keys.Enter, off),
            () => InMenu() ? Line(1, MouseButtons.None) : null, () => Line(1, MouseButtons.Left), () => Line(1, MouseButtons.None));
        var byPlayer = Choose(player, u => ui = u);

        ui = null;
        var commands = new ScriptHost(() => (0, off), () => (0, off));
        var issued = 0;
        commands.OnPump = () =>
        {
            if (ui is null)
            {
                return;
            }
            if (issued == 0)
            {
                ui.Commands.Enqueue(new PressKey(Keys.Enter));
                issued++;
            }
            else if (issued == 1 && InMenu())
            {
                ui.Commands.Enqueue(new ChooseLine(1));
                issued++;
            }
        };
        var byCommands = Choose(commands, u => ui = u);
        if (byPlayer is null || byCommands is null)
        {
            return;
        }
        Assert.Equal(1, byPlayer.Value.Option);
        Assert.Equal(byPlayer, byCommands);
        Assert.False(player.QuitRequested);
        Assert.False(commands.QuitRequested);
        Assert.Equal(2, issued);
        Assert.Null(ui!.CommandTargets);
    }

    /// <summary>Down moves the pointer itself onto the new line (R043+0x1DD1): the next menu, whose highlight starts at -1 (R043+0x1C0A),
    /// lights the line the pointer stands on, so Enter there takes the line the keys chose, not the one the mouse left.</summary>
    [Fact]
    public void TheCursorKeysLeaveThePointerOnTheLineForTheNextMenu()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var off = new MouseState(5, 5, MouseButtons.None);
        GameUi? ui = null;
        ScriptHost? host = null;
        bool InMenu() => ui?.CommandTargets is not null;
        MouseState Here() => host!.Mouse with { Buttons = MouseButtons.None };
        host = new ScriptHost(
            () => (0, off), () => (Keys.Enter, off),
            () => InMenu() && CardScreen.OptionPoint(1) is var (x, y) ? (0, new MouseState(x, y, MouseButtons.None)) : null,
            () => (Keys.Down, Here()), () => (Keys.Enter, Here()),
            () => InMenu() ? null : (Keys.Enter, Here()), () => InMenu() ? (0, Here()) : null, () => (Keys.Enter, Here()));
        var world = WorldData.Load(files);
        ui = new GameUi(host, new GameAssets(files));
        var state = NewGame.Quickstart(world, 1, () => 1);
        state.Data = world;
        Darklands.Cards.CardContext? context = null;
        var screen = new CardScreen(ui, () => context!, new PartyPanel(ui.Assets));
        context = new Darklands.Cards.CardContext(state, world, screen);
        screen.Show("$Other00", Darklands.Cards.PictureNames.Of(34));
        var options = Enumerable.Repeat(Darklands.Cards.OptionState.Enabled, 10).ToList();
        Assert.Equal(2, screen.Choose(0, options));
        Assert.Equal(2, screen.Choose(0, options));
        Assert.False(host.QuitRequested);
    }

    /// <summary>
    /// The keyboard convenience (GameUi.KeyboardCardMenus) is made of player commands: a digit chooses its line as the player's click on
    /// it does (the same option and menu state), and Down with the pointer off the lines puts the pointer onto the first line, which the
    /// menu then lights from the pointer, so Enter takes it.
    /// </summary>
    [Fact]
    public void TheKeyboardConvenienceChoosesAsTheMouseDoes()
    {
        var off = new MouseState(5, 5, MouseButtons.None);
        GameUi? ui = null;
        bool InMenu() => ui?.CommandTargets is not null;
        (int, MouseState) Line(int n, MouseButtons buttons) =>
            CardScreen.OptionPoint(n) is var (x, y) ? (0, new MouseState(x, y, buttons)) : (0, off);
        var player = new ScriptHost(
            () => (0, off), () => (Keys.Enter, off),
            () => InMenu() ? Line(1, MouseButtons.None) : null, () => Line(1, MouseButtons.Left), () => Line(1, MouseButtons.None));
        var byPlayer = Choose(player, u => ui = u);

        ui = null;
        var digit = new ScriptHost(() => (0, off), () => (Keys.Enter, off), () => InMenu() ? (Keys.ForCharacter('2'), off) : null);
        var byDigit = Choose(digit, u => { ui = u; u.KeyboardCardMenus = true; });

        ui = null;
        ScriptHost? down = null;
        MouseState Here() => down!.Mouse with { Buttons = MouseButtons.None };
        (int X, int Y)? first = null;
        down = new ScriptHost(
            () => (0, off), () => (Keys.Enter, off),
            () =>
            {
                if (!InMenu())
                {
                    return null;
                }
                first = CardScreen.OptionPoint(0);
                return (Keys.Down, off);
            },
            () => (0, Here()), () => (Keys.Enter, Here()));
        var byDown = Choose(down, u => { ui = u; u.KeyboardCardMenus = true; });
        if (byPlayer is null || byDigit is null || byDown is null)
        {
            return;
        }
        Assert.Equal(1, byPlayer.Value.Option);
        Assert.Equal(byPlayer, byDigit);
        Assert.Equal(0, byDown.Value.Option);
        // the pointer stands where the original's cursor keys put it: the column's x + 1, the line's y + 2
        Assert.Equal((first!.Value.X, first.Value.Y + 1), (down.Mouse.X, down.Mouse.Y));
        Assert.False(digit.QuitRequested);
        Assert.False(down.QuitRequested);
    }

    private static (int Option, int Member, int Slot, bool Popup)? Choose(ScriptHost host, Action<GameUi> made)
    {
        if (GameFolder.Files is not { } files)
        {
            return null;
        }
        var world = WorldData.Load(files);
        var ui = new GameUi(host, new GameAssets(files));
        made(ui);
        var state = NewGame.Quickstart(world, 1, () => 1);
        state.Data = world;
        Darklands.Cards.CardContext? context = null;
        var screen = new CardScreen(ui, () => context!, new PartyPanel(ui.Assets));
        context = new Darklands.Cards.CardContext(state, world, screen);
        screen.Show("$Other00", Darklands.Cards.PictureNames.Of(34));
        var options = Enumerable.Repeat(Darklands.Cards.OptionState.Enabled, 10).ToList();
        var option = screen.Choose(0, options);
        return (option, context.MenuMember, context.MenuSlot, context.MenuPopup);
    }
}
