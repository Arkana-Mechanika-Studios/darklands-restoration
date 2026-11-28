using Darklands.Content;
using Darklands.Core;
using Darklands.Core.Characters;
using Darklands.Core.Rules;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Simulation;

namespace Darklands.Battles.Tests;

/// <summary>
/// The Comfort interface's mark on the character sheet's Pack list (GitHub issue 4, senner007's follow-up): an asterisk in the panel's
/// margin left of each line whose stack the member has equipped, as on the trade pages' pack lists; Classic draws the 1992 sheet. The
/// original's sheet names the worn items in its Equipment box only, not which pack line is worn.
/// </summary>
public sealed class ComfortSheetEquippedMarkTests
{
    /// <summary>A host that clicks the Pack box open, keeps the frame shown with the list up, then presses Esc.</summary>
    private sealed class PackHost : IHost, IVideo, IInput, IClock
    {
        private int _frames;
        private long _now;

        public IVideo Video => this;
        public IInput Input => this;
        public IAudio Audio { get; } = new SilentAudio();
        public IClock Clock => this;
        public bool QuitRequested => _frames > 600;
        // the Pack box (box 9 at 196, 149, 61 x 13): pressed at frame 40, let go at 42
        public MouseState Mouse => new(226, 155, _frames is >= 40 and < 42 ? MouseButtons.Left : MouseButtons.None);
        public bool ShiftHeld => false;
        public long Milliseconds => _now;
        public byte[]? Pixels { get; private set; }

        public void Present(Screen screen)
        {
            if (_frames is > 100 and < 200)
            {
                Pixels = (byte[])screen.Pixels.Clone();
            }
        }

        public void Sleep(int milliseconds) => _now += Math.Max(1, milliseconds);

        public bool TryReadKey(out int key)
        {
            key = _frames == 220 ? Keys.Escape : _frames == 260 ? Keys.Escape : 0;
            return key != 0;
        }

        public void Pump()
        {
            _frames++;
            _now += 10;
        }
    }

    // the Pack panel (box 9's panel at 62, 30): its lines at x + 9, y + 9 + 8 j; the mark in the margin before them
    private const int PanelX = 62, PanelY = 30, Lines = 14;

    private static (byte[] Pixels, bool[] Worn)? Sheet(bool comfort)
    {
        if (GameFolder.Files is not { } files)
        {
            return null;
        }
        var host = new PackHost();
        var world = WorldData.Load(files);
        var ui = new GameUi(host, new GameAssets(files)) { ComfortInterface = comfort };
        var state = NewGame.Quickstart(world, 1, () => 1);
        state.Data = world;
        state.Party.LeaderIndex = 0;
        var m = state.Party.Slot(0);
        // the member wears the second stack of the pack as body armour (the armour test has no item-kind condition): one line marked
        m.Weapon = m.MissileWeapon = m.LimbArmor = m.Shield = EquippedItem.None;
        m.VitalArmor = new EquippedItem(m.Pack[1].Type, m.Pack[1].Quality);
        var panel = new PartyPanel(ui.Assets);
        var formulas = FormulaBook.Load(ui.Assets.Files, world);
        try
        {
            new CharacterSheetScreen(ui, panel, formulas, world.Items.Saints.Select(s => s.FullName).ToList(), new AlchemyScreen(ui, panel, formulas))
                .Show(state, 0);
        }
        catch (QuitGameException)
        {
        }
        Assert.NotNull(host.Pixels);
        var worn = Enumerable.Range(0, Lines).Select(i => i < m.Pack.Count && Inventory.IsWorn(state, 0, m.Pack[i])).ToArray();
        return (host.Pixels!, worn);
    }

    [Fact]
    public void ThePackListMarksTheWornStacksWithComfortOnly()
    {
        if (Sheet(false) is not var (classic, worn) || Sheet(true) is not var (comfort, _))
        {
            return;
        }
        Assert.Contains(true, worn);
        Assert.Contains(false, worn);
        // the list is up (the panel differs from the closed sheet's picture there) and only the margins of worn lines differ
        for (var y = 0; y < 200; y++)
        {
            for (var x = 0; x < 320; x++)
            {
                var line = y - PanelY - 9 >> 3;
                if (x is >= PanelX + 1 and < PanelX + 9 && line is >= 0 and < Lines && worn[line])
                {
                    continue;
                }
                Assert.True(classic[y * 320 + x] == comfort[y * 320 + x], $"({x}, {y}) differs outside the marks");
            }
        }
        for (var line = 0; line < Lines; line++)
        {
            var marked = false;
            for (var y = PanelY + 9 + line * 8; y < PanelY + 17 + line * 8; y++)
            {
                for (var x = PanelX + 1; x < PanelX + 9; x++)
                {
                    marked |= classic[y * 320 + x] != comfort[y * 320 + x];
                }
            }
            Assert.True(marked == worn[line], $"pack line {line}: marked {marked}, worn {worn[line]}");
        }
    }
}
