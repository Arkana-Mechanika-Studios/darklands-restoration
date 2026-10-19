using Darklands.Cards;
using Darklands.Core.Battles;
using Darklands.Core.Characters;

namespace Darklands.Battles.Tests;

/// <summary>
/// The dragon fights on the player's files: loading the dragon's files sets DS:C21A, and combatant 5 then fights as the original's
/// dragon code has it (its own victims, its breath every few blows, no usual enemy turn).
/// </summary>
public sealed class DragonFightTests
{
    [Fact]
    public void TheLairsDragonFightsAsTheDragon()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        // the dragon's lair (S082 DragonsLair): battlefield 0x60, one enemy of type 0x3F quality 5
        var (screen, tactics) = BattleScreenTests.Start(files, new BattleRequest(0x60, 7, [(0x3F, 5, 1), (-1, 0, 0), (-1, 0, 0)]));
        var b = tactics.Battle;
        var breathed = false;
        var picked = false;
        for (var t = 0; t < 60 * 60 * 3 && b.Result == BattleResult.Running; t += 10)
        {
            BattleScreenTests.RunFor(screen, tactics, 10);
            if (b[5].Status == CharacterStatus.Empty)
            {
                continue;
            }
            var code = tactics.SlotCodes[b[5].AnimationSlot].ToUpperInvariant();
            if (code is "M73" or "M90")
            {
                Assert.Equal(code == "M73" ? Battle.HydraFight : Battle.CaveDragonFight, b.DemoMode);
            }
            if (b.DemoMode == 0)
            {
                continue;
            }
            breathed |= b[5].Action == CombatAction.UseItem && b[5].Spell == 8 && b[5].SpellPower == 50;
            picked |= b[5].Target is >= 0 and < Combatant.PartySlots;
        }
        Assert.True(b.DemoMode != 0, $"the lair's enemy loads a dragon's files ({tactics.SlotCodes[5]})");
        Assert.True(picked, "the dragon picked a member");
        Assert.True(breathed || b.Result != BattleResult.Running, "the dragon breathed (or the fight ended first)");
    }
}
