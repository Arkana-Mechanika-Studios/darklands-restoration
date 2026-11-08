using Darklands.Content;
using Darklands.Core.Characters.Creation;
using Darklands.Platform;
using Darklands.Presentation;

namespace Darklands.Battles.Tests;

/// <summary>
/// The character generator's attribute step: '+' and '-' (the main row's and the keypad's words, R037+0x4BEE) raise and lower the selected
/// attribute; a right click on an attribute row is '-', the mirror of the left click's '+' (a host comfort: the first click selects the row,
/// a click on the selected row acts); a right click anywhere else does nothing.
/// </summary>
public sealed class CharacterGeneratorKeysTests
{
    private sealed class FrameHost(CharacterCreation creation, params (int Key, MouseState Mouse)[] frames) : IHost, IVideo, IInput, IClock
    {
        private readonly Queue<(int Key, MouseState Mouse)> _frames = new(frames);
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

        /// <summary>The attributes and the selected row the last time the attribute step was up.</summary>
        public (int[] Attributes, int Cursor)? Seen { get; private set; }

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
            if (creation.Stage == CreationStage.ChildhoodAttributes)
            {
                Seen = ([.. creation.Attributes], creation.Cursor);
            }
            if (!_frames.TryDequeue(out var frame))
            {
                QuitRequested = ++_idle > 1000;
                (_key, Mouse) = (0, Mouse with { Buttons = MouseButtons.None });
                return;
            }
            (_key, Mouse) = frame;
        }
    }

    private static readonly MouseState Off = new(5, 5, MouseButtons.None);

    private static IEnumerable<(int, MouseState)> Key(int key) => [(key, Off), (0, Off), (0, Off)];

    private static IEnumerable<(int, MouseState)> Click(int x, int y, MouseButtons button) =>
        [(0, new MouseState(x, y, MouseButtons.None)), (0, new MouseState(x, y, button)), (0, new MouseState(x, y, button)), (0, new MouseState(x, y, MouseButtons.None)), (0, Off)];

    /// <summary>The y of attribute row n (the rows from y 0x12, 8 pixels each).</summary>
    private static int Row(int n) => 0x12 + n * 8 + 4;

    /// <summary>
    /// Plays <paramref name="steps"/> on the attribute step (reached with B twice: begin the childhood, the first family background), then
    /// Esc until the generator is left; the attributes and the selected row as the step last had them.
    /// </summary>
    private static (int[] Attributes, int Cursor)? Play(params IEnumerable<(int, MouseState)>[] steps)
    {
        if (GameFolder.Files is not { } files)
        {
            return null;
        }
        var world = WorldData.Load(files);
        var assets = new GameAssets(files);
        var data = Darklands.Content.Formats.Characters.CreationDataDecoder.Decode(files.Read("DARKLAND.EXE"));
        var creation = new CharacterCreation(data.Tables, world.ItemDefinitions, new Darklands.Core.Randomness.MscRandom(1), 1, () => 1);
        var b = Keys.ForCharacter('b');
        var frames = new List<(int, MouseState)>([(0, Off), (0, Off)]);
        frames.AddRange(Key(b));
        frames.AddRange(Key(b));
        foreach (var step in steps)
        {
            frames.AddRange(step);
        }
        for (var i = 0; i < 4; i++)
        {
            frames.AddRange(Key(Keys.Escape));
        }
        var host = new FrameHost(creation, [.. frames]);
        var ui = new GameUi(host, assets);
        new CharacterGeneratorScreen(ui, data, world, creation).Run();
        Assert.False(host.QuitRequested);
        Assert.NotNull(host.Seen);
        return host.Seen;
    }

    [Fact]
    public void PlusAndMinusRaiseAndLowerTheSelectedAttributeFromTheMainRowAndTheKeypad()
    {
        if (Play() is not { } start || Play(Key(0x0D2B), Key(0x4E2B), Key(0x0D3D)) is not { } raised
            || Play(Key(0x0D2B), Key(0x4E2B), Key(0x0D3D), Key(0x0C2D), Key(0x4A2D)) is not { } lowered)
        {
            return;
        }
        Assert.Equal(start.Attributes[start.Cursor] + 3, raised.Attributes[start.Cursor]);
        Assert.Equal(start.Attributes[start.Cursor] + 1, lowered.Attributes[start.Cursor]);
        // what the SDL host now gives for '+' typed on any layout and for '=' (Keys.ForCharacter): the original's words
        Assert.Equal(0x0D2B, Keys.ForCharacter('+'));
        Assert.Equal(0x0D3D, Keys.ForCharacter('='));
    }

    [Fact]
    public void ARightClickOnTheSelectedRowIsMinusAndOnAnotherRowSelectsIt()
    {
        var x = 0xD0;
        // the left click on row 0 (selects it, or raises it if it is selected already) in both runs; then the clicks against the keys
        var clicks = Play(Click(x, Row(0), MouseButtons.Left), Click(x, Row(0), MouseButtons.Left), Click(x, Row(0), MouseButtons.Left),
            Click(x, Row(0), MouseButtons.Right), Click(x, Row(1), MouseButtons.Right), Click(x, Row(1), MouseButtons.Left), Click(x, Row(1), MouseButtons.Right));
        var keys = Play(Click(x, Row(0), MouseButtons.Left), Key(0x0D2B), Key(0x0D2B), Key(0x0C2D), Key(Keys.Down), Key(0x0D2B), Key(0x0C2D));
        if (clicks is not { } c || keys is not { } k)
        {
            return;
        }
        Assert.Equal(1, c.Cursor);
        Assert.Equal(k.Cursor, c.Cursor);
        Assert.Equal(k.Attributes, c.Attributes);
        Assert.NotEqual(Play()!.Value.Attributes, c.Attributes);
    }

    [Fact]
    public void ARightClickOffTheAttributeRowsDoesNothing()
    {
        // the step's list, the '-' panel the left click lowers with, the empty corner, the bar's place
        var off = Play(Click(0x60, 0x32, MouseButtons.Right), Click(0xD0, 0x70, MouseButtons.Right), Click(300, 195, MouseButtons.Right), Click(70, 4, MouseButtons.Right));
        if (off is not { } o || Play() is not { } start)
        {
            return;
        }
        Assert.Equal(start.Attributes, o.Attributes);
        Assert.Equal(start.Cursor, o.Cursor);
    }
}
