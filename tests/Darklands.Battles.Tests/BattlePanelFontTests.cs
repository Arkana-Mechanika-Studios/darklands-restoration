using Darklands.Cards;
using Darklands.Content;
using Darklands.Core.Battles;
using Darklands.Platform;
using Darklands.Presentation.Graphics;

namespace Darklands.Battles.Tests;

/// <summary>
/// The battle's party panel is drawn by the same routines as the map's (party-panel-row-set root 0000:1716, member-gauge-bars root 0000:1A9C) on the
/// window DS:0156, whose font is slot 1 (checked at every call of both routines over a played gate battle): the values are in that font,
/// each followed by a space, in the name's colour.
/// </summary>
public sealed class BattlePanelFontTests
{
    private static readonly BattleRequest Corridor = new(40, 1234, [(34, 4, 3), (-1, 0, 0), (-1, 0, 0)]);

    [Fact]
    public void ThePanelValuesAreInTheMapPanelsFont()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        using var run = new BattleUiTests.Session(files, Corridor);
        run.Play("wait:10");
        var frame = run.LastFrame!;
        var tb = run.Screen.Tactics!;
        var painter = new TextPainter(run.Ui.Assets.Font(1));
        var checkedValues = 0;
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            var c = tb.Battle[i];
            if (c.Member is null || c.Status == Core.Characters.CharacterStatus.Empty)
            {
                continue;
            }
            var favour = (int)Core.Characters.Attribute.DivineFavor;
            var values = new[] { (0x12, c.Endurance), (0x20, c.Strength), (0x2D, c.Attributes[favour]) };
            foreach (var (x, value) in values)
            {
                var reference = new Canvas(new Screen());
                painter.Draw(reference, x, i * 40 + 0x1F, value + " ", 0x0F, 0x38);
                var colours = new HashSet<byte>();
                var lit = 0;
                for (var py = i * 40 + 0x1F; py < i * 40 + 0x1F + 12; py++)
                {
                    for (var px = x; px <= 0x38; px++)
                    {
                        var isLit = reference[px, py] != 0;
                        if (isLit)
                        {
                            lit++;
                            colours.Add(frame[py * Screen.Width + px]);
                        }
                    }
                }
                Assert.True(lit > 0);
                Assert.Single(colours);
                Assert.Contains(colours.Single(), new byte[] { 7, 0x0E, 0x0F });
                checkedValues++;
            }
        }
        Assert.True(checkedValues >= 3);
    }
}
