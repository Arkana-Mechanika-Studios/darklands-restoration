using Darklands.Content;
using Darklands.Core;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Simulation;

namespace Darklands.Battles.Tests;

/// <summary>
/// The character sheet's closed skill boxes: the original prints the third group's values (Healing to Woodwise) at x 0x109, three
/// pixels left of the other two groups' 0x10C (OriginalBehaviour.FixSheetSkillValueColumn off, as the oracle plays); the player's game
/// prints them at 0x10C (GitHub issue 4). Nothing else on the sheet changes.
/// </summary>
// orig: R031+0x39C6 box 7 ("%02d" DS:2EED at the box's x + 3) and boxes 4, 5 ("%02d" at 0x10C)
public sealed class CharacterSheetSkillColumnTests
{
    /// <summary>A host that keeps the frame shown after a few idle frames, then presses Esc.</summary>
    private sealed class ShotHost : IHost, IVideo, IInput, IClock
    {
        private int _frames;
        private long _now;

        public IVideo Video => this;
        public IInput Input => this;
        public IAudio Audio { get; } = new SilentAudio();
        public IClock Clock => this;
        public bool QuitRequested => _frames > 400;
        public MouseState Mouse { get; } = new(5, 5, MouseButtons.None);
        public bool ShiftHeld => false;
        public long Milliseconds => _now;
        public byte[]? Pixels { get; private set; }
        public uint[]? Palette { get; private set; }

        public void Present(Screen screen)
        {
            if (_frames < 30)
            {
                Pixels = (byte[])screen.Pixels.Clone();
                Palette = (uint[])screen.Palette.Clone();
            }
        }

        public void Sleep(int milliseconds) => _now += Math.Max(1, milliseconds);

        public bool TryReadKey(out int key)
        {
            key = ++_frames == 40 ? Keys.Escape : 0;
            _now += 10;
            return key != 0;
        }

        public void Pump()
        {
        }
    }

    private static ShotHost? Sheet(OriginalBehaviour behaviour)
    {
        if (GameFolder.Files is not { } files)
        {
            return null;
        }
        var host = new ShotHost();
        var world = WorldData.Load(files);
        var ui = new GameUi(host, new GameAssets(files));
        var state = NewGame.Quickstart(world, 1, () => 1);
        state.Data = world;
        state.Behaviour = behaviour;
        state.Party.LeaderIndex = 0;
        var panel = new PartyPanel(ui.Assets);
        var formulas = FormulaBook.Load(ui.Assets.Files, world);
        new CharacterSheetScreen(ui, panel, formulas, world.Items.Saints.Select(s => s.FullName).ToList(), new AlchemyScreen(ui, panel, formulas))
            .Show(state, 0);
        Assert.NotNull(host.Pixels);
        return host;
    }

    /// <summary>The sheet's text colour (CharacterSheetScreen.TextColor).</summary>
    private const byte TextColor = 0xBF;

    // the leftmost column of the values' strip x0..x1 over the rows y0..y1 that holds a pixel of the text colour (the paper is textured)
    private static int FirstInk(byte[] pixels, int x0, int x1, int y0, int y1)
    {
        for (var x = x0; x < x1; x++)
        {
            for (var y = y0; y < y1; y++)
            {
                if (pixels[y * Screen.Width + x] == TextColor)
                {
                    return x;
                }
            }
        }
        return -1;
    }

    [Fact]
    public void TheThirdGroupsValuesLineUpWithTheOthersUnlessExact()
    {
        if (Sheet(OriginalBehaviour.Faithful) is not { } exact || Sheet(OriginalBehaviour.DataSafe) is not { } player)
        {
            return;
        }
        if (Environment.GetEnvironmentVariable("DARKLANDS_SHEET_SHOTS") is { Length: > 0 } shots)
        {
            Directory.CreateDirectory(shots);
            foreach (var (name, h) in new[] { ("before-exact", exact), ("after-fixed", player) })
            {
                File.WriteAllBytes(Path.Combine(shots, name + ".idx"), h.Pixels!);
                File.WriteAllBytes(Path.Combine(shots, name + ".pal"), h.Palette!.SelectMany(BitConverter.GetBytes).ToArray());
            }
        }
        // the first two groups' values (y 0x12.., 0x55..) and the third's (y 0x91..), in the strip 0x108..0x118 (inside the boxes' left border)
        int Weapons(byte[] p) => FirstInk(p, 0x108, 0x118, 0x12, 0x12 + 7 * 8);
        int Lower(byte[] p) => FirstInk(p, 0x108, 0x118, 0x91, 0x91 + 6 * 8);
        Assert.Equal(Weapons(exact.Pixels!) - 3, Lower(exact.Pixels!));
        Assert.Equal(Weapons(player.Pixels!), Lower(player.Pixels!));
        Assert.Equal(Weapons(exact.Pixels!), Weapons(player.Pixels!));
        // only the third group's values differ
        for (var i = 0; i < exact.Pixels!.Length; i++)
        {
            if (exact.Pixels[i] != player.Pixels![i])
            {
                var (x, y) = (i % Screen.Width, i / Screen.Width);
                Assert.InRange(x, 0x109, 0x10C + 12);
                Assert.InRange(y, 0x91, 0x91 + 6 * 8);
            }
        }
        Assert.NotEqual(exact.Pixels, player.Pixels);
    }
}
